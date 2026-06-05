using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace OpenWithApp;

/// <summary>
/// Walks the Uninstall registry hives and produces a deduplicated list of
/// real, launchable applications.
/// </summary>
internal static class AppEnumerator
{
    private const string UninstallSubKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private const string UninstallSubKeyWow =
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    public static List<InstalledApp> Enumerate()
    {
        // Dedupe by lower-cased exe path; we want one entry per real binary.
        var byExe = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);

        ReadHive(Registry.LocalMachine, UninstallSubKey,    byExe);
        ReadHive(Registry.LocalMachine, UninstallSubKeyWow, byExe);
        ReadHive(Registry.CurrentUser,  UninstallSubKey,    byExe);

        var result = new List<InstalledApp>(byExe.Values);
        result.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName,
                                             StringComparison.CurrentCultureIgnoreCase));
        return result;
    }

    private static void ReadHive(RegistryKey root, string subKey,
                                 Dictionary<string, InstalledApp> sink)
    {
        RegistryKey? hive = null;
        try
        {
            hive = root.OpenSubKey(subKey, writable: false);
            if (hive == null) return;

            foreach (string name in hive.GetSubKeyNames())
            {
                RegistryKey? entry = null;
                try
                {
                    entry = hive.OpenSubKey(name, writable: false);
                    if (entry == null) continue;

                    var app = TryBuild(entry);
                    if (app == null) continue;

                    // First write wins. HKLM walked before HKCU keeps the
                    // machine-wide registration if both exist.
                    sink.TryAdd(app.ExecutablePath, app);
                }
                catch
                {
                    // skip malformed entries -- never crash on bad registry data
                }
                finally
                {
                    entry?.Dispose();
                }
            }
        }
        catch
        {
            // hive missing or unreadable -- ignore
        }
        finally
        {
            hive?.Dispose();
        }
    }

    private static InstalledApp? TryBuild(RegistryKey entry)
    {
        string? displayName = (entry.GetValue("DisplayName") as string)?.Trim();
        if (string.IsNullOrEmpty(displayName)) return null;

        // Hide system updates, hotfixes, MSI patches, and tombstoned entries.
        if (entry.GetValue("SystemComponent") is int sc && sc == 1) return null;
        if (entry.GetValue("ReleaseType") is string rt &&
            (rt.Equals("Hotfix", StringComparison.OrdinalIgnoreCase) ||
             rt.Equals("Security Update", StringComparison.OrdinalIgnoreCase) ||
             rt.Equals("Update Rollup", StringComparison.OrdinalIgnoreCase)))
            return null;
        if (entry.GetValue("ParentKeyName") is string) return null;

        string? displayIcon     = (entry.GetValue("DisplayIcon") as string)?.Trim();
        string? installLocation = (entry.GetValue("InstallLocation") as string)?.Trim();
        string? uninstallString = (entry.GetValue("UninstallString") as string)?.Trim();
        string? publisher       = (entry.GetValue("Publisher") as string)?.Trim();

        // Find the real exe. Try several strategies in order of accuracy.
        string? exe =
            ExeFromDisplayIcon(displayIcon)
            ?? ExeFromInstallLocation(installLocation, displayName)
            ?? ExeFromUninstallString(uninstallString);

        if (exe == null) return null;
        if (!File.Exists(exe)) return null;
        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;

        return new InstalledApp
        {
            DisplayName     = displayName!,
            ExecutablePath  = exe,
            Publisher       = string.IsNullOrEmpty(publisher) ? null : publisher,
            InstallLocation = string.IsNullOrEmpty(installLocation) ? null : installLocation,
            DisplayIcon     = string.IsNullOrEmpty(displayIcon) ? null : displayIcon,
        };
    }

    private static string? ExeFromDisplayIcon(string? displayIcon)
    {
        if (string.IsNullOrEmpty(displayIcon)) return null;
        string s = displayIcon!.Trim().Trim('"');

        // Strip ",<icon index>" suffix.
        int comma = s.LastIndexOf(',');
        if (comma > 0 && comma < s.Length - 1)
        {
            string tail = s.Substring(comma + 1).Trim();
            if (int.TryParse(tail, out _))
                s = s.Substring(0, comma).Trim().Trim('"');
        }

        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(s))
            return s;
        return null;
    }

    private static string? ExeFromInstallLocation(string? location, string displayName)
    {
        if (string.IsNullOrEmpty(location)) return null;
        location = location!.Trim().Trim('"');
        if (!Directory.Exists(location)) return null;

        // 1) try a file whose name resembles the display name
        string trimmed = SanitizeForFilename(displayName);
        if (trimmed.Length > 0)
        {
            try
            {
                foreach (string f in Directory.EnumerateFiles(location, "*.exe",
                                                              SearchOption.TopDirectoryOnly))
                {
                    string n = Path.GetFileNameWithoutExtension(f);
                    if (n.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                        trimmed.Contains(n, StringComparison.OrdinalIgnoreCase))
                        return f;
                }
            }
            catch { /* permission denied etc. */ }
        }

        // 2) fall back to the first .exe in the install folder
        try
        {
            foreach (string f in Directory.EnumerateFiles(location, "*.exe",
                                                          SearchOption.TopDirectoryOnly))
                return f;
        }
        catch { }

        return null;
    }

    private static string? ExeFromUninstallString(string? uninst)
    {
        if (string.IsNullOrEmpty(uninst)) return null;
        // Uninstall strings often look like:  "C:\Program Files\App\uninst.exe" /S
        // We don't want to launch the *uninstaller*, but its folder usually
        // contains the real exe. Probe the directory.
        string path;
        if (uninst!.StartsWith("\""))
        {
            int end = uninst.IndexOf('"', 1);
            if (end < 0) return null;
            path = uninst.Substring(1, end - 1);
        }
        else
        {
            int space = uninst.IndexOf(' ');
            path = space < 0 ? uninst : uninst.Substring(0, space);
        }

        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;

            // Prefer an .exe that is NOT the uninstaller.
            string uninstName = Path.GetFileNameWithoutExtension(path);
            foreach (string f in Directory.EnumerateFiles(dir, "*.exe",
                                                          SearchOption.TopDirectoryOnly))
            {
                string n = Path.GetFileNameWithoutExtension(f);
                if (n.Contains("unins", StringComparison.OrdinalIgnoreCase)) continue;
                if (n.Equals(uninstName, StringComparison.OrdinalIgnoreCase)) continue;
                return f;
            }
        }
        catch { }

        return null;
    }

    private static string SanitizeForFilename(string s)
    {
        Span<char> buf = stackalloc char[s.Length];
        int n = 0;
        foreach (char c in s)
        {
            if (char.IsLetterOrDigit(c)) buf[n++] = c;
            else if (n > 0 && buf[n - 1] != ' ') buf[n++] = ' ';
        }
        return new string(buf[..n]).Trim();
    }
}

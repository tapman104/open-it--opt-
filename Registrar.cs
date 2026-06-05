using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OpenWithApp;

/// <summary>
/// Writes / removes the three "static verb" registry entries the shell uses
/// to add a custom item to the Explorer right-click menu. We never touch
/// HKLM; HKCU works for the current user with no UAC needed -- but if the
/// caller asked for an all-users install we self-elevate.
/// </summary>
internal static class Registrar
{
    // The subkey name that lives under each *\shell parent.
    // Has to be a stable identifier; the menu text comes from the (default) value.
    private const string VerbKey = Program.AppName;

    // The three Explorer "parents" we hook.
    private static readonly string[] FileParents =
    {
        @"Software\Classes\*\shell",
        @"Software\Classes\Directory\shell",
        @"Software\Classes\Directory\Background\shell",
    };

    public static int RunInstall()
    {
        if (!EnsureElevated("--register"))
            return 0; // we relaunched elevated; the new instance will install

        try
        {
            string exe = GetExePath();
            string menuText = Program.MenuText;
            string iconRef = "\"" + exe + "\",0";

            foreach (string parent in FileParents)
            {
                bool isBackground = parent.EndsWith(@"Background\shell",
                    StringComparison.OrdinalIgnoreCase);

                // %V = current folder path for Directory\Background
                // %1 = the clicked file or folder for the others
                string argToken = isBackground ? "\"%V\"" : "\"%1\"";
                string command = $"\"{exe}\" --open {argToken}";

                using RegistryKey verb = Registry.LocalMachine.CreateSubKey(parent + "\\" + VerbKey)
                    ?? throw new InvalidOperationException("Could not create " + parent);

                verb.SetValue(null, menuText, RegistryValueKind.String);
                verb.SetValue("Icon", iconRef, RegistryValueKind.String);
                // Optional eye candy: show in the "extended" shift-click submenu? no.
                // verb.SetValue("Extended", "", RegistryValueKind.String);

                using RegistryKey cmd = verb.CreateSubKey("command")
                    ?? throw new InvalidOperationException("Could not create command subkey");
                cmd.SetValue(null, command, RegistryValueKind.String);
            }

            MessageBox.Show(
                $"Installed.\n\nRight-click any file or folder to see\n  \"{menuText}\"",
                Program.MenuText,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            MessageBox.Show(
                "Registry access denied. Try running as administrator.",
                Program.MenuText,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 5;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Install failed:\n\n" + ex.Message,
                Program.MenuText,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }

    public static int RunUninstall()
    {
        if (!EnsureElevated("--unregister"))
            return 0;

        try
        {
            foreach (string parent in FileParents)
            {
                try
                {
                    Registry.LocalMachine.DeleteSubKeyTree(parent + "\\" + VerbKey, throwOnMissingSubKey: false);
                }
                catch (ArgumentException)
                {
                    // missing subkey - fine
                }
            }

            MessageBox.Show(
                "Uninstalled.",
                Program.MenuText,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            MessageBox.Show(
                "Registry access denied. Try running as administrator.",
                Program.MenuText,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 5;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Uninstall failed:\n\n" + ex.Message,
                Program.MenuText,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }

    /// <summary>
    /// True if the *.exe verb is currently registered (for any one of the parents).
    /// </summary>
    public static bool IsInstalled()
    {
        try
        {
            foreach (string parent in FileParents)
            {
                using RegistryKey? k = Registry.LocalMachine.OpenSubKey(parent + "\\" + VerbKey, writable: false);
                if (k != null) return true;
            }
        }
        catch { /* ignore */ }
        return false;
    }

    // ---- elevation helpers ------------------------------------------------

    private static bool IsAdmin()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(id);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns true if we're already elevated. Otherwise launches a new copy
    /// of this exe with the UAC "runas" verb and returns false, signalling
    /// that the caller should bail out.
    /// </summary>
    private static bool EnsureElevated(string verbArg)
    {
        if (IsAdmin()) return true;

        var psi = new ProcessStartInfo
        {
            FileName = GetExePath(),
            Arguments = verbArg,
            UseShellExecute = true,
            Verb = "runas",
        };

        try
        {
            Process.Start(psi);
        }
        catch (Win32Exception wex) when (wex.NativeErrorCode == 1223 /* ERROR_CANCELLED */)
        {
            MessageBox.Show(
                "Administrator privileges are required to modify the\n" +
                "system context menu. Operation cancelled.",
                Program.MenuText,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Could not request elevation:\n\n" + ex.Message,
                Program.MenuText,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        return false;
    }

    private static string GetExePath()
    {
        // Environment.ProcessPath is AOT-safe and returns the true exe path
        // for single-file / NativeAOT executables.
        string? path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            throw new InvalidOperationException("Cannot determine own executable path.");
        return path;
    }
}

using System;
using System.Drawing;
using System.IO;

namespace OpenWithApp;

/// <summary>
/// Pulls a small (~16px) icon out of an EXE/DLL/ICO using Shell32.
/// Always returns a managed <see cref="Bitmap"/> (never null), and always
/// frees the underlying HICON so we don't leak GDI handles.
/// </summary>
internal static class IconExtractor
{
    /// <summary>
    /// Returns a small icon (16x16-ish) for the given executable file.
    /// Honors an optional "path,index" form (as found in DisplayIcon values).
    /// Returns a generic fallback bitmap if anything fails. Never throws.
    /// </summary>
    public static Bitmap GetSmallIcon(string exePath, string? displayIconHint = null)
    {
        // Resolve actual file + index.
        string file = exePath;
        int    index = 0;
        TryParseIconRef(displayIconHint, ref file, ref index);

        if (string.IsNullOrEmpty(file) || !File.Exists(file))
            file = exePath; // fall back to the resolved exe

        // 1) Try ExtractIconExW for the small icon at the requested index.
        var bmp = ExtractViaExtractIconEx(file, index, large: false);
        if (bmp != null) return bmp;

        // 2) Same with the large icon (we'll let WinForms downscale).
        bmp = ExtractViaExtractIconEx(file, index, large: true);
        if (bmp != null) return bmp;

        // 3) Fall back to SHGetFileInfo, which asks the shell for the
        //    associated icon (works even when the EXE has no embedded icon).
        bmp = ExtractViaSHGetFileInfo(File.Exists(exePath) ? exePath : file);
        if (bmp != null) return bmp;

        // 4) Last-ditch placeholder so the picker never shows null rows.
        return BuildPlaceholder();
    }

    // ----------------------------------------------------------------------

    private static Bitmap? ExtractViaExtractIconEx(string file, int index, bool large)
    {
        IntPtr[] big   = new IntPtr[1];
        IntPtr[] small = new IntPtr[1];
        big[0] = small[0] = IntPtr.Zero;

        try
        {
            int n = NativeMethods.ExtractIconExW(
                file, index,
                large ? big : null,
                large ? null : small,
                1);
            if (n <= 0) return null;

            IntPtr h = large ? big[0] : small[0];
            return HIconToBitmap(h);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (big[0]   != IntPtr.Zero) NativeMethods.DestroyIcon(big[0]);
            if (small[0] != IntPtr.Zero) NativeMethods.DestroyIcon(small[0]);
        }
    }

    private static Bitmap? ExtractViaSHGetFileInfo(string file)
    {
        var info = default(NativeMethods.SHFILEINFOW);
        IntPtr hr = NativeMethods.SHGetFileInfoW(
            file, 0, ref info,
            (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.SHFILEINFOW>(),
            NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_SMALLICON);

        if (hr == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;

        try { return HIconToBitmap(info.hIcon); }
        finally { NativeMethods.DestroyIcon(info.hIcon); }
    }

    private static Bitmap? HIconToBitmap(IntPtr hIcon)
    {
        if (hIcon == IntPtr.Zero) return null;
        try
        {
            // Icon.FromHandle does NOT take ownership of the handle, so we
            // can DestroyIcon it after copying into a managed bitmap.
            using var icon = Icon.FromHandle(hIcon);
            return icon.ToBitmap();
        }
        catch
        {
            return null;
        }
    }

    private static void TryParseIconRef(string? raw, ref string file, ref int index)
    {
        if (string.IsNullOrWhiteSpace(raw)) return;
        string s = raw!.Trim().Trim('"');

        int comma = s.LastIndexOf(',');
        if (comma > 0 && comma < s.Length - 1)
        {
            string tail = s.Substring(comma + 1).Trim();
            if (int.TryParse(tail, out int idx))
            {
                file  = s.Substring(0, comma).Trim().Trim('"');
                index = idx;
                return;
            }
        }
        file = s;
    }

    private static Bitmap BuildPlaceholder()
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        using var p = new Pen(Color.Gray);
        g.DrawRectangle(p, 2, 1, 11, 13);
        g.DrawLine(p, 4, 4, 11, 4);
        g.DrawLine(p, 4, 7, 11, 7);
        g.DrawLine(p, 4, 10, 11, 10);
        return bmp;
    }
}

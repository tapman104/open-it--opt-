using System;

namespace OpenWithApp;

/// <summary>
/// One installed application as discovered from the Uninstall registry hives.
/// </summary>
internal sealed class InstalledApp
{
    public required string DisplayName { get; init; }
    public required string ExecutablePath { get; init; }
    public string? Publisher { get; init; }
    public string? InstallLocation { get; init; }
    /// <summary>Raw value from the DisplayIcon registry value, may include ",index".</summary>
    public string? DisplayIcon { get; init; }
    /// <summary>The index assigned in the picker's ImageList, -1 if no icon.</summary>
    public int ImageIndex { get; set; } = -1;

    public override string ToString() => DisplayName;
}

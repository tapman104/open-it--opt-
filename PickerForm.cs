using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OpenWithApp;

/// <summary>
/// The user-facing picker. Opens with the target path baked in, lists every
/// installed app discovered via <see cref="AppEnumerator"/>, lets the user
/// type to filter, and launches the chosen app with the target path as its
/// first argument.
///
/// Keyboard:
///   Esc            close
///   Enter          launch highlighted entry
///   Up / Down      move selection (works from inside the search box too)
///   PgUp / PgDn    page through
///   Double-click   launch
/// </summary>
internal sealed class PickerForm : Form
{
    private readonly string _targetPath;

    private readonly TextBox      _searchBox;
    private readonly ListView     _listView;
    private readonly ImageList    _icons;
    private readonly Label        _targetLabel;
    private readonly Button       _openButton;
    private readonly Button       _cancelButton;

    private List<InstalledApp> _allApps      = new();
    private List<InstalledApp> _filteredApps = new();

    public PickerForm(string targetPath)
    {
        _targetPath = targetPath;

        // ---- form chrome ------------------------------------------------
        Text            = Program.MenuText;
        StartPosition   = FormStartPosition.CenterScreen;
        ClientSize      = new Size(640, 520);
        MinimumSize     = new Size(420, 320);
        ShowInTaskbar   = true;
        KeyPreview      = true;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox     = false;
        MaximizeBox     = true;
        Icon            = SystemIcons.Application;
        BackColor       = SystemColors.Window;
        Font            = new Font("Segoe UI", 9f);

        // ---- icons image list -------------------------------------------
        _icons = new ImageList
        {
            ImageSize  = new Size(16, 16),
            ColorDepth = ColorDepth.Depth32Bit,
        };

        // ---- search box --------------------------------------------------
        var searchPanel = new Panel
        {
            Dock    = DockStyle.Top,
            Height  = 38,
            Padding = new Padding(8, 8, 8, 4),
        };
        _searchBox = new TextBox
        {
            Dock         = DockStyle.Fill,
            BorderStyle  = BorderStyle.FixedSingle,
            PlaceholderText = "Search installed applications...",
        };
        _searchBox.TextChanged += (_, _) => ApplyFilter();
        _searchBox.KeyDown     += SearchBox_KeyDown;
        searchPanel.Controls.Add(_searchBox);

        // ---- bottom button row ------------------------------------------
        var buttonPanel = new Panel
        {
            Dock    = DockStyle.Bottom,
            Height  = 44,
            Padding = new Padding(8, 6, 8, 8),
        };
        _openButton = new Button
        {
            Text     = "Open",
            Width    = 90,
            Height   = 28,
            Anchor   = AnchorStyles.Right | AnchorStyles.Top,
            Location = new Point(buttonPanel.ClientSize.Width - 188, 6),
        };
        _cancelButton = new Button
        {
            Text     = "Cancel",
            Width    = 90,
            Height   = 28,
            Anchor   = AnchorStyles.Right | AnchorStyles.Top,
            Location = new Point(buttonPanel.ClientSize.Width - 92, 6),
        };
        _openButton.Click   += (_, _) => LaunchSelected();
        _cancelButton.Click += (_, _) => Close();
        buttonPanel.Controls.Add(_openButton);
        buttonPanel.Controls.Add(_cancelButton);
        AcceptButton = _openButton;
        CancelButton = _cancelButton;

        // ---- target path display ----------------------------------------
        _targetLabel = new Label
        {
            Dock       = DockStyle.Bottom,
            Height     = 26,
            Padding    = new Padding(10, 4, 10, 4),
            TextAlign  = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            ForeColor  = SystemColors.GrayText,
            Text       = "Target: " + targetPath,
        };

        // ---- list view ---------------------------------------------------
        _listView = new ListView
        {
            Dock           = DockStyle.Fill,
            View           = View.Details,
            FullRowSelect  = true,
            HideSelection  = false,
            MultiSelect    = false,
            GridLines      = false,
            VirtualMode    = true,
            SmallImageList = _icons,
            HeaderStyle    = ColumnHeaderStyle.Nonclickable,
            BorderStyle    = BorderStyle.None,
        };
        _listView.Columns.Add("Application", 360);
        _listView.Columns.Add("Publisher",   220);
        _listView.RetrieveVirtualItem += ListView_RetrieveVirtualItem;
        _listView.DoubleClick         += (_, _) => LaunchSelected();
        _listView.KeyDown             += ListView_KeyDown;

        // ---- compose -----------------------------------------------------
        Controls.Add(_listView);     // fill
        Controls.Add(_targetLabel);  // bottom (1)
        Controls.Add(buttonPanel);   // bottom (2)
        Controls.Add(searchPanel);   // top

        Load   += PickerForm_Load;
        Shown  += (_, _) => _searchBox.Focus();
    }

    // ----------------------------------------------------------------------

    private void PickerForm_Load(object? sender, EventArgs e)
    {
        Cursor = Cursors.WaitCursor;
        try
        {
            _allApps      = AppEnumerator.Enumerate();
            _filteredApps = new List<InstalledApp>(_allApps);
            _listView.VirtualListSize = _filteredApps.Count;

            if (_filteredApps.Count > 0)
                _listView.SelectedIndices.Add(0);

            _targetLabel.Text = $"Target ({_allApps.Count} apps found): {_targetPath}";
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        // Icons load asynchronously so the form stays responsive.
        StartBackgroundIconLoad();
    }

    private void StartBackgroundIconLoad()
    {
        var snapshot = _allApps.ToArray();
        Task.Run(() =>
        {
            const int BatchSize = 16;
            var batch = new List<(InstalledApp app, Bitmap bmp)>(BatchSize);

            foreach (var app in snapshot)
            {
                if (IsDisposed || Disposing) return;

                Bitmap? bmp = null;
                try { bmp = IconExtractor.GetSmallIcon(app.ExecutablePath, app.DisplayIcon); }
                catch { /* never let an app's bad icon kill loading */ }

                if (bmp != null)
                    batch.Add((app, bmp));

                if (batch.Count >= BatchSize)
                    FlushBatch(batch);
            }

            if (batch.Count > 0)
                FlushBatch(batch);
        });
    }

    private void FlushBatch(List<(InstalledApp app, Bitmap bmp)> batch)
    {
        var copy = batch.ToArray();
        batch.Clear();

        try
        {
            if (IsDisposed || Disposing) return;
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed || Disposing) return;
                foreach (var (app, bmp) in copy)
                {
                    int idx = _icons.Images.Count;
                    _icons.Images.Add(bmp);
                    app.ImageIndex = idx;
                }
                if (_filteredApps.Count > 0)
                {
                    _listView.RedrawItems(0,
                        Math.Max(0, _filteredApps.Count - 1),
                        invalidateOnly: false);
                }
            }));
        }
        catch (InvalidOperationException)
        {
            // form was closed mid-flight
        }
    }

    // ----------------------------------------------------------------------

    private void ListView_RetrieveVirtualItem(object? sender, RetrieveVirtualItemEventArgs e)
    {
        if (e.ItemIndex < 0 || e.ItemIndex >= _filteredApps.Count)
        {
            e.Item = new ListViewItem("");
            return;
        }

        var app = _filteredApps[e.ItemIndex];
        var item = new ListViewItem(app.DisplayName)
        {
            ImageIndex = app.ImageIndex, // -1 if not yet loaded
            Tag        = app,
        };
        item.SubItems.Add(app.Publisher ?? "");
        e.Item = item;
    }

    private void ApplyFilter()
    {
        string q = (_searchBox.Text ?? "").Trim();

        _filteredApps = q.Length == 0
            ? new List<InstalledApp>(_allApps)
            : FilterByQuery(_allApps, q);

        _listView.VirtualListSize = _filteredApps.Count;
        if (_filteredApps.Count > 0)
        {
            _listView.SelectedIndices.Clear();
            _listView.SelectedIndices.Add(0);
            _listView.EnsureVisible(0);
        }
        _listView.Invalidate();
    }

    private static List<InstalledApp> FilterByQuery(List<InstalledApp> source, string query)
    {
        var hits = new List<InstalledApp>(Math.Min(source.Count, 64));
        foreach (var app in source)
        {
            if (Contains(app.DisplayName, query) ||
                (app.Publisher != null && Contains(app.Publisher, query)))
                hits.Add(app);
        }
        return hits;
    }

    private static bool Contains(string haystack, string needle)
        => haystack.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) >= 0;

    // ----------------------------------------------------------------------

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Down:
                MoveSelection(+1);
                e.Handled = e.SuppressKeyPress = true;
                break;
            case Keys.Up:
                MoveSelection(-1);
                e.Handled = e.SuppressKeyPress = true;
                break;
            case Keys.PageDown:
                MoveSelection(+10);
                e.Handled = e.SuppressKeyPress = true;
                break;
            case Keys.PageUp:
                MoveSelection(-10);
                e.Handled = e.SuppressKeyPress = true;
                break;
            case Keys.Enter:
                LaunchSelected();
                e.Handled = e.SuppressKeyPress = true;
                break;
            case Keys.Escape:
                Close();
                e.Handled = e.SuppressKeyPress = true;
                break;
        }
    }

    private void ListView_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            LaunchSelected();
            e.Handled = e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            Close();
            e.Handled = e.SuppressKeyPress = true;
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Form-wide Escape fallback (covers child controls without their own handler).
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void MoveSelection(int delta)
    {
        int count = _filteredApps.Count;
        if (count == 0) return;

        int cur = _listView.SelectedIndices.Count > 0
            ? _listView.SelectedIndices[0]
            : -1;

        int next = cur < 0 ? 0 : Math.Clamp(cur + delta, 0, count - 1);

        _listView.SelectedIndices.Clear();
        _listView.SelectedIndices.Add(next);
        _listView.EnsureVisible(next);
    }

    // ----------------------------------------------------------------------

    private void LaunchSelected()
    {
        if (_listView.SelectedIndices.Count == 0)
        {
            if (_filteredApps.Count == 0) return;
            _listView.SelectedIndices.Add(0);
        }

        int idx = _listView.SelectedIndices[0];
        if (idx < 0 || idx >= _filteredApps.Count) return;

        var app = _filteredApps[idx];
        if (!File.Exists(app.ExecutablePath))
        {
            MessageBox.Show(this,
                "The selected application no longer exists at:\n\n" + app.ExecutablePath,
                Program.MenuText,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName         = app.ExecutablePath,
                UseShellExecute  = false,
                WorkingDirectory = Path.GetDirectoryName(app.ExecutablePath) ?? "",
            };
            psi.ArgumentList.Add(_targetPath);
            Process.Start(psi);
            Close();
        }
        catch (Win32Exception wex) when (wex.NativeErrorCode == 1223)
        {
            // user cancelled UAC from the launched app -- not our problem
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Could not start the application:\n\n" + ex.Message,
                Program.MenuText,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    // ----------------------------------------------------------------------

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _icons?.Dispose();
        }
        base.Dispose(disposing);
    }
}

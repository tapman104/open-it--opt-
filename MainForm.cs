using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace OpenWithApp;

/// <summary>
/// Small manager UI shown when the EXE is launched with no arguments.
/// Lets the user install, uninstall, or test the context-menu entry.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly Label   _statusLabel;
    private readonly Button  _installButton;
    private readonly Button  _uninstallButton;
    private readonly Button  _testButton;
    private readonly Button  _closeButton;
    private readonly Label   _exePathLabel;

    public MainForm()
    {
        Text            = Program.MenuText + " - Setup";
        StartPosition   = FormStartPosition.CenterScreen;
        ClientSize      = new Size(520, 280);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        ShowInTaskbar   = true;
        Font            = new Font("Segoe UI", 9f);
        KeyPreview      = true;
        Icon            = SystemIcons.Application;

        var header = new Label
        {
            Text     = Program.MenuText,
            Font     = new Font("Segoe UI Semibold", 14f),
            AutoSize = false,
            Location = new Point(20, 18),
            Size     = new Size(480, 28),
        };

        var blurb = new Label
        {
            Text =
                "Adds an \"Open With (OpenWithApp)\" entry to the right-click menu of\n" +
                "files, folders, and folder backgrounds. Selecting it shows a searchable\n" +
                "picker of installed applications.",
            Location = new Point(20, 52),
            Size     = new Size(480, 56),
            ForeColor = SystemColors.GrayText,
        };

        _exePathLabel = new Label
        {
            Text = "Registered to: " + (Environment.ProcessPath ?? "<unknown>"),
            Location  = new Point(20, 116),
            Size      = new Size(480, 18),
            AutoEllipsis = true,
            ForeColor = SystemColors.GrayText,
        };

        _statusLabel = new Label
        {
            Location  = new Point(20, 142),
            Size      = new Size(480, 22),
            Font      = new Font("Segoe UI Semibold", 9f),
        };

        int btnY = 184;
        int btnW = 116;
        int btnH = 30;
        int gap  = 8;

        _installButton = new Button
        {
            Text     = "&Install",
            Location = new Point(20, btnY),
            Size     = new Size(btnW, btnH),
        };
        _uninstallButton = new Button
        {
            Text     = "&Uninstall",
            Location = new Point(20 + (btnW + gap), btnY),
            Size     = new Size(btnW, btnH),
        };
        _testButton = new Button
        {
            Text     = "&Test Picker",
            Location = new Point(20 + 2 * (btnW + gap), btnY),
            Size     = new Size(btnW, btnH),
        };
        _closeButton = new Button
        {
            Text     = "&Close",
            Location = new Point(20 + 3 * (btnW + gap), btnY),
            Size     = new Size(btnW, btnH),
            DialogResult = DialogResult.Cancel,
        };

        _installButton.Click   += OnInstallClicked;
        _uninstallButton.Click += OnUninstallClicked;
        _testButton.Click      += OnTestClicked;
        _closeButton.Click     += (_, _) => Close();

        CancelButton = _closeButton;

        Controls.AddRange(new Control[]
        {
            header, blurb, _exePathLabel, _statusLabel,
            _installButton, _uninstallButton, _testButton, _closeButton,
        });

        Load += (_, _) => RefreshStatus();
    }

    private void RefreshStatus()
    {
        bool installed = Registrar.IsInstalled();
        if (installed)
        {
            _statusLabel.Text      = "Status: INSTALLED (system-wide)";
            _statusLabel.ForeColor = Color.ForestGreen;
            _installButton.Enabled = false;
            _uninstallButton.Enabled = true;
        }
        else
        {
            _statusLabel.Text      = "Status: not installed";
            _statusLabel.ForeColor = Color.Firebrick;
            _installButton.Enabled = true;
            _uninstallButton.Enabled = false;
        }
    }

    private void OnInstallClicked(object? sender, EventArgs e)
    {
        Registrar.RunInstall();
        // Whether install ran in-process or relaunched elevated, the user
        // sees feedback there. We just re-poll for status.
        RefreshStatus();
    }

    private void OnUninstallClicked(object? sender, EventArgs e)
    {
        Registrar.RunUninstall();
        RefreshStatus();
    }

    private void OnTestClicked(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title           = "Pick any file to test the picker",
            CheckFileExists = true,
            Multiselect     = false,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        using var picker = new PickerForm(dlg.FileName);
        picker.ShowDialog(this);
    }
}

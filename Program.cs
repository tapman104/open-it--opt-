using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace OpenWithApp;

internal static class Program
{
    public const string AppName = "OpenWithApp";
    public const string MenuText = "Open With (OpenWithApp)";

    [STAThread]
    private static int Main(string[] args)
    {
        // Single global exception net so a stray error never silently crashes
        // the shell-invoked process.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            FatalError(e.ExceptionObject as Exception);
        Application.ThreadException += (_, e) => FatalError(e.Exception);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        try
        {
            return Dispatch(args);
        }
        catch (Exception ex)
        {
            FatalError(ex);
            return 1;
        }
    }

    private static int Dispatch(string[] args)
    {
        // Normalize: collapse anything starting with '/' or '-' into a verb.
        string verb = (args.Length > 0 ? args[0] : "").TrimStart('-', '/').ToLowerInvariant();

        switch (verb)
        {
            case "register":
            case "install":
                return Registrar.RunInstall();

            case "unregister":
            case "uninstall":
                return Registrar.RunUninstall();

            case "open":
                // args[1] is the target path passed by the shell.
                string? target = args.Length > 1 ? args[1] : null;
                return RunPicker(target);

            case "":
            case "help":
            case "?":
                return RunManager();

            default:
                // Unknown verb -> treat the entire first arg as a target path.
                // This lets users drop a file onto the EXE too.
                return RunPicker(args[0]);
        }
    }

    private static int RunPicker(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            MessageBox.Show(
                "No file or folder path was provided.",
                MenuText,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return 2;
        }

        // Strip surrounding quotes the shell sometimes leaves in.
        targetPath = targetPath.Trim().Trim('"');

        if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
        {
            MessageBox.Show(
                $"The path does not exist:\n\n{targetPath}",
                MenuText,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return 3;
        }

        ApplicationConfiguration.Initialize();
        using var form = new PickerForm(targetPath);
        Application.Run(form);
        return 0;
    }

    private static int RunManager()
    {
        ApplicationConfiguration.Initialize();
        using var form = new MainForm();
        Application.Run(form);
        return 0;
    }

    private static void FatalError(Exception? ex)
    {
        try
        {
            string msg = ex?.ToString() ?? "Unknown error.";
            MessageBox.Show(msg, MenuText + " - Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch
        {
            // last-resort: nothing we can do.
        }
    }
}

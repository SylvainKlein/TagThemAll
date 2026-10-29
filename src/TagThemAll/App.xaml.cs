using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace TagThemAll;

public partial class App : Application
{
    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appID);

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            SetCurrentProcessExplicitAppUserModelID("TagThemAll.UniversalFileTagger");
        }
        catch { }

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            LogCrash("AppDomain", args.ExceptionObject as Exception);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            LogCrash("Dispatcher", args.Exception);
            args.Handled = true; // prevent silent exit
            MessageBox.Show($"Erreur: {args.Exception.Message}\n\n{args.Exception.StackTrace}", "Erreur TagThemAll", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        base.OnStartup(e);
    }

    private static void LogCrash(string source, Exception? ex)
    {
        try
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string logFile = Path.Combine(appDir, "crash.log");
            File.WriteAllText(logFile, $"[{DateTime.UtcNow}] Source: {source}\nException: {ex}\nInner: {ex?.InnerException}\nStack: {ex?.StackTrace}");
        }
        catch { }
    }
}

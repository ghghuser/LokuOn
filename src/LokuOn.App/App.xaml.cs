using System.IO;
using System.Text;
using System.Threading;
using System.Windows;

namespace LokuOn.App;

public partial class App : Application
{
    private Mutex? singleInstance;
    private bool crashDialogShown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        singleInstance = new Mutex(true, "LokuOn.SingleInstance.7c1f2a", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("LokuOn はすでに起動しています。\nタスクトレイや他のウィンドウの後ろを確認してください。",
                "LokuOn", MessageBoxButton.OK, MessageBoxImage.Information);
            singleInstance = null;
            Shutdown();
            return;
        }

        // 画面の操作で例外が起きても、録音(別スレッド)は止めずに続ける
        DispatcherUnhandledException += (s, ex) =>
        {
            WriteCrashLog("UI: " + ex.Exception);
            ex.Handled = true;
            if (!crashDialogShown)
            {
                crashDialogShown = true;
                MessageBox.Show("画面の処理でエラーが発生しました。録音は続行されています。\n\n" + ex.Exception.Message +
                                "\n\n詳細は lokuon-crash.log に記録しました。",
                    "LokuOn", MessageBoxButton.OK, MessageBoxImage.Warning);
                crashDialogShown = false;
            }
        };
        AppDomain.CurrentDomain.UnhandledException += (s, ex) => WriteCrashLog("AppDomain: " + ex.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (s, ex) => { WriteCrashLog("Task: " + ex.Exception); ex.SetObserved(); };

        var win = new MainWindow();
        MainWindow = win;
        win.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { singleInstance?.ReleaseMutex(); } catch { }
        singleInstance?.Dispose();
        base.OnExit(e);
    }

    public static void WriteCrashLog(string text)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}{Environment.NewLine}";
        foreach (var dir in new[] { AppContext.BaseDirectory,
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LokuOn") })
        {
            try
            {
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "lokuon-crash.log"), line, new UTF8Encoding(false));
                return;
            }
            catch { }
        }
    }
}

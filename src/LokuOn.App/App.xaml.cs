using LokuOn.Core;
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
        // 画面の文言を最初に読み込む(言語は settings.ini の Language。画面を作る前に決める必要がある)
        LokuOn.Core.Loc.Init(null, LokuOn.Core.SettingsStore.PeekLanguage(Path.Combine(AppContext.BaseDirectory, "settings.ini")));

        // 多重起動の判定は「同じフォルダのexe」だけ(別のフォルダにコピーした LokuOn は同時に起動できる)
        string dirKey = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\', '/').ToLowerInvariant();
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(dirKey)))[..16];
        singleInstance = new Mutex(true, "LokuOn.SingleInstance." + hash, out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(Loc.Get("app.01"),
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
                MessageBox.Show(Loc.Get("app.02", ex.Exception.Message),
                    "LokuOn", MessageBoxButton.OK, MessageBoxImage.Warning);
                crashDialogShown = false;
            }
        };
        AppDomain.CurrentDomain.UnhandledException += (s, ex) => WriteCrashLog("AppDomain: " + ex.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (s, ex) => { WriteCrashLog("Task: " + ex.Exception); ex.SetObserved(); };

        try
        {
            var win = new MainWindow();
            MainWindow = win;
            win.Show();
        }
        catch (Exception ex)
        {
            // 起動に失敗したときは、記録して、プロセスを残さず終了する(ウィンドウの無いまま常駐させない)
            WriteCrashLog("起動失敗: " + ex);
            MessageBox.Show(Loc.Get("app.03", ex.Message),
                "LokuOn", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
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

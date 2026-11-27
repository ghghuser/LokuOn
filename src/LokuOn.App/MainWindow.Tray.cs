using LokuOn.Core;
using System.Windows;
using System.Windows.Interop;
using LokuOn.Audio;

namespace LokuOn.App;

// タスクトレイ(最小化録音)とグローバルホットキー
public partial class MainWindow
{
    private const int HotkeyIdStartStop = 0x5201;
    private const int HotkeyIdSplit = 0x5202;
    private const int HotkeyIdPause = 0x5203;
    private static string HotkeyHintText => Loc.Get("tray.01");

    private IntPtr hwnd = IntPtr.Zero;
    private HwndSource? hwndSource;
    private bool startStopRegistered;
    private bool splitRegistered;
    private bool pauseRegistered;

    private System.Windows.Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIconIdle;
    private System.Drawing.Icon? trayIconRec;
    private string lastTrayText = "\u0001";   // まだ何も設定していない印

    // ---------------------------------------------------------------- ウィンドウの状態

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        hwnd = new WindowInteropHelper(this).Handle;
        hwndSource = HwndSource.FromHwnd(hwnd);
        hwndSource?.AddHook(WndProc);
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && ctl.Settings.MinimizeToTray)
        {
            EnsureTray();
            Hide();      // タスクバーから消えて、トレイのアイコンだけが残る
        }
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    // ---------------------------------------------------------------- タスクトレイ

    private static System.Drawing.Icon MakeDot(System.Drawing.Color fill)
    {
        using var bmp = new System.Drawing.Bitmap(32, 32);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);
            using var back = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(40, 44, 52));
            g.FillEllipse(back, 1, 1, 30, 30);
            using var dot = new System.Drawing.SolidBrush(fill);
            g.FillEllipse(dot, 7, 7, 18, 18);
        }
        IntPtr h = bmp.GetHicon();
        try { return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(h).Clone(); }
        finally { DestroyIconNative(h); }
    }

    private static System.Drawing.Icon? LoadAppIcon()
    {
        try
        {
            string? path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path)) return System.Drawing.Icon.ExtractAssociatedIcon(path);
        }
        catch { }
        return null;
    }

    /// <summary>アプリのアイコンの右下に赤い点を付けた、録音中用のアイコン。</summary>
    private static System.Drawing.Icon MakeRecordingIcon(System.Drawing.Icon baseIcon)
    {
        try
        {
            using var bmp = new System.Drawing.Bitmap(32, 32);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.Transparent);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.DrawIcon(baseIcon, new System.Drawing.Rectangle(0, 0, 32, 32));
                using var white = new System.Drawing.SolidBrush(System.Drawing.Color.White);
                using var red = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(235, 40, 40));
                g.FillEllipse(white, 15, 15, 17, 17);
                g.FillEllipse(red, 17, 17, 13, 13);
            }
            IntPtr h = bmp.GetHicon();
            try { return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(h).Clone(); }
            finally { DestroyIconNative(h); }
        }
        catch { return MakeDot(System.Drawing.Color.FromArgb(235, 60, 50)); }
    }

    [System.Runtime.InteropServices.LibraryImport("user32.dll", EntryPoint = "DestroyIcon")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool DestroyIconNative(IntPtr handle);

    private void EnsureTray()
    {
        if (tray != null) return;
        try
        {
            // 待機中は通常のアプリのアイコン、録音中はそれに赤い点を付けたアイコン
            trayIconIdle = LoadAppIcon() ?? MakeDot(System.Drawing.Color.FromArgb(120, 170, 230));
            trayIconRec = MakeRecordingIcon(trayIconIdle);

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add(Loc.Get("tray.02"), null, (s, e) => ShowFromTray());
            menu.Items.Add(Loc.Get("tray.03"), null, (s, e) => OnRecordClick(this, new RoutedEventArgs()));
            menu.Items.Add(Loc.Get("tray.04"), null, (s, e) => { if (ctl.IsRecording) { ctl.TogglePauseRecording(); UpdateButtons(); } });
            menu.Items.Add(Loc.Get("tray.05"), null, (s, e) => { if (ctl.IsRecording) OnSplitClick(this, new RoutedEventArgs()); });
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add(Loc.Get("tray.06"), null, (s, e) => { ShowFromTray(); Close(); });

            tray = new System.Windows.Forms.NotifyIcon
            {
                Icon = trayIconIdle,
                Text = "LokuOn",
                ContextMenuStrip = menu,
                Visible = true,
            };
            tray.DoubleClick += (s, e) => ShowFromTray();
            lastTrayText = "\u0001";
        }
        catch (Exception ex) { App.WriteCrashLog("トレイ: " + ex); tray = null; }
    }

    /// <summary>トレイのアイコンと説明文を更新する。recordingText が null なら待機中の表示。</summary>
    private void SetTray(string? recordingText)
    {
        try
        {
            if (!ctl.Settings.MinimizeToTray)
            {
                if (tray != null) tray.Visible = false;
                lastTrayText = "\u0001";
                return;
            }
            EnsureTray();
            if (tray == null) return;
            tray.Visible = true;

            string text = "LokuOn  " + (recordingText ?? Loc.Get("ui.14")) + (ctl.AnyOutputRunning ? Loc.Get("tray.07") : "");
            if (text.Length > 60) text = text.Substring(0, 60);
            if (text == lastTrayText) return;
            lastTrayText = text;
            tray.Text = text;
            tray.Icon = recordingText != null && recordingText.StartsWith(Loc.Get("tray.08")) ? trayIconRec : trayIconIdle;
        }
        catch { }
    }

    private void DisposeTray()
    {
        if (tray != null)
        {
            try { tray.Visible = false; tray.Dispose(); } catch { }
            tray = null;
        }
        try { trayIconIdle?.Dispose(); } catch { }
        try { trayIconRec?.Dispose(); } catch { }
        trayIconIdle = null;
        trayIconRec = null;
    }

    // ---------------------------------------------------------------- グローバルホットキー

    private void RegisterHotkeys()
    {
        UnregisterHotkeys();
        if (hwnd == IntPtr.Zero) return;
        var s = ctl.Settings;
        if (!s.HotkeysEnabled) { HotkeyStatus.Text = HotkeyHintText; return; }

        startStopRegistered = GlobalHotkeys.Register(hwnd, HotkeyIdStartStop, s.HotkeyStartStop);
        splitRegistered = GlobalHotkeys.Register(hwnd, HotkeyIdSplit, s.HotkeySplit);
        pauseRegistered = GlobalHotkeys.Register(hwnd, HotkeyIdPause, s.HotkeyPause);

        var parts = new List<string>();
        parts.Add(startStopRegistered ? Loc.Get("tray.09", s.HotkeyStartStop) : Loc.Get("tray.10", s.HotkeyStartStop));
        parts.Add(splitRegistered ? Loc.Get("tray.11", s.HotkeySplit) : Loc.Get("tray.12", s.HotkeySplit));
        parts.Add(pauseRegistered ? Loc.Get("tray.13", s.HotkeyPause) : Loc.Get("tray.14", s.HotkeyPause));
        string note = (startStopRegistered && splitRegistered && pauseRegistered) ? "" : Loc.Get("tray.15");
        HotkeyStatus.Text = string.Join(" / ", parts) + note;
    }

    private void UnregisterHotkeys()
    {
        if (hwnd == IntPtr.Zero) return;
        if (startStopRegistered) GlobalHotkeys.Unregister(hwnd, HotkeyIdStartStop);
        if (splitRegistered) GlobalHotkeys.Unregister(hwnd, HotkeyIdSplit);
        if (pauseRegistered) GlobalHotkeys.Unregister(hwnd, HotkeyIdPause);
        startStopRegistered = false;
        splitRegistered = false;
        pauseRegistered = false;
    }

    private IntPtr WndProc(IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == GlobalHotkeys.WmHotkey)
        {
            int id = wParam.ToInt32();
            if (id == HotkeyIdStartStop)
            {
                handled = true;
                OnRecordClick(this, new RoutedEventArgs());
            }
            else if (id == HotkeyIdSplit)
            {
                handled = true;
                if (ctl.IsRecording && ctl.EngineState == LokuOn.Core.EngineState.Recording) OnSplitClick(this, new RoutedEventArgs());
            }
            else if (id == HotkeyIdPause)
            {
                handled = true;
                // ボタンと同じ条件(録音中)のときだけ、一時停止/再開を切り替える
                if (ctl.IsRecording && ctl.EngineState == LokuOn.Core.EngineState.Recording) { ctl.TogglePauseRecording(); UpdateButtons(); }
            }
        }
        return IntPtr.Zero;
    }
}

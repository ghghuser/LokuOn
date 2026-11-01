using System.Windows;
using System.Windows.Interop;
using Rokuon.Audio;

namespace Rokuon.App;

// タスクトレイ(最小化録音)とグローバルホットキー
public partial class MainWindow
{
    private const int HotkeyIdStartStop = 0x5201;
    private const int HotkeyIdSplit = 0x5202;
    private const string HotkeyHintText = "例: Ctrl+Alt+F9 (Ctrl / Alt / Shift / Win と、英数字・F1〜F24 などの組み合わせ)";

    private IntPtr hwnd = IntPtr.Zero;
    private HwndSource? hwndSource;
    private bool startStopRegistered;
    private bool splitRegistered;

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

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "DestroyIcon")]
    private static extern bool DestroyIconNative(IntPtr handle);

    private void EnsureTray()
    {
        if (tray != null) return;
        try
        {
            trayIconIdle = MakeDot(System.Drawing.Color.FromArgb(120, 170, 230));
            trayIconRec = MakeDot(System.Drawing.Color.FromArgb(235, 60, 50));

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("ウィンドウを表示", null, (s, e) => ShowFromTray());
            menu.Items.Add("録音の開始 / 停止", null, (s, e) => OnRecordClick(this, new RoutedEventArgs()));
            menu.Items.Add("分割", null, (s, e) => { if (ctl.IsRecording) OnSplitClick(this, new RoutedEventArgs()); });
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("終了", null, (s, e) => { ShowFromTray(); Close(); });

            tray = new System.Windows.Forms.NotifyIcon
            {
                Icon = trayIconIdle,
                Text = "Rokuon",
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

            string text = "Rokuon  " + (recordingText ?? "待機中");
            if (text.Length > 60) text = text.Substring(0, 60);
            if (text == lastTrayText) return;
            lastTrayText = text;
            tray.Text = text;
            tray.Icon = recordingText != null && recordingText.StartsWith("録音") ? trayIconRec : trayIconIdle;
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

        var parts = new List<string>();
        parts.Add(startStopRegistered ? $"録音開始/停止 = {s.HotkeyStartStop} (登録しました)" : $"録音開始/停止 = {s.HotkeyStartStop} (登録できません)");
        parts.Add(splitRegistered ? $"分割 = {s.HotkeySplit} (登録しました)" : $"分割 = {s.HotkeySplit} (登録できません)");
        string note = (startStopRegistered && splitRegistered) ? "" : "\n書式が違うか、ほかのアプリがすでに使っているキーの組み合わせです。別のキーにしてください。";
        HotkeyStatus.Text = string.Join(" / ", parts) + note;
    }

    private void UnregisterHotkeys()
    {
        if (hwnd == IntPtr.Zero) return;
        if (startStopRegistered) GlobalHotkeys.Unregister(hwnd, HotkeyIdStartStop);
        if (splitRegistered) GlobalHotkeys.Unregister(hwnd, HotkeyIdSplit);
        startStopRegistered = false;
        splitRegistered = false;
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
                if (ctl.IsRecording && ctl.EngineState == Rokuon.Core.EngineState.Recording) OnSplitClick(this, new RoutedEventArgs());
            }
        }
        return IntPtr.Zero;
    }
}

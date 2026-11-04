using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LokuOn.Audio;
using LokuOn.Core;

namespace LokuOn.App;

// 録音ファイルの一覧(右クリックメニュー・名前変更・ごみ箱)と、簡易プレイヤー(再生速度つき)
public partial class MainWindow
{
    private readonly ObservableCollection<FileItem> files = new();

    private FilePlayer? player;
    private FileItem? loadedItem;        // プレイヤーに読み込んでいるファイル
    private bool playerIsPlaying;
    private bool seekDragging;
    private bool updatingSeek;
    private DateTime lastPlayerUpdate = DateTime.MinValue;
    private int rescanVersion;

    // ---------------------------------------------------------------- 一覧

    private void UpdateFileCount()
    {
        int sel = FileList.SelectedItems.Count;
        FileCountText.Text = sel > 1 ? $"{files.Count} 件(選択 {sel} 件)" : $"{files.Count} 件";
    }

    private void OnFileFinished(RecordedFile f)
    {
        for (int i = files.Count - 1; i >= 0; i--)
            if (string.Equals(files[i].Path, f.Path, StringComparison.OrdinalIgnoreCase)) files.RemoveAt(i);
        files.Insert(0, FileItem.FromPath(f.Path, f.Seconds));
        UpdateFileCount();
        AddLog("保存しました: " + System.IO.Path.GetFileName(f.Path));
    }

    /// <summary>保存先フォルダ(日付フォルダの中も)から、最近のファイルを読み込み直す。</summary>
    private async void RescanFiles()
    {
        try
        {
            string root = FolderBox.Text.Trim();
            int version = ++rescanVersion;
            var known = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in files) known[it.Path] = it.Seconds;

            List<FileInfo> found = await Task.Run(() => FileOps.ScanRecent(root, 300));
            if (version != rescanVersion) return;      // もっと新しい読み込みが始まっている

            files.Clear();
            foreach (var fi in found)
            {
                known.TryGetValue(fi.FullName, out double? sec);
                files.Add(FileItem.FromPath(fi.FullName, sec));
            }
            UpdateFileCount();
        }
        catch (Exception ex) { App.WriteCrashLog("一覧の読み込み: " + ex); }
    }

    private List<FileItem> SelectedFiles() => FileList.SelectedItems.OfType<FileItem>().ToList();

    private void BuildFileContextMenu()
    {
        var menu = new ContextMenu();
        var play = new MenuItem { Header = "再生 / 一時停止" };
        var reveal = new MenuItem { Header = "エクスプローラーでフォルダを開く" };
        var rename = new MenuItem { Header = "名前を変更…" };
        var trash = new MenuItem { Header = "ごみ箱へ移動" };
        var copy = new MenuItem { Header = "ファイルのパスをコピー" };
        play.Click += (s, e) => PlaySelected();
        reveal.Click += (s, e) => RevealSelected();
        rename.Click += (s, e) => RenameSelected();
        trash.Click += (s, e) => TrashSelected();
        copy.Click += (s, e) => CopySelectedPaths();
        menu.Items.Add(play);
        menu.Items.Add(new Separator());
        menu.Items.Add(reveal);
        menu.Items.Add(rename);
        menu.Items.Add(trash);
        menu.Items.Add(new Separator());
        menu.Items.Add(copy);
        menu.Opened += (s, e) =>
        {
            int n = FileList.SelectedItems.Count;
            play.IsEnabled = n == 1;
            reveal.IsEnabled = n >= 1;
            rename.IsEnabled = n == 1;
            trash.IsEnabled = n >= 1;
            copy.IsEnabled = n >= 1;
        };
        FileList.ContextMenu = menu;
    }

    private void WireFileEvents()
    {
        BtnPlay.Click += (s, e) => PlaySelected();
        BtnStopPlay.Click += (s, e) => StopPlayback();
        BtnReveal.Click += (s, e) => RevealSelected();
        BtnRename.Click += (s, e) => RenameSelected();
        BtnTrash.Click += (s, e) => TrashSelected();
        BtnRescan.Click += (s, e) => RescanFiles();

        FileList.SelectionChanged += (s, e) => UpdateFileCount();
        FileList.MouseDoubleClick += (s, e) =>
        {
            // 項目の上でダブルクリックしたときだけ再生する(見出しなどは対象外)
            if (ItemsControl.ContainerFromElement(FileList, e.OriginalSource as DependencyObject) is ListViewItem) PlaySelected(true);
        };
        FileList.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Delete) { TrashSelected(); e.Handled = true; }
            else if (e.Key == Key.F2) { RenameSelected(); e.Handled = true; }
            else if (e.Key == Key.Enter) { PlaySelected(true); e.Handled = true; }
        };
        // 右クリックした行が選択されていなければ、その行だけを選択する(複数選択中はそのまま)
        FileList.PreviewMouseRightButtonDown += (s, e) =>
        {
            if (ItemsControl.ContainerFromElement(FileList, e.OriginalSource as DependencyObject) is ListViewItem item && !item.IsSelected)
            {
                FileList.SelectedItems.Clear();
                item.IsSelected = true;
            }
        };

        SpeedCombo.SelectionChanged += (s, e) => ApplySpeed();
        VolumeSlider.ValueChanged += (s, e) => { if (player != null) player.Volume = VolumeSlider.Value; };

        // シークバー: つまみをつかんでいる間は位置を自動更新せず、離したときに移動する
        SeekSlider.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((s, e) => seekDragging = true), true);
        SeekSlider.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler((s, e) => CommitSeek()), true);
        SeekSlider.ValueChanged += (s, e) =>
        {
            if (updatingSeek) return;
            PlayPosText.Text = FileItem.FormatClock(SeekSlider.Value) + " / " + FileItem.FormatClock(SeekSlider.Maximum);
        };
    }

    // ---------------------------------------------------------------- エクスプローラー / 名前変更 / ごみ箱

    private void RevealSelected()
    {
        var sel = SelectedFiles();
        if (sel.Count == 0) return;
        try
        {
            var byFolder = sel.GroupBy(x => x.Folder, StringComparer.OrdinalIgnoreCase).ToList();
            if (sel.Count > 1)
            {
                // 複数まとめて開くときは、ウィンドウがたくさん開くので確認する
                var r = MessageBox.Show(this,
                    $"選択した {sel.Count} 件のファイルがあるフォルダ({byFolder.Count} か所)を、エクスプローラーで開きます。\n" +
                    (byFolder.Count > 1 ? $"エクスプローラーのウィンドウが {byFolder.Count} 個開きます。\n" : "") + "\nよろしいですか？",
                    "LokuOn - フォルダを開く", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                if (r != MessageBoxResult.Yes) return;
            }
            foreach (var g in byFolder) FileOps.RevealInExplorer(g.First().Path);
        }
        catch (Exception ex)
        {
            SetInlineError(FileError, "フォルダを開けませんでした: " + ex.Message);
        }
    }

    private void CopySelectedPaths()
    {
        var sel = SelectedFiles();
        if (sel.Count == 0) return;
        try { Clipboard.SetText(string.Join(Environment.NewLine, sel.Select(x => x.Path))); }
        catch (Exception ex) { AddLog("コピーできませんでした: " + ex.Message); }
    }

    private void ReleasePlayerIfUsing(IEnumerable<FileItem> items)
    {
        // 再生のために開いているファイルは、名前変更・削除ができないので先に閉じる
        if (loadedItem != null && items.Any(x => string.Equals(x.Path, loadedItem.Path, StringComparison.OrdinalIgnoreCase)))
            StopPlayback(true);
    }

    private void RenameSelected()
    {
        var sel = SelectedFiles();
        if (sel.Count != 1) { if (sel.Count > 1) AddLog("名前の変更は1件ずつ行ってください"); return; }
        var item = sel[0];
        SetInlineError(FileError, null);
        try
        {
            string ext = System.IO.Path.GetExtension(item.Path);
            string stem = System.IO.Path.GetFileNameWithoutExtension(item.Path);
            string? input = PromptWindow.Show(this, "名前を変更", "新しいファイル名を入力してください(拡張子 " + ext + " は自動で付きます)。", stem);
            if (input == null) return;
            string newStem = NameTemplate.Sanitize(input.Trim());
            if (newStem.Length == 0 || newStem == stem) return;

            string dir = item.Folder;
            string newPath = System.IO.Path.Combine(dir, newStem + ext);
            if (File.Exists(newPath) || Directory.Exists(newPath))
            {
                SetInlineError(FileError, "同じ名前のファイルがすでにあります: " + System.IO.Path.GetFileName(newPath));
                return;
            }
            ReleasePlayerIfUsing(sel);
            File.Move(item.Path, newPath);
            item.Path = newPath;
            AddLog("名前を変更しました: " + System.IO.Path.GetFileName(newPath));
        }
        catch (Exception ex)
        {
            SetInlineError(FileError, "名前を変更できませんでした: " + ex.Message);
        }
    }

    private void TrashSelected()
    {
        var sel = SelectedFiles();
        if (sel.Count == 0) return;
        string what = sel.Count == 1 ? "「" + sel[0].Name + "」" : sel.Count + " 件のファイル";
        var r = MessageBox.Show(this, what + " をごみ箱へ移動しますか？\n(ごみ箱から元に戻せます)", "LokuOn - ごみ箱へ移動",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (r != MessageBoxResult.Yes) return;

        ReleasePlayerIfUsing(sel);
        int ok = 0;
        var failed = new List<string>();
        foreach (var it in sel)
        {
            try
            {
                if (File.Exists(it.Path)) FileOps.MoveToRecycleBin(it.Path);
                files.Remove(it);
                ok++;
            }
            catch (Exception ex) { failed.Add(it.Name + " (" + ex.Message + ")"); }
        }
        UpdateFileCount();
        AddLog($"{ok} 件をごみ箱へ移動しました");
        if (failed.Count > 0)
            SetInlineError(FileError, "次のファイルは移動できませんでした: " + string.Join(" / ", failed.Take(4)));
    }

    // ---------------------------------------------------------------- プレイヤー

    private void InitPlayer()
    {
        try
        {
            player = new FilePlayer { Volume = VolumeSlider.Value };
            player.Ended += () => Dispatcher.InvokeAsync(OnPlayerEnded);
            player.Failed += msg => Dispatcher.InvokeAsync(() => { StopPlayback(true); AddLog("再生できません: " + msg); });
        }
        catch (Exception ex) { App.WriteCrashLog("プレイヤー初期化: " + ex); player = null; }
    }

    private void ShutdownPlayer()
    {
        try { player?.Dispose(); } catch { }
        player = null;
    }

    private double SelectedSpeed() => ValueOf(SpeedCombo, 1.0);

    private void ApplySpeed()
    {
        try { if (player != null) player.SpeedRatio = SelectedSpeed(); } catch { }
    }

    /// <summary>選んでいる1件を再生。すでに読み込み済みなら一時停止/再開。</summary>
    private void PlaySelected(bool restartIfSame = false)
    {
        if (player == null) return;
        var sel = SelectedFiles();
        if (sel.Count != 1)
        {
            if (loadedItem != null) { TogglePause(); return; }
            if (sel.Count > 1) AddLog("再生するファイルを1つだけ選んでください");
            return;
        }
        var item = sel[0];
        if (!File.Exists(item.Path)) { AddLog("ファイルが見つかりません: " + item.Name); return; }

        if (loadedItem != null && ReferenceEquals(loadedItem, item) && !restartIfSame) { TogglePause(); return; }
        try
        {
            loadedItem = item;
            player.Open(item.Path);
            player.Volume = VolumeSlider.Value;
            player.SpeedRatio = SelectedSpeed();
            OnPlayerOpened();
            player.Play();
            playerIsPlaying = true;
            BtnPlay.Content = "❚❚ 一時停止";
            PlayPosText.Text = "0:00 / " + FileItem.FormatClock(SeekSlider.Maximum);
            SetSeek(0);
        }
        catch (Exception ex)
        {
            AddLog("再生できません: " + ex.Message);
            StopPlayback(true);
        }
    }

    private void TogglePause()
    {
        if (player == null || loadedItem == null) return;
        if (playerIsPlaying) { player.Pause(); playerIsPlaying = false; BtnPlay.Content = "▶ 再生"; }
        else { player.SpeedRatio = SelectedSpeed(); player.Play(); playerIsPlaying = true; BtnPlay.Content = "❚❚ 一時停止"; }
    }

    /// <summary>停止。close=true のときはファイルも閉じる(名前変更・削除の前など)。</summary>
    private void StopPlayback(bool close = false)
    {
        try
        {
            if (player != null)
            {
                if (close) player.Close(); else player.Stop();
            }
        }
        catch { }
        playerIsPlaying = false;
        if (close) loadedItem = null;
        BtnPlay.Content = "▶ 再生";
        SetSeek(0);
        if (loadedItem == null) { SeekSlider.Maximum = 1; PlayPosText.Text = "0:00 / 0:00"; }
    }

    private void OnPlayerOpened()
    {
        if (player == null) return;
        try
        {
            double total = player.Duration.TotalSeconds;
            if (total > 0)
            {
                SeekSlider.Maximum = Math.Max(1, total);
                if (loadedItem != null && loadedItem.Seconds == null) loadedItem.Seconds = total;   // 長さがわかったので一覧にも反映
            }
        }
        catch { }
    }

    private void OnPlayerEnded()
    {
        // 最後まで再生したら先頭に戻して停止状態にする
        try { if (player != null) player.Stop(); } catch { }
        playerIsPlaying = false;
        BtnPlay.Content = "▶ 再生";
        SetSeek(0);
    }

    private void SetSeek(double seconds)
    {
        updatingSeek = true;
        try { SeekSlider.Value = Math.Min(Math.Max(0, seconds), SeekSlider.Maximum); }
        finally { updatingSeek = false; }
    }

    private void CommitSeek()
    {
        if (!seekDragging) return;
        seekDragging = false;
        try { if (player != null && loadedItem != null) player.Position = TimeSpan.FromSeconds(SeekSlider.Value); }
        catch { }
    }

    /// <summary>画面のタイマーから呼ばれる。再生位置の表示を更新する。</summary>
    private void UpdatePlayerPosition()
    {
        if (player == null || loadedItem == null || seekDragging) return;
        if ((DateTime.Now - lastPlayerUpdate).TotalMilliseconds < 200) return;
        lastPlayerUpdate = DateTime.Now;
        try
        {
            double pos = player.Position.TotalSeconds;
            SetSeek(pos);
            PlayPosText.Text = FileItem.FormatClock(pos) + " / " + FileItem.FormatClock(SeekSlider.Maximum);
        }
        catch { }
    }
}

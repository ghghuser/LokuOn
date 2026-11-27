using System.Windows;
using LokuOn.Audio;
using LokuOn.Core;

namespace LokuOn.App;

// LokuOnX を参考に取り入れた入力の補助(変数の説明つき編集ウィンドウ・ホットキーのキー入力)
public partial class MainWindow
{
    private void WireHelpers()
    {
        BtnVarTemplate.Click += (s, e) =>
        {
            string? r = VariableEditorWindow.Show(this, Loc.Get("help.01"), TemplateBox.Text, VariableHelp.FileName, PreviewFileName,
                Loc.Get("help.02"));
            if (r != null) TemplateBox.Text = r;
        };
        BtnVarDate.Click += (s, e) =>
        {
            string? r = VariableEditorWindow.Show(this, Loc.Get("help.03"), DateFormatBox.Text, VariableHelp.DateFolder,
                t => NameTemplate.RenderFolder(t, DateTime.Now),
                Loc.Get("help.04"));
            if (r != null) DateFormatBox.Text = r;
        };

        HotkeyCapture.Attach(HotkeyStartBox);
        HotkeyCapture.Attach(HotkeySplitBox);
        HotkeyCapture.Attach(HotkeyPauseBox);
    }

    /// <summary>テンプレートの試し書き用: 36：25 録音した場合のファイル名(拡張子つき)。</summary>
    private string PreviewFileName(string template)
    {
        var p = ReadPreset();
        var ctx = new NameContext
        {
            No = ParseInt(NoBox.Text, 1, 0, 99999999), Track = ParseInt(TrackBox.Text, 1, 0, 99999999),
            StartSec = 0, EndSec = 2185, FileStartTime = DateTime.Now, Preset = p.Name, TimeStyle = p.TimeStyle,
            Device = p.Sources.Where(x => x.DeviceId.Length > 0)
                .Select(x => RecorderController.ShortLabel(x.DeviceId, x.DeviceName)).FirstOrDefault() ?? "",
        };
        return NameTemplate.Sanitize(NameTemplate.Render(template, ctx)) + EncoderSpec.Extension(p.Format);
    }
}

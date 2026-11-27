using LokuOn.Core;
namespace LokuOn.App;

/// <summary>変数1つの説明。Token は入力欄にそのまま挿入される文字列。</summary>
public sealed record VarInfo(string Token, string Description);

/// <summary>「？」ボタンで開く編集ウィンドウに出す変数の一覧(アイデアは LokuOnX から)。</summary>
public static class VariableHelp
{
    private static readonly VarInfo[] DateParts =
    {
        new("{yyyy}", Loc.Get("var.05")), new("{yy}", Loc.Get("var.06")), new("{MM}", Loc.Get("var.07")), new("{dd}", Loc.Get("var.08")),
        new("{HH}", Loc.Get("var.09")), new("{mm}", Loc.Get("var.10")), new("{ss}", Loc.Get("var.11")),
    };

    /// <summary>ファイル名テンプレートで使える変数。日付・時刻は、そのファイルを始めた時刻。</summary>
    public static readonly IReadOnlyList<VarInfo> FileName = new[]
    {
        new VarInfo("{Date}", Loc.Get("var.12")),
        new VarInfo("{Time}", Loc.Get("var.13")),
        new VarInfo("{DateTime}", Loc.Get("var.14")),
    }.Concat(DateParts).Concat(new[]
    {
        new VarInfo("{No}", Loc.Get("var.15")),
        new VarInfo("{Track:000}", Loc.Get("var.16")),
        new VarInfo("{Start}", Loc.Get("var.17")),
        new VarInfo("{End}", Loc.Get("var.18")),
        new VarInfo("{Length}", Loc.Get("var.19")),
        new VarInfo("{Start:m}", Loc.Get("var.20")),
        new VarInfo("{End:m}", Loc.Get("var.21")),
        new VarInfo("{Length:m}", Loc.Get("var.22")),
        new VarInfo("{End:s}", Loc.Get("var.23")),
        new VarInfo("{Device}", Loc.Get("var.24")),
        new VarInfo("{Preset}", Loc.Get("var.25")),
        new VarInfo("{ext}", Loc.Get("var.26")),
    }).ToArray();

    /// <summary>日付フォルダの名前で使える変数(日時の部品のみ。録音を始めた日で固定)。</summary>
    public static readonly IReadOnlyList<VarInfo> DateFolder = new[] { new VarInfo("{Date}", Loc.Get("var.12")) }.Concat(DateParts).ToArray();
}

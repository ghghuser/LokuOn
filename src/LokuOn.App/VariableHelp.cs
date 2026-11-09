namespace LokuOn.App;

/// <summary>変数1つの説明。Token は入力欄にそのまま挿入される文字列。</summary>
public sealed record VarInfo(string Token, string Description);

/// <summary>「？」ボタンで開く編集ウィンドウに出す変数の一覧(アイデアは LokuOnX から)。</summary>
public static class VariableHelp
{
    private static readonly VarInfo[] DateParts =
    {
        new("{yyyy}", "年(4桁)"), new("{yy}", "年(下2桁)"), new("{MM}", "月(2桁)"), new("{dd}", "日(2桁)"),
        new("{HH}", "時(24時間・2桁)"), new("{mm}", "分(2桁)"), new("{ss}", "秒(2桁)"),
    };

    /// <summary>ファイル名テンプレートで使える変数。日付・時刻は、そのファイルを始めた時刻。</summary>
    public static readonly IReadOnlyList<VarInfo> FileName = new[]
    {
        new VarInfo("{Date}", "日付(例 2025-07-06)"),
        new VarInfo("{Time}", "時刻(例 13：05：09。コロンは全角になります)"),
        new VarInfo("{DateTime}", "日付と時刻(例 2025-07-06_13-05-09)"),
    }.Concat(DateParts).Concat(new[]
    {
        new VarInfo("{No}", "通し番号。録音が終わるたびに +1、アプリを閉じても保持"),
        new VarInfo("{Track:000}", "トラック番号(000 は桁数)。開始Trackから、分割のたびに +1"),
        new VarInfo("{Start}", "このファイルの開始時間(録音全体の 0：00 から)"),
        new VarInfo("{End}", "このファイルの終了時間(録音全体の 0：00 から)"),
        new VarInfo("{Length}", "このファイルの長さ(終了 − 開始)"),
        new VarInfo("{Start:m}", "開始時間を分だけで表す(例 125：30)"),
        new VarInfo("{End:m}", "終了時間を分だけで表す"),
        new VarInfo("{Length:m}", "長さを分だけで表す"),
        new VarInfo("{End:s}", "終了時間を秒数で表す(例 7530)"),
        new VarInfo("{Device}", "入力デバイスの名前"),
        new VarInfo("{Preset}", "プリセット名"),
        new VarInfo("{ext}", "拡張子(flac・mp3・m4a など)"),
    }).ToArray();

    /// <summary>日付フォルダの名前で使える変数(日時の部品のみ。録音を始めた日で固定)。</summary>
    public static readonly IReadOnlyList<VarInfo> DateFolder = new[] { new VarInfo("{Date}", "日付(例 2025-07-06)") }.Concat(DateParts).ToArray();
}

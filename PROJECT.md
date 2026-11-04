# LokuOn 引き継ぎメモ（Claude用・新しいチャットはまずこれだけ読む）

最終更新: 2026-11-04 / 最新コミット: 77bd5de 付近（Actionsビルド成功、自己テスト147件成功）

## 1. 目的と運用
- Windows 11 用の自作録音アプリ（用途は自分用）。ユーザー(お嬢)はローカル開発環境なし。**Claudeがこのリポジトリを直接編集→push→GitHub Actionsでビルド**。ユーザーは Actions の Artifacts(`LokuOn-win-x64`) をDLして実機テストし、結果を報告する。
- リポジトリ: https://github.com/ghghuser/LokuOn （非公開）。新チャットでは `add_repo`（owner=ghghuser, repo=LokuOn, access=push）→ clone。コンテナは消えることがある。
- 返答は日本語。大きな作りは先に質問、小さな修正は直接直して push。**実機で動かせない部分（WPF画面）は「未確認」と正直に報告**する。
- コミット文末: `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>` と `Claude-Session: <現セッションURL>`。

## 2. 仕様の正（2026-11-03 簡潔版）
- C#/.NET WPF、自己完結の単一exe、ポータブル、UI日本語。録音=NAudio(WASAPI)、書き出し=同梱FFmpegへパイプで直接エンコード。
- ZIP: `LokuOn\LokuOn.exe` + `LokuOn\ffmpeg\ffmpeg.exe`（実行時に settings.ini / Presets\ が横にできる）。
- 入力: マイク🎤とスピーカー🔊(ループバック)を同じ一覧、既定に＊、「既定のデバイス(追従)」。**最大4台**、ミックス1ファイル/別ファイル、デバイス別ゲイン −30〜+20dB、メーター、クリップ警告。
- 形式: flac(既定)/mp3/aac(.m4a,.aac)/wav/ogg(Vorbis)/opus。音質: ビットレート・周波数・チャンネル・圧縮率(flac0-12, mp3 0-9, opus 0-10。ogg=設定なし)。
- 24時間で強制分割、一時停止/再開、デバイス切断でも同一ファイル継続(無音補填)、異常終了の復旧(`_restored`)、スリープ防止(既定ON)、ディスク1GB未満で警告/100MB未満で自動停止、録音中に閉じる警告。
- 分割: 手動 / 自動(時:分:秒、00:00:00=無効) / 24h強制 / 無音検出(既定OFF)。
- 保存先既定 `ドキュメント\LokuOn\Rec` + 日付フォルダ(録音開始日)。ファイル名テンプレート変数 `{Date}{Time}{yyyy}…{ss}{No}{Track}{Start}{End}{Length}{Device}{Preset}{ext}`、標準「日時形式」「トラック形式」、同名は(2)(3)、禁止文字は全角化。
- タグ(日本語OK、既定空、トラック番号タグ既定OFF)。プリセット=`Presets\名前.json`自動保存(保存先・番号は含めない)。
- 画面: 1280px以内・1画面、低頻度設定は折りたたみ、右上⚙(画面の色 自動/ライト/ダーク、スリープ防止、最小化動作、ヘルプ、バージョン、GitHub)、エラーは赤文字(確認は「はい/いいえ」ダイアログのまま)。
- 多重起動: 同じフォルダのexeだけ警告(2つ目は設定を保存せず終了)。
- ファイル一覧+プレイヤー(0.5〜2倍・音程維持、ごみ箱、名前変更、複数選択)。オプション(既定OFF、自動再開のみON): トレイ、ホットキー、予約録音、無音で自動開始/分割。ASIO(機材次第)。
- 保留/対象外: ニューモーフィズム風スキン(保留)、波形クリックシーク、トリミング、英語UI。

## 3. 構成
- `src/LokuOn.Core`(net8.0, OS非依存): Models(Preset/SourceSetting/AudioFormat), AppSettings(+SettingsStore, TemplateLibrary, BackdropRules), PresetStore(Presets\*.json), EncoderSession(FFmpeg引数), RecorderEngine/SourceBuffer/Resampler/Journal(復旧), NameTemplate, TimeText, WsolaStretcher, IniFile ほか。
- `src/LokuOn.Audio`(net8.0-windows): DeviceCatalog, CaptureSource, RecorderController, FilePlayer。
- `src/LokuOn.App`(net9.0-windows, WPF): MainWindow.xaml + .xaml.cs/.Settings.cs/.Files.cs/.Tray.cs, AppInfoWindow(⚙), ThemeHelper, WindowEffects(Mica/Acrylic), PromptWindow, Controls/。
- `tests/LokuOn.SelfTest`: コンソール自己テスト(147件)。ffmpegで実エンコードまで検証。
- `.github/workflows/build.yml`: windows-latest。FFmpeg取得(BtbN LGPL→gyan)＋エンコーダー確認、自己テスト、単一exe publish、`publish/LokuOn/`を Artifact `LokuOn-win-x64` に。`v*`タグでRelease。

## 4. 作業のやり方（トークン節約）
- 最初から全ファイルを読まない。**変更に関係するファイルだけ grep/Read の部分読み**。MainWindow.Settings.cs(約900行)は特に範囲指定で読む。
- Core変更は Linux で検証可: `apt install dotnet-sdk-8.0` → `cd tests/LokuOn.SelfTest && dotnet restore --configfile <空のnuget.config> && dotnet run --no-restore`（nuget.orgはプロキシで遮断、restoreは空ソース設定で）。**App(WPF)はLinuxでビルド不可** → push して Actions の結果(`gh run list` / `gh run view --log-failed`)で確認。
- 1回のpushにまとめて変更し、ビルドの待ち回数を減らす。ビルド成功まで約3分。

## 5. 既知の注意点・過去の落とし穴
- Fluent テーマの GridView は表示されない → ファイル一覧は ListView+ItemTemplate+手作りヘッダ。
- ダーク+背景効果なしで真っ白になる件 → WindowEffects.Apply で不透明背景を明示。ライトでは背景効果は「なし」固定(BackdropRules.Effective)。
- UniquePath は自分自身のパスを除外して判定(「(2)」誤付与の修正済み)。
- SettingsStore.Load は ini の `[Preset:名前]` を Presets\*.json に移行してから LoadPresets する順序が重要。
- Preset.SaveFolder は `[JsonIgnore]`、ini は [Current] にだけ書く。プリセット適用時は保存先欄を上書きしない。
- 旧設定 AutoSplitMinutes は読み込み時に秒へ換算。ディスク閾値の旧既定(5/0.5)は新既定(1/0.1)に自動更新。

## 6. 未解決・次の候補
1. 実機チェック待ち: 4台目デバイス、Ogg録音、時:分:秒入力、プリセット保存/共有、折りたたみ表示、赤文字エラー、⚙のスリープ防止。
2. 「録音時間とファイル長が1秒ずれる」件: 秒の丸め(切り捨てにするか)未決定。
3. Oggの圧縮率はVorbisに設定がなく無効化中(仕様との差)。
4. 詳細仕様(旧PROJECT.md)は失われているため、この文書と簡潔版が正。差異があればユーザーに確認。
5. ニューモーフィズム風ダークスキン(保留)。ASIO実機確認。

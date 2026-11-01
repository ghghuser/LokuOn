using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace Rokuon.Core;

public sealed class EncoderSpec
{
    public AudioFormat Format { get; set; } = AudioFormat.Flac;
    public int SampleRate { get; set; } = 48000;
    public int Channels { get; set; } = 2;
    public int BitrateKbps { get; set; } = 320;
    public int BitDepth { get; set; } = 16;
    public TagSettings Tags { get; set; } = new();
    public int? TrackTag { get; set; }
    public string FfmpegPath { get; set; } = "ffmpeg";

    public static string Extension(AudioFormat f) => f switch
    {
        AudioFormat.Flac => ".flac",
        AudioFormat.Mp3 => ".mp3",
        AudioFormat.M4a => ".m4a",
        AudioFormat.Aac => ".aac",
        AudioFormat.Wav => ".wav",
        AudioFormat.Opus => ".opus",
        _ => ".bin",
    };

    /// <summary>標準入力からfloat32の生PCMを受け取り、指定形式でファイルへ書くffmpegの引数。</summary>
    public List<string> BuildArgs(string outPath)
    {
        var a = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-y",
            "-f", "f32le", "-ar", SampleRate.ToString(), "-ac", Channels.ToString(), "-i", "pipe:0",
            "-vn",
            // 出力を細かくOSへ書き出す(ffmpegやPCが突然止まっても、そこまでの録音がファイルに残る)
            "-flush_packets", "1",
        };
        switch (Format)
        {
            case AudioFormat.Flac:
                a.AddRange(new[] { "-c:a", "flac", "-compression_level", "5" });
                if (BitDepth >= 24) a.AddRange(new[] { "-sample_fmt", "s32", "-bits_per_raw_sample", "24" });
                else a.AddRange(new[] { "-sample_fmt", "s16" });
                break;
            case AudioFormat.Mp3:
                a.AddRange(new[] { "-c:a", "libmp3lame", "-b:a", BitrateKbps + "k", "-id3v2_version", "3", "-write_id3v1", "0" });
                break;
            case AudioFormat.M4a:
                // 録音中は断片化MP4で書く(途中で電源が落ちても再生できる)。終了時に通常形式へ整える。
                a.AddRange(new[] { "-c:a", "aac", "-b:a", BitrateKbps + "k", "-movflags", "+empty_moov+default_base_moof", "-frag_duration", "2000000" });
                break;
            case AudioFormat.Aac:
                a.AddRange(new[] { "-c:a", "aac", "-b:a", BitrateKbps + "k", "-f", "adts" });
                break;
            case AudioFormat.Wav:
                a.AddRange(new[] { "-c:a", BitDepth >= 32 ? "pcm_f32le" : BitDepth >= 24 ? "pcm_s24le" : "pcm_s16le", "-rf64", "auto" });
                break;
            case AudioFormat.Opus:
                a.AddRange(new[] { "-c:a", "libopus", "-b:a", BitrateKbps + "k", "-vbr", "on" });
                break;
        }
        void Meta(string k, string v) { if (!string.IsNullOrWhiteSpace(v)) { a.Add("-metadata"); a.Add(k + "=" + v.Trim()); } }
        if (Format != AudioFormat.Aac)
        {
            Meta("title", Tags.Title); Meta("artist", Tags.Artist); Meta("album", Tags.Album);
            Meta("album_artist", Tags.AlbumArtist); Meta("genre", Tags.Genre); Meta("date", Tags.Year); Meta("comment", Tags.Comment);
            if (TrackTag.HasValue) Meta("track", TrackTag.Value.ToString());
        }
        a.Add(outPath);
        return a;
    }
}

public sealed class EncodeResult
{
    public bool Ok { get; init; }
    public string Error { get; init; } = "";
}

/// <summary>
/// ffmpegを1つ起動して、1ファイル分の音声を書き出す。Write は待たされない(内部キュー経由)。
/// このアプリが落ちてもffmpegは入力の終了を検知してファイルを閉じるので、途中までの録音が残る。
/// </summary>
public sealed class EncoderSession : IDisposable
{
    private readonly Process proc;
    private readonly BlockingCollection<byte[]> queue = new(12000);   // 10msチャンク×12000 ≒ 2分ぶん
    private readonly Thread writer;
    private readonly StringBuilder stderr = new();
    private volatile bool failed;
    private string failMessage = "";
    private int disposed;

    public string OutputPath { get; }
    public long DroppedChunks;
    public bool Failed => failed;
    public string FailMessage => failMessage;

    public EncoderSession(EncoderSpec spec, string outPath)
    {
        OutputPath = outPath;
        var psi = FfmpegTools.NewStartInfo(spec.FfmpegPath, spec.BuildArgs(outPath), redirectStdin: true);
        proc = new Process { StartInfo = psi };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (stderr) { if (stderr.Length < 20000) stderr.AppendLine(e.Data); } };
        proc.OutputDataReceived += (_, _) => { };
        try { proc.Start(); }
        catch (Exception ex)
        {
            proc.Dispose();
            throw new InvalidOperationException("ffmpeg を起動できません: " + ex.Message, ex);
        }
        proc.BeginErrorReadLine();
        proc.BeginOutputReadLine();
        writer = new Thread(WriterLoop) { IsBackground = true, Name = "Rokuon.EncoderWriter" };
        writer.Start();
    }

    /// <summary>float32インターリーブ(sampleCount=チャンネル込みのサンプル数)を渡す。</summary>
    public void Write(float[] samples, int sampleCount)
    {
        if (failed || queue.IsAddingCompleted) return;
        var bytes = new byte[sampleCount * 4];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        if (!queue.TryAdd(bytes)) Interlocked.Increment(ref DroppedChunks);
    }

    private void WriterLoop()
    {
        var stream = proc.StandardInput.BaseStream;
        try
        {
            foreach (var chunk in queue.GetConsumingEnumerable())
            {
                if (failed) continue;
                try { stream.Write(chunk, 0, chunk.Length); }
                catch (Exception ex) { failed = true; failMessage = "ffmpegへの書き込みに失敗: " + ex.Message; }
            }
        }
        finally
        {
            try { stream.Flush(); } catch { }
            try { stream.Close(); } catch { }
        }
    }

    /// <summary>入力を閉じてffmpegの終了を待つ(ファイルが閉じられる)。</summary>
    public EncodeResult Finish(int timeoutMs = 180_000)
    {
        try { queue.CompleteAdding(); } catch { }
        writer.Join(timeoutMs);
        bool exited = proc.WaitForExit(timeoutMs);
        if (!exited)
        {
            try { proc.Kill(true); } catch { }
            return new EncodeResult { Ok = false, Error = "ffmpegの終了待ちがタイムアウトしました" };
        }
        proc.WaitForExit();
        string err; lock (stderr) err = stderr.ToString().Trim();
        if (failed) return new EncodeResult { Ok = false, Error = failMessage + (err.Length > 0 ? " / " + err : "") };
        if (proc.ExitCode != 0) return new EncodeResult { Ok = false, Error = $"ffmpeg終了コード {proc.ExitCode}: {err}" };
        return new EncodeResult { Ok = true };
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try { queue.CompleteAdding(); } catch { }
        try { if (!proc.HasExited) proc.WaitForExit(2000); } catch { }
        try { proc.Dispose(); } catch { }
    }
}

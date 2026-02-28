---
name: dvd-logic-implementer
description: DVD 再生ロジック、ドライブ検出、字幕パーサー、字幕同期エンジンの実装を支援するスキル
---

# @dvd-logic-implementer - DVD 再生ロジック実装

## 概要
DVD ドライブの検出、再生ロジック、SRT 字幕ファイルのパース、再生位置に同期した字幕表示を実装するためのパターン集。

## DVD ドライブ検出

### WMI を使用した自動検出

```csharp
using System.Management;
using System.IO;

public class DvdManager
{
    /// <summary>
    /// DVD ドライブの一覧を取得する
    /// </summary>
    public static List<DriveInfo> GetDvdDrives()
    {
        return DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.CDRom)
            .ToList();
    }

    /// <summary>
    /// DVD メディアが挿入されているドライブを検出する
    /// VIDEO_TS フォルダの存在で判定
    /// </summary>
    public static DriveInfo? FindDvdWithMedia()
    {
        return GetDvdDrives()
            .FirstOrDefault(d =>
            {
                try
                {
                    return d.IsReady &&
                           Directory.Exists(Path.Combine(d.RootDirectory.FullName, "VIDEO_TS"));
                }
                catch { return false; }
            });
    }
}
```

## SRT 字幕パーサー

### SRT ファイルフォーマット

```
1
00:00:01,000 --> 00:00:04,000
Hello, how are you?

2
00:00:05,000 --> 00:00:08,500
I'm fine, thank you.
```

### SubtitleEntry モデル

```csharp
public class SubtitleEntry
{
    public int Index { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public string Text { get; set; } = string.Empty;
}
```

### SRT パーサー実装

```csharp
public class SrtParser
{
    /// <summary>
    /// SRT ファイルを読み込んで SubtitleEntry のリストに変換する
    /// </summary>
    public static List<SubtitleEntry> Parse(string filePath)
    {
        var entries = new List<SubtitleEntry>();
        var lines = File.ReadAllLines(filePath, Encoding.UTF8);
        var i = 0;

        while (i < lines.Length)
        {
            // 空行をスキップ
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                i++;
                continue;
            }

            // インデックス番号
            if (!int.TryParse(lines[i].Trim(), out int index))
            {
                i++;
                continue;
            }
            i++;

            // タイムコード行  "00:00:01,000 --> 00:00:04,000"
            if (i >= lines.Length) break;
            var timeParts = lines[i].Split(" --> ");
            if (timeParts.Length != 2)
            {
                i++;
                continue;
            }
            var startTime = ParseTimeCode(timeParts[0].Trim());
            var endTime = ParseTimeCode(timeParts[1].Trim());
            i++;

            // テキスト行（複数行対応）
            var textLines = new List<string>();
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]))
            {
                textLines.Add(lines[i]);
                i++;
            }

            entries.Add(new SubtitleEntry
            {
                Index = index,
                StartTime = startTime,
                EndTime = endTime,
                Text = string.Join("\n", textLines)
            });
        }

        return entries;
    }

    private static TimeSpan ParseTimeCode(string timeCode)
    {
        // "00:00:01,000" -> TimeSpan
        timeCode = timeCode.Replace(',', '.');
        return TimeSpan.Parse(timeCode);
    }
}
```

## 字幕同期サービス

```csharp
using System.Timers;

public class SubtitleSyncService : IDisposable
{
    private List<SubtitleEntry> _japaneseSubtitles = new();
    private List<SubtitleEntry> _englishSubtitles = new();
    private System.Timers.Timer _syncTimer;

    public event Action<string>? JapaneseSubtitleChanged;
    public event Action<string>? EnglishSubtitleChanged;

    // MediaPlayer から現在の再生位置を取得する Func
    public Func<TimeSpan>? GetCurrentPosition { get; set; }

    public SubtitleSyncService()
    {
        _syncTimer = new System.Timers.Timer(100); // 100ms 間隔でチェック
        _syncTimer.Elapsed += OnSyncTimerElapsed;
    }

    public void LoadSubtitles(string japaneseSrtPath, string englishSrtPath)
    {
        _japaneseSubtitles = SrtParser.Parse(japaneseSrtPath);
        _englishSubtitles = SrtParser.Parse(englishSrtPath);
    }

    public void Start() => _syncTimer.Start();
    public void Stop() => _syncTimer.Stop();

    private void OnSyncTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (GetCurrentPosition == null) return;
        var pos = GetCurrentPosition();

        var jpSub = _japaneseSubtitles
            .FirstOrDefault(s => pos >= s.StartTime && pos <= s.EndTime);
        var enSub = _englishSubtitles
            .FirstOrDefault(s => pos >= s.StartTime && pos <= s.EndTime);

        JapaneseSubtitleChanged?.Invoke(jpSub?.Text ?? "");
        EnglishSubtitleChanged?.Invoke(enSub?.Text ?? "");
    }

    public void Dispose()
    {
        _syncTimer?.Stop();
        _syncTimer?.Dispose();
    }
}
```

## DVD 再生制御パターン

### チャプター操作

```csharp
// LibVLCSharp の MediaPlayer を使用
// チャプター数の取得
int chapterCount = mediaPlayer.ChapterCount;

// 現在のチャプター
int currentChapter = mediaPlayer.Chapter;

// チャプター移動
mediaPlayer.SetChapter(chapterNumber);

// 次/前のチャプター
mediaPlayer.NextChapter();
mediaPlayer.PreviousChapter();
```

### 音声トラック操作

```csharp
// 利用可能な音声トラック一覧
var audioTracks = mediaPlayer.AudioTrackDescription;

// 音声トラック切り替え
mediaPlayer.SetAudioTrack(trackId);
```

## 注意事項

- DVD 字幕はビットマップ形式（VOBSub）のため、テキストとして直接取得不可
- 外部 SRT ファイルを事前に用意するアプローチを採用
- SRT ファイルは UTF-8 エンコーディングを推奨（日本語字幕対応）
- 字幕同期は 100ms 間隔のポーリングで十分な精度を確保

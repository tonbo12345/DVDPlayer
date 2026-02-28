using DVDPlayer.Models;

namespace DVDPlayer.Services
{
    /// <summary>
    /// メディアプレーヤーの再生位置に同期して字幕を切り替えるサービス
    /// </summary>
    public class SubtitleSyncService : IDisposable
    {
        private List<SubtitleEntry> _japaneseSubtitles = new();
        private List<SubtitleEntry> _englishSubtitles = new();
        private System.Timers.Timer? _syncTimer;
        private string _lastJapaneseText = "";
        private string _lastEnglishText = "";

        /// <summary>日本語字幕が変更された時に発火するイベント</summary>
        public event Action<string>? JapaneseSubtitleChanged;

        /// <summary>英語字幕が変更された時に発火するイベント</summary>
        public event Action<string>? EnglishSubtitleChanged;

        /// <summary>現在の再生位置を取得するデリゲート</summary>
        public Func<TimeSpan>? GetCurrentPosition { get; set; }

        /// <summary>日本語字幕が読み込まれているか</summary>
        public bool HasJapaneseSubtitles => _japaneseSubtitles.Count > 0;

        /// <summary>英語字幕が読み込まれているか</summary>
        public bool HasEnglishSubtitles => _englishSubtitles.Count > 0;

        public SubtitleSyncService()
        {
            _syncTimer = new System.Timers.Timer(100); // 100ms 間隔
            _syncTimer.Elapsed += OnSyncTimerElapsed;
        }

        /// <summary>
        /// 日本語の字幕ファイルを読み込む
        /// </summary>
        public void LoadJapaneseSubtitles(string srtPath)
        {
            _japaneseSubtitles = SrtParser.Parse(srtPath);
        }

        /// <summary>
        /// 英語の字幕ファイルを読み込む
        /// </summary>
        public void LoadEnglishSubtitles(string srtPath)
        {
            _englishSubtitles = SrtParser.Parse(srtPath);
        }

        /// <summary>字幕をクリアする</summary>
        public void ClearSubtitles()
        {
            _japaneseSubtitles.Clear();
            _englishSubtitles.Clear();
            _lastJapaneseText = "";
            _lastEnglishText = "";
        }

        /// <summary>同期タイマーを開始する</summary>
        public void Start() => _syncTimer?.Start();

        /// <summary>同期タイマーを停止する</summary>
        public void Stop() => _syncTimer?.Stop();

        private void OnSyncTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            if (GetCurrentPosition == null) return;

            try
            {
                var pos = GetCurrentPosition();

                // 日本語字幕の同期
                var jpSub = FindCurrentSubtitle(_japaneseSubtitles, pos);
                var jpText = jpSub?.Text ?? "";
                if (jpText != _lastJapaneseText)
                {
                    _lastJapaneseText = jpText;
                    JapaneseSubtitleChanged?.Invoke(jpText);
                }

                // 英語字幕の同期
                var enSub = FindCurrentSubtitle(_englishSubtitles, pos);
                var enText = enSub?.Text ?? "";
                if (enText != _lastEnglishText)
                {
                    _lastEnglishText = enText;
                    EnglishSubtitleChanged?.Invoke(enText);
                }
            }
            catch
            {
                // 再生中のタイミング問題を無視
            }
        }

        /// <summary>
        /// 指定時間に該当する字幕エントリを二分探索で検索する
        /// </summary>
        private static SubtitleEntry? FindCurrentSubtitle(List<SubtitleEntry> entries, TimeSpan position)
        {
            if (entries.Count == 0) return null;

            // 二分探索で高速に検索
            int low = 0;
            int high = entries.Count - 1;

            while (low <= high)
            {
                int mid = (low + high) / 2;
                var entry = entries[mid];

                if (position < entry.StartTime)
                {
                    high = mid - 1;
                }
                else if (position > entry.EndTime)
                {
                    low = mid + 1;
                }
                else
                {
                    return entry; // 範囲内
                }
            }

            return null;
        }

        public void Dispose()
        {
            _syncTimer?.Stop();
            _syncTimer?.Dispose();
            _syncTimer = null;
        }
    }
}

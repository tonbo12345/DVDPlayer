using System.IO;
using System.Text.Json;

namespace DVDPlayer.Services
{
    /// <summary>
    /// 再生位置のレジューム（続きから再生）機能を提供するサービス
    /// メディアごとに再生位置をJSONファイルに保存する
    /// </summary>
    public class ResumeService
    {
        private readonly string _resumeFilePath;
        private Dictionary<string, ResumeData> _resumeData = new();

        public ResumeService()
        {
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DVDPlayer");
            Directory.CreateDirectory(appDataDir);
            _resumeFilePath = Path.Combine(appDataDir, "resume.json");
            Load();
        }

        /// <summary>再生位置を保存する</summary>
        /// <param name="mediaKey">メディアの識別キー（ファイルパスやDVD URI）</param>
        /// <param name="positionMs">再生位置（ミリ秒）</param>
        /// <param name="totalMs">総再生時間（ミリ秒）</param>
        public void SavePosition(string mediaKey, long positionMs, long totalMs)
        {
            if (string.IsNullOrEmpty(mediaKey) || positionMs <= 0) return;

            // 終了間際（残り5秒以下）は保存しない（視聴完了とみなす）
            if (totalMs > 0 && (totalMs - positionMs) < 5000)
            {
                _resumeData.Remove(mediaKey);
            }
            else
            {
                _resumeData[mediaKey] = new ResumeData
                {
                    PositionMs = positionMs,
                    TotalMs = totalMs,
                    LastPlayed = DateTime.Now
                };
            }

            Save();
        }

        /// <summary>保存された再生位置を取得する</summary>
        /// <returns>再生位置(ms)、保存されていない場合は0</returns>
        public long GetPosition(string mediaKey)
        {
            if (_resumeData.TryGetValue(mediaKey, out var data))
            {
                return data.PositionMs;
            }
            return 0;
        }

        /// <summary>保存された再生位置があるか確認する</summary>
        public bool HasResumePosition(string mediaKey)
        {
            return _resumeData.ContainsKey(mediaKey) && _resumeData[mediaKey].PositionMs > 0;
        }

        /// <summary>レジュームデータを削除する</summary>
        public void ClearPosition(string mediaKey)
        {
            _resumeData.Remove(mediaKey);
            Save();
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_resumeFilePath))
                {
                    var json = File.ReadAllText(_resumeFilePath);
                    _resumeData = JsonSerializer.Deserialize<Dictionary<string, ResumeData>>(json) ?? new();
                }
            }
            catch
            {
                _resumeData = new();
            }
        }

        private void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(_resumeData, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_resumeFilePath, json);
            }
            catch
            {
                // ファイル書き込みエラーは無視
            }
        }
    }

    public class ResumeData
    {
        public long PositionMs { get; set; }
        public long TotalMs { get; set; }
        public DateTime LastPlayed { get; set; }
    }
}

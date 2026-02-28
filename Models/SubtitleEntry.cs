namespace DVDPlayer.Models
{
    /// <summary>
    /// SRT 字幕ファイルの1エントリを表すモデル
    /// </summary>
    public class SubtitleEntry
    {
        /// <summary>字幕のインデックス番号</summary>
        public int Index { get; set; }

        /// <summary>表示開始時間</summary>
        public TimeSpan StartTime { get; set; }

        /// <summary>表示終了時間</summary>
        public TimeSpan EndTime { get; set; }

        /// <summary>字幕テキスト（複数行対応）</summary>
        public string Text { get; set; } = string.Empty;
    }
}

using System.IO;
using System.Text;
using DVDPlayer.Models;

namespace DVDPlayer.Services
{
    /// <summary>
    /// SRT (SubRip) 形式の字幕ファイルをパースするサービス
    /// </summary>
    public static class SrtParser
    {
        /// <summary>
        /// SRT ファイルを読み込んで SubtitleEntry のリストに変換する
        /// </summary>
        /// <param name="filePath">SRT ファイルのパス</param>
        /// <returns>字幕エントリのリスト</returns>
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

                // タイムコード行 "00:00:01,000 --> 00:00:04,000"
                if (i >= lines.Length) break;
                var timeLine = lines[i].Trim();
                var timeParts = timeLine.Split(new[] { " --> " }, StringSplitOptions.None);
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
                    // HTML タグを除去 (<i>, <b>, etc.)
                    var cleanedLine = System.Text.RegularExpressions.Regex.Replace(
                        lines[i], @"<[^>]+>", "");
                    textLines.Add(cleanedLine);
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

        /// <summary>
        /// SRT タイムコード文字列を TimeSpan に変換する
        /// </summary>
        private static TimeSpan ParseTimeCode(string timeCode)
        {
            // "00:00:01,000" -> TimeSpan
            timeCode = timeCode.Replace(',', '.');
            if (TimeSpan.TryParse(timeCode, out var result))
            {
                return result;
            }
            return TimeSpan.Zero;
        }
    }
}

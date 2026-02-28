using System.IO;

namespace DVDPlayer.Services
{
    /// <summary>
    /// DVD / Blu-ray ドライブの検出とメディア管理を行うサービス
    /// </summary>
    public class DvdManager
    {
        /// <summary>
        /// システム上の CD/DVD/Blu-ray ドライブの一覧を取得する
        /// </summary>
        public static List<DriveInfo> GetDvdDrives()
        {
            return DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.CDRom)
                .ToList();
        }

        /// <summary>
        /// メディアが挿入されているドライブを検出する
        /// DVD (VIDEO_TS) と Blu-ray (BDMV) の両方をチェック
        /// </summary>
        public static DriveInfo? FindDriveWithMedia()
        {
            return GetDvdDrives()
                .FirstOrDefault(d =>
                {
                    try
                    {
                        if (!d.IsReady) return false;
                        var root = d.RootDirectory.FullName;
                        return Directory.Exists(Path.Combine(root, "VIDEO_TS"))
                            || Directory.Exists(Path.Combine(root, "BDMV"));
                    }
                    catch
                    {
                        return false;
                    }
                });
        }

        /// <summary>
        /// ディスクの種類を判定する
        /// </summary>
        public static DiscType DetectDiscType(string driveLetter)
        {
            try
            {
                var root = driveLetter.TrimEnd('\\', '/');
                if (!root.EndsWith(':')) root += ":";
                root += "\\";

                if (Directory.Exists(Path.Combine(root, "BDMV")))
                    return DiscType.BluRay;
                if (Directory.Exists(Path.Combine(root, "VIDEO_TS")))
                    return DiscType.DVD;

                // ドライブにメディアがあるがフォルダ構造が不明な場合
                var drive = new DriveInfo(driveLetter.TrimEnd('\\', '/'));
                if (drive.IsReady)
                    return DiscType.Unknown;

                return DiscType.None;
            }
            catch
            {
                return DiscType.None;
            }
        }

        /// <summary>
        /// 指定されたドライブレターが有効な CD/DVD/BD ドライブかチェックする
        /// </summary>
        public static bool IsDvdDrive(string driveLetter)
        {
            try
            {
                var drive = new DriveInfo(driveLetter);
                return drive.DriveType == DriveType.CDRom;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// ドライブレターから LibVLC 用の URI に変換する
        /// DVD → dvd:///D:/  Blu-ray → bluray:///D:/
        /// </summary>
        public static string GetMediaUri(string driveLetter, DiscType discType)
        {
            driveLetter = driveLetter.TrimEnd('\\', '/');
            if (!driveLetter.EndsWith(':'))
                driveLetter += ":";

            return discType switch
            {
                DiscType.BluRay => $"bluray:///{driveLetter}/",
                DiscType.DVD => $"dvd:///{driveLetter}/",
                _ => $"dvd:///{driveLetter}/" // デフォルトは DVD として試行
            };
        }

        /// <summary>後方互換性のため残す</summary>
        public static string GetDvdUri(string driveLetter)
        {
            return GetMediaUri(driveLetter, DiscType.DVD);
        }
    }

    /// <summary>ディスクの種類</summary>
    public enum DiscType
    {
        None,
        DVD,
        BluRay,
        Unknown
    }
}

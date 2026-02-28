using System.IO;

namespace DVDPlayer.Services
{
    /// <summary>
    /// DVD ドライブの検出と DVD メディアの管理を行うサービス
    /// </summary>
    public class DvdManager
    {
        /// <summary>
        /// システム上の DVD/CD-ROM ドライブの一覧を取得する
        /// </summary>
        public static List<DriveInfo> GetDvdDrives()
        {
            return DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.CDRom)
                .ToList();
        }

        /// <summary>
        /// DVD メディアが挿入されているドライブを検出する
        /// VIDEO_TS フォルダの存在で DVD ディスクかどうかを判定
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
                    catch
                    {
                        return false;
                    }
                });
        }

        /// <summary>
        /// 指定されたドライブレターが有効な DVD ドライブかチェックする
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
        /// DVD ドライブのパスから LibVLC 用の dvd:// URI に変換する
        /// </summary>
        /// <param name="driveLetter">ドライブレター (例: "D:")</param>
        /// <returns>dvd:///D: 形式の URI</returns>
        public static string GetDvdUri(string driveLetter)
        {
            // ドライブレターの正規化
            driveLetter = driveLetter.TrimEnd('\\', '/');
            if (!driveLetter.EndsWith(':'))
            {
                driveLetter += ":";
            }
            return $"dvd:///{driveLetter}";
        }
    }
}

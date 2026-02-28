using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LibVLCSharp.Shared;
using Microsoft.Win32;
using DVDPlayer.Services;

using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace DVDPlayer
{
    /// <summary>
    /// DVD Player メインウィンドウ
    /// デュアル字幕（日本語/英語）表示対応 + 英語学習機能
    /// </summary>
    public partial class MainWindow : Window
    {
        private LibVLC? _libVLC;
        private VlcMediaPlayer? _mediaPlayer;
        private SubtitleSyncService? _subtitleSync;
        private ResumeService? _resumeService;
        private System.Windows.Threading.DispatcherTimer? _uiTimer;
        private bool _isSeeking = false;
        private bool _isFullScreen = false;
        private WindowState _previousWindowState;
        private WindowStyle _previousWindowStyle;
        private ResizeMode _previousResizeMode;

        // 現在のメディアキー（レジューム用）
        private string _currentMediaKey = "";

        // A-B リピート
        private long _abPointA = -1;  // ミリ秒
        private long _abPointB = -1;  // ミリ秒
        private bool _abRepeatActive = false;
        private int _abClickState = 0; // 0=idle, 1=A set, 2=B set

        // 再生速度
        private float _currentSpeed = 1.0f;

        // 字幕表示状態
        private bool _jpSubVisible = true;
        private bool _enSubVisible = true;

        public MainWindow()
        {
            InitializeComponent();
        }

        #region ウィンドウイベント

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // LibVLC の初期化
            Core.Initialize();
            _libVLC = new LibVLC(
                "--verbose=2",             // ログ出力
                "--no-video-title-show",   // VLC タイトル非表示
                "--disc-caching=3000"      // ディスクキャッシュ
            );

            // LibVLC のログをコンソールに出力（デバッグ用）
            _libVLC.Log += (s, logArgs) =>
            {
                System.Diagnostics.Debug.WriteLine($"[VLC] {logArgs.FormattedLog}");
            };

            _mediaPlayer = new VlcMediaPlayer(_libVLC);
            VideoView.MediaPlayer = _mediaPlayer;

            // レジュームサービスの初期化
            _resumeService = new ResumeService();

            // 字幕同期サービスの初期化
            _subtitleSync = new SubtitleSyncService();
            _subtitleSync.GetCurrentPosition = () =>
            {
                if (_mediaPlayer != null && _mediaPlayer.IsPlaying)
                {
                    return TimeSpan.FromMilliseconds(_mediaPlayer.Time);
                }
                return TimeSpan.Zero;
            };
            _subtitleSync.JapaneseSubtitleChanged += OnJapaneseSubtitleChanged;
            _subtitleSync.EnglishSubtitleChanged += OnEnglishSubtitleChanged;

            // UI 更新タイマー（シークバー・時間表示・A-Bリピート用）
            _uiTimer = new System.Windows.Threading.DispatcherTimer();
            _uiTimer.Interval = TimeSpan.FromMilliseconds(200);
            _uiTimer.Tick += UiTimer_Tick;
            _uiTimer.Start();

            // メディアプレーヤーイベント
            _mediaPlayer.Playing += (s, args) => Dispatcher.Invoke(() =>
            {
                BtnPlayPause.Content = "⏸";
                WelcomePanel.Visibility = Visibility.Collapsed;
            });
            _mediaPlayer.Paused += (s, args) => Dispatcher.Invoke(() =>
            {
                BtnPlayPause.Content = "▶";
            });
            _mediaPlayer.Stopped += (s, args) => Dispatcher.Invoke(() =>
            {
                BtnPlayPause.Content = "▶";
                SeekBar.Value = 0;
                TxtCurrentTime.Text = "00:00:00";
                WelcomePanel.Visibility = Visibility.Visible;
            });
            _mediaPlayer.EndReached += (s, args) => Dispatcher.Invoke(() =>
            {
                BtnPlayPause.Content = "▶";
                WelcomePanel.Visibility = Visibility.Visible;
            });
            _mediaPlayer.EncounteredError += (s, args) => Dispatcher.Invoke(() =>
            {
                Title = "DVD Player - エラー発生";
                System.Diagnostics.Debug.WriteLine("LibVLC EncounteredError event fired");
            });

            // VLC の内蔵字幕を無効化（外部SRT字幕オーバーレイを使用）
            _mediaPlayer.SetSpu(-1);

            // DVD の自動検出
            AutoDetectDvd();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // レジューム位置を保存
            SaveResumePosition();

            _subtitleSync?.Stop();
            _subtitleSync?.Dispose();
            _uiTimer?.Stop();

            _mediaPlayer?.Stop();
            _mediaPlayer?.Dispose();
            _libVLC?.Dispose();
        }

        #endregion

        #region DVD / Blu-ray 操作

        private void AutoDetectDvd()
        {
            var drive = DvdManager.FindDriveWithMedia();
            if (drive != null)
            {
                var discType = DvdManager.DetectDiscType(drive.Name);
                var discLabel = discType == DiscType.BluRay ? "Blu-ray" : "DVD";

                // ドライブのボリュームラベルを取得
                string volumeLabel = "";
                try { volumeLabel = drive.VolumeLabel; } catch { }
                var labelStr = string.IsNullOrEmpty(volumeLabel) ? "" : $" ({volumeLabel})";

                var result = MessageBox.Show(
                    $"{discLabel} が検出されました: {drive.Name}{labelStr}\n再生しますか？",
                    $"{discLabel} 検出",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    PlayDisc(drive.Name.TrimEnd('\\'));
                }
            }
        }

        private void PlayDisc(string driveLetter)
        {
            if (_libVLC == null || _mediaPlayer == null) return;

            try
            {
                var discType = DvdManager.DetectDiscType(driveLetter);
                _currentMediaKey = $"disc:{driveLetter}";

                // ドライブパスの正規化
                var normalizedDrive = driveLetter.TrimEnd('\\', '/');
                if (!normalizedDrive.EndsWith(':')) normalizedDrive += ":";

                // 方式1: dvd:// / bluray:// URI を試行
                // 方式2: 直接パスを指定
                // 方式3: VIDEO_TS / BDMV フォルダを直接指定
                var urisToTry = new List<(string uri, FromType fromType, string description)>();

                if (discType == DiscType.BluRay)
                {
                    urisToTry.Add(($"bluray:///{normalizedDrive}/", FromType.FromLocation, "Blu-ray URI"));
                    var bdmvPath = System.IO.Path.Combine(normalizedDrive + "\\", "BDMV");
                    urisToTry.Add((bdmvPath, FromType.FromPath, "BDMV フォルダパス"));
                }
                else
                {
                    urisToTry.Add(($"dvd:///{normalizedDrive}/", FromType.FromLocation, "DVD URI"));
                    var videoTsPath = System.IO.Path.Combine(normalizedDrive + "\\", "VIDEO_TS");
                    urisToTry.Add((videoTsPath, FromType.FromPath, "VIDEO_TS フォルダパス"));
                }

                // 直接ドライブパスも追加
                urisToTry.Add((normalizedDrive + "\\", FromType.FromPath, "ドライブ直接パス"));

                // タイトルバーに状態を表示
                Title = $"DVD Player - {(discType == DiscType.BluRay ? "Blu-ray" : "DVD")} 読み込み中...";

                TryPlayUris(urisToTry, 0, discType);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ディスクの再生に失敗しました:\n{ex.Message}\n\nドライブ: {driveLetter}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                Title = "DVD Player - Dual Subtitles";
            }
        }

        /// <summary>
        /// 複数の URI を順番に試行して再生する
        /// </summary>
        private void TryPlayUris(List<(string uri, FromType fromType, string description)> uris, int index, DiscType discType)
        {
            if (_libVLC == null || _mediaPlayer == null) return;
            if (index >= uris.Count)
            {
                MessageBox.Show(
                    "すべての再生方式を試行しましたが、ディスクを再生できませんでした。\n\n" +
                    "考えられる原因:\n" +
                    "• Blu-ray ディスクの AACS 暗号化\n" +
                    "• ディスクが破損している\n" +
                    "• VLC でこのディスクが再生できるか確認してください",
                    "再生エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                Title = "DVD Player - Dual Subtitles";
                return;
            }

            var (uri, fromType, description) = uris[index];
            System.Diagnostics.Debug.WriteLine($"[PlayDisc] 試行 {index + 1}/{uris.Count}: {description} -> {uri}");
            Title = $"DVD Player - {description} で試行中...";

            try
            {
                var media = new Media(_libVLC, uri, fromType);

                // DVD メニューをスキップして本編を直接再生するオプション
                if (discType == DiscType.DVD)
                {
                    media.AddOption(":dvd-angle=1");
                    media.AddOption(":no-disc-menu");   // メニューをスキップ
                }

                bool errorOccurred = false;
                media.StateChanged += (s, args) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[VLC Media State] {args.State}");
                    if (args.State == VLCState.Error && !errorOccurred)
                    {
                        errorOccurred = true;
                        Dispatcher.Invoke(() =>
                        {
                            System.Diagnostics.Debug.WriteLine($"[PlayDisc] {description} 失敗、次を試行...");
                            // 次の URI を試行
                            TryPlayUris(uris, index + 1, discType);
                        });
                    }
                };

                _mediaPlayer.Media = media;
                _mediaPlayer.Play();
                _subtitleSync?.Start();

                // 5秒後に再生が開始されたかチェック
                System.Threading.Tasks.Task.Delay(5000).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (_mediaPlayer != null && _mediaPlayer.IsPlaying)
                        {
                            // 再生成功！
                            Title = "DVD Player - Dual Subtitles";
                            _mediaPlayer.SetSpu(-1); // 内蔵字幕を無効化
                            CheckResume(_currentMediaKey);
                        }
                        else if (!errorOccurred && _mediaPlayer != null && !_mediaPlayer.IsPlaying)
                        {
                            // 5秒経っても再生が始まらない場合、次を試行
                            System.Diagnostics.Debug.WriteLine($"[PlayDisc] {description} タイムアウト、次を試行...");
                            errorOccurred = true;
                            System.Threading.Tasks.Task.Run(() => _mediaPlayer?.Stop());
                            System.Threading.Tasks.Task.Delay(500).ContinueWith(__ =>
                            {
                                Dispatcher.Invoke(() => TryPlayUris(uris, index + 1, discType));
                            });
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PlayDisc] {description} 例外: {ex.Message}");
                TryPlayUris(uris, index + 1, discType);
            }
        }

        private void PlayMedia(string path)
        {
            if (_libVLC == null || _mediaPlayer == null) return;

            try
            {
                _currentMediaKey = path;
                var media = new Media(_libVLC, new Uri(path));
                _mediaPlayer.Media = media;
                _mediaPlayer.Play();
                _subtitleSync?.Start();

                // レジューム確認
                CheckResume(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"再生に失敗しました:\n{ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region レジューム機能

        private void CheckResume(string mediaKey)
        {
            if (_resumeService == null || _mediaPlayer == null) return;

            if (_resumeService.HasResumePosition(mediaKey))
            {
                var position = _resumeService.GetPosition(mediaKey);
                var timeStr = TimeSpan.FromMilliseconds(position).ToString(@"hh\:mm\:ss");

                // 少し遅延してからレジューム確認（メディアの読み込み待ち）
                System.Threading.Tasks.Task.Delay(3000).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        var result = MessageBox.Show(
                            $"前回の再生位置({timeStr})から続きを再生しますか？",
                            "続きから再生",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (result == MessageBoxResult.Yes && _mediaPlayer != null)
                        {
                            _mediaPlayer.Time = position;
                        }
                    });
                });
            }
        }

        private void SaveResumePosition()
        {
            if (_resumeService == null || _mediaPlayer == null) return;
            if (string.IsNullOrEmpty(_currentMediaKey)) return;

            _resumeService.SavePosition(_currentMediaKey, _mediaPlayer.Time, _mediaPlayer.Length);
        }

        private void MenuResume_Click(object sender, RoutedEventArgs e)
        {
            if (_resumeService == null || _mediaPlayer == null) return;
            if (string.IsNullOrEmpty(_currentMediaKey)) return;

            var position = _resumeService.GetPosition(_currentMediaKey);
            if (position > 0)
            {
                _mediaPlayer.Time = position;
            }
        }

        #endregion

        #region A-B リピート

        private void MenuSetA_Click(object sender, RoutedEventArgs e)
        {
            SetPointA();
        }

        private void MenuSetB_Click(object sender, RoutedEventArgs e)
        {
            SetPointB();
        }

        private void MenuClearAB_Click(object sender, RoutedEventArgs e)
        {
            ClearABRepeat();
        }

        private void BtnABRepeat_Click(object sender, RoutedEventArgs e)
        {
            // ボタンクリックで順番に A → B → 解除
            switch (_abClickState)
            {
                case 0:
                    SetPointA();
                    _abClickState = 1;
                    break;
                case 1:
                    SetPointB();
                    _abClickState = 2;
                    break;
                case 2:
                    ClearABRepeat();
                    _abClickState = 0;
                    break;
            }
        }

        private void SetPointA()
        {
            if (_mediaPlayer == null) return;
            _abPointA = _mediaPlayer.Time;
            _abPointB = -1;
            _abRepeatActive = false;

            var timeStr = TimeSpan.FromMilliseconds(_abPointA).ToString(@"mm\:ss");
            AbRepeatIndicator.Visibility = Visibility.Visible;
            TxtAbRepeat.Text = $"A: {timeStr} → B: ...";
            BtnABRepeat.Content = "🅰";
        }

        private void SetPointB()
        {
            if (_mediaPlayer == null || _abPointA < 0) return;
            _abPointB = _mediaPlayer.Time;

            // A が B より大きい場合はスワップ
            if (_abPointA > _abPointB)
            {
                (_abPointA, _abPointB) = (_abPointB, _abPointA);
            }

            _abRepeatActive = true;
            var timeA = TimeSpan.FromMilliseconds(_abPointA).ToString(@"mm\:ss");
            var timeB = TimeSpan.FromMilliseconds(_abPointB).ToString(@"mm\:ss");
            TxtAbRepeat.Text = $"🔁 A: {timeA} → B: {timeB}";
            BtnABRepeat.Content = "🅱";
        }

        private void ClearABRepeat()
        {
            _abPointA = -1;
            _abPointB = -1;
            _abRepeatActive = false;
            _abClickState = 0;
            AbRepeatIndicator.Visibility = Visibility.Collapsed;
            BtnABRepeat.Content = "🔁";
        }

        #endregion

        #region 再生速度

        private void MenuSpeed_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayer == null) return;
            if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.Tag is string speedStr)
            {
                if (float.TryParse(speedStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float speed))
                {
                    SetPlaybackSpeed(speed);
                }
            }
        }

        private void SetPlaybackSpeed(float speed)
        {
            if (_mediaPlayer == null) return;

            _currentSpeed = speed;
            _mediaPlayer.SetRate(speed);

            if (Math.Abs(speed - 1.0f) < 0.01f)
            {
                SpeedIndicator.Visibility = Visibility.Collapsed;
                TxtSpeedBar.Text = "";
            }
            else
            {
                SpeedIndicator.Visibility = Visibility.Visible;
                TxtSpeed.Text = $"⏩ {speed:F2}x";
                TxtSpeedBar.Text = $"[{speed:F1}x]";
            }
        }

        #endregion

        #region 字幕フォントサイズ

        private void MenuJpFontSize_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.Tag is string sizeStr)
            {
                if (double.TryParse(sizeStr, out double size))
                {
                    JapaneseSubtitle.FontSize = size;
                }
            }
        }

        private void MenuEnFontSize_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.Tag is string sizeStr)
            {
                if (double.TryParse(sizeStr, out double size))
                {
                    EnglishSubtitle.FontSize = size;
                }
            }
        }

        private void MenuHideJpSub_Click(object sender, RoutedEventArgs e)
        {
            _jpSubVisible = !_jpSubVisible;
            JapaneseSubtitle.Visibility = _jpSubVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void MenuHideEnSub_Click(object sender, RoutedEventArgs e)
        {
            _enSubVisible = !_enSubVisible;
            EnglishSubtitle.Visibility = _enSubVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        #endregion

        #region スナップショット

        private void MenuSnapshot_Click(object sender, RoutedEventArgs e)
        {
            TakeSnapshot();
        }

        private void TakeSnapshot()
        {
            if (_mediaPlayer == null || !_mediaPlayer.IsPlaying) return;

            try
            {
                // スナップショット保存先
                var picturesDir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                var snapshotDir = Path.Combine(picturesDir, "DVDPlayer_Snapshots");
                Directory.CreateDirectory(snapshotDir);

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var jpText = JapaneseSubtitle.Text?.Replace("\n", " ") ?? "";
                var enText = EnglishSubtitle.Text?.Replace("\n", " ") ?? "";

                // VLC のスナップショット機能を使用
                var imagePath = Path.Combine(snapshotDir, $"snapshot_{timestamp}.png");
                _mediaPlayer.TakeSnapshot(0, imagePath, 0, 0);

                // 字幕テキストも一緒にテキストファイルに保存
                var textPath = Path.Combine(snapshotDir, $"snapshot_{timestamp}.txt");
                var playTime = TimeSpan.FromMilliseconds(_mediaPlayer.Time).ToString(@"hh\:mm\:ss");
                File.WriteAllText(textPath,
                    $"Time: {playTime}\n" +
                    $"Japanese: {jpText}\n" +
                    $"English: {enText}\n");

                // 保存通知（一時的に字幕エリアに表示）
                var originalJp = JapaneseSubtitle.Text;
                JapaneseSubtitle.Text = $"📸 スナップショット保存: {imagePath}";
                JapaneseSubtitle.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA6, 0xE3, 0xA1));

                // 2秒後に元に戻す
                System.Threading.Tasks.Task.Delay(2000).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        JapaneseSubtitle.Text = originalJp;
                        JapaneseSubtitle.Foreground = new SolidColorBrush(Colors.White);
                    });
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"スナップショットの保存に失敗しました:\n{ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region 字幕イベント

        private void OnJapaneseSubtitleChanged(string text)
        {
            Dispatcher.Invoke(() =>
            {
                if (_jpSubVisible)
                    JapaneseSubtitle.Text = text;
            });
        }

        private void OnEnglishSubtitleChanged(string text)
        {
            Dispatcher.Invoke(() =>
            {
                if (_enSubVisible)
                    EnglishSubtitle.Text = text;
            });
        }

        #endregion

        #region UI 更新

        private void UiTimer_Tick(object? sender, EventArgs e)
        {
            if (_mediaPlayer == null || _isSeeking) return;

            if (_mediaPlayer.IsPlaying)
            {
                var current = TimeSpan.FromMilliseconds(_mediaPlayer.Time);
                var total = TimeSpan.FromMilliseconds(_mediaPlayer.Length);

                TxtCurrentTime.Text = current.ToString(@"hh\:mm\:ss");
                TxtTotalTime.Text = total.ToString(@"hh\:mm\:ss");

                if (_mediaPlayer.Length > 0)
                {
                    SeekBar.Value = (double)_mediaPlayer.Time / _mediaPlayer.Length * 100;
                }

                // A-B リピートのチェック
                if (_abRepeatActive && _abPointA >= 0 && _abPointB >= 0)
                {
                    if (_mediaPlayer.Time >= _abPointB)
                    {
                        _mediaPlayer.Time = _abPointA;
                    }
                }

                // レジューム位置を定期的に保存（30秒ごと）
                if (_mediaPlayer.Time % 30000 < 200)
                {
                    SaveResumePosition();
                }
            }
        }

        #endregion

        #region コントロールバーのイベント

        private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayer == null) return;

            if (_mediaPlayer.IsPlaying)
            {
                _mediaPlayer.Pause();
                _subtitleSync?.Stop();
                SaveResumePosition();
            }
            else
            {
                _mediaPlayer.Play();
                _subtitleSync?.Start();
            }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayer == null) return;
            SaveResumePosition();
            _subtitleSync?.Stop();
            ClearABRepeat();
            System.Threading.Tasks.Task.Run(() => _mediaPlayer.Stop());
        }

        private void BtnPrevChapter_Click(object sender, RoutedEventArgs e)
        {
            _mediaPlayer?.PreviousChapter();
        }

        private void BtnNextChapter_Click(object sender, RoutedEventArgs e)
        {
            _mediaPlayer?.NextChapter();
        }

        private void BtnOpenDvd_Click(object sender, RoutedEventArgs e)
        {
            var dvdDrives = DvdManager.GetDvdDrives();

            if (dvdDrives.Count == 0)
            {
                OpenMediaFile();
            }
            else if (dvdDrives.Count == 1)
            {
                PlayDisc(dvdDrives[0].Name.TrimEnd('\\'));
            }
            else
            {
                var driveWithMedia = DvdManager.FindDriveWithMedia();
                if (driveWithMedia != null)
                {
                    PlayDisc(driveWithMedia.Name.TrimEnd('\\'));
                }
                else
                {
                    PlayDisc(dvdDrives[0].Name.TrimEnd('\\'));
                }
            }
        }

        private void MenuOpenFile_Click(object sender, RoutedEventArgs e)
        {
            OpenMediaFile();
        }

        private void OpenMediaFile()
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "メディアファイルを開く",
                Filter = "動画ファイル|*.mp4;*.mkv;*.avi;*.wmv;*.mov;*.iso|すべてのファイル|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                PlayMedia(openFileDialog.FileName);
            }
        }

        private void BtnLoadSubJp_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "日本語字幕ファイルを選択",
                Filter = "SRT 字幕ファイル|*.srt|すべてのファイル|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                _subtitleSync?.LoadJapaneseSubtitles(openFileDialog.FileName);
                _jpSubVisible = true;
                JapaneseSubtitle.Visibility = Visibility.Visible;
            }
        }

        private void BtnLoadSubEn_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "英語字幕ファイルを選択",
                Filter = "SRT 字幕ファイル|*.srt|すべてのファイル|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                _subtitleSync?.LoadEnglishSubtitles(openFileDialog.FileName);
                _enSubVisible = true;
                EnglishSubtitle.Visibility = Visibility.Visible;
            }
        }

        private void BtnVolume_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayer == null) return;

            _mediaPlayer.Mute = !_mediaPlayer.Mute;
            BtnVolume.Content = _mediaPlayer.Mute ? "🔇" : "🔊";
        }

        private void VolumeBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_mediaPlayer != null)
            {
                _mediaPlayer.Volume = (int)VolumeBar.Value;
            }
        }

        private void BtnFullScreen_Click(object sender, RoutedEventArgs e)
        {
            ToggleFullScreen();
        }

        #endregion

        #region シークバー

        private void SeekBar_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isSeeking = true;
        }

        private void SeekBar_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isSeeking = false;
            if (_mediaPlayer != null && _mediaPlayer.Length > 0)
            {
                var newTime = (long)(SeekBar.Value / 100.0 * _mediaPlayer.Length);
                _mediaPlayer.Time = newTime;
            }
        }

        private void SeekBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isSeeking && _mediaPlayer != null && _mediaPlayer.Length > 0)
            {
                var newTime = TimeSpan.FromMilliseconds(SeekBar.Value / 100.0 * _mediaPlayer.Length);
                TxtCurrentTime.Text = newTime.ToString(@"hh\:mm\:ss");
            }
        }

        #endregion

        #region フルスクリーン

        private void ToggleFullScreen()
        {
            if (_isFullScreen)
            {
                WindowStyle = _previousWindowStyle;
                WindowState = _previousWindowState;
                ResizeMode = _previousResizeMode;
                BtnFullScreen.Content = "⛶";
            }
            else
            {
                _previousWindowState = WindowState;
                _previousWindowStyle = WindowStyle;
                _previousResizeMode = ResizeMode;
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
                ResizeMode = ResizeMode.NoResize;
                BtnFullScreen.Content = "🗗";
            }
            _isFullScreen = !_isFullScreen;
        }

        #endregion

        #region キーボードショートカット

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Space:
                    BtnPlayPause_Click(sender, e);
                    e.Handled = true;
                    break;
                case Key.F:
                case Key.F11:
                    ToggleFullScreen();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    if (_isFullScreen) ToggleFullScreen();
                    e.Handled = true;
                    break;
                case Key.Left:
                    if (_mediaPlayer != null)
                    {
                        _mediaPlayer.Time = Math.Max(0, _mediaPlayer.Time - 10000);
                        e.Handled = true;
                    }
                    break;
                case Key.Right:
                    if (_mediaPlayer != null)
                    {
                        _mediaPlayer.Time += 10000;
                        e.Handled = true;
                    }
                    break;
                case Key.Up:
                    VolumeBar.Value = Math.Min(100, VolumeBar.Value + 5);
                    e.Handled = true;
                    break;
                case Key.Down:
                    VolumeBar.Value = Math.Max(0, VolumeBar.Value - 5);
                    e.Handled = true;
                    break;
                case Key.M:
                    BtnVolume_Click(sender, e);
                    e.Handled = true;
                    break;
                case Key.S:
                    TakeSnapshot();
                    e.Handled = true;
                    break;
                // A-B リピート: [ キーで A 地点、] キーで B 地点
                case Key.OemOpenBrackets:
                    SetPointA();
                    _abClickState = 1;
                    e.Handled = true;
                    break;
                case Key.OemCloseBrackets:
                    SetPointB();
                    _abClickState = 2;
                    e.Handled = true;
                    break;
                case Key.OemBackslash:
                    ClearABRepeat();
                    e.Handled = true;
                    break;
                // 再生速度: - で遅く、= で速く
                case Key.OemMinus:
                    SetPlaybackSpeed(Math.Max(0.25f, _currentSpeed - 0.25f));
                    e.Handled = true;
                    break;
                case Key.OemPlus:
                    SetPlaybackSpeed(Math.Min(4.0f, _currentSpeed + 0.25f));
                    e.Handled = true;
                    break;
                // 速度リセット
                case Key.D0:
                    SetPlaybackSpeed(1.0f);
                    e.Handled = true;
                    break;
            }
        }

        #endregion

        #region マウスイベント

        private void VideoArea_MouseMove(object sender, MouseEventArgs e)
        {
            ControlBar.Visibility = Visibility.Visible;
        }

        private void SubtitleOverlay_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            // 右クリックメニューはContextMenuで自動表示
        }

        #endregion
    }
}
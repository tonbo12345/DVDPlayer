using System.Windows;
using System.Windows.Input;
using LibVLCSharp.Shared;
using Microsoft.Win32;
using DVDPlayer.Services;

namespace DVDPlayer
{
    /// <summary>
    /// DVD Player メインウィンドウ
    /// デュアル字幕（日本語/英語）表示対応
    /// </summary>
    public partial class MainWindow : Window
    {
        private LibVLC? _libVLC;
        private MediaPlayer? _mediaPlayer;
        private SubtitleSyncService? _subtitleSync;
        private System.Windows.Threading.DispatcherTimer? _uiTimer;
        private bool _isSeeking = false;
        private bool _isFullScreen = false;
        private WindowState _previousWindowState;
        private WindowStyle _previousWindowStyle;
        private ResizeMode _previousResizeMode;

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
                "--dvdnav",
                "--no-video-title-show"
            );

            _mediaPlayer = new MediaPlayer(_libVLC);
            VideoView.MediaPlayer = _mediaPlayer;

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

            // UI 更新タイマー（シークバー・時間表示用）
            _uiTimer = new System.Windows.Threading.DispatcherTimer();
            _uiTimer.Interval = TimeSpan.FromMilliseconds(250);
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

            // VLC の内蔵字幕を無効化（外部SRT字幕オーバーレイを使用）
            _mediaPlayer.SetSpu(-1);

            // DVD の自動検出
            AutoDetectDvd();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _subtitleSync?.Stop();
            _subtitleSync?.Dispose();
            _uiTimer?.Stop();

            _mediaPlayer?.Stop();
            _mediaPlayer?.Dispose();
            _libVLC?.Dispose();
        }

        #endregion

        #region DVD 操作

        /// <summary>DVD ドライブを自動検出してメッセージを表示</summary>
        private void AutoDetectDvd()
        {
            var dvdDrive = DvdManager.FindDvdWithMedia();
            if (dvdDrive != null)
            {
                var result = MessageBox.Show(
                    $"DVD が検出されました: {dvdDrive.Name}\n再生しますか？",
                    "DVD 検出",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    PlayDvd(dvdDrive.Name.TrimEnd('\\'));
                }
            }
        }

        /// <summary>DVD を再生する</summary>
        private void PlayDvd(string driveLetter)
        {
            if (_libVLC == null || _mediaPlayer == null) return;

            try
            {
                var dvdUri = DvdManager.GetDvdUri(driveLetter);
                var media = new Media(_libVLC, new Uri(dvdUri));
                _mediaPlayer.Media = media;
                _mediaPlayer.Play();
                _subtitleSync?.Start();

                // VLC の内蔵字幕を無効化
                System.Threading.Tasks.Task.Delay(2000).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        _mediaPlayer?.SetSpu(-1);
                    });
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"DVD の再生に失敗しました:\n{ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>メディアファイルを再生する（DVD以外も対応）</summary>
        private void PlayMedia(string path)
        {
            if (_libVLC == null || _mediaPlayer == null) return;

            try
            {
                var media = new Media(_libVLC, new Uri(path));
                _mediaPlayer.Media = media;
                _mediaPlayer.Play();
                _subtitleSync?.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"再生に失敗しました:\n{ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region 字幕イベント

        private void OnJapaneseSubtitleChanged(string text)
        {
            Dispatcher.Invoke(() =>
            {
                JapaneseSubtitle.Text = text;
            });
        }

        private void OnEnglishSubtitleChanged(string text)
        {
            Dispatcher.Invoke(() =>
            {
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
            _subtitleSync?.Stop();

            // Stop は別スレッドから呼ぶ必要がある
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
            // DVD ドライブの選択ダイアログ
            var dvdDrives = DvdManager.GetDvdDrives();

            if (dvdDrives.Count == 0)
            {
                // DVD ドライブがない場合、ファイルを開く
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
            else if (dvdDrives.Count == 1)
            {
                PlayDvd(dvdDrives[0].Name.TrimEnd('\\'));
            }
            else
            {
                // 複数ドライブの場合、最初のメディアがあるドライブを選択
                var dvdWithMedia = DvdManager.FindDvdWithMedia();
                if (dvdWithMedia != null)
                {
                    PlayDvd(dvdWithMedia.Name.TrimEnd('\\'));
                }
                else
                {
                    PlayDvd(dvdDrives[0].Name.TrimEnd('\\'));
                }
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
            // シーク中のみ時間表示を更新
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
                // ウィンドウモードに戻る
                WindowStyle = _previousWindowStyle;
                WindowState = _previousWindowState;
                ResizeMode = _previousResizeMode;
                BtnFullScreen.Content = "⛶";
            }
            else
            {
                // フルスクリーンに切り替え
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
                    if (_isFullScreen)
                    {
                        ToggleFullScreen();
                        e.Handled = true;
                    }
                    break;
                case Key.Left:
                    if (_mediaPlayer != null)
                    {
                        _mediaPlayer.Time -= 10000; // 10秒戻る
                        e.Handled = true;
                    }
                    break;
                case Key.Right:
                    if (_mediaPlayer != null)
                    {
                        _mediaPlayer.Time += 10000; // 10秒進む
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
            }
        }

        #endregion

        #region マウスイベント

        private void VideoArea_MouseMove(object sender, MouseEventArgs e)
        {
            // マウスが動いたらコントロールバーを表示
            ControlBar.Visibility = Visibility.Visible;
        }

        #endregion
    }
}
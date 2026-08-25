using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using QuizHelper.Services;
using QuizHelper.Windows;

namespace QuizHelper
{
    public partial class MainWindow : Window
    {
        private readonly OcrService _ocrService;
        private readonly CsvDataService _csvDataService;
        private readonly WindowCaptureService _windowCaptureService;
        private readonly DispatcherTimer _scanTimer;

        private BorderWindow? _borderWindow;
        private System.Drawing.Rectangle _captureRegion;
        private bool _isScanning;
        private string _lastOcrText = string.Empty;
        private bool _isMinimizedMode; // 스캔 중 최소화 UI 모드

        // 창 기반 캡처를 위한 필드
        private IntPtr _targetWindowHandle = IntPtr.Zero;
        private string _targetWindowTitle = string.Empty;

        // 설정 저장 경로
        private static readonly string SettingsPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        private const int ScanIntervalMs = 2000; // Scan every 2 seconds
        private const int FuzzyMatchThreshold = 80; // Minimum match score (increased for better accuracy)

        /// <summary>
        /// Reads the version stamped in from the csproj so the badge always matches
        /// the released build instead of a hard-coded string.
        /// </summary>
        private static string GetVersionLabel()
        {
            var info = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            if (string.IsNullOrWhiteSpace(info))
                return "v2";

            // strip any build metadata suffix, e.g. "2.0.1+abc1234"
            var plus = info.IndexOf('+');
            if (plus > 0)
                info = info.Substring(0, plus);

            return "v" + info;
        }

        public MainWindow()
        {
            InitializeComponent();
            VersionBadge.Text = GetVersionLabel();

            _ocrService = new OcrService();
            _csvDataService = new CsvDataService();
            _windowCaptureService = new WindowCaptureService();

            _scanTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(ScanIntervalMs)
            };
            _scanTimer.Tick += ScanTimer_Tick;

            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await InitializeCategoriesAsync();
            LoadLastWindowSettings();
        }

        /// <summary>
        /// 마지막으로 사용한 창 설정 로드
        /// </summary>
        private void LoadLastWindowSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    
                    if (settings != null && !string.IsNullOrEmpty(settings.LastWindowTitle))
                    {
                        // 저장된 창 제목으로 현재 실행 중인 창 찾기
                        var windows = _windowCaptureService.GetOpenWindows();
                        var matchedWindow = windows.Find(w => 
                            w.Title.Contains(settings.LastWindowTitle) || 
                            settings.LastWindowTitle.Contains(w.Title));
                        
                        if (matchedWindow != null)
                        {
                            _targetWindowHandle = matchedWindow.Handle;
                            _targetWindowTitle = matchedWindow.Title;
                            UpdateSelectedWindowDisplay();
                            
                            ResultText.Text = $"이전 창 '{TruncateText(_targetWindowTitle, 30)}' 감지됨\n🎯 영역 선택을 클릭하여 영역을 지정하세요.";
                        }
                    }
                }
            }
            catch
            {
                // 설정 로드 실패는 무시
            }
        }

        /// <summary>
        /// 현재 창 설정 저장
        /// </summary>
        private void SaveWindowSettings()
        {
            try
            {
                var settings = new AppSettings
                {
                    LastWindowTitle = _targetWindowTitle
                };
                
                var json = JsonSerializer.Serialize(settings);
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // 저장 실패는 무시
            }
        }

        /// <summary>
        /// 선택된 창 정보 UI 업데이트
        /// </summary>
        private void UpdateSelectedWindowDisplay()
        {
            if (_targetWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(_targetWindowTitle))
            {
                SelectedWindowText.Text = $"🪟 {TruncateText(_targetWindowTitle, 40)}";
                SelectedWindowText.Visibility = Visibility.Visible;
            }
            else
            {
                SelectedWindowText.Text = "";
                SelectedWindowText.Visibility = Visibility.Collapsed;
            }
        }

        private async Task InitializeCategoriesAsync()
        {
            try
            {
                StatusText.Text = "상태: 카테고리 로딩 중...";
                
                // Get available categories
                var categories = _csvDataService.GetAvailableCategories();
                
                CategoryComboBox.Items.Clear();
                foreach (var category in categories)
                {
                    CategoryComboBox.Items.Add(category);
                }

                // Select first category by default
                if (CategoryComboBox.Items.Count > 0)
                {
                    CategoryComboBox.SelectedIndex = 0;
                }
                else
                {
                    StatusText.Text = "상태: CSV 파일을 찾을 수 없습니다";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"상태: 오류 발생";
                MessageBox.Show($"카테고리 로딩 실패: {ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void CategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CategoryComboBox.SelectedItem is string selectedCategory)
            {
                await LoadCategoryDataAsync(selectedCategory);
            }
        }

        private async Task LoadCategoryDataAsync(string categoryName)
        {
            try
            {
                StatusText.Text = $"상태: {categoryName} 데이터 로딩 중...";
                int count = await _csvDataService.LoadCategoryAsync(categoryName);
                StatusText.Text = $"상태: 준비됨 ({count}개 문제 로드됨)";
                
                // Reset result display
                ClearResultDisplay();
                ResultText.Text = "🎯 영역 선택 버튼을 클릭하여 시작하세요.";
                ResultText.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"상태: 데이터 로딩 오류";
                MessageBox.Show($"CSV 데이터 로딩 실패: {ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ClearResultDisplay()
        {
            QuestionText.Text = "";
            QuestionText.Visibility = Visibility.Collapsed;
            AnswerText.Text = "";
            AnswerText.Visibility = Visibility.Collapsed;
            AlternativesBorder.Visibility = Visibility.Collapsed;
            AlternativesList.ItemsSource = null;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            _scanTimer.Stop();
            _borderWindow?.Close();
            SaveWindowSettings();
        }

        /// <summary>
        /// 영역 선택 버튼 클릭 - 창 선택과 영역 선택을 통합
        /// </summary>
        private void SelectAreaButton_Click(object sender, RoutedEventArgs e)
        {
            // 창이 선택되지 않았거나 유효하지 않은 경우 창 선택부터 시작
            if (_targetWindowHandle == IntPtr.Zero || !_windowCaptureService.IsWindowValid(_targetWindowHandle))
            {
                if (!SelectTargetWindow())
                {
                    return; // 창 선택이 취소됨
                }
            }

            // 영역 선택 진행
            SelectCaptureRegion();
        }

        /// <summary>
        /// 영역 선택 버튼 우클릭 - 다른 창 선택
        /// </summary>
        private void SelectAreaButton_RightClick(object sender, MouseButtonEventArgs e)
        {
            // 강제로 창 선택 다이얼로그 표시
            if (SelectTargetWindow())
            {
                // 새 창 선택 후 영역도 선택
                SelectCaptureRegion();
            }
        }

        /// <summary>
        /// 창 선택 다이얼로그 표시
        /// </summary>
        /// <returns>창이 선택되었으면 true</returns>
        private bool SelectTargetWindow()
        {
            var windowSelectionWindow = new WindowSelectionWindow();
            if (windowSelectionWindow.ShowDialog() == true && windowSelectionWindow.SelectedWindow != null)
            {
                var selectedWindow = windowSelectionWindow.SelectedWindow;
                _targetWindowHandle = selectedWindow.Handle;
                _targetWindowTitle = selectedWindow.Title;
                
                UpdateSelectedWindowDisplay();
                SaveWindowSettings();
                
                // 기존 영역 초기화
                _captureRegion = System.Drawing.Rectangle.Empty;
                _borderWindow?.Hide();
                StartStopButton.IsEnabled = false;
                
                return true;
            }
            
            return false;
        }

        /// <summary>
        /// 캡처 영역 선택
        /// </summary>
        private void SelectCaptureRegion()
        {
            // 창이 유효한지 확인
            if (!_windowCaptureService.IsWindowValid(_targetWindowHandle))
            {
                MessageBox.Show("선택한 창이 더 이상 존재하지 않습니다. 다시 선택해주세요.", "오류", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                _targetWindowHandle = IntPtr.Zero;
                _targetWindowTitle = string.Empty;
                UpdateSelectedWindowDisplay();
                return;
            }

            // Temporarily hide windows for selection
            _borderWindow?.Hide();
            this.Hide();

            // 선택된 창의 현재 위치와 크기를 가져옴
            var windowBounds = _windowCaptureService.GetWindowBounds(_targetWindowHandle);
            
            var selectionWindow = new RegionSelectionWindow(windowBounds);
            if (selectionWindow.ShowDialog() == true)
            {
                // 창 내부 상대 좌표로 저장
                _captureRegion = selectionWindow.SelectedRegion;

                // 화면 절대 좌표로 변환하여 BorderWindow에 표시
                var absoluteRegion = new System.Drawing.Rectangle(
                    windowBounds.X + _captureRegion.X,
                    windowBounds.Y + _captureRegion.Y,
                    _captureRegion.Width,
                    _captureRegion.Height
                );

                // Show the border window around selected region
                if (_borderWindow == null)
                {
                    _borderWindow = new BorderWindow();
                }

                _borderWindow.SetRegion(absoluteRegion);
                _borderWindow.Show();

                StartStopButton.IsEnabled = true;
                StatusText.Text = $"상태: 영역 선택됨 ({_captureRegion.Width}x{_captureRegion.Height})";
                
                ClearResultDisplay();
                ResultText.Text = "✅ 준비 완료! ▶ 시작 버튼을 눌러주세요.";
                ResultText.Visibility = Visibility.Visible;
            }

            this.Show();
        }

        private void StartStopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isScanning)
            {
                StopScanning();
            }
            else
            {
                StartScanning();
            }
        }

        private void StartScanning()
        {
            _isScanning = true;
            _lastOcrText = string.Empty;
            StartStopButton.Content = "⏹ 정지";
            StatusText.Text = "상태: 스캔 중...";
            _scanTimer.Start();

            // Enable minimized mode (hide UI except result area)
            SetMinimizedMode(true);

            // Perform immediate scan
            _ = PerformScanAsync();
        }

        private void StopScanning()
        {
            _isScanning = false;
            _scanTimer.Stop();
            StartStopButton.Content = "▶ 시작";
            StatusText.Text = $"상태: 정지됨 ({_csvDataService.QuestionCount}개 문제 로드됨)";

            // Disable minimized mode (show full UI)
            SetMinimizedMode(false);
        }

        private async void ScanTimer_Tick(object? sender, EventArgs e)
        {
            await PerformScanAsync();
        }

        private async Task PerformScanAsync()
        {
            if (!_isScanning) return;

            try
            {
                // 창이 유효한지 확인
                if (!_windowCaptureService.IsWindowValid(_targetWindowHandle))
                {
                    StopScanning();
                    ClearResultDisplay();
                    ResultText.Text = "⚠️ 선택한 창이 닫혔습니다.\n🎯 영역 선택을 클릭하여 다시 시작하세요.";
                    ResultText.Visibility = Visibility.Visible;
                    _targetWindowHandle = IntPtr.Zero;
                    _targetWindowTitle = string.Empty;
                    UpdateSelectedWindowDisplay();
                    return;
                }

                // 창 기반 캡처 (창이 가려져도 캡처 가능)
                var bitmap = _windowCaptureService.CaptureWindow(_targetWindowHandle, _captureRegion);
                if (bitmap == null)
                {
                    // 캡처 실패 시 화면 캡처로 폴백
                    var windowBounds = _windowCaptureService.GetWindowBounds(_targetWindowHandle);
                    var absoluteRegion = new System.Drawing.Rectangle(
                        windowBounds.X + _captureRegion.X,
                        windowBounds.Y + _captureRegion.Y,
                        _captureRegion.Width,
                        _captureRegion.Height
                    );
                    bitmap = CaptureScreen(absoluteRegion);
                }
                
                if (bitmap == null) return;

                // BorderWindow 위치 업데이트 (창이 이동했을 경우)
                UpdateBorderWindowPosition();

                // Perform OCR
                string ocrText = await _ocrService.RecognizeTextAsync(bitmap);
                bitmap.Dispose();

                // Skip if text hasn't changed (de-duplication)
                if (string.Equals(ocrText.Trim(), _lastOcrText.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                _lastOcrText = ocrText;

                if (string.IsNullOrWhiteSpace(ocrText))
                {
                    ClearResultDisplay();
                    ResultText.Text = "선택된 영역에서 텍스트가 감지되지 않았습니다.";
                    ResultText.Visibility = Visibility.Visible;
                    return;
                }

                // 1단계: FindBestMatch()로 1등 빠르게 표시 (Early Exit 활성화)
                var bestMatch = _csvDataService.FindBestMatch(ocrText, FuzzyMatchThreshold);

                if (bestMatch != null)
                {
                    // 즉시 1등 표시
                    ResultText.Visibility = Visibility.Collapsed;
                    
                    QuestionText.Text = TruncateText(bestMatch.Question, 150);
                    QuestionText.Visibility = Visibility.Visible;
                    
                    AnswerText.Text = $"✓ {bestMatch.Answer}";
                    AnswerText.Visibility = Visibility.Visible;
                    
                    // kkong 카테고리일 때 정답 자동 복사
                    if (_csvDataService.CurrentCategory?.Equals("kkong", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        try
                        {
                            string cleanedAnswer = CleanAnswer(bestMatch.Answer);
                            Clipboard.SetText(cleanedAnswer);
                            System.Diagnostics.Debug.WriteLine($"[CLIPBOARD] kkong 정답 복사됨: {cleanedAnswer}");
                            
                            // 시각적 피드백 표시
                            ShowCopyFeedback(cleanedAnswer);
                        }
                        catch (Exception clipEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"[CLIPBOARD] 복사 실패: {clipEx.Message}");
                        }
                    }
                    else
                    {
                        StatusText.Text = $"상태: 매칭 발견 (정확도: {bestMatch.Score}%)";
                    }
                    
                    // 2, 3등은 일단 숨김
                    AlternativesBorder.Visibility = Visibility.Collapsed;
                    AlternativesList.ItemsSource = null;
                    
                    // 2단계: 백그라운드에서 2, 3등 검색 후 업데이트
                    string capturedOcrText = ocrText;
                    string bestAnswer = bestMatch.Answer;
                    _ = Task.Run(() =>
                    {
                        var results = _csvDataService.FindTopMatches(capturedOcrText, FuzzyMatchThreshold, 3);
                        
                        // UI 스레드에서 2, 3등 업데이트
                        Dispatcher.Invoke(() =>
                        {
                            // OCR 텍스트가 변경되지 않았을 때만 업데이트
                            if (_lastOcrText == capturedOcrText && results.HasAlternatives)
                            {
                                // 중복 답 제거: 1등과 같은 답은 제외
                                var alternatives = results.Candidates
                                    .Skip(1)
                                    .Where(c => c.Answer != bestAnswer)
                                    .ToList();
                                
                                if (alternatives.Count > 0)
                                {
                                    AlternativesList.ItemsSource = alternatives;
                                    AlternativesBorder.Visibility = Visibility.Visible;
                                }
                            }
                        });
                    });
                }
                else
                {
                    ClearResultDisplay();
                    ResultText.Text = $"매칭되는 답을 찾지 못했습니다.\n\n감지된 텍스트: \"{TruncateText(ocrText, 80)}\"";
                    ResultText.Visibility = Visibility.Visible;
                    StatusText.Text = "상태: 스캔 중...";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"상태: 스캔 오류";
                System.Diagnostics.Debug.WriteLine($"Scan error: {ex.Message}");
            }
        }

        private void UpdateBorderWindowPosition()
        {
            if (_borderWindow == null || _targetWindowHandle == IntPtr.Zero) return;
            
            var windowBounds = _windowCaptureService.GetWindowBounds(_targetWindowHandle);
            var absoluteRegion = new System.Drawing.Rectangle(
                windowBounds.X + _captureRegion.X,
                windowBounds.Y + _captureRegion.Y,
                _captureRegion.Width,
                _captureRegion.Height
            );
            
            _borderWindow.SetRegion(absoluteRegion);
        }

        private static System.Drawing.Bitmap? CaptureScreen(System.Drawing.Rectangle region)
        {
            if (region.Width <= 0 || region.Height <= 0)
                return null;

            var bitmap = new System.Drawing.Bitmap(region.Width, region.Height);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(region.Location, System.Drawing.Point.Empty, region.Size);
            }
            return bitmap;
        }

        private static string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = text.Replace("\r\n", " ").Replace("\n", " ");
            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
        }

        /// <summary>
        /// 정답에서 순수한 답만 추출 (닉네임, 별표 등 제거)
        /// </summary>
        private static string CleanAnswer(string answer)
        {
            if (string.IsNullOrWhiteSpace(answer))
                return string.Empty;

            string cleaned = answer;

            // 1. 슬래시(/)로 구분된 경우 첫 번째만 선택
            //    예: "상파울로 / 상파울루" → "상파울로"
            if (cleaned.Contains('/'))
            {
                cleaned = cleaned.Split('/')[0].Trim();
            }

            // 2. 특수문자(별표 등) 이후 텍스트 제거
            //    예: "조개★닉네임" → "조개"
            var separators = new[] { '★', '☆', '*', '※', '•', '▪', '▫' };
            foreach (var sep in separators)
            {
                int idx = cleaned.IndexOf(sep);
                if (idx > 0)
                {
                    cleaned = cleaned.Substring(0, idx).Trim();
                    break;
                }
            }

            // 3. 하이픈(-) 이후 제거
            //    예: "시너지-닉네임" → "시너지"
            int hyphenIdx = cleaned.IndexOf('-');
            if (hyphenIdx > 0)
            {
                cleaned = cleaned.Substring(0, hyphenIdx).Trim();
            }

            return cleaned.Trim();
        }

        /// <summary>
        /// 정답 텍스트 클릭 시 클립보드에 복사
        /// </summary>
        private void AnswerText_Click(object sender, MouseButtonEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(AnswerText.Text))
                return;

            try
            {
                // "✓ " 접두사 제거 후 정제된 답 복사
                string answer = AnswerText.Text;
                if (answer.StartsWith("✓ "))
                {
                    answer = answer.Substring(2);
                }

                string cleanedAnswer = CleanAnswer(answer);
                Clipboard.SetText(cleanedAnswer);
                
                // 시각적 피드백 표시
                ShowCopyFeedback(cleanedAnswer);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CLIPBOARD] 복사 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 2, 3순위 답 클릭 시 클립보드에 복사
        /// </summary>
        private void AlternativeItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is Models.MatchResult match)
            {
                try
                {
                    string cleanedAnswer = CleanAnswer(match.Answer);
                    Clipboard.SetText(cleanedAnswer);
                    StatusText.Text = $"📋 복사됨: {cleanedAnswer}";
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CLIPBOARD] 복사 실패: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 복사 성공 시 시각적 피드백 표시
        /// </summary>
        private void ShowCopyFeedback(string copiedText)
        {
            // 1. StatusText에 복사됨 메시지 표시
            StatusText.Text = $"📋 복사됨: {copiedText}";

            // 2. 정답 텍스트 색상을 초록색으로 변경
            AnswerText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x00, 0xFF, 0x00));

            // 3. 0.5초 후 원래 노란색으로 복원
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += (s, args) =>
            {
                AnswerText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0x00));
                timer.Stop();
            };
            timer.Start();
        }

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (OpacityValueText != null && UiBorder != null)
            {
                // Apply opacity to UI background
                UiBorder.Opacity = e.NewValue;
                
                // For ResultBorder, change background alpha instead of Opacity
                // This way the background becomes transparent but texts stay fully visible
                byte alpha = (byte)(0x22 * e.NewValue);  // 0x22 is the original alpha
                ResultBorder.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF));
                
                OpacityValueText.Text = $"{(int)(e.NewValue * 100)}%";
            }
        }

        /// <summary>
        /// 최소화 모드 설정 (스캔 중 result area만 표시)
        /// </summary>
        private void SetMinimizedMode(bool minimized)
        {
            _isMinimizedMode = minimized;
            var visibility = minimized ? Visibility.Collapsed : Visibility.Visible;

            HeaderPanel.Visibility = visibility;
            ControlPanel.Visibility = visibility;
            SelectedWindowText.Visibility = visibility;
            StatusText.Visibility = visibility;
            OpacityPanel.Visibility = visibility;

            // 최소화 모드일 때 창 크기 자동 조절
            if (minimized)
            {
                // 최소 크기로 축소 (result area만 보이도록)
                this.MinHeight = 100;
            }
            else
            {
                this.MinHeight = 200;
                // 선택된 창 정보가 있으면 다시 표시
                if (_targetWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(_targetWindowTitle))
                {
                    SelectedWindowText.Visibility = Visibility.Visible;
                }
            }
        }

        /// <summary>
        /// 마우스가 창 위에 올라왔을 때 - 전체 UI 표시
        /// </summary>
        private void Window_MouseEnter(object sender, MouseEventArgs e)
        {
            if (_isMinimizedMode)
            {
                // 스캔 중이지만 마우스가 올라오면 전체 UI 표시
                HeaderPanel.Visibility = Visibility.Visible;
                ControlPanel.Visibility = Visibility.Visible;
                StatusText.Visibility = Visibility.Visible;
                OpacityPanel.Visibility = Visibility.Visible;
                
                if (_targetWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(_targetWindowTitle))
                {
                    SelectedWindowText.Visibility = Visibility.Visible;
                }
            }
        }

        /// <summary>
        /// 마우스가 창에서 벗어났을 때 - 최소화 모드면 UI 숨김
        /// </summary>
        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_isMinimizedMode)
            {
                // 스캔 중이면 다시 UI 숨김
                HeaderPanel.Visibility = Visibility.Collapsed;
                ControlPanel.Visibility = Visibility.Collapsed;
                SelectedWindowText.Visibility = Visibility.Collapsed;
                StatusText.Visibility = Visibility.Collapsed;
                OpacityPanel.Visibility = Visibility.Collapsed;
            }
        }
    }

    /// <summary>
    /// 앱 설정을 저장하기 위한 클래스
    /// </summary>
    public class AppSettings
    {
        public string LastWindowTitle { get; set; } = string.Empty;
    }
}

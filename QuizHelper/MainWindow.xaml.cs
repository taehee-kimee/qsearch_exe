using System;
using System.Threading.Tasks;
using System.Windows;
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
        private readonly DispatcherTimer _scanTimer;

        private BorderWindow? _borderWindow;
        private System.Drawing.Rectangle _captureRegion;
        private bool _isScanning;
        private string _lastOcrText = string.Empty;

        private const int ScanIntervalMs = 2000; // Scan every 2 seconds
        private const int FuzzyMatchThreshold = 70; // Minimum match score

        public MainWindow()
        {
            InitializeComponent();

            _ocrService = new OcrService();
            _csvDataService = new CsvDataService();

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
            await LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            try
            {
                StatusText.Text = "Status: Loading quiz data...";
                int count = await _csvDataService.LoadAllCsvFilesAsync();
                StatusText.Text = $"Status: Ready ({count} questions loaded)";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Status: Error loading data";
                MessageBox.Show($"Failed to load CSV data: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
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
        }

        private void SelectAreaButton_Click(object sender, RoutedEventArgs e)
        {
            // Temporarily hide windows for selection
            _borderWindow?.Hide();
            this.Hide();

            var selectionWindow = new RegionSelectionWindow();
            if (selectionWindow.ShowDialog() == true)
            {
                _captureRegion = selectionWindow.SelectedRegion;

                // Show the border window around selected region
                if (_borderWindow == null)
                {
                    _borderWindow = new BorderWindow();
                }

                _borderWindow.SetRegion(_captureRegion);
                _borderWindow.Show();

                StartStopButton.IsEnabled = true;
                StatusText.Text = $"Status: Area selected ({_captureRegion.Width}x{_captureRegion.Height})";
                ResultText.Text = "Area selected! Press Start to begin scanning.";
                ResultText.Foreground = System.Windows.Media.Brushes.White;
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
            StartStopButton.Content = "⏹ Stop";
            StatusText.Text = "Status: Scanning...";
            _scanTimer.Start();

            // Perform immediate scan
            _ = PerformScanAsync();
        }

        private void StopScanning()
        {
            _isScanning = false;
            _scanTimer.Stop();
            StartStopButton.Content = "▶ Start";
            StatusText.Text = $"Status: Stopped ({_csvDataService.QuestionCount} questions loaded)";
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
                // Capture the selected region
                var bitmap = CaptureScreen(_captureRegion);
                if (bitmap == null) return;

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
                    ResultText.Text = "No text detected in the selected area.";
                    ResultText.Foreground = System.Windows.Media.Brushes.Gray;
                    return;
                }

                // Search for matching answer
                var result = _csvDataService.FindBestMatch(ocrText, FuzzyMatchThreshold);

                if (result != null)
                {
                    ResultText.Text = $"✓ {result.Answer}";
                    ResultText.Foreground = System.Windows.Media.Brushes.Yellow;
                    StatusText.Text = $"Status: Match found ({result.Score}% confidence)";
                }
                else
                {
                    ResultText.Text = $"No match found.\n\nDetected: \"{TruncateText(ocrText, 100)}\"";
                    ResultText.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(170, 255, 255, 255));
                    StatusText.Text = "Status: Scanning...";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Status: Scan error";
                System.Diagnostics.Debug.WriteLine($"Scan error: {ex.Message}");
            }
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

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (OpacityValueText != null)
            {
                this.Opacity = e.NewValue;
                OpacityValueText.Text = $"{(int)(e.NewValue * 100)}%";
            }
        }
    }
}

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace QuizHelper.Windows
{
    public partial class RegionSelectionWindow : Window
    {
        private Point _startPoint;
        private bool _isSelecting;
        private System.Drawing.Rectangle _windowBounds;

        public System.Drawing.Rectangle SelectedRegion { get; private set; }

        /// <summary>
        /// 전체 화면에서 영역 선택 (기본 생성자)
        /// </summary>
        public RegionSelectionWindow() : this(System.Drawing.Rectangle.Empty)
        {
        }

        /// <summary>
        /// 특정 창 영역 내에서만 선택할 수 있도록 합니다.
        /// </summary>
        /// <param name="windowBounds">선택 가능한 창의 화면 좌표 영역</param>
        public RegionSelectionWindow(System.Drawing.Rectangle windowBounds)
        {
            InitializeComponent();
            _windowBounds = windowBounds;
            
            Loaded += RegionSelectionWindow_Loaded;
        }

        private void RegionSelectionWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_windowBounds.IsEmpty)
            {
                // 전체 화면 모드
                WindowState = WindowState.Maximized;
                WindowHighlight.Visibility = Visibility.Collapsed;
                FullOverlay.Fill = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(0x44, 0x00, 0x00, 0x00));
            }
            else
            {
                // 특정 창 영역 모드 - 화면 전체 크기로 설정
                WindowState = WindowState.Normal;
                
                // 전체 화면 크기로 창 설정
                Left = 0;
                Top = 0;
                Width = SystemParameters.VirtualScreenWidth;
                Height = SystemParameters.VirtualScreenHeight;
                
                // 창 영역 하이라이트 표시
                Canvas.SetLeft(WindowHighlight, _windowBounds.X);
                Canvas.SetTop(WindowHighlight, _windowBounds.Y);
                WindowHighlight.Width = _windowBounds.Width;
                WindowHighlight.Height = _windowBounds.Height;
                WindowHighlight.Visibility = Visibility.Visible;
                
                // 안내 텍스트 위치 설정 (창 영역 내부 상단)
                Canvas.SetLeft(InstructionText, _windowBounds.X);
                Canvas.SetTop(InstructionText, _windowBounds.Y);
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var clickPoint = e.GetPosition(SelectionCanvas);
            
            // 특정 창 모드일 때 창 영역 밖 클릭은 무시
            if (!_windowBounds.IsEmpty)
            {
                if (!IsPointInWindowBounds(clickPoint))
                {
                    return;
                }
            }
            
            _startPoint = clickPoint;
            _isSelecting = true;

            // Position the selection rectangle
            Canvas.SetLeft(SelectionRectangle, _startPoint.X);
            Canvas.SetTop(SelectionRectangle, _startPoint.Y);
            SelectionRectangle.Width = 0;
            SelectionRectangle.Height = 0;
            SelectionRectangle.Visibility = Visibility.Visible;

            InstructionText.Visibility = Visibility.Collapsed;
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isSelecting) return;

            Point currentPoint = e.GetPosition(SelectionCanvas);
            
            // 특정 창 모드일 때 창 영역 내로 제한
            if (!_windowBounds.IsEmpty)
            {
                currentPoint = ClampToWindowBounds(currentPoint);
            }

            double x = System.Math.Min(_startPoint.X, currentPoint.X);
            double y = System.Math.Min(_startPoint.Y, currentPoint.Y);
            double width = System.Math.Abs(currentPoint.X - _startPoint.X);
            double height = System.Math.Abs(currentPoint.Y - _startPoint.Y);

            Canvas.SetLeft(SelectionRectangle, x);
            Canvas.SetTop(SelectionRectangle, y);
            SelectionRectangle.Width = width;
            SelectionRectangle.Height = height;
        }

        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isSelecting) return;
            _isSelecting = false;

            Point endPoint = e.GetPosition(SelectionCanvas);
            
            // 특정 창 모드일 때 창 영역 내로 제한
            if (!_windowBounds.IsEmpty)
            {
                endPoint = ClampToWindowBounds(endPoint);
            }

            // Calculate the rectangle in screen coordinates
            int x = (int)System.Math.Min(_startPoint.X, endPoint.X);
            int y = (int)System.Math.Min(_startPoint.Y, endPoint.Y);
            int width = (int)System.Math.Abs(endPoint.X - _startPoint.X);
            int height = (int)System.Math.Abs(endPoint.Y - _startPoint.Y);

            // Minimum size check
            if (width < 10 || height < 10)
            {
                InstructionText.Text = "선택 영역이 너무 작습니다. 다시 시도하거나 ESC를 눌러 취소하세요.";
                InstructionText.Visibility = Visibility.Visible;
                SelectionRectangle.Visibility = Visibility.Collapsed;
                return;
            }

            // 창 내부 상대 좌표로 변환
            if (!_windowBounds.IsEmpty)
            {
                x -= _windowBounds.X;
                y -= _windowBounds.Y;
            }

            SelectedRegion = new System.Drawing.Rectangle(x, y, width, height);
            DialogResult = true;
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        }

        /// <summary>
        /// 포인트가 창 영역 내에 있는지 확인합니다.
        /// </summary>
        private bool IsPointInWindowBounds(Point point)
        {
            return point.X >= _windowBounds.X && 
                   point.X <= _windowBounds.X + _windowBounds.Width &&
                   point.Y >= _windowBounds.Y && 
                   point.Y <= _windowBounds.Y + _windowBounds.Height;
        }

        /// <summary>
        /// 포인트를 창 영역 내로 제한합니다.
        /// </summary>
        private Point ClampToWindowBounds(Point point)
        {
            double x = System.Math.Max(_windowBounds.X, System.Math.Min(point.X, _windowBounds.X + _windowBounds.Width));
            double y = System.Math.Max(_windowBounds.Y, System.Math.Min(point.Y, _windowBounds.Y + _windowBounds.Height));
            return new Point(x, y);
        }
    }
}

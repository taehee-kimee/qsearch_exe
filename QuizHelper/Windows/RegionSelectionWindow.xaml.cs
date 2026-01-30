using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace QuizHelper.Windows
{
    public partial class RegionSelectionWindow : Window
    {
        private Point _startPoint;
        private bool _isSelecting;

        public System.Drawing.Rectangle SelectedRegion { get; private set; }

        public RegionSelectionWindow()
        {
            InitializeComponent();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _startPoint = e.GetPosition(SelectionCanvas);
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

            // Calculate the rectangle in screen coordinates
            int x = (int)System.Math.Min(_startPoint.X, endPoint.X);
            int y = (int)System.Math.Min(_startPoint.Y, endPoint.Y);
            int width = (int)System.Math.Abs(endPoint.X - _startPoint.X);
            int height = (int)System.Math.Abs(endPoint.Y - _startPoint.Y);

            // Minimum size check
            if (width < 10 || height < 10)
            {
                InstructionText.Text = "Selection too small. Try again or press ESC to cancel.";
                InstructionText.Visibility = Visibility.Visible;
                SelectionRectangle.Visibility = Visibility.Collapsed;
                return;
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
    }
}

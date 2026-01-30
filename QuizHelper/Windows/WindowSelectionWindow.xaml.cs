using System;
using System.Windows;
using System.Windows.Input;
using QuizHelper.Services;

namespace QuizHelper.Windows
{
    public partial class WindowSelectionWindow : Window
    {
        private readonly WindowCaptureService _captureService;

        public WindowInfo? SelectedWindow { get; private set; }

        public WindowSelectionWindow()
        {
            InitializeComponent();
            _captureService = new WindowCaptureService();
            
            Loaded += WindowSelectionWindow_Loaded;
        }

        private void WindowSelectionWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshWindowList();
        }

        private void RefreshWindowList()
        {
            var windows = _captureService.GetOpenWindows();
            WindowListBox.ItemsSource = windows;
            
            // 선택 버튼 비활성화
            SelectButton.IsEnabled = false;
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshWindowList();
        }

        private void WindowListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (WindowListBox.SelectedItem is WindowInfo window)
            {
                SelectedWindow = window;
                DialogResult = true;
                Close();
            }
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (WindowListBox.SelectedItem is WindowInfo window)
            {
                SelectedWindow = window;
                DialogResult = true;
                Close();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
            else if (e.Key == Key.Enter && WindowListBox.SelectedItem != null)
            {
                SelectButton_Click(this, new RoutedEventArgs());
            }
            base.OnPreviewKeyDown(e);
        }

        // ListBox 선택 변경 시 버튼 활성화
        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            WindowListBox.SelectionChanged += (s, args) =>
            {
                SelectButton.IsEnabled = WindowListBox.SelectedItem != null;
            };
        }
    }
}

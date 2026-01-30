using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace QuizHelper.Services
{
    /// <summary>
    /// 창 정보를 담는 클래스
    /// </summary>
    public class WindowInfo
    {
        public IntPtr Handle { get; set; }
        public string Title { get; set; } = string.Empty;
        public Rectangle Bounds { get; set; }

        public override string ToString()
        {
            return Title;
        }
    }

    /// <summary>
    /// Win32 API를 활용한 창 캡처 서비스
    /// </summary>
    public class WindowCaptureService
    {
        #region Win32 API Declarations

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out bool pvAttribute, int cbAttribute);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height,
            IntPtr hdcSrc, int xSrc, int ySrc, uint rop);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const int SW_RESTORE = 9;
        private const uint PW_CLIENTONLY = 0x00000001;
        private const uint PW_RENDERFULLCONTENT = 0x00000002;
        private const uint SRCCOPY = 0x00CC0020;
        private const int DWMWA_CLOAKED = 14;

        #endregion

        /// <summary>
        /// 현재 실행 중인 모든 보이는 창 목록을 가져옵니다.
        /// </summary>
        public List<WindowInfo> GetOpenWindows()
        {
            var windows = new List<WindowInfo>();

            EnumWindows((hWnd, lParam) =>
            {
                // 보이는 창만 필터링
                if (!IsWindowVisible(hWnd))
                    return true;

                // 최소화된 창 제외
                if (IsIconic(hWnd))
                    return true;

                // 제목이 있는 창만
                int length = GetWindowTextLength(hWnd);
                if (length == 0)
                    return true;

                // 창 제목 가져오기
                var sb = new StringBuilder(length + 1);
                GetWindowText(hWnd, sb, sb.Capacity);
                string title = sb.ToString();

                // 빈 제목 제외
                if (string.IsNullOrWhiteSpace(title))
                    return true;

                // 일부 시스템 창 제외
                if (title == "Program Manager" || title == "Windows Shell Experience Host")
                    return true;

                // DWM cloaked 창 제외 (Windows 10+)
                if (IsCloaked(hWnd))
                    return true;

                // 창 위치 가져오기
                if (GetWindowRect(hWnd, out RECT rect))
                {
                    var bounds = new Rectangle(
                        rect.Left,
                        rect.Top,
                        rect.Right - rect.Left,
                        rect.Bottom - rect.Top
                    );

                    // 너무 작은 창 제외
                    if (bounds.Width > 50 && bounds.Height > 50)
                    {
                        windows.Add(new WindowInfo
                        {
                            Handle = hWnd,
                            Title = title,
                            Bounds = bounds
                        });
                    }
                }

                return true;
            }, IntPtr.Zero);

            return windows;
        }

        /// <summary>
        /// 특정 창의 현재 위치와 크기를 가져옵니다.
        /// </summary>
        public Rectangle GetWindowBounds(IntPtr hWnd)
        {
            if (GetWindowRect(hWnd, out RECT rect))
            {
                return new Rectangle(
                    rect.Left,
                    rect.Top,
                    rect.Right - rect.Left,
                    rect.Bottom - rect.Top
                );
            }
            return Rectangle.Empty;
        }

        /// <summary>
        /// 특정 창의 클라이언트 영역 크기를 가져옵니다.
        /// </summary>
        public Size GetClientSize(IntPtr hWnd)
        {
            if (GetClientRect(hWnd, out RECT rect))
            {
                return new Size(rect.Right - rect.Left, rect.Bottom - rect.Top);
            }
            return Size.Empty;
        }

        /// <summary>
        /// 특정 창의 특정 영역을 캡처합니다.
        /// </summary>
        /// <param name="hWnd">캡처할 창의 핸들</param>
        /// <param name="region">창 내부의 상대 좌표 영역 (null이면 전체 창 캡처)</param>
        /// <returns>캡처된 비트맵</returns>
        public Bitmap? CaptureWindow(IntPtr hWnd, Rectangle? region = null)
        {
            if (hWnd == IntPtr.Zero)
                return null;

            // 최소화된 창이면 복원
            if (IsIconic(hWnd))
            {
                ShowWindow(hWnd, SW_RESTORE);
                System.Threading.Thread.Sleep(100);
            }

            // 창의 현재 크기 가져오기
            Rectangle windowBounds = GetWindowBounds(hWnd);
            if (windowBounds.Width <= 0 || windowBounds.Height <= 0)
                return null;

            try
            {
                // PrintWindow 방식으로 캡처 시도
                return CaptureWithPrintWindow(hWnd, windowBounds, region);
            }
            catch
            {
                // 실패 시 BitBlt 방식으로 폴백
                return CaptureWithBitBlt(hWnd, windowBounds, region);
            }
        }

        /// <summary>
        /// PrintWindow API를 사용한 캡처 (다른 창에 가려져도 캡처 가능)
        /// </summary>
        private Bitmap? CaptureWithPrintWindow(IntPtr hWnd, Rectangle windowBounds, Rectangle? region)
        {
            // 전체 창 캡처
            var fullBitmap = new Bitmap(windowBounds.Width, windowBounds.Height);
            
            using (var graphics = Graphics.FromImage(fullBitmap))
            {
                IntPtr hdc = graphics.GetHdc();
                
                // PW_RENDERFULLCONTENT는 Windows 8.1 이상에서 더 나은 결과를 제공
                bool success = PrintWindow(hWnd, hdc, PW_RENDERFULLCONTENT);
                
                if (!success)
                {
                    // 일반 PrintWindow 시도
                    success = PrintWindow(hWnd, hdc, 0);
                }
                
                graphics.ReleaseHdc(hdc);
                
                if (!success)
                {
                    fullBitmap.Dispose();
                    return null;
                }
            }

            // 특정 영역만 필요한 경우 크롭
            if (region.HasValue && region.Value.Width > 0 && region.Value.Height > 0)
            {
                return CropBitmap(fullBitmap, region.Value);
            }

            return fullBitmap;
        }

        /// <summary>
        /// BitBlt API를 사용한 캡처 (더 빠르지만 창이 가려지면 안됨)
        /// </summary>
        private Bitmap? CaptureWithBitBlt(IntPtr hWnd, Rectangle windowBounds, Rectangle? region)
        {
            IntPtr hdcWindow = GetWindowDC(hWnd);
            if (hdcWindow == IntPtr.Zero)
                return null;

            try
            {
                var fullBitmap = new Bitmap(windowBounds.Width, windowBounds.Height);
                
                using (var graphics = Graphics.FromImage(fullBitmap))
                {
                    IntPtr hdcMem = graphics.GetHdc();
                    BitBlt(hdcMem, 0, 0, windowBounds.Width, windowBounds.Height, 
                           hdcWindow, 0, 0, SRCCOPY);
                    graphics.ReleaseHdc(hdcMem);
                }

                // 특정 영역만 필요한 경우 크롭
                if (region.HasValue && region.Value.Width > 0 && region.Value.Height > 0)
                {
                    return CropBitmap(fullBitmap, region.Value);
                }

                return fullBitmap;
            }
            finally
            {
                ReleaseDC(hWnd, hdcWindow);
            }
        }

        /// <summary>
        /// 비트맵의 특정 영역을 크롭합니다.
        /// </summary>
        private Bitmap CropBitmap(Bitmap source, Rectangle region)
        {
            // 영역이 소스를 벗어나지 않도록 조정
            int x = Math.Max(0, region.X);
            int y = Math.Max(0, region.Y);
            int width = Math.Min(region.Width, source.Width - x);
            int height = Math.Min(region.Height, source.Height - y);

            if (width <= 0 || height <= 0)
            {
                source.Dispose();
                return new Bitmap(1, 1); // 빈 비트맵 반환
            }

            var cropped = new Bitmap(width, height);
            using (var graphics = Graphics.FromImage(cropped))
            {
                graphics.DrawImage(source, 
                    new Rectangle(0, 0, width, height),
                    new Rectangle(x, y, width, height),
                    GraphicsUnit.Pixel);
            }
            
            source.Dispose();
            return cropped;
        }

        /// <summary>
        /// 창이 DWM에 의해 숨겨져 있는지 확인합니다.
        /// </summary>
        private bool IsCloaked(IntPtr hWnd)
        {
            try
            {
                int result = DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out bool isCloaked, sizeof(int));
                return result == 0 && isCloaked;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 창이 아직 존재하는지 확인합니다.
        /// </summary>
        public bool IsWindowValid(IntPtr hWnd)
        {
            return hWnd != IntPtr.Zero && IsWindowVisible(hWnd);
        }
    }
}

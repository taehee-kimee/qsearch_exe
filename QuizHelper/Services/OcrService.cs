using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace QuizHelper.Services
{
    public class OcrService
    {
        private OcrEngine? _ocrEngine;
        private readonly Language _koreanLanguage;
        private readonly Language _englishLanguage;

        public OcrService()
        {
            _koreanLanguage = new Language("ko");
            _englishLanguage = new Language("en");

            InitializeEngine();
        }

        private void InitializeEngine()
        {
            // Try Korean first, then English, then system default
            if (OcrEngine.IsLanguageSupported(_koreanLanguage))
            {
                _ocrEngine = OcrEngine.TryCreateFromLanguage(_koreanLanguage);
            }
            else if (OcrEngine.IsLanguageSupported(_englishLanguage))
            {
                _ocrEngine = OcrEngine.TryCreateFromLanguage(_englishLanguage);
            }
            else
            {
                // Fall back to user profile languages
                _ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
            }

            if (_ocrEngine == null)
            {
                throw new InvalidOperationException(
                    "Failed to initialize OCR engine. Please ensure Windows OCR language packs are installed.");
            }
        }

        public async Task<string> RecognizeTextAsync(System.Drawing.Bitmap bitmap)
        {
            if (_ocrEngine == null)
                return string.Empty;

            // 1. 이미지 전처리: 2배 확대 및 고대비 필터 적용
            using var processedBitmap = PreprocessImage(bitmap);

            // Convert System.Drawing.Bitmap to SoftwareBitmap
            using var stream = new InMemoryRandomAccessStream();

            // Save bitmap to stream as PNG
            processedBitmap.Save(stream.AsStream(), System.Drawing.Imaging.ImageFormat.Png);
            stream.Seek(0);

            // Decode the image
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied);

            // Perform OCR
            var result = await _ocrEngine.RecognizeAsync(softwareBitmap);

            return result.Text ?? string.Empty;
        }

        /// <summary>
        /// OCR 인식률 향상을 위한 이미지 전처리
        /// 1. 2배 확대 (Upscale)
        /// 2. 그레이스케일 + 대비 증가 (Grayscale + High Contrast)
        /// 3. 줄 간격이 좁은 경우 줄 사이에 흰 여백 삽입
        /// </summary>
        private System.Drawing.Bitmap PreprocessImage(System.Drawing.Bitmap original)
        {
            // 1. 2배 확대 (Bicubic 보간법 사용)
            int width = original.Width * 2;
            int height = original.Height * 2;
            var resizedBitmap = new System.Drawing.Bitmap(width, height);

            using (var graphics = System.Drawing.Graphics.FromImage(resizedBitmap))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;

                // 2. ColorMatrix를 사용한 대비 증가 + 그레이스케일
                // 대비(Contrast)를 높여서 글자를 더 선명하게 만듦
                float scale = 1.5f; // 대비 강도 (1.0 = 원본, 높을수록 강함)
                float translate = -(scale - 1) / 2.0f; // 밝기 중심 보정

                var colorMatrix = new System.Drawing.Imaging.ColorMatrix(new float[][]
                {
                    new float[] {0.299f * scale, 0.299f * scale, 0.299f * scale, 0, 0}, // Red
                    new float[] {0.587f * scale, 0.587f * scale, 0.587f * scale, 0, 0}, // Green
                    new float[] {0.114f * scale, 0.114f * scale, 0.114f * scale, 0, 0}, // Blue
                    new float[] {0, 0, 0, 1, 0},
                    new float[] {translate, translate, translate, 0, 1}
                });

                var attributes = new System.Drawing.Imaging.ImageAttributes();
                attributes.SetColorMatrix(colorMatrix);

                // 확대와 동시에 필터 적용
                graphics.DrawImage(original,
                    new System.Drawing.Rectangle(0, 0, width, height),
                    0, 0, original.Width, original.Height,
                    System.Drawing.GraphicsUnit.Pixel,
                    attributes);
            }

            // 3. 줄 간격이 좁은 경우 줄 사이에 흰 여백 삽입
            var spacedBitmap = AddLineSpacing(resizedBitmap);
            if (!ReferenceEquals(spacedBitmap, resizedBitmap))
                resizedBitmap.Dispose();

            return spacedBitmap;
        }

        /// <summary>
        /// 줄 사이 간격이 너무 좁으면 흰 여백을 삽입한다.
        /// 인접한 텍스트 줄이 OCR에서 한 줄로 합쳐지거나 자모가 위아래 줄로 흘러붙는 문제를 완화.
        /// 줄이 1개 이하이거나 이미 충분히 떨어져 있으면 원본을 그대로 반환한다.
        /// </summary>
        private static System.Drawing.Bitmap AddLineSpacing(System.Drawing.Bitmap source)
        {
            int W = source.Width;
            int H = source.Height;

            // 1) 행별 어두움 분포 추출 (LockBits로 빠르게)
            byte[] pixels;
            int stride;
            int bpp = System.Drawing.Image.GetPixelFormatSize(source.PixelFormat) / 8;
            if (bpp < 3) return source; // 회색조 분석 불가하면 패스

            var rect = new System.Drawing.Rectangle(0, 0, W, H);
            var data = source.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, source.PixelFormat);
            try
            {
                stride = data.Stride;
                pixels = new byte[stride * H];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            }
            finally
            {
                source.UnlockBits(data);
            }

            // ColorMatrix로 그레이스케일 처리됐으므로 R 채널만 봐도 됨
            const int darkThreshold = 128;
            int minDarkPerRow = System.Math.Max(3, W / 100); // 행 너비의 1% 이상 어두워야 텍스트 행

            bool[] isText = new bool[H];
            for (int y = 0; y < H; y++)
            {
                int rowStart = y * stride;
                int darkCount = 0;
                for (int x = 0; x < W; x++)
                {
                    // BGRA: B=0, G=1, R=2
                    byte r = pixels[rowStart + x * bpp + 2];
                    if (r < darkThreshold)
                    {
                        darkCount++;
                        if (darkCount >= minDarkPerRow) break;
                    }
                }
                isText[y] = darkCount >= minDarkPerRow;
            }

            // 2) 텍스트 줄 경계 추출 (3px 미만 노이즈는 무시)
            var lines = new System.Collections.Generic.List<(int start, int end)>();
            int? lineStart = null;
            for (int y = 0; y < H; y++)
            {
                if (isText[y])
                {
                    if (lineStart == null) lineStart = y;
                }
                else if (lineStart != null)
                {
                    if (y - lineStart.Value >= 3)
                        lines.Add((lineStart.Value, y - 1));
                    lineStart = null;
                }
            }
            if (lineStart != null && H - lineStart.Value >= 3)
                lines.Add((lineStart.Value, H - 1));

            if (lines.Count < 2) return source;

            // 3) 목표 간격: 평균 줄 높이의 60%, 최소 8px
            int totalLineHeight = 0;
            foreach (var l in lines) totalLineHeight += l.end - l.start + 1;
            int avgLineHeight = totalLineHeight / lines.Count;
            int targetGap = System.Math.Max(8, (int)(avgLineHeight * 0.6));

            // 4) 부족한 간격만 추가하기 위한 새 이미지 높이 계산
            int extra = 0;
            for (int i = 0; i < lines.Count - 1; i++)
            {
                int existingGap = lines[i + 1].start - lines[i].end - 1;
                if (existingGap < targetGap)
                    extra += targetGap - existingGap;
            }

            if (extra == 0) return source; // 이미 충분히 떨어짐

            // 5) 새 이미지 합성
            int newH = H + extra;
            var output = new System.Drawing.Bitmap(W, newH);
            using (var g = System.Drawing.Graphics.FromImage(output))
            {
                g.Clear(System.Drawing.Color.White);

                int srcY = 0;
                int dstY = 0;
                for (int i = 0; i < lines.Count; i++)
                {
                    int copyEnd = lines[i].end + 1; // exclusive
                    int copyHeight = copyEnd - srcY;

                    g.DrawImage(source,
                        new System.Drawing.Rectangle(0, dstY, W, copyHeight),
                        new System.Drawing.Rectangle(0, srcY, W, copyHeight),
                        System.Drawing.GraphicsUnit.Pixel);

                    dstY += copyHeight;
                    srcY = copyEnd;

                    if (i < lines.Count - 1)
                    {
                        int existingGap = lines[i + 1].start - lines[i].end - 1;
                        if (existingGap < targetGap)
                            dstY += targetGap - existingGap;
                    }
                }

                // 마지막 줄 이후 남은 영역도 복사
                if (srcY < H)
                {
                    int remain = H - srcY;
                    g.DrawImage(source,
                        new System.Drawing.Rectangle(0, dstY, W, remain),
                        new System.Drawing.Rectangle(0, srcY, W, remain),
                        System.Drawing.GraphicsUnit.Pixel);
                }
            }

            return output;
        }

        public string GetCurrentLanguage()
        {
            return _ocrEngine?.RecognizerLanguage.DisplayName ?? "Unknown";
        }
    }
}

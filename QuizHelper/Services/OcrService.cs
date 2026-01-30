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

            return resizedBitmap;
        }

        public string GetCurrentLanguage()
        {
            return _ocrEngine?.RecognizerLanguage.DisplayName ?? "Unknown";
        }
    }
}

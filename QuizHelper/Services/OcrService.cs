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

            // Convert System.Drawing.Bitmap to SoftwareBitmap
            using var stream = new InMemoryRandomAccessStream();

            // Save bitmap to stream as PNG
            bitmap.Save(stream.AsStream(), System.Drawing.Imaging.ImageFormat.Png);
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

        public string GetCurrentLanguage()
        {
            return _ocrEngine?.RecognizerLanguage.DisplayName ?? "Unknown";
        }
    }
}

#if AI_EDITION
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FuzzySharp;

namespace QuizHelper.Services
{
    /// <summary>AI 서버(/api/ask) 응답</summary>
    public sealed class AiReply
    {
        public bool HttpOk;
        public bool Ok;
        public string Model = string.Empty;      // jev | claude | jev-vision | claude-vision
        public string Choice = string.Empty;     // 보기 번호 또는 O/X
        public string Answer = string.Empty;
        public string Question = string.Empty;   // 사진으로 읽은 문제
        public string Error = string.Empty;
        public double? Confidence;
        public double? Probability;
        public double? Screen;                   // 퀴즈 화면일 확률 (Jev 사진)
        public List<string> Choices = new();     // 사진으로 읽은 보기
        public bool Success => HttpOk && Ok;
    }

    /// <summary>AI에게 물어볼 문제. Vision이면 글자를 믿을 수 없어 캡처 사진으로 묻는다.</summary>
    public sealed class AiQuestion
    {
        public string Question = string.Empty;
        public Dictionary<string, string> Choices = new();
        public bool Vision;
    }

    public sealed class AiRow
    {
        public string Label { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
        public bool Weak { get; set; }
    }

    /// <summary>화면의 '🤖 AI 추정' 칸에 그릴 내용</summary>
    public sealed class AiView
    {
        public string Header { get; set; } = "🤖 AI 추정";
        public string Question { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
        public List<AiRow> Rows { get; set; } = new();
        public string Pending { get; set; } = string.Empty; // "Claude 확인하는 중…" 같은 진행 표시
        public string CopyAnswer { get; set; } = string.Empty;
    }

    /// <summary>
    /// 족보에 없는 문제를 웹 족보와 같은 AI 서버(qplay-search.vercel.app/api/ask)에 물어본다.
    /// 판단 규칙은 웹(js/app.js)에서 실사용으로 다듬은 것을 그대로 옮겼다.
    /// </summary>
    public static class AiAskService
    {
        public static string Endpoint { get; set; } = "https://qplay-search.vercel.app/api/ask";

        public const string SheetOx = "ox,xo";
        public const string SheetOlla = "올라올라(꼬로록)";

        private static readonly HttpClient _http = CreateHttp();

        private static HttpClient CreateHttp()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("QuizHelper-Desktop-AI");
            return client;
        }

        private static string AppFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuizHelper");

        // ===== 설정 (켜짐 여부 + 비밀번호) =====
        // 비밀번호는 이 윈도우 계정만 풀 수 있게 암호화해서 둔다 (DPAPI)

        private static string SettingsPath => Path.Combine(AppFolder, "ai_settings.json");

        public static bool On { get; private set; }
        public static string Pass { get; private set; } = string.Empty;

        private class SettingsFile
        {
            public bool on { get; set; }
            public string pass { get; set; } = string.Empty;
        }

        public static void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;
                var file = JsonSerializer.Deserialize<SettingsFile>(File.ReadAllText(SettingsPath, Encoding.UTF8));
                if (file == null || string.IsNullOrEmpty(file.pass)) return;
                byte[] plain = ProtectedData.Unprotect(Convert.FromBase64String(file.pass), null, DataProtectionScope.CurrentUser);
                Pass = Encoding.UTF8.GetString(plain);
                On = file.on && Pass.Length > 0;
            }
            catch
            {
                On = false;
                Pass = string.Empty;
            }
        }

        public static void SaveSettings(bool on, string pass)
        {
            Pass = pass ?? string.Empty;
            On = on && Pass.Length > 0;
            try
            {
                Directory.CreateDirectory(AppFolder);
                string enc = Pass.Length == 0 ? string.Empty
                    : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(Pass), null, DataProtectionScope.CurrentUser));
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new SettingsFile { on = On, pass = enc }), new UTF8Encoding(false));
            }
            catch { /* 저장 실패: 이번 실행 동안만 유지 */ }
        }

        // ===== 서버 호출 =====

        // 연결을 새로 맺으면 첫 답이 0.5~1초 늦는다 → 스캔 중에는 40초마다 가벼운 요청으로 연결과 서버를 깨워 둔다
        private static DateTime _lastWarm;

        public static void KeepWarm()
        {
            if (!On || (DateTime.UtcNow - _lastWarm).TotalSeconds < 40) return;
            _lastWarm = DateTime.UtcNow;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Options, Endpoint);
                    using var response = await _http.SendAsync(request).ConfigureAwait(false);
                }
                catch { /* 오프라인 등: 무시 */ }
            });
        }

        private static async Task<AiReply> PostAsync(object body)
        {
            var reply = new AiReply();
            _lastWarm = DateTime.UtcNow;
            try
            {
                using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                using var response = await _http.PostAsync(Endpoint, content).ConfigureAwait(false);
                reply.HttpOk = response.IsSuccessStatusCode;
                string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return reply;
                reply.Ok = root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
                reply.Model = Str(root, "model");
                reply.Choice = Str(root, "choice");
                reply.Answer = Str(root, "answer");
                reply.Question = Str(root, "question");
                reply.Error = Str(root, "error");
                reply.Confidence = Num(root, "confidence");
                reply.Probability = Num(root, "probability");
                reply.Screen = Num(root, "screen");
                if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
                    reply.Choices = choices.EnumerateArray().Select(c => c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : c.ToString()).ToList();
            }
            catch (Exception e)
            {
                reply.Ok = false;
                if (reply.Error.Length == 0) reply.Error = "network:" + e.GetType().Name;
            }
            return reply;
        }

        private static string Str(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var v)) return string.Empty;
            return v.ValueKind switch
            {
                JsonValueKind.String => v.GetString() ?? string.Empty,
                JsonValueKind.Number => v.ToString(),
                _ => string.Empty
            };
        }

        private static double? Num(JsonElement root, string name) =>
            root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

        /// <summary>비밀번호만 확인. 맞으면 빈 문자열, 틀리면 오류 코드.</summary>
        public static async Task<string> VerifyAsync(string pass)
        {
            var reply = await PostAsync(new { verify = true, pass }).ConfigureAwait(false);
            if (reply.Success) return string.Empty;
            return reply.Error.Length > 0 ? reply.Error : "failed";
        }

        /// <param name="second">true면 Jev가 자신 없던 문제를 Claude에게 다시 묻는다</param>
        public static Task<AiReply> AskTextAsync(string sheet, AiQuestion q, bool second = false)
        {
            if (second)
                return PostAsync(new { sheet, question = q.Question, choices = q.Choices, pass = Pass, second = "claude" });
            return PostAsync(new { sheet, question = q.Question, choices = q.Choices, pass = Pass });
        }

        /// <param name="jev">true면 Jev가 사진을 보고 보기 번호(O/X)만 고른다 (빠름). 아니면 Claude가 문제까지 읽는다.</param>
        public static Task<AiReply> AskImageAsync(string sheet, byte[] jpeg, bool jev)
        {
            string image = "data:image/jpeg;base64," + Convert.ToBase64String(jpeg);
            if (jev)
                return PostAsync(new { sheet, image, pass = Pass, engine = "jev" });
            return PostAsync(new { sheet, image, pass = Pass });
        }

        // ===== 캡처 → JPEG =====

        /// <summary>
        /// 가로 1024 이하로 줄여 JPEG(품질 80)로. minSide가 있으면 그보다 작은 변은 검은 여백을 붙인다
        /// (Jev 사진은 가로·세로 모두 280px 이상이어야 함).
        /// </summary>
        public static byte[]? EncodeJpeg(System.Drawing.Bitmap source, int minSide)
        {
            try
            {
                double scale = Math.Min(1.0, 1024.0 / source.Width);
                int w = Math.Max(1, (int)Math.Round(source.Width * scale));
                int h = Math.Max(1, (int)Math.Round(source.Height * scale));
                int cw = Math.Max(w, minSide);
                int ch = Math.Max(h, minSide);
                using var canvas = new System.Drawing.Bitmap(cw, ch, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                using (var g = System.Drawing.Graphics.FromImage(canvas))
                {
                    g.Clear(System.Drawing.Color.Black);
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(source, 0, 0, w, h);
                }
                var codec = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders()
                    .First(c => c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
                using var parameters = new System.Drawing.Imaging.EncoderParameters(1);
                parameters.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 80L);
                using var ms = new MemoryStream();
                canvas.Save(ms, codec, parameters);
                return ms.ToArray();
            }
            catch
            {
                return null;
            }
        }

        // ===== 글자 판별 (웹 js/app.js 와 같은 규칙) =====

        private static int Count(string s, string pattern) => Regex.Matches(s, pattern).Count;

        private const string LobbyWords = @"방정보|블랙리스트|서바이벌|PLA[VY]ING|성별아이디|게임이종료|랭킹|에관한(관한)?문+제";

        /// <summary>화면 인식이 깨진 글("방 정 보 ㅣ 000043000 서 바 을 라")인지. 이런 글을 AI에게 보내면 엉뚱한 답이 나온다.</summary>
        public static bool LooksGarbled(string? text, bool isChoice = false)
        {
            string t = text ?? string.Empty;
            string compact = Regex.Replace(t, @"\s", "");
            if (compact.Length == 0) return true;
            int hangul = Count(compact, "[가-힣]");
            int latin = Count(compact, "[a-zA-Z]");
            int jamo = Count(compact, "[ㄱ-ㅎㅏ-ㅣ]");
            int garbage = Count(compact, @"[^가-힣ㄱ-ㅎa-zA-Z0-9?？.,'""‘’“”()<>\[\]〈〉《》『』「」!~·:%/\-+ㆍ]");
            // 000043000 같은 숫자 덩어리 (100000원 같은 정상 숫자는 통과)
            if (Regex.IsMatch(compact, @"[0-9]{7,}")) return true;
            // 낱자 모음(ㅣ ㅡ)은 깨진 글의 신호. 자음(ㄱ, ㄴ)은 '보기 ㄱ, ㄴ' 같은 정상 문제에도 쓰여서 많을 때만 본다
            int vowelJamo = Count(compact, "[ㅏ-ㅣ]");
            if (vowelJamo >= (isChoice ? 1 : 2) || jamo - vowelJamo >= 6) return true;
            if ((double)garbage / compact.Length > (isChoice ? 0.15 : 0.1)) return true;
            if (!isChoice && hangul < 4 && latin < 8) return true;
            if (isChoice && hangul + latin + (jamo - vowelJamo) + Count(compact, "[0-9]") < compact.Length * 0.6) return true;
            // 대기실·방 목록 화면
            if (Regex.IsMatch(compact, LobbyWords, RegexOptions.IgnoreCase)) return true;
            // 영문·숫자가 뒤섞인 덩어리("Za|2it", "H'EE2")는 한글이 깨져 읽힌 것
            var tokens = Regex.Split(t, @"\s+").Where(w => w.Length > 0).ToList();
            int mixed = tokens.Count(w => Regex.IsMatch(w, "[a-zA-Z]") && Regex.IsMatch(w, "[0-9]") && w.Length >= 4);
            int symbolic = tokens.Count(w => Regex.IsMatch(w, @"[|{}」€]|[a-zA-Z]'[a-zA-Z]"));
            return isChoice ? (mixed + symbolic) >= 1 : (mixed >= 1 || symbolic >= 2);
        }

        /// <summary>한글 보기들 사이에 한글 없는 보기("387")가 끼어 있으면 깨져 읽힌 것</summary>
        public static bool ChoicesLookMixedUp(IReadOnlyCollection<string> opts)
        {
            if (opts.Count < 3) return false;
            int withHangul = opts.Count(o => Regex.IsMatch(o, "[가-힣]"));
            return withHangul >= opts.Count - 2 && withHangul < opts.Count;
        }

        /// <summary>한글 뒤에 영문 한 글자 조각이 붙은 보기 ('여푸디 n') → 잘못 읽힌 것</summary>
        private static bool ChoiceHasLatinScrap(string choice) =>
            Regex.IsMatch(choice.Trim(), @"[가-힣]\s+[a-zA-Z]$|^[a-zA-Z]\s+[가-힣]");

        public static bool QuestionClean(AiQuestion q)
        {
            if (LooksGarbled(q.Question)) return false;
            var opts = q.Choices.Values.ToList();
            if (ChoicesLookMixedUp(opts)) return false;
            if (opts.Any(ChoiceHasLatinScrap)) return false;
            return opts.All(c => !LooksGarbled(c, true));
        }

        /// <summary>글자는 못 믿겠지만 문제 화면은 맞아 보이는지 (→ 캡처 사진으로 묻는다)</summary>
        public static bool LooksLikeQuestionScreen(string raw)
        {
            string compact = Regex.Replace(raw ?? string.Empty, @"\s", "");
            if (Regex.IsMatch(compact, "정[답담][은는]")) return false;                    // 정답 발표 화면
            if (Regex.IsMatch(compact, LobbyWords, RegexOptions.IgnoreCase)) return false; // 대기실·랭킹·안내
            return compact.Contains("문제") || Regex.IsMatch(compact, "[?？]");
        }

        /// <summary>화면 테두리·버튼 조각 떼기: 보기 끝 "탈춤 |", "칠레 /", "중국 ㄱ", 문제 앞 "= 즈", ": "</summary>
        public static string TrimOcrEdges(string? text, bool isChoice)
        {
            string t = (text ?? string.Empty).Trim();
            t = Regex.Replace(t, @"^[\s=:;|/_~>\-'""`.,·]+", "");
            if (!isChoice) t = Regex.Replace(t, @"^(?:[가-힣ㄱ-ㅎ]{1,2}\s*[=:|]\s*)+", "");
            // 끝에 공백 뒤 붙은 1~2글자 조각 ("중국 ㄱ") — 단 "ㄱ, ㄴ" 같은 보기 목록은 그대로
            t = Regex.Replace(t, @"(?:(?<![,，])\s+[ㄱ-ㅎㅏ-ㅣ|/_~>\-:;'""`.,·]{1,2})+\s*$", "");
            if (!isChoice) t = Regex.Replace(t, @"([?？])\s*\S{1,3}$", "$1"); // 물음표 뒤 부스러기
            t = Regex.Replace(t, @"[\s|/_~>\-:;'""`,·]+$", "");
            if (Regex.IsMatch(t, @"[(\[]$")) t = Regex.Replace(t, @"\s*[(\[]$", ""); // 짝 없는 여는 괄호 꼬리
            return t.Trim();
        }

        public static string StripQuizTail(string? q)
        {
            string s = q ?? string.Empty;
            s = Regex.Replace(s, @"맞으면\s*[0Oo]\s*[,.]?\s*틀리면\s*[Xx×].*$", "");
            s = Regex.Replace(s, @"[^?？]*에\s*관한\s*문제(입니다|이다)\.?", "");
            return s.Trim();
        }

        /// <summary>두 글이 얼마나 비슷한지 0~100 (공백·기호 무시)</summary>
        public static int Similarity(string? a, string? b)
        {
            string ca = CsvDataService.NormalizeText(a ?? string.Empty).Replace(" ", "");
            string cb = CsvDataService.NormalizeText(b ?? string.Empty).Replace(" ", "");
            if (ca.Length == 0 || cb.Length == 0) return 0;
            return Fuzz.Ratio(ca, cb);
        }

        // ===== OCR 글자 → 문제·보기 =====

        private static string StripQuestionMarker(string body)
        {
            string s = Regex.Replace(body,
                @"^\s*[\[<]?\s*문\s*제(?:\s*\d{1,2}(?:[\]>1l|]|[가-힣](?=\s))?|[\]>1l|]|[가-힣](?=\s))?\s*", "");
            return s.Trim();
        }

        /// <summary>
        /// 화면 글자에서 문제와 보기를 뽑는다. 문제 화면이 아니면 null.
        /// (글자 수 힌트 "(4 글자)"는 AI가 답 길이를 맞추는 데 쓰므로 남겨 둔다)
        /// </summary>
        public static AiQuestion? ParseRaw(string ocrText)
        {
            string text = Regex.Replace((ocrText ?? string.Empty).Replace("\r", " ").Replace("\n", " "), @"\s+", " ").Trim();
            if (text.Length == 0) return null;
            if (Regex.IsMatch(text, @"정\s*[답담]\s*[은는]")) return null;
            if (QuestionCollector.TryParseAnswerScreen(text, out _, out _)) return null;

            var marker = Regex.Match(text, @"[\[<]\s*문\s*제");
            string after;
            if (marker.Success)
            {
                after = text.Substring(marker.Index);
            }
            else
            {
                // "[문제]" 표시가 안 읽힌 경우: 물음표로 끝나는 본문 + 보기 3개 이상이면 문제 화면
                var probe = QuestionCollector.ExtractNumberedChoices(text, out int probeIndex);
                bool withChoices = probe.Count >= 3 && probeIndex >= 0 && Regex.IsMatch(text.Substring(0, probeIndex), "[?？]");
                if (!withChoices && !Regex.IsMatch(text, "[?？]")) return null;
                after = text;
                if (withChoices) after = Regex.Replace(after, @"^\s*\d{1,2}\s*[\]>)|]\s*", ""); // "5] 고구려…"
            }

            // "이미지속 한글은?" 매크로 방지 문제는 플레이어마다 답이 달라 AI도 못 맞힘
            if (Regex.IsMatch(after, @"이미지\s*속?\s*(의\s*)?한글")) return null;

            var choices = QuestionCollector.ExtractNumberedChoices(after, out int firstIndex);
            string body = firstIndex >= 0 ? after.Substring(0, firstIndex) : after;
            body = Regex.Replace(body, @"출제자\s*[:：]\s*\S+\s*", "");
            body = Regex.Replace(body, "\"[^\"]+\"에\\s*관한\\s*문제입니다\\.?", "");
            body = StripQuestionMarker(body);
            body = Regex.Replace(body, @"([?？])\s*\S{1,2}$", "$1");
            body = Regex.Replace(body, @"\s+([?？])", "$1");
            body = Regex.Replace(body, @"\s+", " ").Trim();
            if (body.Length == 0) return null;

            var q = new AiQuestion();
            string trimmed = TrimOcrEdges(body, false);
            q.Question = trimmed.Length > 0 ? trimmed : body;
            foreach (var kv in choices.OrderBy(kv => kv.Key))
            {
                string c = TrimOcrEdges(kv.Value, true);
                if (c.Length > 0) q.Choices[kv.Key.ToString()] = c;
            }
            return q;
        }

        /// <summary>
        /// 족보에 없는 화면을 AI에게 어떻게 물을지 정한다.
        /// null: 묻지 않음 / Vision: 캡처 사진으로 / 그 외: 글자로.
        /// </summary>
        public static AiQuestion? BuildQuestion(string ocrText, string sheet, bool haveCapture)
        {
            var type = CsvDataService.ClassifyScreen(ocrText).Type;
            if (type == ScreenType.SystemMessage || type == ScreenType.Answer) return null;

            AiQuestion? Vision(AiQuestion? q) =>
                haveCapture && LooksLikeQuestionScreen(ocrText)
                    ? new AiQuestion { Vision = true, Question = q?.Question ?? string.Empty, Choices = q?.Choices ?? new() }
                    : null;

            if (Regex.IsMatch(ocrText, @"이미지\s*속?\s*(의\s*)?한글")) return null;
            var parsed = ParseRaw(ocrText);
            if (parsed == null) return Vision(null);                                  // 문제를 글자로 못 읽음
            int n = parsed.Choices.Count;
            if (n > 0 && n < 4) return Vision(parsed);                                // 보기가 덜 읽힘 (정답 보기가 빠졌을 수 있음)
            if (n == 0 && sheet == SheetOlla) return Vision(parsed);                  // 올라올라는 늘 4지선다
            if (!QuestionClean(parsed)) return Vision(parsed);                        // 화면 인식이 깨짐
            return parsed;
        }

        /// <summary>복사할 답만 남기기: 닉네임 꼬리 "-차니-", 객관식 번호 "4. " 제거</summary>
        public static string CleanCopyAnswer(string? answer)
        {
            string s = answer ?? string.Empty;
            s = Regex.Replace(s, @"\s*-[^-]{1,12}-\s*$", "");
            s = Regex.Replace(s, @"^\d+\.\s*", "");
            s = Regex.Replace(s, @"[\s|/_~>-]+$", "");
            return s.Trim();
        }

        // ===== 확신 있는 답 저장 (이 PC) =====
        // Jev 확신 높음, Jev·Claude 일치, 사진으로 읽은 답은 저장해 두고 같은 문제가 다시 나오면 AI를 부르지 않는다

        private const int CacheMax = 3000;
        private static string CachePath => Path.Combine(AppFolder, "ai_cache.json");
        private static Dictionary<string, AiView>? _cache;
        private static readonly object _cacheLock = new();

        private static string CacheKey(string sheet, AiQuestion q)
        {
            static string Norm(string t) => Regex.Replace(t ?? string.Empty, "[^0-9A-Za-z가-힣]", "");
            string opts = string.Join("|", q.Choices.Values.Select(Norm).OrderBy(s => s, StringComparer.Ordinal));
            return sheet + "|" + Norm(StripQuizTail(q.Question)) + "|" + opts;
        }

        private static Dictionary<string, AiView> Cache()
        {
            if (_cache != null) return _cache;
            try
            {
                _cache = File.Exists(CachePath)
                    ? JsonSerializer.Deserialize<Dictionary<string, AiView>>(File.ReadAllText(CachePath, Encoding.UTF8))
                    : null;
            }
            catch { _cache = null; }
            return _cache ??= new Dictionary<string, AiView>();
        }

        public static AiView? CacheGet(string sheet, AiQuestion q)
        {
            lock (_cacheLock)
            {
                return Cache().TryGetValue(CacheKey(sheet, q), out var view) ? view : null;
            }
        }

        public static void CachePut(string sheet, AiQuestion q, AiView view)
        {
            string json;
            lock (_cacheLock)
            {
                var cache = Cache();
                cache[CacheKey(sheet, q)] = view;
                if (cache.Count > CacheMax)
                {
                    foreach (var key in cache.Keys.Take(cache.Count - CacheMax).ToList()) cache.Remove(key);
                }
                json = JsonSerializer.Serialize(cache);
            }
            _ = Task.Run(() =>
            {
                try
                {
                    lock (_cacheLock)
                    {
                        Directory.CreateDirectory(AppFolder);
                        string temp = CachePath + ".tmp";
                        File.WriteAllText(temp, json, new UTF8Encoding(false));
                        File.Move(temp, CachePath, overwrite: true);
                    }
                }
                catch { /* 저장 실패는 무시 */ }
            });
        }
    }
}
#endif

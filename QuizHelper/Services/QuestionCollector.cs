using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FuzzySharp;
using QuizHelper.Models;

namespace QuizHelper.Services
{
    /// <summary>
    /// 족보에 없는 문제 수집기.
    /// 문제 화면에서 답을 못 찾았거나(또는 찾은 답이 틀렸을 때) 그 문제를 기억해 뒀다가,
    /// 이어서 나오는 정답 화면("[정 답] 정답은 3. 노량대첩")에서 진짜 답을 읽어 짝지어 저장한다.
    /// 저장 위치: %APPDATA%\QuizHelper\collected\{카테고리}.csv (질문\t답\t메모)
    /// </summary>
    public class QuestionCollector
    {
        private readonly CsvDataService _dataService;
        private PendingQuestion? _pending;

        // 문제 화면 → 정답 화면 사이 허용 시간
        private static readonly TimeSpan PendingLifetime = TimeSpan.FromSeconds(90);

        public QuestionCollector(CsvDataService dataService)
        {
            _dataService = dataService;
        }

        /// <summary>이번 실행에서 새로 수집한 문제 수</summary>
        public int SessionCount { get; private set; }

        /// <summary>수집 결과 (저장된 경우에만 반환)</summary>
        public class CollectedItem
        {
            public string Question { get; set; } = string.Empty;
            public string Answer { get; set; } = string.Empty;
            public string Note { get; set; } = string.Empty;
        }

        private class PendingQuestion
        {
            public string Body = string.Empty;
            public Dictionary<int, string> Choices = new();
            public Dictionary<int, List<string>> ChoiceVariants = new(); // 스캔마다 다르게 읽힌 보기들
            public string? MatchedAnswer;
            public int Quality;
            public DateTime SeenAt;
        }

        /// <summary>
        /// 매 스캔마다 호출. OCR 텍스트와 (문제 화면이라면) 매칭 결과를 넘긴다.
        /// 새 문제를 저장했으면 그 항목을, 아니면 null을 반환.
        /// </summary>
        public CollectedItem? Observe(string ocrText, MatchResult? match, DateTime? now = null)
        {
            if (string.IsNullOrWhiteSpace(ocrText) || _dataService.CurrentCategory == null)
                return null;

            var time = now ?? DateTime.Now;
            string text = Regex.Replace(ocrText.Replace("\r", " ").Replace("\n", " "), @"\s+", " ").Trim();

            // 1) 정답 화면
            if (TryParseAnswerScreen(text, out int? number, out string? answerText))
                return OnAnswerScreen(number, answerText, time);

            // 2) 문제 화면
            OnQuestionScreen(text, match, time);
            return null;
        }

        private void OnQuestionScreen(string text, MatchResult? match, DateTime time)
        {
            // "[문제 N]" 표시가 있는 화면만 (방 정보·대기 화면 등 UI 텍스트 제외)
            var marker = Regex.Match(text, @"[\[<]\s*문\s*제");
            if (!marker.Success) return;
            // 본문이 심하게 깨지면 AntiMacro로 분류될 수 있으므로 그것도 받는다
            var type = CsvDataService.ClassifyScreen(text).Type;
            if (type != ScreenType.Question && type != ScreenType.AntiMacro) return;

            string afterMarker = text.Substring(marker.Index);

            // "이미지속 한글은?" 매크로 방지 문제는 플레이어마다 답이 달라 수집 불가
            if (Regex.IsMatch(afterMarker, @"이미지\s*속?\s*(의\s*)?한글"))
            {
                _pending = null;
                return;
            }

            var choices = ExtractNumberedChoices(afterMarker, out int firstChoiceIndex);
            string body = firstChoiceIndex >= 0 ? afterMarker.Substring(0, firstChoiceIndex) : afterMarker;
            body = StripQuestionMarker(CsvDataService.CleanQuestionText(body));

            if (body.Length == 0 && choices.Count < 2) return;

            var candidate = new PendingQuestion
            {
                Body = body,
                Choices = choices,
                ChoiceVariants = choices.ToDictionary(kv => kv.Key, kv => new List<string> { kv.Value }),
                MatchedAnswer = match?.Answer,
                Quality = QualityOf(body) + choices.Count * 5,
                SeenAt = time
            };

            // 같은 문제를 여러 번 스캔한 경우: 가장 잘 읽힌 본문/보기를 유지
            if (_pending != null && time - _pending.SeenAt < PendingLifetime && IsSameQuestion(_pending, candidate))
            {
                if (candidate.Quality > _pending.Quality)
                {
                    _pending.Body = candidate.Body;
                    _pending.Quality = candidate.Quality;
                }
                foreach (var kv in candidate.Choices)
                {
                    if (!_pending.Choices.TryGetValue(kv.Key, out var old) || QualityOf(kv.Value) > QualityOf(old))
                        _pending.Choices[kv.Key] = kv.Value;
                    if (!_pending.ChoiceVariants.TryGetValue(kv.Key, out var list))
                        _pending.ChoiceVariants[kv.Key] = list = new List<string>();
                    if (!list.Contains(kv.Value)) list.Add(kv.Value);
                }
                if (candidate.MatchedAnswer != null) _pending.MatchedAnswer = candidate.MatchedAnswer;
                _pending.SeenAt = time;
            }
            else
            {
                _pending = candidate;
            }
        }

        private CollectedItem? OnAnswerScreen(int? number, string? answerText, DateTime time)
        {
            var pending = _pending;
            if (pending == null || time - pending.SeenAt > PendingLifetime)
                return null;

            // 보기 번호가 나오면 문제 화면에서 읽은 보기 글자를 우선 사용 (정답 화면은 잘려 나오는 일이 많음)
            string? trueAnswer = null;
            if (number.HasValue && pending.Choices.TryGetValue(number.Value, out var choiceText))
                trueAnswer = choiceText;
            else if (!string.IsNullOrWhiteSpace(answerText) && (number.HasValue || pending.Choices.Count == 0))
                trueAnswer = answerText;

            if (string.IsNullOrWhiteSpace(trueAnswer))
                return null;

            _pending = null; // 한 문제당 한 번만

            trueAnswer = trueAnswer.Trim();

            // 족보가 이미 맞게 알고 있으면 저장하지 않음.
            // OCR이 보기 글자를 조금씩 틀리게 읽으므로(탱크→행크) 여기서는 너그럽게 비교한다.
            if (pending.MatchedAnswer != null)
            {
                var readings = new List<string> { trueAnswer };
                if (!string.IsNullOrWhiteSpace(answerText)) readings.Add(answerText);
                if (number.HasValue && pending.ChoiceVariants.TryGetValue(number.Value, out var variants))
                    readings.AddRange(variants);

                if (readings.Any(r => AnswersEquivalent(pending.MatchedAnswer, r, 75, 2) ||
                                      IsTruncatedReading(pending.MatchedAnswer, r)))
                    return null;
            }

            string question;
            string note;
            bool damaged = CsvDataService.IsQuestionBodyDamaged(pending.Body);
            if (damaged)
            {
                // 본문이 가려져 못 읽었으면 보기 목록을 질문 자리에 남긴다 (다음에 같은 보기가 나오면 매칭됨)
                // 보기가 2개 이하로만 읽혔으면 진짜 답이 안 보이는 보기일 수 있어 신뢰하지 않음
                if (pending.Choices.Count < 3) return null;
                question = "(가려진 문제) 보기: " + string.Join(" / ", pending.Choices.OrderBy(kv => kv.Key).Select(kv => kv.Value));
                note = "본문가려짐";
            }
            else
            {
                question = pending.Body;
                note = pending.MatchedAnswer == null ? "족보없음" : $"오답매칭({pending.MatchedAnswer})";
            }

            if (question.Length < 4 || _dataService.ContainsPair(question, trueAnswer))
                return null;

            var item = new CollectedItem { Question = question, Answer = trueAnswer, Note = note };
            if (!AppendToFile(_dataService.CurrentCategory!, item, time))
                return null;

            _dataService.QueueCollectedEntry(new QuizEntry
            {
                Question = question,
                Answer = trueAnswer,
                Category = _dataService.CurrentCategory!
            });
            SessionCount++;
            return item;
        }

        // ===== 파싱 =====

        /// <summary>
        /// "[정 답] 정답은 3. 노량대첩 [설 명] ..." / "[정 답1 정답은 4 [설 명] ..." / "정답은 사과 입니다"
        /// </summary>
        internal static bool TryParseAnswerScreen(string text, out int? number, out string? answerText)
        {
            number = null;
            answerText = null;

            var m = Regex.Match(text, @"(?:정\s*[답담]\s*\S?\s*)?정답은\s*(.+?)\s*(?:\[\s*설|$)");
            if (!m.Success) return false;

            string rest = m.Groups[1].Value.Trim();
            if (rest.Contains("플레이어마다")) return true; // 매크로 방지 문제 정답 화면 → 수집 안 함

            var numbered = Regex.Match(rest, @"^([1-4])(?:\s*[.,]\s*(.*))?$");
            if (numbered.Success)
            {
                number = int.Parse(numbered.Groups[1].Value);
                string t = numbered.Groups[2].Value.Trim();
                answerText = t.Length > 0 ? t : null;
                return true;
            }

            // 주관식: 꼬리 "입니다", "!" 제거
            rest = Regex.Replace(rest, @"\s*(입니다|이다)?\s*[.!]*$", "").Trim();
            answerText = rest.Length > 0 ? rest : null;
            return true;
        }

        /// <summary>
        /// "1. 진주대첩 3. 노량대첩 2. 한산도대첩 4. 행주대첩" → {1:진주대첩, 2:한산도대첩, 3:노량대첩, 4:행주대첩}
        /// </summary>
        internal static Dictionary<int, string> ExtractNumberedChoices(string text, out int firstIndex)
        {
            var choices = new Dictionary<int, string>();
            firstIndex = -1;
            var matches = Regex.Matches(text, @"(?<!\d)([1-4])\s*\.\s*(\D{1,30}?)\s*(?=(?<!\d)[1-4]\s*\.|$)");
            foreach (Match m in matches)
            {
                int n = int.Parse(m.Groups[1].Value);
                string value = m.Groups[2].Value.Trim();
                if (value.Length == 0 || choices.ContainsKey(n)) continue;
                if (firstIndex < 0) firstIndex = m.Index;
                choices[n] = value;
            }
            return choices;
        }

        /// <summary>"[문제 4]", "[문제 91"(9]의 오인식), "[문제기" 같은 머리표 제거</summary>
        private static string StripQuestionMarker(string body)
        {
            // "]"가 기/의/이/시/지 등으로 읽히는 경우: 번호 뒤나 "문제" 바로 뒤에 붙은 한 글자 + 공백
            string s = Regex.Replace(body,
                @"^\s*[\[<]?\s*문\s*제(?:\s*\d{1,2}(?:[\]>1l|]|[가-힣](?=\s))?|[\]>1l|]|[가-힣](?=\s))?\s*", "");
            return s.Trim();
        }

        private static int QualityOf(string s)
        {
            int hangul = Regex.Matches(s, "[가-힣]").Count;
            int garbage = Regex.Matches(s, @"[^가-힣a-zA-Z0-9\s?？.,'""()!~·:%/\-]").Count;
            return hangul - garbage * 3;
        }

        private static bool IsSameQuestion(PendingQuestion a, PendingQuestion b)
        {
            // 보기가 2개 이상 겹치면 같은 문제
            int shared = a.Choices.Count(kv => b.Choices.TryGetValue(kv.Key, out var v) && AnswersEquivalent(kv.Value, v));
            if (shared >= 2) return true;

            string na = CsvDataService.NormalizeText(a.Body);
            string nb = CsvDataService.NormalizeText(b.Body);
            return na.Length >= 5 && nb.Length >= 5 && Fuzz.Ratio(na, nb) >= 80;
        }

        internal static bool AnswersEquivalent(string a, string b, int jamoThreshold = 85, int minFuzzyLength = 3)
        {
            var keysA = CsvDataService.GetAnswerKeys(a).ToList();
            var keysB = CsvDataService.GetAnswerKeys(b).ToList();
            foreach (var ka in keysA)
            {
                foreach (var kb in keysB)
                {
                    if (ka == kb) return true;
                    if (ka.Length >= minFuzzyLength && kb.Length >= minFuzzyLength &&
                        Fuzz.Ratio(CsvDataService.ToJamo(ka), CsvDataService.ToJamo(kb)) >= jamoThreshold)
                        return true;
                }
            }
            return false;
        }

        /// <summary>정답 화면 글자가 잘려 나온 경우 ("헤세-보" ⊂ "헤세-보봐리부인")</summary>
        private static bool IsTruncatedReading(string fullAnswer, string reading)
        {
            string r = CsvDataService.NormalizeText(reading).Replace(" ", "");
            if (r.Length < 2) return false;
            return CsvDataService.GetAnswerKeys(fullAnswer).Any(k => k.Length > r.Length && k.StartsWith(r));
        }

        // ===== 저장 =====

        private static bool AppendToFile(string category, CollectedItem item, DateTime time)
        {
            try
            {
                Directory.CreateDirectory(CsvDataService.CollectedFolderPath);
                string path = CsvDataService.GetCollectedFilePath(category);
                var sb = new StringBuilder();
                if (!File.Exists(path))
                    sb.Append("질문\t답\t메모\r\n"); // 첫 줄 헤더 (로더가 건너뜀)
                sb.Append($"{Sanitize(item.Question)}\t{Sanitize(item.Answer)}\t{item.Note} {time:yyyy-MM-dd HH:mm}\r\n");
                File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string Sanitize(string s) => s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
}

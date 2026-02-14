using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FuzzySharp;
using QuizHelper.Models;

namespace QuizHelper.Services
{
    /// <summary>
    /// OCR 화면 타입 분류
    /// </summary>
    public enum ScreenType
    {
        Question,       // 문제 화면 - DB 매칭 필요
        Answer,         // 정답 화면 - 정답 직접 추출
        SystemMessage,  // 시스템 메시지 - 무시
        AntiMacro,      // 매크로 방지 화면 - 무시
        Unknown         // 알 수 없음
    }

    /// <summary>
    /// 화면 분류 결과
    /// </summary>
    public class ScreenClassification
    {
        public ScreenType Type { get; set; }
        public string? ExtractedAnswer { get; set; }  // 정답 화면일 때 추출된 정답
        public int? AnswerLength { get; set; }        // 글자수 힌트 (있는 경우)
        public string CleanedText { get; set; } = string.Empty;  // 전처리된 텍스트
    }

    public class CsvDataService
    {
        private readonly List<QuizEntry> _entries = new();
        private readonly string _dataFolderPath;
        private readonly string _logFilePath;

        public int QuestionCount => _entries.Count;
        public string? CurrentCategory { get; private set; }
        
        /// <summary>
        /// 로그 파일에 메시지 기록
        /// </summary>
        private void Log(string message)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                File.AppendAllText(_logFilePath, $"[{timestamp}] {message}\r\n");
            }
            catch { /* 로그 실패는 무시 */ }
        }
        
        /// <summary>
        /// 로그 파일 초기화 (앱 시작 시)
        /// </summary>
        private void ClearLog()
        {
            try
            {
                if (File.Exists(_logFilePath))
                    File.WriteAllText(_logFilePath, $"=== Quiz Helper 매칭 로그 ({DateTime.Now:yyyy-MM-dd HH:mm:ss}) ===\r\n\r\n");
            }
            catch { }
        }

        public CsvDataService()
        {
            // Get the data folder path relative to the executable
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            _dataFolderPath = Path.Combine(basePath, "data");
            _logFilePath = Path.Combine(basePath, "match_log.txt");

            // Also check parent directories for development scenario
            if (!Directory.Exists(_dataFolderPath))
            {
                string? currentDir = basePath;
                for (int i = 0; i < 5; i++)
                {
                    currentDir = Directory.GetParent(currentDir)?.FullName;
                    if (currentDir == null) break;

                    string potentialPath = Path.Combine(currentDir, "data");
                    if (Directory.Exists(potentialPath))
                    {
                        _dataFolderPath = potentialPath;
                        _logFilePath = Path.Combine(currentDir, "match_log.txt");
                        break;
                    }
                }
            }
            
            // 로그 파일 초기화
            ClearLog();
        }

        /// <summary>
        /// Get list of available CSV categories (file names without extension)
        /// Only includes CSV files in the root data folder (excludes subfolders like backup)
        /// </summary>
        public List<string> GetAvailableCategories()
        {
            var categories = new List<string>();

            if (!Directory.Exists(_dataFolderPath))
                return categories;

            // TopDirectoryOnly: backup 등 하위 폴더 제외
            var csvFiles = Directory.GetFiles(_dataFolderPath, "*.csv", SearchOption.TopDirectoryOnly);
            foreach (var file in csvFiles)
            {
                categories.Add(Path.GetFileNameWithoutExtension(file));
            }

            return categories;
        }

        /// <summary>
        /// Load a specific CSV file by category name
        /// </summary>
        public async Task<int> LoadCategoryAsync(string categoryName)
        {
            _entries.Clear();
            CurrentCategory = categoryName;

            if (!Directory.Exists(_dataFolderPath))
            {
                Directory.CreateDirectory(_dataFolderPath);
                return 0;
            }

            string filePath = Path.Combine(_dataFolderPath, $"{categoryName}.csv");
            if (File.Exists(filePath))
            {
                await LoadCsvFileAsync(filePath);
            }

            return _entries.Count;
        }

        public async Task<int> LoadAllCsvFilesAsync()
        {
            _entries.Clear();
            CurrentCategory = null;

            if (!Directory.Exists(_dataFolderPath))
            {
                Directory.CreateDirectory(_dataFolderPath);
                return 0;
            }

            var csvFiles = Directory.GetFiles(_dataFolderPath, "*.csv", SearchOption.AllDirectories);

            foreach (var file in csvFiles)
            {
                await LoadCsvFileAsync(file);
            }

            return _entries.Count;
        }

        private async Task LoadCsvFileAsync(string filePath)
        {
            try
            {
                // Get category from filename
                string category = Path.GetFileNameWithoutExtension(filePath);

                // Read with UTF-8 encoding (for Korean text)
                var lines = await File.ReadAllLinesAsync(filePath, Encoding.UTF8);

                bool isFirstLine = true;
                foreach (var line in lines)
                {
                    // Skip header row
                    if (isFirstLine)
                    {
                        isFirstLine = false;
                        // Check if this looks like a header
                        if (line.Contains("question", StringComparison.OrdinalIgnoreCase) ||
                            line.Contains("answer", StringComparison.OrdinalIgnoreCase) ||
                            line.Contains("질문", StringComparison.OrdinalIgnoreCase) ||
                            line.Contains("답", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var parts = ParseCsvLine(line);
                    if (parts.Length >= 2)
                    {
                        _entries.Add(new QuizEntry
                        {
                            Question = parts[0].Trim(),
                            Answer = parts[1].Trim(),
                            Category = category
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading CSV {filePath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Parse a CSV line using TAB as delimiter.
        /// All CSV files should be normalized using the normalize_csv.ps1 script.
        /// </summary>
        private static string[] ParseCsvLine(string line)
        {
            // Use TAB as the primary delimiter (normalized format)
            if (line.Contains('\t'))
            {
                return line.Split('\t');
            }
            
            // Fallback: Try to split by last '?' followed by space (for non-normalized data)
            int lastQuestionMark = line.LastIndexOf('?');
            if (lastQuestionMark > 0 && lastQuestionMark < line.Length - 2)
            {
                string question = line.Substring(0, lastQuestionMark + 1).Trim();
                string answer = line.Substring(lastQuestionMark + 1).Trim();
                if (!string.IsNullOrEmpty(answer))
                {
                    return new[] { question, answer };
                }
            }
            
            // If no valid split found, return the whole line as a single element
            return new[] { line };
        }

        public MatchResult? FindBestMatch(string ocrText, int minimumScore = 80)
        {
            if (string.IsNullOrWhiteSpace(ocrText) || _entries.Count == 0)
                return null;

            // === 1단계: 화면 분류 및 전처리 ===
            var classification = ClassifyScreen(ocrText);

            Log("");
            Log("=== OCR 매칭 시작 ===");
            Log($"원본 OCR: {ocrText.Replace("\r\n", " ").Replace("\n", " ")}");
            Log($"화면 타입: {classification.Type}");

            // 시스템 메시지나 매크로 방지 화면은 무시
            if (classification.Type == ScreenType.SystemMessage)
            {
                Log("[SKIP] 시스템 메시지 - 매칭 불필요");
                return null;
            }

            if (classification.Type == ScreenType.AntiMacro)
            {
                Log("[SKIP] 매크로 방지 화면 - 매칭 불가");
                return null;
            }

            // 정답 화면인 경우: 정답 직접 반환 (DB에서 역조회)
            if (classification.Type == ScreenType.Answer && !string.IsNullOrEmpty(classification.ExtractedAnswer))
            {
                Log($"[ANSWER] 정답 화면 감지 - 추출된 정답: {classification.ExtractedAnswer}");

                // DB에서 해당 정답을 가진 질문 찾기 (학습/확인 용도)
                var matchedEntry = _entries.Find(e =>
                    NormalizeText(e.Answer).Equals(NormalizeText(classification.ExtractedAnswer), StringComparison.OrdinalIgnoreCase));

                return new MatchResult
                {
                    Question = matchedEntry?.Question ?? "(정답 화면에서 추출)",
                    Answer = classification.ExtractedAnswer,
                    Category = matchedEntry?.Category ?? "answer_screen",
                    Score = 100
                };
            }

            // === 2단계: 문제 화면 처리 ===
            // 전처리된 텍스트 사용 (포맷 텍스트 제거됨)
            string cleanedText = !string.IsNullOrEmpty(classification.CleanedText)
                ? classification.CleanedText
                : ocrText;

            string normalizedOcr = NormalizeText(cleanedText);
            int? answerLengthHint = classification.AnswerLength;

            Log($"전처리된 텍스트: {cleanedText}");
            Log($"정규화된 OCR: {normalizedOcr}");

            // === 최소 길이 체크 ===
            // 정규화 후 10글자 미만이면 오매칭 가능성 높음 (예: 외계어 ":뇨't>l" → "뇨 t l")
            const int MinimumOcrLength = 10;
            if (normalizedOcr.Length < MinimumOcrLength)
            {
                Log($"[SKIP] OCR 텍스트가 너무 짧음 ({normalizedOcr.Length}글자 < {MinimumOcrLength}글자)");
                return null;
            }
            if (answerLengthHint.HasValue)
            {
                Log($"글자수 힌트: {answerLengthHint}글자");
            }

            // Extract choices (1.xxx 2.xxx 3.xxx 4.xxx) from OCR text
            var choices = ExtractChoices(ocrText);
            if (choices.Count > 0)
            {
                Log($"추출된 보기: {string.Join(" | ", choices)}");
            }

            // === 3단계: 매칭 후보 검색 ===
            // Store top candidates for debugging (QLen for tie-breaking)
            var topCandidates = new List<(string Question, string Answer, int Score, string MatchType, int QLen)>();

            foreach (var entry in _entries)
            {
                // 글자수 힌트가 있으면 정답 길이로 필터링 (성능 및 정확도 향상)
                if (answerLengthHint.HasValue)
                {
                    int answerLen = entry.Answer.Replace(" ", "").Length;
                    // 허용 오차: ±1 글자 (OCR 오인식 고려)
                    if (Math.Abs(answerLen - answerLengthHint.Value) > 1)
                        continue;
                }

                string normalizedQuestion = NormalizeText(entry.Question);
                string normalizedAnswer = NormalizeText(entry.Answer);

                // Method 1: Standard fuzzy matching on question
                int partialScore = Fuzz.PartialRatio(normalizedOcr, normalizedQuestion);
                int tokenScore = Fuzz.TokenSetRatio(normalizedOcr, normalizedQuestion);
                int weightedScore = Fuzz.WeightedRatio(normalizedOcr, normalizedQuestion);
                int fuzzyScore = Math.Max(Math.Max(partialScore, tokenScore), weightedScore);
                
                // 길이 비율 페널티: OCR이 길고 질문이 짧을 때 점수 감소
                // (짧은 OCR + 짧은 질문은 정상 매칭, 긴 OCR + 짧은 질문은 페널티)
                int ocrLen = normalizedOcr.Length;
                int qLen = normalizedQuestion.Length;
                if (qLen > 0 && ocrLen > qLen * 5)  // OCR이 질문보다 5배 이상 길면
                {
                    // 비율에 따라 점수 감소 (최대 50% 감소)
                    double ratio = (double)ocrLen / qLen;
                    double penalty = Math.Min(0.5, (ratio - 5) * 0.05);  // 5배부터 시작, 15배에서 최대
                    fuzzyScore = (int)(fuzzyScore * (1 - penalty));
                }
                // Method 2: Choice-based matching (if choices were extracted)
                // 방향 A 변형: Fuzzy 점수에 따라 Choice 점수 결정
                // - Fuzzy >= 60%: Choice는 보너스로만 사용 (+10점)
                // - Fuzzy < 60%: Choice 단독 허용하되 70점 상한
                int choiceScore = 0;
                if (choices.Count >= 2)
                {
                    foreach (var choice in choices)
                    {
                        string normalizedChoice = NormalizeText(choice);
                        int answerMatch = Fuzz.Ratio(normalizedChoice, normalizedAnswer);

                        if (answerMatch >= 80)
                        {
                            // 질문에 보기가 포함되어 있는지 확인
                            int questionContainsChoices = 0;
                            foreach (var c in choices)
                            {
                                if (normalizedQuestion.Contains(NormalizeText(c)))
                                    questionContainsChoices++;
                            }

                            if (fuzzyScore >= 60)
                            {
                                // Fuzzy >= 60%: 보너스로만 사용
                                if (questionContainsChoices >= 2)
                                {
                                    // 질문에 보기가 많이 포함되면 더 큰 보너스
                                    choiceScore = Math.Max(choiceScore, fuzzyScore + 15);
                                }
                                else
                                {
                                    choiceScore = Math.Max(choiceScore, fuzzyScore + 10);
                                }
                            }
                            else
                            {
                                // Fuzzy < 60%: Choice 단독 허용하되 70점 상한
                                choiceScore = Math.Max(choiceScore, 70);
                            }
                        }
                    }
                }
                
                // Method 3: Keyword extraction matching
                int keywordScore = 0;
                var keywords = ExtractKeywords(normalizedOcr);
                if (keywords.Count >= 2)
                {
                    int matchedKeywords = 0;
                    foreach (var keyword in keywords)
                    {
                        if (normalizedQuestion.Contains(keyword) || normalizedAnswer.Contains(keyword))
                            matchedKeywords++;
                    }
                    
                    if (keywords.Count > 0)
                    {
                        keywordScore = (matchedKeywords * 100) / keywords.Count;
                        // Require at least 50% keyword match
                        if (keywordScore < 50) keywordScore = 0;
                    }
                }

                // Take the best score from all methods
                int finalScore = Math.Max(Math.Max(fuzzyScore, choiceScore), keywordScore);
                string matchType = finalScore == fuzzyScore ? "Fuzzy" : 
                                   finalScore == choiceScore ? "Choice" : "Keyword";

                // Store candidates with score >= 50 for debugging (include question length for tie-breaking)
                if (finalScore >= 50)
                {
                    topCandidates.Add((entry.Question, entry.Answer, finalScore, matchType, normalizedQuestion.Length));
                }
            }
            
            // Sort by score descending, then by question length similarity to OCR (closer = better)
            int ocrLength = normalizedOcr.Length;
            topCandidates.Sort((a, b) => 
            {
                int scoreCompare = b.Score.CompareTo(a.Score);
                if (scoreCompare != 0) return scoreCompare;
                
                // 동점인 경우: OCR 길이와 가까운 질문을 우선 (더 구체적인 매칭)
                int aDiff = Math.Abs(a.QLen - ocrLength);
                int bDiff = Math.Abs(b.QLen - ocrLength);
                return aDiff.CompareTo(bDiff);
            });
            
            // Log top 3 candidates
            Log($"상위 매칭 후보 (최소 점수: {minimumScore}):");
            for (int i = 0; i < Math.Min(3, topCandidates.Count); i++)
            {
                var candidate = topCandidates[i];
                Log($"  {i + 1}. [{candidate.Score}%][{candidate.MatchType}] Q: {TruncateForLog(candidate.Question, 40)} -> A: {candidate.Answer}");
            }
            
            // === 4단계: 최종 판단 ===
            // 방안 2: 점수 차이 기반 판단
            // - 기준 점수(80%) 이상이면 반환
            // - 또는 1위 >= 65% AND 1위-2위 차이 >= 15점이면 반환
            if (topCandidates.Count > 0)
            {
                var best = topCandidates[0];
                int secondScore = topCandidates.Count > 1 ? topCandidates[1].Score : 0;
                int scoreDiff = best.Score - secondScore;

                bool meetsMinimum = best.Score >= minimumScore;
                bool hasSignificantLead = best.Score >= 65 && scoreDiff >= 15;

                if (meetsMinimum || hasSignificantLead)
                {
                    string reason = meetsMinimum ? $"{best.Score}% >= {minimumScore}%"
                                                  : $"{best.Score}% (차이 {scoreDiff}점)";
                    Log($"[SUCCESS] 매칭 성공: {reason} ({best.MatchType})");

                    var matchedEntry = _entries.Find(e => e.Question == best.Question);
                    return new MatchResult
                    {
                        Question = best.Question,
                        Answer = best.Answer,
                        Category = matchedEntry?.Category ?? "",
                        Score = best.Score
                    };
                }

                Log($"[FAIL] 매칭 실패: 최고 점수 {best.Score}% (차이 {scoreDiff}점) - 기준 미달");
            }
            else
            {
                Log("[FAIL] 매칭 실패: 후보 없음");
            }

            return null;
        }
        
        /// <summary>
        /// OCR 텍스트에서 보기(1. xxx 2. xxx 형태)를 추출
        /// </summary>
        private static List<string> ExtractChoices(string text)
        {
            var choices = new List<string>();
            
            // Pattern: 1. xxx 2. xxx 3. xxx 4. xxx 또는 1.xxx 2.xxx
            var pattern = @"[1-4]\s*[.\.]\s*([^\d]{2,30})(?=[1-4]\s*[.\.]|$)";
            var matches = System.Text.RegularExpressions.Regex.Matches(text, pattern);
            
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    string choice = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(choice) && choice.Length >= 2)
                    {
                        choices.Add(choice);
                    }
                }
            }
            
            return choices;
        }
        
        /// <summary>
        /// 텍스트에서 의미있는 키워드 추출 (2글자 이상 한글 단어)
        /// </summary>
        private static List<string> ExtractKeywords(string text)
        {
            var keywords = new List<string>();
            
            // 한글 단어 추출 (2글자 이상)
            var pattern = @"[가-힣]{2,}";
            var matches = System.Text.RegularExpressions.Regex.Matches(text, pattern);
            
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                string word = match.Value;
                // 일반적인 조사나 어미 제외
                if (!IsCommonWord(word))
                {
                    keywords.Add(word);
                }
            }
            
            return keywords.Distinct().Take(10).ToList();
        }
        
        /// <summary>
        /// 일반적인 조사/어미 등 무의미한 단어 체크
        /// </summary>
        private static bool IsCommonWord(string word)
        {
            var commonWords = new HashSet<string>
            {
                "것이다", "입니다", "습니다", "한다", "이다", "있다", "없다",
                "하는", "되는", "있는", "없는", "것은", "것을", "것이",
                "무엇", "어떤", "다음", "중에서", "가운데", "대한"
            };
            return commonWords.Contains(word);
        }
        
        private static string TruncateForLog(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
        }

        /// <summary>
        /// OCR 텍스트의 화면 타입 분류 및 전처리
        /// </summary>
        public static ScreenClassification ClassifyScreen(string ocrText)
        {
            var result = new ScreenClassification { Type = ScreenType.Unknown };

            if (string.IsNullOrWhiteSpace(ocrText))
                return result;

            string text = ocrText.Trim();

            // 1. 시스템 메시지 감지 (우선순위 높음)
            if (IsSystemMessage(text))
            {
                result.Type = ScreenType.SystemMessage;
                return result;
            }

            // 2. 정답 화면 감지
            string? extractedAnswer = TryExtractAnswer(text);
            if (extractedAnswer != null)
            {
                result.Type = ScreenType.Answer;
                result.ExtractedAnswer = extractedAnswer;
                return result;
            }

            // 3. 매크로 방지 화면 감지 (OCR 품질 체크)
            if (IsAntiMacroScreen(text))
            {
                result.Type = ScreenType.AntiMacro;
                return result;
            }

            // 4. 문제 화면으로 판단
            result.Type = ScreenType.Question;
            result.AnswerLength = ExtractAnswerLength(text);
            result.CleanedText = CleanQuestionText(text);

            return result;
        }

        /// <summary>
        /// 시스템 메시지 여부 확인
        /// </summary>
        private static bool IsSystemMessage(string text)
        {
            var systemPatterns = new[]
            {
                // 기존 패턴
                @"시상식",
                @"상금으로.*원을",
                @"경험치를.*만큼",
                @"문제를\s*모두\s*풀었습니다",
                @"다시\s*시작해\s*?주세요",
                @"대단한\s*실력",
                @"기쁜\s*소식",
                @"재널\s*변경",
                @"방정보",
                @"에\s*관한\s*문제입니다\.?\s*$",  // "<문제 N> "xxx"에 관한 문제입니다." 형태

                // 게임 UI 메시지 (로그 분석으로 추가)
                @"사용자\s*대기중",
                @"오신\s*것을\s*환영",
                @"오신것을\s*환영",
                @"게임을\s*시작할\s*수\s*있습니다",
                @"게임을\s*시작하기\s*위해",
                @"틀리게\s*되면",
                @"틀리게되면",
                @"더\s*이상\s*문제를\s*푸실",
                @"정답\s*번호를\s*클릭",
                @"올라오는\s*정답",
                @"주관식으로\s*문제를",
                @"객관식으로\s*문제를",
                
                // 숫자 포함 대기 메시지 (매칭 리소스 낭비 방지)
                @"현재\s*\d+\s*명으로.*게임",           // "현재 5명으로, 게임을..."
                @"\d+\s*명이\s*더\s*들어와야",          // "1명이 더 들어와야..."
                @"님이\s*선두입니다",                    // "xxx님이 선두입니다"
                @"PLAYING|WAITING|CHANNEL",              // 게임 UI 텍스트
            };

            foreach (var pattern in systemPatterns)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(text, pattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 정답 화면에서 정답 추출 시도
        /// </summary>
        private static string? TryExtractAnswer(string text)
        {
            // 패턴: "[정 답] 정답은 XXX" 또는 "정답은 XXX 입니다"
            var patterns = new[]
            {
                @"\[정\s*답\]\s*정답은\s+([^\s\[]+)",           // [정 답] 정답은 XXX
                @"정답은\s+([가-힣a-zA-Z0-9]+)\s*(입니다|!|$)", // 정답은 XXX 입니다
            };

            foreach (var pattern in patterns)
            {
                var match = System.Text.RegularExpressions.Regex.Match(text, pattern);
                if (match.Success && match.Groups.Count > 1)
                {
                    string answer = match.Groups[1].Value.Trim();
                    // 유효한 정답인지 확인 (2글자 이상, 특수문자 제외)
                    if (answer.Length >= 2 && System.Text.RegularExpressions.Regex.IsMatch(answer, @"^[가-힣a-zA-Z0-9]+$"))
                    {
                        return answer;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 매크로 방지 화면 감지 (OCR 품질 기반)
        /// </summary>
        private static bool IsAntiMacroScreen(string text)
        {
            // 1. "이미지에 적힌 글자" 패턴 감지
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"이미.{0,3}에\s*적.{0,3}글자"))
                return true;

            // 2. OCR 품질 점수 계산
            int totalChars = text.Length;
            if (totalChars < 10) return false;

            // 한글, 영문, 숫자, 공백만 카운트
            int validChars = System.Text.RegularExpressions.Regex.Matches(text, @"[가-힣a-zA-Z0-9\s]").Count;
            double validRatio = (double)validChars / totalChars;

            // 의미없는 문자가 40% 이상이면 매크로 방지로 판단
            if (validRatio < 0.6)
                return true;

            // 3. 깨진 한글 패턴 감지 (자음/모음만 연속)
            int brokenKorean = System.Text.RegularExpressions.Regex.Matches(text, @"[ㄱ-ㅎㅏ-ㅣ]{2,}").Count;
            if (brokenKorean >= 3)
                return true;

            return false;
        }

        /// <summary>
        /// OCR 텍스트에서 글자수 힌트 추출
        /// </summary>
        private static int? ExtractAnswerLength(string text)
        {
            // 패턴: (4글자), (3글자), 4글자, OOO(3개) 등
            var patterns = new[]
            {
                @"\((\d+)\s*글자\)",     // (4글자)
                @"(\d+)\s*글자",          // 4글자
                @"O{2,}",                 // OOO -> 3글자
                @"○{2,}",                 // ○○○ -> 3글자
                @"0{2,}(?=라\s*한다)",    // 0000라 한다 -> 4글자
            };

            // 숫자로 명시된 경우
            foreach (var pattern in patterns.Take(2))
            {
                var match = System.Text.RegularExpressions.Regex.Match(text, pattern);
                if (match.Success && match.Groups.Count > 1)
                {
                    if (int.TryParse(match.Groups[1].Value, out int length) && length >= 2 && length <= 10)
                        return length;
                }
            }

            // O 또는 ○ 개수로 추정
            var oMatch = System.Text.RegularExpressions.Regex.Match(text, @"[O○0]{2,}");
            if (oMatch.Success && oMatch.Value.Length >= 2 && oMatch.Value.Length <= 10)
                return oMatch.Value.Length;

            return null;
        }

        /// <summary>
        /// 문제 텍스트에서 포맷 텍스트 제거 (질문 핵심부만 추출)
        /// </summary>
        private static string CleanQuestionText(string text)
        {
            string result = text;

            // 1. 출제자 정보 제거: "출제자 : XXX", "출제자: XXX"
            result = System.Text.RegularExpressions.Regex.Replace(result,
                @"출제자\s*[:：]\s*\S+\s*", "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // 2. 문제 마커 제거: [문제], [문제], <문제 N>
            result = System.Text.RegularExpressions.Regex.Replace(result,
                @"\[문제\]|\[문제\s*\]|<문제\s*\d*>", "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // 3. 글자수 힌트 제거: (4글자), (3글자)
            result = System.Text.RegularExpressions.Regex.Replace(result,
                @"\(\d+\s*글자\)", "");

            // 4. 정답 형식 힌트 제거: (맞다 or 아니다)
            result = System.Text.RegularExpressions.Regex.Replace(result,
                @"\(맞다\s*(or|또는)\s*아니다\)", "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // 5. 카테고리 안내 제거: "xxx"에 관한 문제입니다
            // Regex: "([^"]+)"에 관한 문제
            result = System.Text.RegularExpressions.Regex.Replace(result,
                "\"[^\"]+\"에\\s*관한\\s*문제입니다\\.?", "");

            // 6. 앞뒤 공백 및 연속 공백 정리
            result = System.Text.RegularExpressions.Regex.Replace(result.Trim(), @"\s+", " ");

            return result;
        }

        private static string NormalizeText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            // Step 1: Fix common OCR misrecognitions
            var result = text
                .Replace('|', 'l')     // 파이프 -> l
                .Replace('`', '\'')   // 백틱 -> 작은따옴표
                .Replace('~', '-');    // 물결 -> 하이픈
            
            // Step 1A: Fix number-related OCR errors (숫자 관련 OCR 오류 보정)
            // 온도 표기 오류 수정
            result = System.Text.RegularExpressions.Regex.Replace(result, @"(\d+)로C", "$1℃");     // 2로C -> 2℃
            result = System.Text.RegularExpressions.Regex.Replace(result, @"(\d+)도C", "$1℃");     // 25도C -> 25℃
            result = System.Text.RegularExpressions.Regex.Replace(result, @"(\d+)結C", "$1℃");     // 37結C -> 37℃
            result = result.Replace("결C", "℃").Replace("도C", "℃");                               // 결C, 도C -> ℃
            
            // 흔한 단어 오인식 수정
            result = System.Text.RegularExpressions.Regex.Replace(result, @"입루출저빅", "입출력");  // 입출력 오인식
            result = System.Text.RegularExpressions.Regex.Replace(result, @"ß루출저빅", "입출력");   // 입출력 오인식 변형
            
            // Step 2: Remove special characters but keep Korean, English, numbers
            result = System.Text.RegularExpressions.Regex.Replace(result, @"[^\w\s가-힣ㄱ-ㅎㅏ-ㅣ]", " ");
            
            // Step 3: Remove extra whitespace and normalize
            result = System.Text.RegularExpressions.Regex.Replace(result.Trim(), @"\s+", " ");
            
            // Step 4: Convert to lowercase for comparison
            return result.ToLowerInvariant();
        }
    }
}

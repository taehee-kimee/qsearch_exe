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
        /// </summary>
        public List<string> GetAvailableCategories()
        {
            var categories = new List<string>();
            
            if (!Directory.Exists(_dataFolderPath))
                return categories;

            var csvFiles = Directory.GetFiles(_dataFolderPath, "*.csv", SearchOption.AllDirectories);
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

            // Normalize the OCR text
            string normalizedOcr = NormalizeText(ocrText);
            
            // Extract choices (1.xxx 2.xxx 3.xxx 4.xxx) from OCR text
            var choices = ExtractChoices(ocrText);
            
            // Log the normalized OCR text
            Log("");
            Log("=== OCR 매칭 시작 ===");
            Log($"원본 OCR: {ocrText.Replace("\r\n", " ").Replace("\n", " ")}");
            Log($"정규화된 OCR: {normalizedOcr}");
            if (choices.Count > 0)
            {
                Log($"추출된 보기: {string.Join(" | ", choices)}");
            }

            // Store top candidates for debugging (QLen for tie-breaking)
            var topCandidates = new List<(string Question, string Answer, int Score, string MatchType, int QLen)>();

            foreach (var entry in _entries)
            {
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
                int choiceScore = 0;
                if (choices.Count >= 2)
                {
                    // Check if any choice matches the answer
                    foreach (var choice in choices)
                    {
                        string normalizedChoice = NormalizeText(choice);
                        
                        // If a choice matches the answer, boost score significantly
                        int answerMatch = Fuzz.Ratio(normalizedChoice, normalizedAnswer);
                        if (answerMatch >= 80)
                        {
                            // Also check if other choices appear in the question
                            int questionContainsChoices = 0;
                            foreach (var c in choices)
                            {
                                if (normalizedQuestion.Contains(NormalizeText(c)))
                                    questionContainsChoices++;
                            }
                            
                            // If question contains multiple choices and answer matches, high confidence
                            if (questionContainsChoices >= 2)
                            {
                                choiceScore = 95;
                                break;
                            }
                            else if (answerMatch >= 90)
                            {
                                choiceScore = Math.Max(choiceScore, 85);
                            }
                        }
                        
                        // Also try partial matching with question
                        int choiceInQuestion = Fuzz.PartialRatio(normalizedChoice, normalizedQuestion);
                        if (choiceInQuestion >= 90)
                        {
                            choiceScore = Math.Max(choiceScore, 70 + (choiceInQuestion - 90));
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
            
            // Return best match if it meets minimum score
            if (topCandidates.Count > 0 && topCandidates[0].Score >= minimumScore)
            {
                var best = topCandidates[0];
                Log($"[SUCCESS] 매칭 성공: {best.Score}% ({best.MatchType})");
                
                // Find the original entry to get category
                var matchedEntry = _entries.Find(e => e.Question == best.Question);
                return new MatchResult
                {
                    Question = best.Question,
                    Answer = best.Answer,
                    Category = matchedEntry?.Category ?? "",
                    Score = best.Score
                };
            }
            
            Log($"[FAIL] 매칭 실패: 최고 점수 {(topCandidates.Count > 0 ? topCandidates[0].Score : 0)}% < 기준 {minimumScore}%");
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

        private static string NormalizeText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            // Step 1: Fix common OCR misrecognitions
            var result = text
                .Replace('|', 'l')     // 파이프 -> l
                .Replace('`', '\'')   // 백틱 -> 작은따옴표
                .Replace('~', '-');    // 물결 -> 하이픈
            
            // Step 2: Remove special characters but keep Korean, English, numbers
            result = System.Text.RegularExpressions.Regex.Replace(result, @"[^\w\s가-힣ㄱ-ㅎㅏ-ㅣ]", " ");
            
            // Step 3: Remove extra whitespace and normalize
            result = System.Text.RegularExpressions.Regex.Replace(result.Trim(), @"\s+", " ");
            
            // Step 4: Convert to lowercase for comparison
            return result.ToLowerInvariant();
        }
    }
}

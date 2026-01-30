using System;
using System.Collections.Generic;
using System.IO;
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

        public int QuestionCount => _entries.Count;

        public CsvDataService()
        {
            // Get the data folder path relative to the executable
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            _dataFolderPath = Path.Combine(basePath, "data");

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
                        break;
                    }
                }
            }
        }

        public async Task<int> LoadAllCsvFilesAsync()
        {
            _entries.Clear();

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

        private static string[] ParseCsvLine(string line)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            foreach (char c in line)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            result.Add(current.ToString());
            return result.ToArray();
        }

        public MatchResult? FindBestMatch(string ocrText, int minimumScore = 70)
        {
            if (string.IsNullOrWhiteSpace(ocrText) || _entries.Count == 0)
                return null;

            // Normalize the OCR text
            string normalizedOcr = NormalizeText(ocrText);

            MatchResult? bestMatch = null;
            int bestScore = minimumScore - 1;

            foreach (var entry in _entries)
            {
                string normalizedQuestion = NormalizeText(entry.Question);

                // Use FuzzySharp for fuzzy matching
                // Try partial ratio for substring matching (handles partial OCR)
                int partialScore = Fuzz.PartialRatio(normalizedOcr, normalizedQuestion);

                // Also try token set ratio for handling word order differences
                int tokenScore = Fuzz.TokenSetRatio(normalizedOcr, normalizedQuestion);

                // Take the best score
                int score = Math.Max(partialScore, tokenScore);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestMatch = new MatchResult
                    {
                        Question = entry.Question,
                        Answer = entry.Answer,
                        Category = entry.Category,
                        Score = score
                    };
                }
            }

            return bestMatch;
        }

        private static string NormalizeText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            // Remove extra whitespace and normalize
            return System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"\s+", " ").ToLowerInvariant();
        }
    }
}

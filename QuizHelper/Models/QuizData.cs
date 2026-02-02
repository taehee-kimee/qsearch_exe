using System.Collections.Generic;
using System.Linq;

namespace QuizHelper.Models
{
    public class QuizEntry
    {
        public string Question { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
    }

    public class MatchResult
    {
        public string Question { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public int Score { get; set; }
        public int Rank { get; set; }
    }

    /// <summary>
    /// Top N 검색 결과를 담는 클래스
    /// </summary>
    public class SearchResults
    {
        public List<MatchResult> Candidates { get; set; } = new();
        public MatchResult? Best => Candidates.FirstOrDefault();
        public bool HasAlternatives => Candidates.Count > 1;
    }
}

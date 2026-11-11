using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace QuizHelper.Services
{
    /// <summary>
    /// 📥 배지를 누르면 여는 '모은 문제' 페이지 (웹 족보의 모은 문제 페이지와 같은 모양).
    /// 이 PC에서 모은 문제를 채워 넣은 HTML을 수집 폴더에 쓰고 기본 브라우저로 연다.
    /// 족보에 반영된 목록은 페이지가 웹의 collected-status.json 을 읽어 표시한다.
    /// </summary>
    public static class CollectedPage
    {
        public static string StatusUrl { get; set; } = "https://taehee-kimee.github.io/qplay_search/collected-status.json";

        private static string TemplatePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "collected_page.html");
        public static string PagePath => Path.Combine(CsvDataService.CollectedFolderPath, "모은문제.html");

        public class Row
        {
            public string sheet { get; set; } = string.Empty;
            public string question { get; set; } = string.Empty;
            public string answer { get; set; } = string.Empty;
            public string time { get; set; } = string.Empty;
            public bool pending { get; set; }
        }

        /// <summary>이 PC 수집 파일들을 읽어 표시용 목록으로</summary>
        public static List<Row> ReadCollected()
        {
            var rows = new List<Row>();
            string folder = CsvDataService.CollectedFolderPath;
            if (!Directory.Exists(folder)) return rows;

            var pendingKeys = new HashSet<string>(CollectSync.ReadOutbox().Select(i => Key(i.question, i.answer)));
            foreach (var file in Directory.GetFiles(folder, "*.csv", SearchOption.TopDirectoryOnly))
            {
                string category = Path.GetFileNameWithoutExtension(file);
                string sheet = CollectSync.SheetNames.TryGetValue(category, out var s) ? s : category;
                string[] lines;
                try { lines = File.ReadAllLines(file, Encoding.UTF8); }
                catch { continue; }

                foreach (var line in lines.Skip(1)) // 첫 줄은 머리글
                {
                    var parts = line.Split('\t');
                    if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0])) continue;
                    string note = parts.Length > 2 ? parts[2] : string.Empty;
                    var time = Regex.Match(note, @"\d{4}-\d{2}-\d{2} \d{2}:\d{2}");
                    rows.Add(new Row
                    {
                        sheet = sheet,
                        question = parts[0].Trim(),
                        answer = parts[1].Trim(),
                        time = time.Success ? time.Value : string.Empty,
                        pending = pendingKeys.Contains(Key(parts[0], parts[1]))
                    });
                }
            }
            return rows;
        }

        private static string Key(string q, string a) =>
            Regex.Replace((q ?? "") + "|" + (a ?? ""), @"[^0-9A-Za-z가-힣|]", "").ToLowerInvariant();

        private static readonly System.Net.Http.HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(6) };

        /// <summary>족보 반영 현황을 앱이 미리 받아 둔다 (브라우저가 로컬 파일의 인터넷 요청을 막아도 보이게)</summary>
        public static async System.Threading.Tasks.Task<JsonElement?> TryDownloadStatusAsync()
        {
            try
            {
                string text = await _http.GetStringAsync(StatusUrl + "?t=" + DateTime.UtcNow.Ticks);
                using var doc = JsonDocument.Parse(text);
                return doc.RootElement.Clone();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>페이지 HTML을 만든다 (테스트에서도 사용)</summary>
        public static string Build(JsonElement? status = null)
        {
            string template = File.ReadAllText(TemplatePath, Encoding.UTF8);
            var data = new
            {
                items = ReadCollected(),
                folder = CsvDataService.CollectedFolderPath,
                pending = CollectSync.ReadOutbox().Count,
                statusUrl = StatusUrl,
                status
            };
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }).Replace("</", "<\\/"); // <script> 안에서 안전하게
            const string marker = "/*__DATA_JSON__*/{ \"items\": [], \"folder\": \"\", \"pending\": 0, \"statusUrl\": \"\" }";
            return template.Replace(marker, json);
        }

        /// <summary>페이지를 만들어 기본 브라우저로 연다</summary>
        public static async System.Threading.Tasks.Task OpenAsync()
        {
            Directory.CreateDirectory(CsvDataService.CollectedFolderPath);
            var status = await TryDownloadStatusAsync();
            File.WriteAllText(PagePath, Build(status), new UTF8Encoding(false));
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = PagePath,
                UseShellExecute = true
            });
        }
    }
}

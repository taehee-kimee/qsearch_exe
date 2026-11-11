using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace QuizHelper.Services
{
    /// <summary>
    /// 설치형에서 모은 문제를 웹과 같은 공유 서버(/api/collect)로 보낸다.
    /// 보내기 전까지는 collected\outbox.json 에 보관했다가, 인터넷이 될 때 다시 보낸다.
    /// 서버에 모인 문제는 매일 밤 족보 병합 루틴이 족보에 합친다.
    /// </summary>
    public static class CollectSync
    {
        public static string Endpoint { get; set; } = "https://qplay-search.vercel.app/api/collect";

        // 설치형 카테고리(파일 이름) → 웹·서버의 카테고리 이름
        public static readonly Dictionary<string, string> SheetNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["garosero"] = "가로세로",
            ["kkong"] = "꽁꽁",
            ["ollaolla"] = "올라올라(꼬로록)",
            ["ox_xo"] = "ox,xo",
        };

        public class Item
        {
            public string sheet { get; set; } = string.Empty;
            public string question { get; set; } = string.Empty;
            public string answer { get; set; } = string.Empty;
            public string note { get; set; } = string.Empty;
        }

        private static readonly SemaphoreSlim _lock = new(1, 1);
        private static readonly HttpClient _http = CreateHttp();

        private static HttpClient CreateHttp()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("QuizHelper-Desktop");
            return client;
        }

        private static string OutboxPath => Path.Combine(CsvDataService.CollectedFolderPath, "outbox.json");
        private static string ClientIdPath => Path.Combine(CsvDataService.CollectedFolderPath, "client-id.txt");

        /// <summary>이 PC 설치형의 고유 ID (서버에서 같은 사람이 보낸 것인지 구분용)</summary>
        public static string ClientId
        {
            get
            {
                try
                {
                    if (File.Exists(ClientIdPath))
                    {
                        string id = File.ReadAllText(ClientIdPath).Trim();
                        if (id.Length > 0) return id;
                    }
                    Directory.CreateDirectory(CsvDataService.CollectedFolderPath);
                    string newId = "desktop-" + Guid.NewGuid().ToString();
                    File.WriteAllText(ClientIdPath, newId);
                    return newId;
                }
                catch
                {
                    return "desktop-unknown";
                }
            }
        }

        public static List<Item> ReadOutbox()
        {
            try
            {
                if (!File.Exists(OutboxPath)) return new List<Item>();
                return JsonSerializer.Deserialize<List<Item>>(File.ReadAllText(OutboxPath, Encoding.UTF8)) ?? new List<Item>();
            }
            catch
            {
                return new List<Item>();
            }
        }

        private static void WriteOutbox(List<Item> items)
        {
            Directory.CreateDirectory(CsvDataService.CollectedFolderPath);
            string temp = OutboxPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(items), new UTF8Encoding(false));
            File.Move(temp, OutboxPath, overwrite: true);
        }

        /// <summary>새로 모은 문제를 보낼 대기열에 넣고 바로 보내 본다.</summary>
        public static void Enqueue(string category, string question, string answer, string note)
        {
            if (!SheetNames.TryGetValue(category, out var sheet)) return;
            _ = EnqueueAndFlushAsync(new Item { sheet = sheet, question = question, answer = answer, note = note });
        }

        private static async Task EnqueueAndFlushAsync(Item item)
        {
            await _lock.WaitAsync();
            try
            {
                var box = ReadOutbox();
                box.Add(item);
                WriteOutbox(box.Count > 200 ? box.Skip(box.Count - 200).ToList() : box);
            }
            catch { /* 대기열 저장 실패: 로컬 수집 파일에는 남아 있음 */ }
            finally
            {
                _lock.Release();
            }
            await FlushAsync();
        }

        /// <summary>대기열을 서버로 보낸다. 남은 개수를 돌려준다.</summary>
        public static async Task<int> FlushAsync()
        {
            await _lock.WaitAsync();
            try
            {
                var box = ReadOutbox();
                while (box.Count > 0)
                {
                    var batch = box.Take(20).ToList();
                    string body = JsonSerializer.Serialize(new { client = ClientId, items = batch });
                    using var content = new StringContent(body, Encoding.UTF8, "application/json");
                    HttpResponseMessage response;
                    try
                    {
                        response = await _http.PostAsync(Endpoint, content);
                    }
                    catch
                    {
                        break; // 오프라인 등: 다음 기회에
                    }
                    // 200: 저장됨, 400: 서버가 거절한 형식 → 둘 다 대기열에서 뺀다. 503 등은 다음에 다시
                    if (!response.IsSuccessStatusCode && (int)response.StatusCode != 400) break;
                    box = box.Skip(batch.Count).ToList();
                    WriteOutbox(box);
                }
                return box.Count;
            }
            catch
            {
                return ReadOutbox().Count;
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}

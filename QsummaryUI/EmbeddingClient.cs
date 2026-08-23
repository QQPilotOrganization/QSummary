using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace QsummaryUI
{
    internal class EmbeddingClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private string ServerUrl { get; set; } = "";
        private string ApiKey { get; set; } = "";
        private string ModelName { get; set; } = "";
        private bool disposed = false;

        public EmbeddingClient(string serverUrl, string apiKey, string modelName, int timeoutSeconds = 300)
        {
            ServerUrl = serverUrl?.Trim() ?? "";
            ApiKey = apiKey?.Trim() ?? "";
            ModelName = modelName?.Trim() ?? "";

            if (ServerUrl.Equals("ollama", StringComparison.OrdinalIgnoreCase))
            {
                ServerUrl = "http://localhost:11434/v1";
            }

            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        }

        /// <summary>
        /// 生成单个文本的嵌入向量
        /// </summary>
        public async Task<float[]?> GetEmbeddingAsync(string text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<float>();
            var result = await GetEmbeddingsAsync(new[] { text });
            return result?.FirstOrDefault();
        }

        /// <summary>
        /// 批量生成多个文本的嵌入向量
        /// </summary>
        public async Task<float[][]?> GetEmbeddingsAsync(IEnumerable<string> texts)
        {
            var textList = texts?.Where(t => !string.IsNullOrEmpty(t)).ToList();
            if (textList == null || textList.Count == 0) return Array.Empty<float[]>();

            var requestBody = new Dictionary<string, object>
            {
                ["model"] = ModelName,
                ["input"] = textList
            };

            string json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            if (!string.IsNullOrEmpty(ApiKey) && !ServerUrl.Contains("localhost") && !ServerUrl.Contains("127.0.0.1"))
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);
            }
            else
            {
                _httpClient.DefaultRequestHeaders.Authorization = null;
            }

            try
            {
                QSummaryCore.Log.Print($"[Embedding] Sending to {ServerUrl}/embeddings, model={ModelName}, count={textList.Count}");
                HttpResponseMessage response = await _httpClient.PostAsync($"{ServerUrl}/embeddings", content);
                string responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    QSummaryCore.Log.Print($"[Embedding] API Error: {response.StatusCode} - {responseBody}", QSummaryCore.Log.Stat.ERROR);
                    return null;
                }

                using JsonDocument doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("data", out JsonElement dataElement) && dataElement.ValueKind == JsonValueKind.Array)
                {
                    var results = new List<float[]>();
                    foreach (var item in dataElement.EnumerateArray())
                    {
                        if (item.TryGetProperty("embedding", out JsonElement embeddingElement) && embeddingElement.ValueKind == JsonValueKind.Array)
                        {
                            var vector = new List<float>();
                            foreach (var val in embeddingElement.EnumerateArray())
                            {
                                vector.Add(val.GetSingle());
                            }
                            results.Add(vector.ToArray());
                        }
                    }
                    QSummaryCore.Log.Print($"[Embedding] Got {results.Count} vectors, dim={results.FirstOrDefault()?.Length ?? 0}");
                    return results.ToArray();
                }

                QSummaryCore.Log.Print("[Embedding] Response has no 'data' array", QSummaryCore.Log.Stat.WARN);
                return null;
            }
            catch (Exception ex)
            {
                QSummaryCore.Log.Print($"[Embedding] Request failed: {ex.Message}", QSummaryCore.Log.Stat.ERROR);
                return null;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed) return;
            if (disposing)
            {
                _httpClient?.Dispose();
            }
            disposed = true;
        }

        ~EmbeddingClient() => Dispose(false);
    }
}

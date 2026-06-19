using IniParser;
using IniParser.Model;
using QSummaryCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace QsummaryUI
{
    internal class Answer
    {
        private readonly HttpClient _httpClient;
        private FileIniDataParser parser = new();
        private IniData? config;
        private string ModelName { get; set; } = "";
        private string ServerUrl { get; set; } = "";
        private bool IsVisionModel { get; set; }
        private int MaxImageCount { get; set; }
        private int RemoteServerTimeout { get; set; }
        private string ApiKey { get; set; } = "";
        private bool Builtin { get; set; } = false;
        private string sysPmpt { get; set; } = "";

        // 常量
        private const int MAX_LENGTH = 2048;

        public Answer()
        {
            QSummaryCore.Log.Print(Path.GetFullPath("config.ini"));
            config = parser.ReadFile("config.ini", Encoding.UTF8);
            ModelName = config["general"]["modelname"];
            ServerUrl = config["general"]["server_url"];
            IsVisionModel = config["general"]["isvisionmodel"].Equals("true", StringComparison.OrdinalIgnoreCase);
            MaxImageCount = int.Parse(config["general"]["maximagecount"]);
            RemoteServerTimeout = int.Parse(config["general"]["remote_server_timeout"]);
            ApiKey = config["general"]["api_key"];
            sysPmpt = File.Exists("system.txt") ? File.ReadAllText("system.txt") : "";

            // 配置 HttpClient
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(RemoteServerTimeout);

            if (ServerUrl.Equals("ollama", StringComparison.OrdinalIgnoreCase))
            {
                ServerUrl = "http://localhost:11434/v1";
            }
            else if (ServerUrl.Equals("builtin", StringComparison.OrdinalIgnoreCase))
            {
                Builtin = true;
            }
            // 否则 ServerUrl 就是用户自定义的 base URL（如 http://192.168.1.100:8000/v1）
        }

        // --- 工具方法 ---

        private static string ImageToBase64(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            return Convert.ToBase64String(bytes);
        }

        private static (bool hasTime, List<string> validTimes) IsTime(string text)
        {
            var pattern1 = @"\(?([0-2]?[0-9]):([0-5][0-9])\)?";
            var pattern2 = @"\(?([0-2]?[0-9])\.([0-5][0-9])\)?";

            var matches1 = Regex.Matches(text, pattern1);
            var matches2 = Regex.Matches(text, pattern2);

            var allMatches = matches1.Cast<Match>().Concat(matches2.Cast<Match>());
            var validTimes = new List<string>();

            foreach (var m in allMatches)
            {
                if (int.TryParse(m.Groups[1].Value, out int h) &&
                    int.TryParse(m.Groups[2].Value, out int min) &&
                    h >= 0 && h <= 23 && min >= 0 && min <= 59)
                {
                    validTimes.Add($"{h:D2}:{min:D2}");
                }
            }

            return (validTimes.Count > 0, validTimes);
        }

        private List<Dictionary<string, object>> ConcatenateText(List<QSummaryCore.ChatContent> textList, List<string> images)
        {
            var messages = new List<Dictionary<string, object>>();

            // 处理历史消息（除最后一条）
            foreach (var t in textList.Take(textList.Count - 1))
            {
                if (string.IsNullOrEmpty(t.Text)) continue;
                messages.Add(new Dictionary<string, object>
                {
                    ["role"] = t.OwnByMyself ? "assistant" : "user",
                    ["content"] = t.ToString()
                });
            }

            // 最后一条消息
            string lastText = textList.Any() ? textList.Last().ToString() : "_";
            if (string.IsNullOrEmpty(lastText)) lastText = "_";

            if (IsVisionModel && images.Any())
            {
                var contentList = new List<object>
                {
                    new Dictionary<string, string> { ["type"] = "text", ["text"] = lastText }
                };

                foreach (var img in images.Take(MaxImageCount))
                {
                    if (!File.Exists(img)) continue;

                    string b64 = ImageToBase64(img);
                    string mime = img.ToLower().EndsWith(".png") ? "image/png" : "image/jpeg";
                    contentList.Add(new Dictionary<string, object>
                    {
                        ["type"] = "image_url",
                        ["image_url"] = new Dictionary<string, string>
                        {
                            ["url"] = $"data:{mime};base64,{b64}"
                        }
                    });
                }

                messages.Add(new Dictionary<string, object>
                {
                    ["role"] = "user",
                    ["content"] = contentList
                });
            }
            else
            {
                messages.Add(new Dictionary<string, object>
                {
                    ["role"] = "user",
                    ["content"] = lastText
                });
            }

            if (messages.Count == 0)
            {
                messages.Add(new Dictionary<string, object>
                {
                    ["role"] = "user",
                    ["content"] = "_"
                });
            }

            return messages;
        }

        // --- 主逻辑：直接 POST 调用 API ---
        JsonSerializerOptions? jsonSerializerOptionsForPosting;
        JsonSerializerOptions? jsonSerializerOptionsForPrinting;
        public async Task<string?> GetAnswerAsync(List<QSummaryCore.ChatContent> text, string systemPrompt = "auto")
        {
            if (text == null || text.Count == 0) return "";

            // 内置模型
        
            // 系统提示
            string finalSystemPrompt = systemPrompt switch
            {
                "auto" => sysPmpt,
                "" or "None" => "",
                _ => systemPrompt
            };

            // 收集图片
            var imageList = new List<string>();
            foreach (var t in text)
            {
                if (!t.OwnByMyself)
                {
                    foreach (var img in t.ImagePaths)
                    {
                        if (File.Exists(img))
                        {
                            imageList.Add(img);
                            if (imageList.Count >= MaxImageCount) break;
                        }
                        else
                        {
                            QSummaryCore.Log.Print($"× 没有找到图片 {img}",QSummaryCore.Log.Stat.WARN);
                        }
                    }
                    if (imageList.Count >= MaxImageCount) break;
                }
            }

            // 构建 messages
            var messages = new List<Dictionary<string, object>>();
            if (!string.IsNullOrEmpty(finalSystemPrompt))
            {
                messages.Add(new Dictionary<string, object>
                {
                    ["role"] = "system",
                    ["content"] = finalSystemPrompt
                });
            }

            messages.AddRange(ConcatenateText(text, imageList));

            // 构造请求体
            var requestBody = new Dictionary<string, object>
            {
                ["model"] = ModelName,
                ["messages"] = messages,
                ["max_tokens"] = MAX_LENGTH,
                ["temperature"] = 0.7
            };

            jsonSerializerOptionsForPosting ??= new JsonSerializerOptions { WriteIndented = false };
            string json = JsonSerializer.Serialize(requestBody,jsonSerializerOptionsForPosting! );

            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // 设置 Headers
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
                var startTime = DateTime.UtcNow;
                QSummaryCore.Log.Print($"Sending request to: {ServerUrl}/chat/completions");
                jsonSerializerOptionsForPrinting ??= new JsonSerializerOptions
                {
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    WriteIndented = true // 可选：美化输出
                };
                 QSummaryCore.Log.Print(JsonSerializer.Serialize(requestBody, jsonSerializerOptionsForPrinting!)); 

                HttpResponseMessage response = await _httpClient.PostAsync($"{ServerUrl}/chat/completions", content);
                string responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var Err= $"API Error: {response.StatusCode} - {responseBody}";
                    QSummaryCore.Log.Print(Err, QSummaryCore.Log.Stat.ERROR);
                    return Err;
                }
                QSummaryCore.Log.Print($"\n\nResponse:\n{responseBody}");

                using JsonDocument doc = JsonDocument.Parse(responseBody);
                string? answer = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                QSummaryCore.Log.Print($"用时 {elapsed:F2}s");
                QSummaryCore.Log.Print(answer?.Trim()??"");

                return answer?.Trim();
            }
            catch (Exception ex)
            {
                QSummaryCore.Log.Print($"HTTP request failed: {ex.Message}",QSummaryCore.Log.Stat.ERROR);
                return $"HTTP request failed: {ex.Message}";
                return null;
            }
        }

        // 同步版本
        public string? GetAnswer(List<QSummaryCore.ChatContent> text, string systemPrompt = "auto")
        {
            return GetAnswerAsync(text, systemPrompt).GetAwaiter().GetResult();
        }
        public string GetAnswerSync(List<QSummaryCore.ChatContent> text, string systemPrompt = "auto")
        {
            if (text == null || text.Count == 0) return "错误：输入内容为空";

            // 系统提示
            string finalSystemPrompt = systemPrompt switch
            {
                "auto" => sysPmpt,
                "" or "None" => "",
                _ => systemPrompt
            };

            // 收集图片
            var imageList = new List<string>();
            foreach (var t in text)
            {
                if (!t.OwnByMyself)
                {
                    foreach (var img in t.ImagePaths)
                    {
                        if (File.Exists(img))
                        {
                            imageList.Add(img);
                            if (imageList.Count >= MaxImageCount) break;
                        }
                        else
                        {
                            QSummaryCore.Log.Print($"× 没有找到图片 {img}",QSummaryCore.Log.Stat.WARN);
                        }
                    }
                    if (imageList.Count >= MaxImageCount) break;
                }
            }

            // 构建 messages
            var messages = new List<Dictionary<string, object>>();
            if (!string.IsNullOrEmpty(finalSystemPrompt))
            {
                messages.Add(new Dictionary<string, object>
                {
                    ["role"] = "system",
                    ["content"] = finalSystemPrompt
                });
            }

            messages.AddRange(ConcatenateText(text, imageList));

            // 构造请求体
            var requestBody = new Dictionary<string, object>
            {
                ["model"] = ModelName,
                ["messages"] = messages,
                ["max_tokens"] = MAX_LENGTH,
                ["temperature"] = 0.7
            };

            string json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { WriteIndented = false });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // 设置 Headers
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
                var startTime = DateTime.UtcNow;
                QSummaryCore.Log.Print($"Sending request to: {ServerUrl}/chat/completions");
                QSummaryCore.Log.Print(json); // 可选：调试输出

                // 同步调用
                HttpResponseMessage response = _httpClient.PostAsync($"{ServerUrl}/chat/completions", content).GetAwaiter().GetResult();
                string responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                if (!response.IsSuccessStatusCode)
                {
                    QSummaryCore.Log.Print($"API Error: {response.StatusCode} - {responseBody}",QSummaryCore.Log.Stat.ERROR);
                    
                    // 尝试解析详细的错误信息
                    string errorDetail = string.Empty;
                    try
                    {
                        using JsonDocument errorDoc = JsonDocument.Parse(responseBody);
                        if (errorDoc.RootElement.TryGetProperty("error", out JsonElement errorElement))
                        {
                            errorDetail = errorElement.GetString() ?? string.Empty;
                        }
                    }
                    catch { }
                    
                    if (!string.IsNullOrEmpty(errorDetail))
                    {
                        return $"错误：API请求失败 ({(int)response.StatusCode}) - {errorDetail}";
                    }
                    return $"错误：API请求失败 ({(int)response.StatusCode})";
                }

                using JsonDocument doc = JsonDocument.Parse(responseBody);
                string? answer = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                QSummaryCore.Log.Print($"用时 {elapsed:F2}s");
                QSummaryCore.Log.Print(answer?.Trim()??"");

                return !string.IsNullOrEmpty(answer) ? answer.Trim() : "错误：未能获取回答内容";
            }
            catch (Exception ex)
            {
                QSummaryCore.Log.Print($"HTTP request failed: {ex.Message}", QSummaryCore.Log.Stat.ERROR  );
                return $"错误：请求异常 - {ex.Message}";
            }
        }

        public string? GetAnswerStrSync(List<QSummaryCore.ChatContent> text, string systemPrompt = "auto")
        {
            if (text == null || text.Count == 0) return "";


            // 系统提示
            string finalSystemPrompt = systemPrompt switch
            {
                "auto" => sysPmpt,
                "" or "None" => "",
                _ => systemPrompt
            };

            // 收集图片
            var imageList = new List<string>();
            foreach (var t in text)
            {
                if (!t.OwnByMyself)
                {
                    foreach (var img in t.ImagePaths)
                    {
                        if (File.Exists(img))
                        {
                            imageList.Add(img);
                            if (imageList.Count >= MaxImageCount) break;
                        }
                        else
                        {
                            QSummaryCore.Log.Print($"× 没有找到图片 {img}", QSummaryCore.Log.Stat.WARN);
                        }
                    }
                    if (imageList.Count >= MaxImageCount) break;
                }
            }

            // 构建 messages
            var messages = new List<Dictionary<string, object>>();
            if (!string.IsNullOrEmpty(finalSystemPrompt))
            {
                messages.Add(new Dictionary<string, object>
                {
                    ["role"] = "system",
                    ["content"] = finalSystemPrompt
                });
            }

            messages.AddRange(ConcatenateText(text, imageList));

            // 构造请求体
            var requestBody = new Dictionary<string, object>
            {
                ["model"] = ModelName,
                ["messages"] = messages,
                ["max_tokens"] = MAX_LENGTH,
                ["temperature"] = 0.7
            };

            string json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { WriteIndented = false });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // 设置 Headers
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
                var startTime = DateTime.UtcNow;
                QSummaryCore.Log.Print($"Sending request to: {ServerUrl}/chat/completions");
                QSummaryCore.Log.Print(json); // 可选：调试输出

                // 同步调用
                HttpResponseMessage response = _httpClient.PostAsync($"{ServerUrl}/chat/completions", content).GetAwaiter().GetResult();
                string responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                if (!response.IsSuccessStatusCode)
                {
                    QSummaryCore.Log.Print($"API Error: {response.StatusCode} - {responseBody}", QSummaryCore.Log.Stat.ERROR);
                    return null;
                }

                using JsonDocument doc = JsonDocument.Parse(responseBody);
                string? answer = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                QSummaryCore.Log.Print($"用时 {elapsed:F2}s");
                QSummaryCore.Log.Print(answer?.Trim() ?? "");

                return answer?.Trim();
            }
            catch (Exception ex)
            {
                QSummaryCore.Log.Print($"HTTP request failed: {ex.Message}", QSummaryCore.Log.Stat.ERROR);
                return null;
            }
        }
        public void Test()
        {
            try
            {
                QSummaryCore.ChatContent c = new("", [], "你好", "", false);
                QSummaryCore.Log.Print($"[ASSISTANT]: {GetAnswer([c])}");
            }
            catch (Exception ex)
            {
                QSummaryCore.Log.Print($"Test failed: {ex.Message}"  , QSummaryCore.Log.Stat.ERROR);
            }
        }
    }
}
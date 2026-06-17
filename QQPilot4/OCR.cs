using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace QSummaryCore
{
    /// <summary>
    /// OCR识别结果项（data.format=dict时使用）
    /// </summary>
    public class Ocr
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("score")]
        public double Score { get; set; }

        [JsonPropertyName("box")]
        public List<List<int>> Box { get; set; } = new();

        [JsonPropertyName("end")]
        public string End { get; set; } = string.Empty;
    }

    /// <summary>
    /// OCR统一响应结果
    /// </summary>
    public class OcrResponseResult
    {
        public int Code { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public List<Ocr> Items { get; set; } = new();
        public string PlainText { get; set; } = string.Empty;
        public double Time { get; set; }
        public double Timestamp { get; set; }

        public bool IsSuccess => Code == 100;
        public bool IsNoText => Code == 101;
    }

    /// <summary>
    /// Base64图片OCR识别客户端
    /// </summary>
    public class OcrClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly Dictionary<string, object> _defaultOptions;

        /// <summary>
        /// 初始化OCR客户端
        /// </summary>
        /// <param name="baseUrl">服务地址，如 http://127.0.0.1:1224</param>
        /// <param name="timeoutSeconds">超时秒数，默认30s</param>
        /// <param name="defaultOptions">默认识别参数，可在每次调用时覆盖</param>
        public OcrClient(string baseUrl= "http://127.0.0.1:1224", int timeoutSeconds = 30, Dictionary<string, object>? defaultOptions = null)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };

            _defaultOptions = defaultOptions ?? new Dictionary<string, object>
            {
                ["ocr.language"] = "models/config_chinese.txt",
                ["ocr.cls"] = true,
                ["ocr.limit_side_len"] = 4320,
                ["tbpu.parser"] = "multi_none",
                ["data.format"] = "dict"
            };
        }

        /// <summary>
        /// 通过Base64字符串识别图片
        /// </summary>
        /// <param name="base64Image">纯Base64编码，不含data:image前缀</param>
        /// <param name="optionsOverride">可选，覆盖默认参数</param>
        /// <param name="cancellationToken">取消令牌</param>
        public async Task<OcrResponseResult> RecognizeBase64Async(
            string base64Image,
            Dictionary<string, object>? optionsOverride = null,
            CancellationToken cancellationToken = default)
        {
            // 合并参数：默认参数 + 覆盖参数
            var mergedOptions = new Dictionary<string, object>(_defaultOptions);
            if (optionsOverride != null)
            {
                foreach (var kv in optionsOverride)
                    mergedOptions[kv.Key] = kv.Value;
            }

            var requestBody = new { base64 = base64Image, options = mergedOptions };
            string jsonContent = JsonSerializer.Serialize(requestBody);
            using var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_baseUrl}/api/ocr", content, cancellationToken);
            string rawResponse = await response.Content.ReadAsStringAsync(cancellationToken);

            // ⚠️ 关键兼容处理：防止HTTP库自动将\\n转为真实换行导致JSON解析失败
            string safeResponse = rawResponse.Replace("\r\n", "\\n").Replace("\n", "\\n");

            using var doc = JsonDocument.Parse(safeResponse);
            var root = doc.RootElement;

            var result = new OcrResponseResult
            {
                Code = root.GetProperty("code").GetInt32(),
                Time = root.GetProperty("time").GetDouble(),
                Timestamp = root.GetProperty("timestamp").GetDouble()
            };

            var dataElement = root.GetProperty("data");
            string dataFormat = mergedOptions.ContainsKey("data.format")
                ? mergedOptions["data.format"].ToString()!
                : "dict";

            if (result.IsSuccess)
            {
                if (dataFormat == "text")
                {
                    result.PlainText = dataElement.GetString() ?? string.Empty;
                }
                else
                {
                    // dict格式：反序列化为结构化列表
                    result.Items = JsonSerializer.Deserialize<List<Ocr>>(
                        dataElement.GetRawText()) ?? new List<Ocr>();

                    // 同时生成拼接后的纯文本，方便直接使用
                    var sb = new StringBuilder();
                    foreach (var item in result.Items)
                    {
                        sb.Append(item.Text);
                        sb.Append(item.End ?? "");
                    }
                    result.PlainText = sb.ToString();
                }
            }
            else
            {
                // code==101(无文本) 或其他失败码，data均为string
                result.ErrorMessage = dataElement.GetString() ?? "未知错误";
            }

            return result;
        }

        /// <summary>
        /// 便捷方法：直接传入图片文件路径进行识别
        /// </summary>
        public async Task<OcrResponseResult> RecognizeFileAsync(
            string filePath,
            Dictionary<string, object>? optionsOverride = null,
            CancellationToken cancellationToken = default)
        {
            byte[] bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            string base64 = Convert.ToBase64String(bytes);
            return await RecognizeBase64Async(base64, optionsOverride, cancellationToken);
        }

        public void Dispose() => _httpClient.Dispose();
    }
}
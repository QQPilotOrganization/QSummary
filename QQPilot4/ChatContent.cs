using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QSummaryCore
{
    public class ChatContent(string username, List<string> imagePaths, string text, string time, bool ownByMyself)
    {
        // 属性定义
        public string Username { get; set; } = username;
        public List<string> ImagePaths { get; set; } = imagePaths;
        public string Text { get; set; } = text;
        public string Time { get; set; } = time;
        public bool OwnByMyself { get; set; } = ownByMyself;

        // 生成报告的方法
        // 逻辑：检查文件是否存在，格式化输出
        public string Report()
        {
            // 1. 前缀标记
            string prefix = OwnByMyself ? "[你]" : "";

            // 2. 文本处理 (空内容显示为【空】)
            string content = string.IsNullOrEmpty(Text) ? "【空】" : Text;

            // 3. 图片有效性检查 (核心逻辑移植)
            // 筛选出真实存在于硬盘上的图片路径
            var validImages = ImagePaths.Where(path => File.Exists(path)).ToList();

            string imagePart;
            if (validImages.Any())
            {
                // 将路径列表转换为字符串表示形式，例如 ["path1", "path2"]
                imagePart = "[ " + string.Join(", ", validImages.Select(p => $"\"{p}\"")) + " ]";
            }
            else
            {
                imagePart = "无";
            }

            return $"{prefix}{Username}: {content}\n{Time}\n 图片：{imagePart}";
        }

        // 重写 ToString 方法
        public override string ToString()
        {
            string content = string.IsNullOrEmpty(Text) ? "【空】" : Text;


            if (OwnByMyself)
            {
                return content[..(content.ToString().Length - 1)];
            }
            else
            {
                return $"{Username}:{content}";
            }
        }

        public static bool operator ==(ChatContent self, ChatContent other)
        {
            return (self.Text==other.Text && self.Time==other.Time && self.Username==other.Username);
        }
        public static bool operator !=(ChatContent self, ChatContent other)
        {
            return !(self == other);
        }
        public string ToJson(bool indented = false)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = indented,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase, // 属性名转小驼峰
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull // 忽略null值
            };

            var data = new
            {
                Username,
                ImagePaths = ImagePaths?.Where(p => File.Exists(p)).ToList(), // 仅保留有效图片
                Text = string.IsNullOrEmpty(Text) ? "【空】" : Text,
                Time,
                OwnByMyself
            };

            return JsonSerializer.Serialize(data, options);
        }
    }
}
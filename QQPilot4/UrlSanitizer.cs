using System;
using System.Collections.Generic;
using System.Text;

namespace QsummaryCore
{
    using System.Text;

    public static class UrlSanitizer
    {
        /// <summary>
        /// 保留中文、字母、数字等安全字符，仅替换 URL 路径段中的非法字符
        /// </summary>
        public static string SanitizeUrlSegment(string input, char replacement = '-')
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            var sb = new StringBuilder(input.Length);
            bool lastWasReplacement = false;

            foreach (char c in input)
            {
                // ✅ 允许的字符：
                // - 字母、数字
                // - 中文及 CJK 扩展区 (\u4e00-\u9fff, \u3400-\u4dbf)
                // - URL 路径段安全符号: - _ . ~
                // - 常见 Unicode 标点/文字（日文、韩文、Emoji 等按需添加）
                bool isSafe = (c >= 'a' && c <= 'z') ||
                              (c >= 'A' && c <= 'Z') ||
                              (c >= '0' && c <= '9') ||
                              (c >= '\u4e00' && c <= '\u9fff') ||   // CJK 统一汉字
                              (c >= '\u3400' && c <= '\u4dbf') ||   // CJK 扩展 A
                              (c >= '\uf900' && c <= '\ufaff') ||   // CJK 兼容汉字
                              c == '-' || c == '_' || c == '.' || c == '~';

                if (isSafe)
                {
                    sb.Append(c);
                    lastWasReplacement = false;
                }
                else if (!lastWasReplacement)
                {
                    // 连续非法字符合并为一个替换符，避免 "---"
                    sb.Append(replacement);
                    lastWasReplacement = true;
                }
            }

            return sb.ToString().Trim(replacement);
        }
    }
}

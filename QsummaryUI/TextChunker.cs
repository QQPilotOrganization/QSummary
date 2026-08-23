using QSummaryCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace QsummaryUI
{
    /// <summary>
    /// 聊天记录分块工具
    /// </summary>
    internal static class TextChunker
    {
        /// <summary>
        /// 将聊天记录按消息数量分成多个块
        /// </summary>
        /// <param name="comments">聊天记录列表（按时间排序）</param>
        /// <param name="messagesPerChunk">每个块的消息数（权重）</param>
        /// <param name="overlapMessages">相邻块之间重叠的消息数</param>
        internal static List<ChatChunk> ChunkByMessageCount(List<ChatContent> comments, int messagesPerChunk = 50, int overlapMessages = 5)
        {
            if (comments == null || comments.Count == 0) return new List<ChatChunk>();
            if (messagesPerChunk <= 0) messagesPerChunk = 50;
            if (overlapMessages < 0) overlapMessages = 0;
            if (overlapMessages >= messagesPerChunk) overlapMessages = Math.Max(0, messagesPerChunk / 5);

            var chunks = new List<ChatChunk>();
            int index = 0;
            int start = 0;

            while (start < comments.Count)
            {
                int end = Math.Min(start + messagesPerChunk, comments.Count);
                var chunkComments = comments.Skip(start).Take(end - start).ToList();

                chunks.Add(new ChatChunk
                {
                    ChunkIndex = index++,
                    Comments = chunkComments,
                    MessageCount = chunkComments.Count,
                    TimeRange = GetTimeRange(chunkComments),
                    Usernames = string.Join(", ", chunkComments.Select(c => c.Username).Distinct().ToList())
                });

                if (end >= comments.Count) break;
                // 前进 (messagesPerChunk - overlapMessages) 条
                start += Math.Max(1, messagesPerChunk - overlapMessages);
            }

            return chunks;
        }

        /// <summary>
        /// 按大概字符数分块（每块达到 targetChars 字符后，在最近的一条消息处切分）
        /// </summary>
        internal static List<ChatChunk> ChunkByCharCount(List<ChatContent> comments, int targetChars = 4000, int overlapMessages = 2)
        {
            if (comments == null || comments.Count == 0) return new List<ChatChunk>();
            if (targetChars <= 0) targetChars = 4000;
            if (overlapMessages < 0) overlapMessages = 0;

            var chunks = new List<ChatChunk>();
            int index = 0;
            int start = 0;

            while (start < comments.Count)
            {
                var currentList = new List<ChatContent>();
                int currentChars = 0;
                int end = start;
                for (; end < comments.Count; end++)
                {
                    string msgText = comments[end].ToString();
                    if (currentList.Count > 0 && currentChars + msgText.Length > targetChars)
                    {
                        break;
                    }
                    currentList.Add(comments[end]);
                    currentChars += msgText.Length;
                }

                if (currentList.Count == 0 && end < comments.Count)
                {
                    // 单条消息就超过了上限，硬塞进去
                    currentList.Add(comments[end]);
                    end++;
                }

                chunks.Add(new ChatChunk
                {
                    ChunkIndex = index++,
                    Comments = currentList,
                    MessageCount = currentList.Count,
                    TimeRange = GetTimeRange(currentList),
                    Usernames = string.Join(", ", currentList.Select(c => c.Username).Distinct().ToList())
                });

                if (end >= comments.Count) break;
                start = Math.Max(0, end - overlapMessages);
            }

            return chunks;
        }

        /// <summary>
        /// 把块里的消息格式化为一段文本（用于 embedding / 发送给 LLM）
        /// </summary>
        internal static string FormatChunkText(ChatChunk chunk, bool includeTime = true)
        {
            var sb = new StringBuilder();
            foreach (var c in chunk.Comments)
            {
                if (includeTime && !string.IsNullOrEmpty(c.Time))
                {
                    sb.Append($"[{c.Time}] ");
                }
                sb.AppendLine(c.ToString());
            }
            return sb.ToString().TrimEnd();
        }

        private static string GetTimeRange(List<ChatContent> comments)
        {
            if (comments == null || comments.Count == 0) return "";
            string? first = comments.FirstOrDefault()?.Time;
            string? last = comments.LastOrDefault()?.Time;
            if (string.IsNullOrEmpty(first) && string.IsNullOrEmpty(last)) return "";
            if (first == last) return first ?? "";
            return $"{first} ~ {last}";
        }
    }

    internal class ChatChunk
    {
        public int ChunkIndex { get; set; }
        public List<ChatContent> Comments { get; set; } = new();
        public int MessageCount { get; set; }
        public string TimeRange { get; set; } = "";
        public string Usernames { get; set; } = "";
    }
}

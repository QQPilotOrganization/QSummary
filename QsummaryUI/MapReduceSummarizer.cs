using QSummaryCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QsummaryUI
{
    /// <summary>
    /// Map-Reduce 摘要器：
    /// 1. Map 阶段：将聊天记录切分成多块，分别让 AI 总结
    /// 2. Reduce 阶段：将所有小块总结拼接，让 AI 做最终归纳
    /// </summary>
    internal class MapReduceSummarizer
    {
        private readonly Answer _answer;

        // 系统提示词模板
        private const string CHUNK_SUMMARY_SYSTEM_PROMPT = @"你是一个专业的聊天记录摘要助手。
请阅读下面这段群聊记录，提取关键信息，写出一段简洁但内容完整的摘要。
要求：
1. 保留所有核心讨论的话题、结论、决定、待办事项
2. 保留关键的数字、时间点、人物
3. 不要遗漏有实质内容的讨论
4. 用清晰的中文撰写，不需要寒暄，直接写要点
5. 输出控制在 300~800 字以内";

        private const string FINAL_REDUCE_SYSTEM_PROMPT = @"你是一个专业的会议纪要和群聊总结助手。
下面是多段聊天记录的分块摘要，请把它们整合为一份完整、逻辑清晰的最终总结。
要求：
1. 先写总体概览（讨论主题、参与人数、消息规模、时间范围）
2. 再按话题分点详细论述，每个话题要有『讨论内容 + 结论/决定』
3. 合并重复的讨论，去重后整理
4. 最后列出所有待办事项 / Action Items（如果有）
5. 用结构化、清晰的中文撰写";

        public MapReduceSummarizer(Answer answer)
        {
            _answer = answer;
        }

        /// <summary>
        /// 使用 Map-Reduce 策略生成完整摘要
        /// </summary>
        /// <param name="groupName">群名</param>
        /// <param name="comments">聊天记录</param>
        /// <param name="messagesPerChunk">每块消息数（越大单块总结越详细，但块数越少）</param>
        /// <param name="useCharBased">true=按字符数分块，false=按消息数分块</param>
        public string SummarizeMapReduce(string groupName, List<ChatContent> comments,
            int messagesPerChunk = 60, bool useCharBased = false)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== {groupName} 聊天总结（Map-Reduce 策略）===");
            sb.AppendLine($"生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"消息总数: {comments.Count}");

            var participants = comments.Select(c => c.Username).Distinct().ToList();
            sb.AppendLine($"参与成员 ({participants.Count} 人): {string.Join(", ", participants)}");

            string timeRange = GetTimeRange(comments);
            if (!string.IsNullOrEmpty(timeRange))
            {
                sb.AppendLine($"时间范围: {timeRange}");
            }
            sb.AppendLine();

            // Step 1: 分块（Chunking）
            Log.Print($"[MapReduce] 开始分块，共 {comments.Count} 条消息");
            List<ChatChunk> chunks;
            if (useCharBased)
            {
                chunks = TextChunker.ChunkByCharCount(comments, targetChars: 6000);
            }
            else
            {
                chunks = TextChunker.ChunkByMessageCount(comments, messagesPerChunk: messagesPerChunk, overlapMessages: 5);
            }
            Log.Print($"[MapReduce] 分块完成，共 {chunks.Count} 块");
            sb.AppendLine($"分块数量: {chunks.Count} (每块约 {messagesPerChunk} 条消息)");
            sb.AppendLine();

            // 如果只有 1 块，直接调用普通总结即可
            if (chunks.Count <= 1)
            {
                Log.Print("[MapReduce] 块数 <= 1，走单块总结路径");
                string single = SummarySingleChunk(chunks[0], includeHeader: false);
                sb.AppendLine("--- 总结 ---");
                sb.AppendLine(single);
                return sb.ToString();
            }

            // Step 2: Map 阶段 - 对每个块分别总结
            sb.AppendLine("--- 分块总结（Map 阶段）---");
            var chunkSummaries = new List<(int Index, string TimeRange, string Summary)>();

            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                Log.Print($"[MapReduce] 正在总结第 {i + 1}/{chunks.Count} 块 (消息数: {chunk.MessageCount}, 时间: {chunk.TimeRange})");

                string chunkSummary = SummarySingleChunk(chunk, includeHeader: false);
                chunkSummaries.Add((i, chunk.TimeRange, chunkSummary));

                sb.AppendLine();
                sb.AppendLine($"【块 {i + 1} / {chunks.Count}】 时间: {chunk.TimeRange}");
                sb.AppendLine($"参与: {chunk.Usernames}");
                sb.AppendLine(chunkSummary);
                sb.AppendLine();
            }

            // Step 3: Reduce 阶段 - 把小块总结拼起来让 AI 做最终归纳
            Log.Print("[MapReduce] 进入 Reduce 阶段，生成最终总结");
            sb.AppendLine();
            sb.AppendLine("--- 最终归纳（Reduce 阶段）---");

            string finalSummary = ReduceSummaries(groupName, comments, chunkSummaries);
            sb.AppendLine(finalSummary);

            Log.Print("[MapReduce] 全部完成");
            return sb.ToString();
        }

        /// <summary>
        /// 总结单个块
        /// </summary>
        private string SummarySingleChunk(ChatChunk chunk, bool includeHeader = true)
        {
            var sb = new StringBuilder();
            if (includeHeader)
            {
                sb.AppendLine($"时间: {chunk.TimeRange}");
                sb.AppendLine($"参与: {chunk.Usernames}");
                sb.AppendLine("聊天记录:");
            }
            sb.AppendLine(TextChunker.FormatChunkText(chunk));

            var content = new ChatContent("", new List<string>(), sb.ToString(), "", false);
            return _answer.GetAnswerSync(new List<ChatContent> { content }, CHUNK_SUMMARY_SYSTEM_PROMPT);
        }

        /// <summary>
        /// Reduce：把多个小块总结整合为最终归纳
        /// </summary>
        private string ReduceSummaries(string groupName, List<ChatContent> allComments,
            List<(int Index, string TimeRange, string Summary)> chunkSummaries)
        {
            var reducePrompt = new StringBuilder();
            reducePrompt.AppendLine($"群组: {groupName}");
            reducePrompt.AppendLine($"参与人数: {allComments.Select(c => c.Username).Distinct().Count()}");
            reducePrompt.AppendLine($"消息总数: {allComments.Count}");
            reducePrompt.AppendLine($"时间范围: {GetTimeRange(allComments)}");
            reducePrompt.AppendLine();
            reducePrompt.AppendLine("--- 以下是分块摘要 ---");
            reducePrompt.AppendLine();

            foreach (var cs in chunkSummaries)
            {
                reducePrompt.AppendLine($"【第 {cs.Index + 1} 块 - 时间: {cs.TimeRange}】");
                reducePrompt.AppendLine(cs.Summary);
                reducePrompt.AppendLine();
            }

            var content = new ChatContent("", new List<string>(), reducePrompt.ToString(), "", false);
            return _answer.GetAnswerSync(new List<ChatContent> { content }, FINAL_REDUCE_SYSTEM_PROMPT);
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
}

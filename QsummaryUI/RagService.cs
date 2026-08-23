using QSummaryCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QsummaryUI
{
    /// <summary>
    /// RAG（检索增强生成）服务：
    /// 1. 索引：把聊天记录分块 -> 生成 embedding -> 存入向量数据库
    /// 2. 检索：用户查询 -> 生成 query embedding -> 从向量库找相似块
    /// 3. 生成：把相关块发给 AI 做针对性总结
    /// </summary>
    internal class RagService
    {
        private readonly VectorDB _vectorDB;
        private readonly EmbeddingClient _embeddingClient;
        private readonly Answer _answer;

        private const string RAG_SUMMARY_SYSTEM_PROMPT = @"你是一个专业的聊天记录问答助手。
用户提出了一个特定的问题，下面是从聊天记录中检索到的与该问题最相关的片段。
请基于这些相关片段回答用户的问题，并生成一份针对性的总结。
要求：
1. 如果相关片段中有答案，明确回答并引用相关内容
2. 如果相关片段不能回答用户的问题，明确说明『在当前检索到的聊天片段中没有找到相关信息』
3. 保留关键的人名、时间、数字、结论
4. 回答要清晰、有条理，使用中文
5. 如果有多个相关话题，按重要程度或时间顺序排列";

        public RagService(VectorDB vectorDB, EmbeddingClient embeddingClient, Answer answer)
        {
            _vectorDB = vectorDB;
            _embeddingClient = embeddingClient;
            _answer = answer;
        }

        /// <summary>
        /// 为某个群的聊天记录建立向量索引（分块 -> embedding -> 入库）
        /// </summary>
        /// <returns>是否成功，以及详细信息</returns>
        public async Task<(bool Success, string Message, int ChunkCount)> IndexGroupAsync(string groupName, List<ChatContent> comments)
        {
            if (comments == null || comments.Count == 0)
            {
                return (false, "没有聊天记录", 0);
            }

            try
            {
                // 清空旧索引
                _vectorDB.ClearGroupIndex(groupName);
                int groupGuid = _vectorDB.InsertGroup(groupName);
                if (groupGuid <= 0) return (false, "无法获取或创建群组记录", 0);

                // 分块：RAG 用较小的块以提高召回精度
                var chunks = TextChunker.ChunkByMessageCount(comments, messagesPerChunk: 20, overlapMessages: 3);
                Log.Print($"[RAG] 分块完成：{chunks.Count} 块，开始生成嵌入向量...");

                int processed = 0;
                int failed = 0;

                // 为了控制单次 embedding 请求的文本长度，逐个处理块
                for (int i = 0; i < chunks.Count; i++)
                {
                    var chunk = chunks[i];
                    string chunkText = TextChunker.FormatChunkText(chunk);

                    if (string.IsNullOrWhiteSpace(chunkText)) continue;

                    // 单条 embedding（可以改为批量，视服务稳定性）
                    var vector = await _embeddingClient.GetEmbeddingAsync(chunkText);
                    if (vector == null || vector.Length == 0)
                    {
                        failed++;
                        Log.Print($"[RAG] 块 {i + 1}/{chunks.Count} 生成向量失败，跳过", Log.Stat.WARN);
                        continue;
                    }

                    _vectorDB.InsertChunk(
                        groupGuid,
                        chunk.ChunkIndex,
                        chunkText,
                        chunk.TimeRange,
                        chunk.Usernames,
                        chunk.MessageCount,
                        vector
                    );
                    processed++;

                    if ((i + 1) % 5 == 0 || i == chunks.Count - 1)
                    {
                        Log.Print($"[RAG] 已索引 {i + 1}/{chunks.Count} 块");
                    }
                }

                string msg = $"索引完成。成功 {processed} 块，失败 {failed} 块";
                Log.Print($"[RAG] {msg}");
                return (true, msg, processed);
            }
            catch (Exception ex)
            {
                Log.Print($"[RAG] 索引失败: {ex.Message}", Log.Stat.ERROR);
                return (false, $"索引失败: {ex.Message}", 0);
            }
        }

        /// <summary>
        /// 检查群是否已索引
        /// </summary>
        public bool IsGroupIndexed(string groupName) => _vectorDB.IsGroupIndexed(groupName);

        /// <summary>
        /// RAG 检索 + 总结：针对特定查询生成回答
        /// </summary>
        public async Task<RagQueryResult> QueryAndSummarizeAsync(string groupName, string query, int topK = 8, double minSimilarity = 0.25)
        {
            var result = new RagQueryResult
            {
                Query = query,
                GroupName = groupName
            };

            if (string.IsNullOrWhiteSpace(query))
            {
                result.ErrorMessage = "查询内容为空";
                return result;
            }

            try
            {
                // Step 1: 生成查询的 embedding
                Log.Print($"[RAG] 生成查询向量: {query}");
                var queryVector = await _embeddingClient.GetEmbeddingAsync(query);
                if (queryVector == null || queryVector.Length == 0)
                {
                    result.ErrorMessage = "无法为查询生成嵌入向量，请检查 Embedding 服务配置";
                    return result;
                }

                // Step 2: 向量检索
                Log.Print("[RAG] 开始语义检索...");
                var matches = _vectorDB.SearchSimilar(groupName, queryVector, topK: topK, minSimilarity: minSimilarity);
                result.RetrievedChunks = matches;
                Log.Print($"[RAG] 检索到 {matches.Count} 个相关块 (最低相似度阈值: {minSimilarity})");

                if (matches.Count == 0)
                {
                    result.Summary = $"在聊天记录中没有找到与『{query}』相关的内容。\n可能的原因：\n1. 还没建立索引（请先点「建立/重建索引」）\n2. 话题确实不存在于当前群的聊天记录中\n3. 查询关键词过于模糊，请尝试更具体的描述";
                    return result;
                }

                // Step 3: 组装检索到的上下文，让 AI 总结
                var contextSb = new StringBuilder();
                contextSb.AppendLine($"群组: {groupName}");
                contextSb.AppendLine($"用户问题: {query}");
                contextSb.AppendLine();
                contextSb.AppendLine($"已从聊天记录中检索到 {matches.Count} 个相关片段：");
                contextSb.AppendLine();

                for (int i = 0; i < matches.Count; i++)
                {
                    var m = matches[i];
                    contextSb.AppendLine($"--- 相关片段 #{i + 1} (相似度: {m.Similarity:F3} | 时间: {m.TimeRange}) ---");
                    contextSb.AppendLine($"参与人: {m.Usernames}");
                    contextSb.AppendLine(m.ChunkText);
                    contextSb.AppendLine();
                }

                var content = new ChatContent("", new List<string>(), contextSb.ToString(), "", false);
                Log.Print("[RAG] 发送给 LLM 做最终总结...");
                string summary = _answer.GetAnswerSync(new List<ChatContent> { content }, RAG_SUMMARY_SYSTEM_PROMPT);

                result.Summary = summary;
                result.Success = true;
                Log.Print("[RAG] RAG 总结完成");
                return result;
            }
            catch (Exception ex)
            {
                Log.Print($"[RAG] 查询失败: {ex.Message}", Log.Stat.ERROR);
                result.ErrorMessage = $"查询失败: {ex.Message}";
                return result;
            }
        }
    }

    internal class RagQueryResult
    {
        public bool Success { get; set; }
        public string GroupName { get; set; } = "";
        public string Query { get; set; } = "";
        public string Summary { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
        public List<ChunkSearchResult> RetrievedChunks { get; set; } = new();
    }
}

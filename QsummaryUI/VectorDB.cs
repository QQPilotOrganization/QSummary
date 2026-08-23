using Microsoft.Data.Sqlite;
using QSummaryCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QsummaryUI
{
    public class VectorDB : IDisposable
    {
        readonly SqliteConnection dbConnection;
        bool disposed = false;

        public VectorDB(string file = "vectors.sqlite3")
        {
            Log.Print($"[VectorDB] File={Path.GetFullPath(file)}");
            dbConnection = new SqliteConnection($"Data Source={file};");
            dbConnection.Open();
            InitializeTables();
        }

        private void InitializeTables()
        {
            using var command = dbConnection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS groups (
                    guid INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL UNIQUE
                );";
            command.ExecuteNonQuery();

            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS chat_chunks (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    group_guid INTEGER,
                    chunk_index INTEGER NOT NULL,
                    chunk_text TEXT NOT NULL,
                    time_range TEXT,
                    usernames TEXT,
                    message_count INTEGER NOT NULL,
                    vector BLOB,
                    vector_dim INTEGER,
                    FOREIGN KEY (group_guid) REFERENCES groups(guid)
                );";
            command.ExecuteNonQuery();

            command.CommandText = @"
                CREATE INDEX IF NOT EXISTS idx_chunks_group ON chat_chunks(group_guid);";
            command.ExecuteNonQuery();
        }

        public int InsertGroup(string name)
        {
            name = name.Replace("\n", "");
            using (var command = dbConnection.CreateCommand())
            {
                command.CommandText = "INSERT OR IGNORE INTO groups (name) VALUES ($name);";
                command.Parameters.AddWithValue("$name", name);
                command.ExecuteNonQuery();
            }

            using (var command = dbConnection.CreateCommand())
            {
                command.CommandText = "SELECT guid FROM groups WHERE name = $name;";
                command.Parameters.AddWithValue("$name", name);
                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    return reader.GetInt32(0);
                }
            }
            return -1;
        }

        /// <summary>
        /// 检查某个群是否已经建立了索引
        /// </summary>
        public bool IsGroupIndexed(string groupName)
        {
            using var command = dbConnection.CreateCommand();
            command.CommandText = @"
                SELECT COUNT(*) FROM chat_chunks c
                JOIN groups g ON c.group_guid = g.guid
                WHERE g.name = $name AND c.vector IS NOT NULL;";
            command.Parameters.AddWithValue("$name", groupName);
            long? count = (long?)command.ExecuteScalar();
            return (count ?? 0) > 0;
        }

        /// <summary>
        /// 删除某个群的向量索引
        /// </summary>
        public void ClearGroupIndex(string groupName)
        {
            int guid = InsertGroup(groupName);
            if (guid <= 0) return;

            using var command = dbConnection.CreateCommand();
            command.CommandText = "DELETE FROM chat_chunks WHERE group_guid = $guid;";
            command.Parameters.AddWithValue("$guid", guid);
            command.ExecuteNonQuery();
            Log.Print($"[VectorDB] Cleared index for group: {groupName}");
        }

        /// <summary>
        /// 存储一个块及其向量
        /// </summary>
        public void InsertChunk(int groupGuid, int chunkIndex, string chunkText,
            string timeRange, string usernames, int messageCount, float[] vector)
        {
            using var command = dbConnection.CreateCommand();
            command.CommandText = @"
                INSERT INTO chat_chunks (group_guid, chunk_index, chunk_text, time_range, usernames, message_count, vector, vector_dim)
                VALUES ($group_guid, $chunk_index, $chunk_text, $time_range, $usernames, $message_count, $vector, $vector_dim);";
            command.Parameters.AddWithValue("$group_guid", groupGuid);
            command.Parameters.AddWithValue("$chunk_index", chunkIndex);
            command.Parameters.AddWithValue("$chunk_text", chunkText);
            command.Parameters.AddWithValue("$time_range", timeRange ?? "");
            command.Parameters.AddWithValue("$usernames", usernames ?? "");
            command.Parameters.AddWithValue("$message_count", messageCount);
            command.Parameters.AddWithValue("$vector", VectorToBlob(vector));
            command.Parameters.AddWithValue("$vector_dim", vector?.Length ?? 0);
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// 搜索与查询向量最相似的前 K 个块
        /// </summary>
        internal List<ChunkSearchResult> SearchSimilar(string groupName, float[] queryVector, int topK = 10, double minSimilarity = 0.3)
        {
            if (queryVector == null || queryVector.Length == 0)
            {
                return new List<ChunkSearchResult>();
            }

            var chunks = new List<(int Id, int ChunkIndex, string ChunkText, string TimeRange, string Usernames, int MessageCount, float[] Vector)>();

            using (var command = dbConnection.CreateCommand())
            {
                command.CommandText = @"
                    SELECT c.id, c.chunk_index, c.chunk_text, c.time_range, c.usernames, c.message_count, c.vector
                    FROM chat_chunks c
                    JOIN groups g ON c.group_guid = g.guid
                    WHERE g.name = $name AND c.vector IS NOT NULL;";
                command.Parameters.AddWithValue("$name", groupName);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    byte[] blob = (byte[])reader[6];
                    var vec = BlobToVector(blob);
                    chunks.Add((
                        reader.GetInt32(0),
                        reader.GetInt32(1),
                        reader.GetString(2),
                        reader.IsDBNull(3) ? "" : reader.GetString(3),
                        reader.IsDBNull(4) ? "" : reader.GetString(4),
                        reader.GetInt32(5),
                        vec
                    ));
                }
            }

            // 计算余弦相似度
            var results = new List<ChunkSearchResult>();
            foreach (var chunk in chunks)
            {
                if (chunk.Vector == null || chunk.Vector.Length == 0) continue;
                double similarity = CosineSimilarity(queryVector, chunk.Vector);
                if (similarity >= minSimilarity)
                {
                    results.Add(new ChunkSearchResult
                    {
                        ChunkIndex = chunk.ChunkIndex,
                        ChunkText = chunk.ChunkText,
                        TimeRange = chunk.TimeRange,
                        Usernames = chunk.Usernames,
                        MessageCount = chunk.MessageCount,
                        Similarity = similarity
                    });
                }
            }

            return results
                .OrderByDescending(r => r.Similarity)
                .Take(topK)
                .ToList();
        }

        /// <summary>
        /// float[] -> byte[] BLOB（使用 BinaryWriter）
        /// </summary>
        private static byte[] VectorToBlob(float[]? vector)
        {
            if (vector == null || vector.Length == 0) return Array.Empty<byte>();
            using var ms = new MemoryStream(vector.Length * sizeof(float));
            using var bw = new BinaryWriter(ms);
            foreach (var v in vector) bw.Write(v);
            return ms.ToArray();
        }

        /// <summary>
        /// byte[] BLOB -> float[]
        /// </summary>
        private static float[] BlobToVector(byte[]? blob)
        {
            if (blob == null || blob.Length == 0) return Array.Empty<float>();
            int count = blob.Length / sizeof(float);
            var result = new float[count];
            using var ms = new MemoryStream(blob);
            using var br = new BinaryReader(ms);
            for (int i = 0; i < count; i++) result[i] = br.ReadSingle();
            return result;
        }

        /// <summary>
        /// 计算两个向量的余弦相似度
        /// </summary>
        public static double CosineSimilarity(float[] a, float[] b)
        {
            if (a == null || b == null) return 0;
            int len = Math.Min(a.Length, b.Length);
            if (len == 0) return 0;

            double dot = 0, normA = 0, normB = 0;
            for (int i = 0; i < len; i++)
            {
                dot += (double)a[i] * b[i];
                normA += (double)a[i] * a[i];
                normB += (double)b[i] * b[i];
            }
            normA = Math.Sqrt(normA);
            normB = Math.Sqrt(normB);
            if (normA == 0 || normB == 0) return 0;
            return dot / (normA * normB);
        }

        public void Close() => dbConnection?.Close();

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed) return;
            if (disposing) dbConnection?.Dispose();
            disposed = true;
        }

        ~VectorDB() => Dispose(false);
    }

    internal class ChunkSearchResult
    {
        public int ChunkIndex { get; set; }
        public string ChunkText { get; set; } = "";
        public string TimeRange { get; set; } = "";
        public string Usernames { get; set; } = "";
        public int MessageCount { get; set; }
        public double Similarity { get; set; }
    }
}

using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;

namespace QSummaryCore
{
    public class DB : IDisposable
    {
        readonly SqliteConnection dbConnection;
        bool disposed = false;
        
        public DB(string file= "groups.sqlite3")
        {   
            Log.Print($"File={Path.GetFullPath(file)}");
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
                CREATE TABLE IF NOT EXISTS comments (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    guid INTEGER,
                    Username TEXT NOT NULL,
                    imagePathSepByCol TEXT NOT NULL,
                    text TEXT NOT NULL,
                    time TEXT NOT NULL,
                    ownByMyself BOOLEAN NOT NULL,
                    FOREIGN KEY (guid) REFERENCES groups(guid)
                );";
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
        
        public void InsertComments(int guid, ChatContent[] contents)
        {
            foreach (var content in contents)
            {
                // 先检查是否已存在相同的记录（所有字段组合唯一）
                using (var checkCommand = dbConnection.CreateCommand())
                {
                    checkCommand.CommandText = @"
                        SELECT COUNT(*) FROM comments 
                        WHERE guid = $guid 
                        AND Username = $username 
                        AND imagePathSepByCol = $imagePath 
                        AND text = $text 
                        AND time = $time 
                        AND ownByMyself = $ownByMyself;";
                    checkCommand.Parameters.AddWithValue("$guid", guid);
                    checkCommand.Parameters.AddWithValue("$username", content.Username ?? "");
                    checkCommand.Parameters.AddWithValue("$imagePath", content.ImagePaths != null ? string.Join(";", content.ImagePaths) : "");
                    checkCommand.Parameters.AddWithValue("$text", content.Text ?? "");
                    checkCommand.Parameters.AddWithValue("$time", content.Time ?? "");
                    checkCommand.Parameters.AddWithValue("$ownByMyself", content.OwnByMyself);
                    
                    long? count = (long?)checkCommand.ExecuteScalar();
                    if ((count??1) > 0)
                    {
                        // 已存在相同记录，跳过插入
                        continue;
                    }
                }
                
                // 不存在相同记录，执行插入
                using var command = dbConnection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO comments (guid, Username, imagePathSepByCol, text, time, ownByMyself)
                    VALUES ($guid, $username, $imagePath, $text, $time, $ownByMyself);";
                command.Parameters.AddWithValue("$guid", guid);
                command.Parameters.AddWithValue("$username", content.Username ?? "");
                command.Parameters.AddWithValue("$imagePath", content.ImagePaths != null ? string.Join(";", content.ImagePaths) : "");
                command.Parameters.AddWithValue("$text", content.Text ?? "");
                command.Parameters.AddWithValue("$time", content.Time ?? "");
                command.Parameters.AddWithValue("$ownByMyself", content.OwnByMyself);
                command.ExecuteNonQuery();
            }
        }
        
        public void Insert(string name, List<ChatContent> contents)
        {
            int guid = InsertGroup(name);
            if (guid > 0 && contents != null)
            {
                InsertComments(guid, [.. contents]);
            }
        }
        public void Insert(string name, ChatContent[] contents)
        {
            this.Insert(name, (List<ChatContent>)[.. contents]);
        }
        public List<ChatContent> GetCommentsByGroupName(string groupName)
        {
            var comments = new List<ChatContent>();
            using var command = dbConnection.CreateCommand();
            command.CommandText = @"
                SELECT c.Username, c.imagePathSepByCol, c.text, c.time, c.ownByMyself
                FROM comments c
                JOIN groups g ON c.guid = g.guid
                WHERE g.name = $name;";
            command.Parameters.AddWithValue("$name", groupName);
            
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string imagePathStr = reader.GetString(1);
                var imagePaths = string.IsNullOrEmpty(imagePathStr)
                    ? new List<string>()
                    : [.. imagePathStr.Split(';')];
                
                comments.Add(new ChatContent(
                    reader.GetString(0),
                    imagePaths,
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetBoolean(4)
                ));
            }
            return comments;
        }
        
        public List<string> GetAllGroups()
        {
            var groups = new List<string>();
            using var command = dbConnection.CreateCommand();
            command.CommandText = "SELECT name FROM groups;";
            
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                groups.Add(reader.GetString(0));
            }
            return groups;
        }
        
        public void Close()
        {
            dbConnection?.Close();
        }
        
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;
            
            if (disposing)
            {
                dbConnection?.Dispose();
            }
            
            disposed = true;
        }
        
        ~DB()
        {
            Dispose(false);
        }
        
        public void ClearAll()
        {
            using var command = dbConnection.CreateCommand();
            
            // 先删除 comments 表中的所有数据
            command.CommandText = "DELETE FROM comments;";
            command.ExecuteNonQuery();
            
            // 再删除 groups 表中的所有数据
            command.CommandText = "DELETE FROM groups;";
            command.ExecuteNonQuery();
            
            // 重置自增ID（可选）
            command.CommandText = "DELETE FROM sqlite_sequence WHERE name IN ('comments', 'groups');";
            command.ExecuteNonQuery();
            
            Log.Print("数据库已清空");
        }
        
        public static void Test()
        {
            string testDbPath = "test_db.db";
            
            if (System.IO.File.Exists(testDbPath))
            {
                System.IO.File.Delete(testDbPath);
            }
            
            try
            {
                using (var db = new DB(testDbPath))
                {
                    Log.Print("1. 测试 InsertGroup");
                    int gid1 = db.InsertGroup("群1");
                    int gid2 = db.InsertGroup("群2");
                    int gidDup = db.InsertGroup("群1");
                    Log.Print($"群1 ID: {gid1}, 群2 ID: {gid2}, 重复群1 ID: {gidDup}");
                    
                    Log.Print("\n2. 测试 GetAllGroups");
                    var groups = db.GetAllGroups();
                    Log.Print($"群列表: {string.Join(", ", groups)}");
                    
                    Log.Print("\n3. 测试 InsertComments");
                    var contents = new ChatContent[]
                    {
                        new("用户A", ["img1.jpg"], "你好", "10:00", false),
                        new("用户B", [], "你好呀", "10:01", true)
                    };
                    db.InsertComments(gid1, contents);
                    Log.Print("插入成功");
                    
                    Log.Print("\n4. 测试 GetCommentsByGroupName");
                    var result = db.GetCommentsByGroupName("群1");
                    foreach (var c in result)
                    {
                        Log.Print($"{c.Username}: {c.Text} [{c.Time}] 自己:{c.OwnByMyself}");
                    }
                    
                    Log.Print("\n5. 测试 Insert (综合)");
                    db.Insert("新群", new ChatContent[] { new("管理员", [], "欢迎", "09:00", false) });
                    var newGroups = db.GetAllGroups();
                    Log.Print($"更新后群列表: {string.Join(", ", newGroups)}");
                }
                
                Log.Print("\n=== 测试通过 ===");
            }
            catch (Exception ex)
            {
                Log.Print($"测试失败: {ex.Message}");
            }
            finally
            {
                //if (System.IO.File.Exists(testDbPath))
                //{
                //    System.IO.File.Delete(testDbPath);
                //}
            }
        }
    }
}
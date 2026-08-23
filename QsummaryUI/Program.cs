using IniParser.Model;
using QSummaryCore;
using QsummaryUI;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(new DB());
builder.Services.AddSingleton(new Config());
builder.Services.AddSingleton<VectorDB>();
builder.Services.AddCors();

var app = builder.Build();

app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
app.UseDefaultFiles();
Console.WriteLine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot"));
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
        Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot")))
});

// ---------- 工具方法：创建 EmbeddingClient（从 Config 读取配置）----------
EmbeddingClient CreateEmbeddingClient(Config cfg)
{
    return new EmbeddingClient(
        serverUrl: cfg.EmbeddingServerUrl,
        apiKey: cfg.EmbeddingApiKey,
        modelName: cfg.EmbeddingModelName,
        timeoutSeconds: cfg.RemoteServerTimeout
    );
}

// ---------- 工具方法：创建 RagService ----------
RagService CreateRagService(Config cfg, VectorDB vdb, Answer ans)
{
    var emb = CreateEmbeddingClient(cfg);
    return new RagService(vdb, emb, ans);
}

// ==================================================================
// 原有 API
// ==================================================================
app.MapGet("/api/groups", (DB db) =>
{
    try
    {
        var groups = db.GetAllGroups();
        Console.WriteLine("Groups: " + string.Join(", ", groups));
        return Results.Json(groups);
    }
    catch (Exception ex)
    {
        Console.WriteLine("Error getting groups: " + ex.Message);
        return Results.Json(new List<string>());
    }
});

app.MapGet("/api/comments/{groupName}", (string groupName, DB db) =>
{
    var comments = db.GetCommentsByGroupName(groupName);

    var result = comments.Select(c => new {
        c.Username,
        Text = c.Text,
        Time = c.Time,
        OwnByMyself = c.OwnByMyself,
        Images = c.ImagePaths?.Select(imgPath => {
            try {
                if (File.Exists(imgPath)) {
                    byte[] imageBytes = File.ReadAllBytes(imgPath);
                    string base64 = Convert.ToBase64String(imageBytes);
                    string extension = Path.GetExtension(imgPath).ToLower();
                    string mimeType = extension switch {
                        ".jpg" or ".jpeg" => "image/jpeg",
                        ".png" => "image/png",
                        ".gif" => "image/gif",
                        ".bmp" => "image/bmp",
                        _ => "image/jpeg"
                    };
                    return $"data:{mimeType};base64,{base64}";
                }
            } catch { }
            return null;
        }).Where(img => img != null).ToList()
    });

    return Results.Json(result);
});

// 原有普通总结 API（保留，走全量一次性总结）
app.MapPost("/api/summary", async (HttpRequest req, DB db) =>
{
    string body = await new StreamReader(req.Body).ReadToEndAsync();
    Console.WriteLine($"[Summary API] Received body: {body}");

    var data = JsonSerializer.Deserialize<Dictionary<string, string>>(body);
    Console.WriteLine($"[Summary API] Parsed data: {(data != null ? string.Join(", ", data) : "null")}");

    if (data == null || !data.TryGetValue("groupName", out string? groupName) || string.IsNullOrEmpty(groupName))
    {
        Console.WriteLine("[Summary API] Error: Missing or empty groupName");
        return Results.Json(new { success = false, message = "缺少 groupName 参数" });
    }

    Console.WriteLine($"[Summary API] groupName: '{groupName}'");

    try
    {
        var comments = db.GetCommentsByGroupName(groupName);
        string summary = GenerateSummary(groupName, comments);

        var dateStr = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string fileName = $"{groupName}-{dateStr}.txt";
        string filePath = Path.Combine(AppContext.BaseDirectory, "summaries", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, summary);

        return Results.Json(new { success = true, summary, filePath, fileName });
    }
    catch (Exception ex)
    {
        return Results.Json(new { success = false, message = ex.Message });
    }
});

// ==================================================================
// 新增 1. Map-Reduce 总结 API
// ==================================================================
app.MapPost("/api/summary/mapreduce", async (HttpRequest req, DB db) =>
{
    string body = await new StreamReader(req.Body).ReadToEndAsync();
    Log.Print($"[MapReduce API] Received: {body}");

    var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body);
    if (data == null || !data.TryGetValue("groupName", out var gnEl))
    {
        return Results.Json(new { success = false, message = "缺少 groupName 参数" });
    }
    string groupName = gnEl.GetString() ?? "";
    if (string.IsNullOrEmpty(groupName))
    {
        return Results.Json(new { success = false, message = "groupName 不能为空" });
    }

    // 可选参数
    int messagesPerChunk = 60;
    if (data.TryGetValue("messagesPerChunk", out var mpcEl) && mpcEl.TryGetInt32(out int mpc))
    {
        messagesPerChunk = Math.Clamp(mpc, 5, 500);
    }
    bool useCharBased = false;
    if (data.TryGetValue("useCharBased", out var ucbEl))
    {
        useCharBased = ucbEl.GetBoolean();
    }

    try
    {
        var comments = db.GetCommentsByGroupName(groupName);
        Log.Print($"[MapReduce API] group={groupName}, comments={comments.Count}, chunkSize={messagesPerChunk}");

        var answer = new Answer();
        var summarizer = new MapReduceSummarizer(answer);
        string summary = summarizer.SummarizeMapReduce(groupName, comments, messagesPerChunk, useCharBased);

        var dateStr = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string fileName = $"{groupName}-MR-{dateStr}.txt";
        string filePath = Path.Combine(AppContext.BaseDirectory, "summaries", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, summary);

        return Results.Json(new { success = true, summary, filePath, fileName });
    }
    catch (Exception ex)
    {
        Log.Print($"[MapReduce API] Error: {ex.Message}", Log.Stat.ERROR);
        return Results.Json(new { success = false, message = ex.Message });
    }
});

// ==================================================================
// 新增 2. RAG 相关 API
// ==================================================================

// 检查群是否已建立向量索引
app.MapGet("/api/rag/status/{groupName}", (string groupName, VectorDB vdb) =>
{
    bool indexed = vdb.IsGroupIndexed(groupName);
    return Results.Json(new { groupName, indexed, success = true });
});

// 为某个群建立/重建向量索引
app.MapPost("/api/rag/index", async (HttpRequest req, DB db, VectorDB vdb, Config cfg) =>
{
    string body = await new StreamReader(req.Body).ReadToEndAsync();
    Log.Print($"[RAG Index API] Received: {body}");

    var data = JsonSerializer.Deserialize<Dictionary<string, string>>(body);
    if (data == null || !data.TryGetValue("groupName", out string? groupName) || string.IsNullOrEmpty(groupName))
    {
        return Results.Json(new { success = false, message = "缺少 groupName 参数" });
    }

    if (string.IsNullOrEmpty(cfg.EmbeddingModelName))
    {
        return Results.Json(new { success = false, message = "尚未配置 Embedding 模型，请先到「设置」里配置 Embedding 服务 URL、API Key 和模型名称" });
    }

    try
    {
        var comments = db.GetCommentsByGroupName(groupName);
        if (comments.Count == 0)
        {
            return Results.Json(new { success = false, message = "该群暂无聊天记录" });
        }

        using var embClient = CreateEmbeddingClient(cfg);
        var answer = new Answer();
        var rag = new RagService(vdb, embClient, answer);

        var (success, msg, chunkCount) = await rag.IndexGroupAsync(groupName, comments);
        return Results.Json(new { success, message = msg, chunkCount });
    }
    catch (Exception ex)
    {
        Log.Print($"[RAG Index API] Error: {ex.Message}", Log.Stat.ERROR);
        return Results.Json(new { success = false, message = ex.Message });
    }
});

// RAG 查询 + 针对性总结（「总结某个特定话题」）
app.MapPost("/api/rag/query", async (HttpRequest req, DB db, VectorDB vdb, Config cfg) =>
{
    string body = await new StreamReader(req.Body).ReadToEndAsync();
    Log.Print($"[RAG Query API] Received: {body}");

    var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body);
    if (data == null)
    {
        return Results.Json(new { success = false, message = "参数格式错误" });
    }

    string groupName = data.TryGetValue("groupName", out var gn) ? gn.GetString() ?? "" : "";
    string query = data.TryGetValue("query", out var qr) ? qr.GetString() ?? "" : "";

    if (string.IsNullOrEmpty(groupName)) return Results.Json(new { success = false, message = "缺少 groupName" });
    if (string.IsNullOrEmpty(query)) return Results.Json(new { success = false, message = "查询内容不能为空" });

    int topK = 8;
    if (data.TryGetValue("topK", out var tkEl) && tkEl.TryGetInt32(out int tk))
    {
        topK = Math.Clamp(tk, 1, 30);
    }
    double minSim = 0.25;
    if (data.TryGetValue("minSimilarity", out var msEl) && msEl.TryGetDouble(out double ms))
    {
        minSim = Math.Clamp(ms, 0, 1);
    }

    if (string.IsNullOrEmpty(cfg.EmbeddingModelName))
    {
        return Results.Json(new { success = false, message = "尚未配置 Embedding 模型，请先到「设置」里配置 Embedding 服务" });
    }

    if (!vdb.IsGroupIndexed(groupName))
    {
        return Results.Json(new { success = false, message = "该群组尚未建立索引，请先点击「建立/重建索引」" });
    }

    try
    {
        using var embClient = CreateEmbeddingClient(cfg);
        var answer = new Answer();
        var rag = new RagService(vdb, embClient, answer);

        var result = await rag.QueryAndSummarizeAsync(groupName, query, topK, minSim);

        if (!string.IsNullOrEmpty(result.ErrorMessage))
        {
            return Results.Json(new { success = false, message = result.ErrorMessage });
        }

        var retrieved = result.RetrievedChunks
            .Select(r => new { r.ChunkIndex, r.TimeRange, r.Usernames, r.MessageCount, Similarity = r.Similarity, r.ChunkText })
            .ToList();

        return Results.Json(new
        {
            success = true,
            groupName,
            query,
            summary = result.Summary,
            retrievedChunks = retrieved
        });
    }
    catch (Exception ex)
    {
        Log.Print($"[RAG Query API] Error: {ex.Message}", Log.Stat.ERROR);
        return Results.Json(new { success = false, message = ex.Message });
    }
});

// ==================================================================
// DB 清空 API
// ==================================================================
app.MapPost("/api/db/clear", (DB db) =>
{
    try
    {
        db.ClearAll();
        return Results.Json(new { success = true, message = "数据库已清空" });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[DB Clear API] Error: {ex.Message}");
        return Results.Json(new { success = false, message = ex.Message });
    }
});

// ==================================================================
// Config API - GET
// ==================================================================
app.MapGet("/api/config", (Config config) =>
{
    return Results.Json(new
    {
        config.Version,
        config.Width,
        config.Height,
        config.ModelName,
        config.IsVisionModel,
        config.ApiKey,
        config.ServerUrl,
        config.Scroll,
        config.AutoFocusing,
        config.AtDetect,
        config.TabTimes,
        config.RemoteServerTimeout,
        config.MaxImageCount,
        config.Scale,
        SystemContent = config.SystemContent,

        // Embedding 配置
        config.EmbeddingServerUrl,
        config.EmbeddingApiKey,
        config.EmbeddingModelName,

        // Map-Reduce 默认参数
        config.MapReduceChunkSize,
        config.MapReduceUseCharBased
    });
});

// ==================================================================
// Config API - POST
// ==================================================================
app.MapPost("/api/config", async (HttpRequest req, Config config) =>
{
    try
    {
        var body = await new StreamReader(req.Body).ReadToEndAsync();
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(body);

        List<string> fieldsToSave = new List<string>();
        if (data != null && data.ContainsKey("_fields"))
        {
            var fieldsJson = data["_fields"];
            fieldsToSave = JsonSerializer.Deserialize<List<string>>(fieldsJson) ?? new List<string>();
            data.Remove("_fields");
        }

        if (data != null)
        {
            foreach (KeyValuePair<string, string> kvp in data)
            {
                Console.WriteLine($"{kvp.Key}:{kvp.Value}");

                switch (kvp.Key.ToLower())
                {
                    case "version":
                        config.Version = kvp.Value;
                        break;
                    case "width":
                        config.Width = int.Parse(kvp.Value);
                        break;
                    case "height":
                        config.Height = int.Parse(kvp.Value);
                        break;
                    case "modelname":
                        config.ModelName = kvp.Value;
                        break;
                    case "isvisionmodel":
                        config.IsVisionModel = kvp.Value.Equals("true", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "api_key":
                        config.ApiKey = kvp.Value;
                        break;
                    case "server_url":
                        config.ServerUrl = kvp.Value;
                        break;
                    case "scroll":
                        config.Scroll = int.Parse(kvp.Value);
                        break;
                    case "autofocusing":
                        config.AutoFocusing = kvp.Value.Equals("true", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "atdetect":
                        config.AtDetect = kvp.Value.Equals("true", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "tab_times":
                        config.TabTimes = int.Parse(kvp.Value);
                        break;
                    case "remote_server_timeout":
                        config.RemoteServerTimeout = int.Parse(kvp.Value);
                        break;
                    case "maximagecount":
                        int maxImageCount = int.Parse(kvp.Value);
                        config.MaxImageCount = maxImageCount < 1 ? 1 : maxImageCount;
                        break;
                    case "scale":
                        config.Scale = float.Parse(kvp.Value);
                        break;

                    // Embedding 配置
                    case "embedding_server_url":
                        config.EmbeddingServerUrl = kvp.Value;
                        break;
                    case "embedding_api_key":
                        config.EmbeddingApiKey = kvp.Value;
                        break;
                    case "embedding_model_name":
                        config.EmbeddingModelName = kvp.Value;
                        break;

                    // Map-Reduce 参数
                    case "mapreduce_chunksize":
                        config.MapReduceChunkSize = int.Parse(kvp.Value);
                        break;
                    case "mapreduce_charbased":
                        config.MapReduceUseCharBased = kvp.Value.Equals("true", StringComparison.OrdinalIgnoreCase);
                        break;

                    default:
                        continue;
                }

                if (!fieldsToSave.Contains(kvp.Key, StringComparer.OrdinalIgnoreCase))
                {
                    fieldsToSave.Add(kvp.Key);
                }
            }

            if (data.ContainsKey("SystemContent"))
            {
                config.SystemContent = data["SystemContent"];
                if (!fieldsToSave.Contains("SystemContent", StringComparer.OrdinalIgnoreCase))
                {
                    fieldsToSave.Add("SystemContent");
                }
            }
        }

        config.Save(fieldsToSave.Count > 0 ? fieldsToSave : null);
        return Results.Json(new { success = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { success = false, message = ex.Message });
    }
});

app.Run("http://localhost:8080");

// ==================================================================
// 原有普通总结（全量一次性）
// ==================================================================
string GenerateSummary(string groupName, List<ChatContent> comments)
{
    var sb = new System.Text.StringBuilder();
    sb.AppendLine($"=== {groupName} 聊天总结 ===");
    sb.AppendLine($"生成时间: {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}");
    sb.AppendLine($"消息总数: {comments.Count}");
    sb.AppendLine();

    Answer a = new();
    ChatContent content = new("", new List<string>(), "", "", false);

    var participants = comments.Select(c => c.Username).Distinct().ToList();
    sb.AppendLine($"参与成员: {string.Join(", ", participants)}");
    sb.AppendLine();

    foreach (var comment in comments)
    {
        content.Text += comment.ToString() + "\n";
        foreach (var imagePath in comment.ImagePaths)
        {
            content.ImagePaths.Add(imagePath);
        }
    }
    Log.Print($"{content}");
    Log.Print("images");

    foreach (string i in content.ImagePaths)
    {
        Log.Print(i);
    }
    var answer = a.GetAnswerSync(new List<ChatContent> { content });
    sb.Append(answer);
    sb.AppendLine();

    return sb.ToString();
}

// ==================================================================
// Config 类 - 扩展支持 Embedding 和 Map-Reduce 参数
// ==================================================================
public class Config
{
    private readonly string basePath = AppContext.BaseDirectory;
    private readonly string configPath;
    private readonly string systemPath;

    // 原有配置
    public string Version { get; set; } = "QQPilot 1.5.12";
    public int Width { get; set; } = 1285;
    public int Height { get; set; } = 720;
    public string ModelName { get; set; } = "qwen2.5:0.5b";
    public bool IsVisionModel { get; set; } = false;
    public string ApiKey { get; set; } = "";
    public string ServerUrl { get; set; } = "builtin";
    public int Scroll { get; set; } = 10;
    public bool WithImage { get; set; } = true;
    public bool AutoFocusing { get; set; } = true;
    public bool AtDetect { get; set; } = false;
    public int TabTimes { get; set; } = 8;
    public int RemoteServerTimeout { get; set; } = 300;
    public int MaxImageCount { get; set; } = 5;
    public string System { get; set; } = "editSystemtxtInstead";
    public double Scale { get; set; } = 1.5;
    public string SystemContent { get; set; } = "";

    // ===== Embedding 配置（RAG 用）=====
    public string EmbeddingServerUrl { get; set; } = "ollama";
    public string EmbeddingApiKey { get; set; } = "";
    public string EmbeddingModelName { get; set; } = "qwen3-embedding:0.6b";

    // ===== Map-Reduce 默认参数 =====
    public int MapReduceChunkSize { get; set; } = 60;
    public bool MapReduceUseCharBased { get; set; } = false;

    public Config()
    {
        configPath = Path.GetFullPath("config.ini");
        systemPath = Path.GetFullPath("system.txt");

        Console.WriteLine($"[Config] BasePath: {basePath}");
        Console.WriteLine($"[Config] ConfigPath: {configPath} (exists: {File.Exists(configPath)})");
        Console.WriteLine($"[Config] SystemPath: {systemPath} (exists: {File.Exists(systemPath)})");

        Load();
    }

    void Load()
    {
        try
        {
            if (File.Exists(configPath))
            {
                IniParser.FileIniDataParser parser = new();
                IniData data;

                try
                {
                    data = parser.ReadFile(configPath, new UTF8Encoding(false));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Config] Failed to read config.ini with UTF-8: {ex.Message}");
                    data = parser.ReadFile(configPath);
                }

                var general = data["general"];
                var rag = data.Sections.ContainsSection("rag") ? data["rag"] : null;

                if (general.ContainsKey("version")) Version = general["version"];
                if (general.ContainsKey("width")) Width = int.Parse(general["width"]);
                if (general.ContainsKey("height")) Height = int.Parse(general["height"]);
                if (general.ContainsKey("modelname")) ModelName = general["modelname"];
                if (general.ContainsKey("isvisionmodel")) IsVisionModel = bool.Parse(general["isvisionmodel"]);
                if (general.ContainsKey("api_key")) ApiKey = general["api_key"];
                if (general.ContainsKey("server_url")) ServerUrl = general["server_url"];
                if (general.ContainsKey("scroll")) Scroll = int.Parse(general["scroll"]);
                if (general.ContainsKey("autofocusing")) AutoFocusing = bool.Parse(general["autofocusing"]);
                if (general.ContainsKey("atdetect")) AtDetect = bool.Parse(general["atdetect"]);
                if (general.ContainsKey("tab_times")) TabTimes = int.Parse(general["tab_times"]);
                if (general.ContainsKey("remote_server_timeout")) RemoteServerTimeout = int.Parse(general["remote_server_timeout"]);
                if (general.ContainsKey("maximagecount"))
                {
                    int val = int.Parse(general["maximagecount"]);
                    MaxImageCount = val < 1 ? 1 : val;
                }
                if (general.ContainsKey("scale")) Scale = float.Parse(general["scale"]);

                // RAG / Embedding 配置（优先从 [rag] section 读取，兼容 general 读取）
                if (rag != null)
                {
                    if (rag.ContainsKey("embedding_server_url")) EmbeddingServerUrl = rag["embedding_server_url"];
                    if (rag.ContainsKey("embedding_api_key")) EmbeddingApiKey = rag["embedding_api_key"];
                    if (rag.ContainsKey("embedding_model_name")) EmbeddingModelName = rag["embedding_model_name"];
                    if (rag.ContainsKey("mapreduce_chunksize")) MapReduceChunkSize = int.Parse(rag["mapreduce_chunksize"]);
                    if (rag.ContainsKey("mapreduce_charbased")) MapReduceUseCharBased = bool.Parse(rag["mapreduce_charbased"]);
                }
                else
                {
                    // 兼容从 general 读取
                    if (general.ContainsKey("embedding_server_url")) EmbeddingServerUrl = general["embedding_server_url"];
                    if (general.ContainsKey("embedding_api_key")) EmbeddingApiKey = general["embedding_api_key"];
                    if (general.ContainsKey("embedding_model_name")) EmbeddingModelName = general["embedding_model_name"];
                    if (general.ContainsKey("mapreduce_chunksize")) MapReduceChunkSize = int.Parse(general["mapreduce_chunksize"]);
                    if (general.ContainsKey("mapreduce_charbased")) MapReduceUseCharBased = bool.Parse(general["mapreduce_charbased"]);
                }
            }
            SystemContent = File.ReadAllText(systemPath, new UTF8Encoding(false));
            Console.WriteLine($"[Config] Loaded SystemContent ({SystemContent.Length} chars)");
            Console.WriteLine($"[Config] Embedding: server={EmbeddingServerUrl}, model={EmbeddingModelName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Load error: {ex.Message}");
        }
    }

    public void Save(IEnumerable<string>? fieldsToSave = null)
    {
        IniParser.FileIniDataParser parser = new();
        IniData data;

        if (File.Exists(configPath))
        {
            try
            {
                data = parser.ReadFile(configPath, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Config] Failed to read config.ini with UTF-8 in Save: {ex.Message}");
                data = parser.ReadFile(configPath);
            }
        }
        else
        {
            data = new IniData();
            data.Sections.AddSection("general");
        }

        if (!data.Sections.ContainsSection("rag"))
        {
            data.Sections.AddSection("rag");
        }

        KeyDataCollection general = data["general"];
        KeyDataCollection rag = data["rag"];

        if (fieldsToSave != null && fieldsToSave.Any())
        {
            foreach (var field in fieldsToSave)
            {
                SaveField(general, rag, field);
            }
        }
        else
        {
            SaveAllFields(general, rag);
        }

        parser.WriteFile(configPath, data, new UTF8Encoding(false));

        if (fieldsToSave == null || fieldsToSave.Contains("SystemContent", StringComparer.OrdinalIgnoreCase))
        {
            File.WriteAllText(systemPath, SystemContent);
        }
    }

    private void SaveAllFields(KeyDataCollection general, KeyDataCollection rag)
    {
        general["version"] = Version;
        general["width"] = Width.ToString();
        general["height"] = Height.ToString();
        general["modelname"] = ModelName;
        general["isvisionmodel"] = IsVisionModel.ToString().ToLower();
        general["api_key"] = ApiKey;
        general["server_url"] = ServerUrl;
        general["scroll"] = Scroll.ToString();
        general["autofocusing"] = AutoFocusing.ToString().ToLower();
        general["atdetect"] = AtDetect.ToString().ToLower();
        general["tab_times"] = TabTimes.ToString();
        general["remote_server_timeout"] = RemoteServerTimeout.ToString();
        general["maximagecount"] = MaxImageCount.ToString();
        general["scale"] = Scale.ToString();

        rag["embedding_server_url"] = EmbeddingServerUrl;
        rag["embedding_api_key"] = EmbeddingApiKey;
        rag["embedding_model_name"] = EmbeddingModelName;
        rag["mapreduce_chunksize"] = MapReduceChunkSize.ToString();
        rag["mapreduce_charbased"] = MapReduceUseCharBased.ToString().ToLower();
    }

    private void SaveField(KeyDataCollection general, KeyDataCollection rag, string fieldName)
    {
        switch (fieldName.ToLower())
        {
            case "version":
                general["version"] = Version;
                break;
            case "width":
                general["width"] = Width.ToString();
                break;
            case "height":
                general["height"] = Height.ToString();
                break;
            case "modelname":
                general["modelname"] = ModelName;
                break;
            case "isvisionmodel":
                general["isvisionmodel"] = IsVisionModel.ToString().ToLower();
                break;
            case "api_key":
                general["api_key"] = ApiKey;
                break;
            case "server_url":
                general["server_url"] = ServerUrl;
                break;
            case "scroll":
                general["scroll"] = Scroll.ToString();
                break;
            case "autofocusing":
                general["autofocusing"] = AutoFocusing.ToString().ToLower();
                break;
            case "atdetect":
                general["atdetect"] = AtDetect.ToString().ToLower();
                break;
            case "tab_times":
                general["tab_times"] = TabTimes.ToString();
                break;
            case "remote_server_timeout":
                general["remote_server_timeout"] = RemoteServerTimeout.ToString();
                break;
            case "maximagecount":
                general["maximagecount"] = MaxImageCount.ToString();
                break;
            case "scale":
                general["scale"] = Scale.ToString();
                break;
            case "embedding_server_url":
                rag["embedding_server_url"] = EmbeddingServerUrl;
                break;
            case "embedding_api_key":
                rag["embedding_api_key"] = EmbeddingApiKey;
                break;
            case "embedding_model_name":
                rag["embedding_model_name"] = EmbeddingModelName;
                break;
            case "mapreduce_chunksize":
                rag["mapreduce_chunksize"] = MapReduceChunkSize.ToString();
                break;
            case "mapreduce_charbased":
                rag["mapreduce_charbased"] = MapReduceUseCharBased.ToString().ToLower();
                break;
        }
    }
}

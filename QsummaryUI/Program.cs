using IniParser.Model;
using QSummaryCore;
using QsummaryUI;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(new DB());
builder.Services.AddSingleton(new Config());
builder.Services.AddCors();

var app = builder.Build();

app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
        Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot")))
});

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
        SystemContent = config.SystemContent
    });
});

app.MapPost("/api/config", async (HttpRequest req, Config config) =>
{
    try
    {
        var body = await new StreamReader(req.Body).ReadToEndAsync();
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(body);
        
        // 提取要保存的字段列表（如果有）
        List<string> fieldsToSave = new List<string>();
        if (data.ContainsKey("_fields"))
        {
            var fieldsJson = data["_fields"];
            fieldsToSave = JsonSerializer.Deserialize<List<string>>(fieldsJson) ?? new List<string>();
            data.Remove("_fields");
        }
        
        // 显式处理每个配置项
        foreach (KeyValuePair<string, string> kvp in data!)
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
                    config.MaxImageCount = maxImageCount < 1 ? 1 : maxImageCount; // 确保至少为1
                    break;
                case "scale":
                    config.Scale = float.Parse(kvp.Value);
                    break;
                default:
                    continue; // 未知字段跳过，不加入保存列表
            }
            
            // ✅ 关键修复：在 switch 外部统一添加当前字段名
            // 无论前端是否传了 _fields，都把实际修改了的字段记录下来
            if (!fieldsToSave.Contains(kvp.Key, StringComparer.OrdinalIgnoreCase))
            {
                fieldsToSave.Add(kvp.Key);
            }
        }
        
        // SystemContent 单独处理（不在 switch 中）
        if (data.ContainsKey("SystemContent"))
        {
            config.SystemContent = data["SystemContent"];
            if (!fieldsToSave.Contains("SystemContent", StringComparer.OrdinalIgnoreCase))
            {
                fieldsToSave.Add("SystemContent");
            }
        }
        
        // 保存指定的字段或所有字段
        config.Save(fieldsToSave.Count > 0 ? fieldsToSave : null);
        return Results.Json(new { success = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { success = false, message = ex.Message });
    }
});

app.Run("http://localhost:8080");

string GenerateSummary(string groupName, List<ChatContent> comments)
{
    var sb = new System.Text.StringBuilder();
    sb.AppendLine($"=== {groupName} 聊天总结 ===");
    sb.AppendLine($"生成时间: {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}");
    sb.AppendLine($"消息总数: {comments.Count}");
    sb.AppendLine();
    
    Answer a=new();
    //a.GetAnswerSync();
    ChatContent content = new("",[],"","",false);

    //(string username, List<string> imagePaths, string text, string time, bool ownByMyself)

    var participants = comments.Select(c => c.Username).Distinct().ToList();
    sb.AppendLine($"参与成员: {string.Join(", ", participants)}");
    sb.AppendLine();
    
    //sb.AppendLine("--- 聊天记录 ---");
    foreach (var comment in comments)
    {
        content.Text+=comment.ToString()+"\n";
        foreach(var imagePath in comment.ImagePaths)
        {
            content.ImagePaths.Add(imagePath);
        }
    }
    Log.Print($"{content}");
    Log.Print("images");

    //sb.AppendLine($"{content}");
    //sb.AppendLine($"images:");

    foreach (string i in content.ImagePaths)
    {
        Log.Print(i);
    }
    var answer = a.GetAnswerSync([content]);
    sb.Append(answer);
    sb.AppendLine();
    
    return sb.ToString();
}

public class Config
{
    // ✅ 统一使用程序集所在目录，而非工作目录
    private readonly string basePath = AppContext.BaseDirectory;
    private readonly string configPath;
    private readonly string systemPath;

    public string Version { get; set; } = "QQPilot 1.5.11";
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
    public int MaxImageCount { get; set; } = 5; // 默认值为5，用户要求>=1
    public string System { get; set; } = "editSystemtxtInstead";
    public double Scale { get; set; } = 1.5;
    public string SystemContent { get; set; } = "";

    public Config()
    {
        // 优先在当前程序集目录查找
        configPath = Path.GetFullPath( "config.ini");
        systemPath = Path.GetFullPath( "system.txt");


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
                    // 尝试使用默认编码读取
                    data = parser.ReadFile(configPath);
                }
                
                // 显式读取每个配置项
                var general = data["general"];
                
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
                if (general.ContainsKey("maximagecount")) {
                    int val = int.Parse(general["maximagecount"]);
                    MaxImageCount = val < 1 ? 1 : val; // 确保至少为1
                }
                if (general.ContainsKey("scale")) Scale = float.Parse(general["scale"]);
            }
            SystemContent = File.ReadAllText(systemPath, new UTF8Encoding(false));
            Console.WriteLine($"[Config] Loaded SystemContent ({SystemContent.Length} chars)");
            //if (File.Exists(systemPath))
            //{
            //    SystemContent = File.ReadAllText(systemPath, Encoding.UTF8);
            //    Console.WriteLine($"[Config] Loaded SystemContent ({SystemContent.Length} chars)");
            //}
            //else
            //{
            //    Console.WriteLine($"[Config] WARNING: system.txt not found at {systemPath}");
            //}
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
        
        // ✅ 先加载已有配置，避免覆盖未修改的字段
        if (File.Exists(configPath))
        {
            try
            {
                data = parser.ReadFile(configPath, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Config] Failed to read config.ini with UTF-8 in Save: {ex.Message}");
                // 尝试使用默认编码读取
                data = parser.ReadFile(configPath);
            }
        }
        else
        {
            data = new IniData();
            data.Sections.AddSection("general");
        }
        
        KeyDataCollection general = data["general"];
        
        // 如果指定了要保存的字段，只保存这些字段；否则保存所有字段
        if (fieldsToSave != null && fieldsToSave.Any())
        {
            foreach (var field in fieldsToSave)
            {
                SaveField(general, field);
            }
        }
        else
        {
            // 保存所有字段
            SaveAllFields(general);
        }
        
        parser.WriteFile(configPath, data, new UTF8Encoding(false));
        
        // 如果指定了 SystemContent 或没有指定字段，则保存 system.txt
        if (fieldsToSave == null || fieldsToSave.Contains("SystemContent", StringComparer.OrdinalIgnoreCase))
        {
            File.WriteAllText(systemPath, SystemContent);
        }
    }
    
    private void SaveAllFields(KeyDataCollection general)
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
    }
    private void SaveField(KeyDataCollection general, string fieldName)
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
        }
    }
}

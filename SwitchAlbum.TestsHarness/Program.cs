using SwitchAlbum.Models;
using SwitchAlbum.Services;
using SwitchAlbum.Services.Parsing;

var tests = new List<(string Name, Func<Task<bool>> Run)>();

void Add(string name, Func<bool> run) => tests.Add((name, () => Task.FromResult(run())));
void AddAsync(string name, Func<Task<bool>> run) => tests.Add((name, run));

static AlbumItem Item(string game, string file, DateTime ts, bool video = false) => new()
{
    GameTitle = game,
    DevicePath = "\\Album\\" + game + "\\" + file,
    FileName = file,
    Timestamp = ts,
    IsVideo = video,
};

// ---------- TimestampParser ----------
Add("时间戳: 16位带后缀", () =>
    TimestampParser.TryParse("2025031118460900_s", out var d) && d == new DateTime(2025, 3, 11, 18, 46, 9));
Add("时间戳: 14位", () =>
    TimestampParser.TryParse("20250311184609", out var d) && d == new DateTime(2025, 3, 11, 18, 46, 9));
Add("时间戳: 非法输入", () =>
    !TimestampParser.TryParse("2025133218460900", out _)
    && !TimestampParser.TryParse("abc", out _)
    && !TimestampParser.TryParse("2025031", out _));

// ---------- FolderNameParser ----------
Add("文件夹: 游戏名直读", () =>
{
    var r = FolderNameParser.Parse("塞尔达传说 王国之泪");
    return r.Title == "塞尔达传说 王国之泪" && r.TitleId == null && r.Timestamp == null;
});
Add("文件夹: 旧式titleid", () =>
{
    var r = FolderNameParser.Parse("2024081512304500-0100f2c0115b6000");
    return r.TitleId == "0100F2C0115B6000" && r.Timestamp == new DateTime(2024, 8, 15, 12, 30, 45);
});
Add("文件夹: 空名", () => FolderNameParser.Parse("   ").Title == null);
Add("文件夹: 纯日期不误判旧式", () =>
{
    var r = FolderNameParser.Parse("20240815");
    return r.Title == "20240815" && r.TitleId == null;
});

// ---------- FileNameSanitizer ----------
Add("清理: 非法字符替换", () =>
    FileNameSanitizer.Sanitize("A:B/C\\D*E?F\"G<H>I|J") == "A B C D E F G H I J");
Add("清理: 保留全角字符", () =>
    FileNameSanitizer.Sanitize("NARUTO×BORUTO 新忍出击") == "NARUTO×BORUTO 新忍出击");
Add("清理: 保留名加前缀", () =>
    FileNameSanitizer.Sanitize("CON").StartsWith('_') && FileNameSanitizer.Sanitize("con").StartsWith('_'));
Add("清理: 去结尾点与空格", () => FileNameSanitizer.Sanitize("abc.. ") == "abc");
Add("清理: 空输入", () => FileNameSanitizer.Sanitize("   ") == "未知");
Add("清理: 超长截断", () => FileNameSanitizer.Sanitize(new string('长', 300)).Length <= 120);
Add("清理: 含点号非保留名", () =>
    FileNameSanitizer.Sanitize("Mario vs. Donkey Kong") == "Mario vs. Donkey Kong");

// ---------- RenamePlanner ----------
Add("重命名: 同组序号递增", () =>
{
    var items = new[]
    {
        Item("游戏A", "2025031118460900_s.jpg", new DateTime(2025, 3, 11, 18, 46, 9)),
        Item("游戏A", "2025031118461000_s.jpg", new DateTime(2025, 3, 11, 18, 46, 10)),
    };
    var plan = RenamePlanner.Plan(items, null, true);
    return plan.Count == 2
        && plan.Values.Contains("20250311_游戏A_001.jpg")
        && plan.Values.Contains("20250311_游戏A_002.jpg");
});
Add("重命名: 跨日期序号重置", () =>
{
    var items = new[]
    {
        Item("游戏A", "a.jpg", new DateTime(2025, 3, 11, 1, 0, 0)),
        Item("游戏A", "b.jpg", new DateTime(2025, 3, 12, 1, 0, 0)),
    };
    var plan = RenamePlanner.Plan(items, null, true);
    return plan.Values.Contains("20250311_游戏A_001.jpg") && plan.Values.Contains("20250312_游戏A_001.jpg");
});
Add("重命名: 与已有文件冲突避让", () =>
{
    var plan = RenamePlanner.Plan(
        new[] { Item("游戏A", "x.jpg", new DateTime(2025, 3, 11, 1, 0, 0)) },
        new[] { "20250311_游戏A_001.jpg" }, true);
    return plan.Values.Single() == "20250311_游戏A_002.jpg";
});
Add("重命名: 保留原名", () =>
{
    var plan = RenamePlanner.Plan(
        new[] { Item("游戏A", "2025031118460900_s.jpg", new DateTime(2025, 3, 11, 18, 46, 9)) },
        null, false);
    return plan.Values.Single() == "2025031118460900_s.jpg";
});
Add("重命名: 原名冲突追加序号", () =>
{
    var plan = RenamePlanner.Plan(
        new[] { Item("游戏A", "a.jpg", default), Item("游戏B", "a.jpg", default) }, null, false);
    return plan.Count == 2 && plan.Values.Contains("a.jpg") && plan.Values.Contains("a_1.jpg");
});
Add("重命名: 无时间戳用未知日期", () =>
{
    var plan = RenamePlanner.Plan(new[] { Item("游戏A", "x.jpg", default) }, null, true);
    return plan.Values.Single() == "未知日期_游戏A_001.jpg";
});
Add("重命名: 游戏名含非法字符被清理", () =>
{
    var plan = RenamePlanner.Plan(
        new[] { Item("A:B", "x.jpg", new DateTime(2025, 3, 11, 1, 0, 0)) }, null, true);
    return plan.Values.Single() == "20250311_A B_001.jpg";
});
Add("重命名: 视频扩展名保留", () =>
{
    var plan = RenamePlanner.Plan(
        new[] { Item("游戏A", "2025031118460900_s.mp4", new DateTime(2025, 3, 11, 18, 46, 9), video: true) },
        null, true);
    return plan.Values.Single() == "20250311_游戏A_001.mp4";
});

// ---------- DuplicateTracker ----------
Add("去重: 标记持久化", () =>
{
    var file = TempFile();
    try
    {
        var t1 = new DuplicateTracker(file);
        t1.MarkSaved("游戏A", "a.jpg", "20250311_游戏A_001.jpg", toPc: true, toPhone: false);
        t1.Flush();
        var t2 = new DuplicateTracker(file);
        return t2.GetState("游戏A", "a.jpg") == SavedState.SavedToPc
            && t2.GetState("游戏A", "b.jpg") == SavedState.NotSaved;
    }
    finally { File.Delete(file); }
});
Add("去重: 电脑与手机状态独立", () =>
{
    var file = TempFile();
    try
    {
        var t1 = new DuplicateTracker(file);
        t1.MarkSaved("游戏A", "a.jpg", null, toPc: false, toPhone: true);
        t1.Flush();
        var t2 = new DuplicateTracker(file);
        return t2.GetState("游戏A", "a.jpg") == SavedState.SavedToPhone;
    }
    finally { File.Delete(file); }
});

// ---------- TitleDb ----------
Add("标题库: 资源名诊断", () =>
{
    foreach (var name in typeof(TitleDbService).Assembly.GetManifestResourceNames())
    {
        Console.WriteLine("  RES: " + name);
    }

    return true;
});
Add("标题库: 规范化", () =>
{
    return TitleDbService.Normalize("集合啦！动物森友会") == "集合啦动物森友会"
        && TitleDbService.Normalize("Mario vs. Donkey Kong") == "mariovsdonkeykong"
        && TitleDbService.Normalize("スプラトゥーン３") == "スプラトゥーン3";
});
Add("标题库: 嵌入库加载与查名", () =>
{
    var db = TitleDbService.LoadEmbedded();
    if (db == null)
    {
        return false;
    }

    var totk = db.GetNameByTitleId("0100F2C0115B6000");
    var mk8 = db.GetTitleIdByName("马力欧卡丁车8 豪华版");
    return totk is { Length: > 0 }
        && db.GetIconUrl("0100F2C0115B6000")?.Contains("img-eshop.cdn.nintendo.net") == true
        && mk8 is { Length: 16 } && mk8.All(Uri.IsHexDigit);
});
Add("标题库: 繁体名注册简体变体（含品牌别名）", () =>
{
    var db = TitleDbService.LoadEmbedded();
    // HK.zh 只有繁体「薩爾達傳說」，注册后应能按官方简体反查（别名 萨尔达→塞尔达）
    return db?.GetTitleIdByName("塞尔达传说 王国之泪") == "0100F2C0115B6000"
        && db.GetTitleIdByName("薩爾達傳說 王國之淚") == "0100F2C0115B6000"
        && db.GetTitleIdByName("塞尔达传说 旷野之息") == "01007EF00011E000";
});

// ---------- 标题库: 文件夹名模糊反查（合成库，确定性） ----------
static TitleDbService SyntheticDb()
{
    var json = """
    {
      "0100AAAA00000001": { "zh": "塞尔达传说 王国之泪", "en": "The Legend of Zelda: Tears of the Kingdom" },
      "0100AAAA00000002": { "zh": "超级马力欧兄弟 惊奇" },
      "0100AAAA00000003": { "en": "Splatoon 3" },
      "0100AAAA00000004": { "zht": "霍格華茲的傳承" },
      "0100AAAA00000005": { "ja": "ゼノブレイド２" },
      "0100AAAA00000006": { "zh": "神之天平 Revision" }
    }
    """;
    var t2s = "華\t华\n茲\t兹\n傳\t传\n承\t承\n";
    using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
    using var t2sStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(t2s));
    return new TitleDbService(stream, t2sStream);
}

Add("模糊反查: Switch 2 后缀剥离", () =>
{
    var db = SyntheticDb();
    return db.GetTitleIdByFolderName("塞尔达传说 王国之泪 Nintendo Switch 2 Edition") == "0100AAAA00000001"
        && db.GetTitleIdByFolderName("塞尔达传说 王国之泪 體驗版") == "0100AAAA00000001"
        && db.GetTitleIdByFolderName("塞尔达传说 王国之泪 DEMO") == "0100AAAA00000001";
});
Add("模糊反查: 括号注释剥离", () =>
{
    var db = SyntheticDb();
    return db.GetTitleIdByFolderName("Splatoon 3（斯普拉遁 3）") == "0100AAAA00000003"
        && db.GetTitleIdByFolderName("Splatoon 3 (スプラトゥーン3)") == "0100AAAA00000003";
});
Add("模糊反查: 包含匹配（后缀+捆绑）", () =>
{
    var db = SyntheticDb();
    return db.GetTitleIdByFolderName("超级马力欧兄弟 惊奇 Nintendo Switch 2 Edition + 同游铃铃公园") == "0100AAAA00000002"
        && db.GetTitleIdByFolderName("神之天平") == "0100AAAA00000006";
});
Add("模糊反查: 品牌别名（繁体霍格華茲→霍格沃茨之遗）", () =>
{
    var db = SyntheticDb();
    return db.GetTitleIdByFolderName("霍格沃茨之遗") == "0100AAAA00000004";
});
Add("模糊反查: 日文名注册与全角数字", () =>
{
    var db = SyntheticDb();
    return db.GetTitleIdByFolderName("ゼノブレイド2") == "0100AAAA00000005";
});
Add("模糊反查: 真实库 Switch 2 风格文件夹名", () =>
{
    var db = TitleDbService.LoadEmbedded();
    return db?.GetTitleIdByFolderName("塞尔达传说 王国之泪 Nintendo Switch 2 Edition") == "0100F2C0115B6000"
        && db.GetTitleIdByFolderName("塞尔达传说 旷野之息 Nintendo Switch 2 Edition") == "01007EF00011E000";
});
Add("模糊反查: 真实库日文文件夹名", () =>
{
    var db = TitleDbService.LoadEmbedded();
    // titles.json 已合并 titledb JP.ja 日文名
    var xb2 = db?.GetTitleIdByFolderName("ゼノブレイド２");
    return xb2 is { Length: 16 } && xb2.All(Uri.IsHexDigit);
});

// ---------- 集成: 模拟设备扫描 ----------
AddAsync("模拟: 扫描相册结构", async () =>
{
    var root = TempDir();
    try
    {
        DemoAlbumGenerator.Generate(root);
        var provider = new MockMediaProvider(root, Path.Combine(root, "phone"));
        var devices = await provider.GetDevicesAsync(default);
        var switchDevice = devices.Single(d => d.IsSwitch);
        await using var session = await provider.ConnectAsync(switchDevice, default);
        var scan = await AlbumScanner.ScanAsync(session, null, default);
        if (scan == null)
        {
            return false;
        }

        // 6 个中文游戏 + 旧式文件夹 + 全角符号游戏 = 8 个游戏
        return scan.Games.Count == 8
            && scan.Games.Any(g => g.Title == "NARUTO×BORUTO 新忍出击")
            && scan.Games.Any(g => g.Title == "未知游戏 (0100F2C0…)")
            && scan.AllItems.Count > 20
            && scan.AllItems.Any(i => i.IsVideo)
            && scan.AllItems.All(i => i.Timestamp != default);
    }
    finally { Directory.Delete(root, recursive: true); }
});

// ---------- 集成: 保存到电脑 + 重复跳过 ----------
AddAsync("模拟: 保存到电脑按游戏分文件夹并跳过重复", async () =>
{
    var root = TempDir();
    try
    {
        DemoAlbumGenerator.Generate(root);
        var outDir = Path.Combine(root, "out");
        var provider = new MockMediaProvider(root, Path.Combine(root, "phone"));
        var tracker = new DuplicateTracker(Path.Combine(root, "saved.json"));
        var save = new SaveService(tracker);
        var devices = await provider.GetDevicesAsync(default);
        await using var session = await provider.ConnectAsync(devices.Single(d => d.IsSwitch), default);
        var scan = await AlbumScanner.ScanAsync(session, null, default);
        if (scan == null)
        {
            return false;
        }

        var r1 = await save.SaveToPcAsync(scan.AllItems, session, outDir, autoRename: true, null, default);
        var files = Directory.GetFiles(outDir, "*", SearchOption.AllDirectories);
        var namesOk = files.All(f =>
        {
            var n = Path.GetFileName(f);
            return n.StartsWith("20") && n.Contains('_') && !n.EndsWith(".part");
        });
        var firstOk = r1.SavedCount == scan.AllItems.Count && r1.SkippedCount == 0 && r1.Failed.Count == 0;
        var videoOk = files.Any(f => f.EndsWith(".mp4"));

        // 每个文件都位于以其游戏名命名的子文件夹内
        var subdirOk = files.All(f =>
        {
            var dirName = Path.GetFileName(Path.GetDirectoryName(f)!)!;
            return dirName != "out" && Directory.Exists(Path.Combine(outDir, dirName));
        });
        var gameDirs = Directory.GetDirectories(outDir).Select(Path.GetFileName).ToArray();
        var totkOk = gameDirs.Contains("塞尔达传说 王国之泪");

        var r2 = await save.SaveToPcAsync(scan.AllItems, session, outDir, autoRename: true, null, default);
        var skipOk = r2.SkippedCount == scan.AllItems.Count && r2.SavedCount == 0;

        return namesOk && firstOk && videoOk && subdirOk && totkOk && skipOk;
    }
    finally { Directory.Delete(root, recursive: true); }
});

// ---------- 集成: 保存到手机按游戏分文件夹 ----------
AddAsync("模拟: 保存到手机按游戏分文件夹", async () =>
{
    var root = TempDir();
    try
    {
        DemoAlbumGenerator.Generate(root);
        var provider = new MockMediaProvider(root, Path.Combine(root, "phone"));
        var tracker = new DuplicateTracker(Path.Combine(root, "saved.json"));
        var settings = new SettingsService(Path.Combine(root, "settings.json"));
        var phoneSave = new PhoneSaveService(tracker, settings);
        var devices = await provider.GetDevicesAsync(default);
        await using var switchSession = await provider.ConnectAsync(devices.Single(d => d.IsSwitch), default);
        await using var phoneSession = await provider.ConnectAsync(devices.Single(d => !d.IsSwitch), default);
        var scan = await AlbumScanner.ScanAsync(switchSession, null, default);
        if (scan == null)
        {
            return false;
        }

        var r = await phoneSave.SaveToPhoneAsync(
            scan.AllItems, switchSession, phoneSession, autoRename: true, null, default);
        var dcim = Path.Combine(root, "phone", "Internal shared storage", "DCIM", "SwitchAlbum");
        var dirs = Directory.Exists(dcim) ? Directory.GetDirectories(dcim).Select(Path.GetFileName).ToArray() : Array.Empty<string?>();
        var files = Directory.Exists(dcim) ? Directory.GetFiles(dcim, "*", SearchOption.AllDirectories) : Array.Empty<string>();

        return r.SavedCount == scan.AllItems.Count
            && dirs.Contains("塞尔达传说 王国之泪")
            && !dirs.Any(d => d is { Length: 10 } && d.Contains('-'))   // 不再按日期建目录
            && files.Length == scan.AllItems.Count;
    }
    finally { Directory.Delete(root, recursive: true); }
});

// ---------- 集成: 本地相册页扫描 → 保存到手机 ----------
AddAsync("模拟: 本地目录扫描并保存到手机（本地相册页流程）", async () =>
{
    var root = TempDir();
    try
    {
        DemoAlbumGenerator.Generate(root);
        var outDir = Path.Combine(root, "out");
        var provider = new MockMediaProvider(root, Path.Combine(root, "phone"));
        var tracker = new DuplicateTracker(Path.Combine(root, "saved.json"));
        var settings = new SettingsService(Path.Combine(root, "settings.json"));
        var save = new SaveService(tracker);
        var phoneSave = new PhoneSaveService(tracker, settings);
        var devices = await provider.GetDevicesAsync(default);

        // 第一步：Switch → 电脑（分游戏文件夹）
        await using (var switchSession = await provider.ConnectAsync(devices.Single(d => d.IsSwitch), default))
        {
            var scan = await AlbumScanner.ScanAsync(switchSession, null, default);
            if (scan == null)
            {
                return false;
            }

            var r = await save.SaveToPcAsync(scan.AllItems, switchSession, outDir, autoRename: true, null, default);
            if (r.SavedCount != scan.AllItems.Count)
            {
                return false;
            }
        }

        // 第二步：本地目录会话扫描电脑保存目录（与本地相册页同一路径）
        await using var localSession = new LocalFolderSession(outDir);
        var localScan = await AlbumScanner.ScanAsync(localSession, null, default);
        var scanOk = localScan != null
            && localScan.AllItems.Count == Directory.GetFiles(outDir, "*", SearchOption.AllDirectories).Length
            && localScan.Games.Any(g => g.Title == "塞尔达传说 王国之泪");

        // 第三步：本地 → 手机
        await using var phoneSession = await provider.ConnectAsync(devices.Single(d => !d.IsSwitch), default);
        var r2 = await phoneSave.SaveToPhoneAsync(
            localScan!.AllItems, localSession, phoneSession, autoRename: true, null, default);
        var dcim = Path.Combine(root, "phone", "Internal shared storage", "DCIM", "SwitchAlbum");
        var gameDirOk = Directory.Exists(Path.Combine(dcim, "塞尔达传说 王国之泪"));

        return scanOk && r2.SavedCount == localScan.AllItems.Count && gameDirOk;
    }
    finally { Directory.Delete(root, recursive: true); }
});

// ---------- 运行 ----------
var passed = 0;
foreach (var (name, run) in tests)
{
    try
    {
        var ok = await run();
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
        if (ok)
        {
            passed++;
        }
    }
    catch (Exception e)
    {
        Console.WriteLine($"FAIL  {name}  （异常: {e.Message}）");
    }
}

Console.WriteLine();
Console.WriteLine($"{passed}/{tests.Count} 通过");
Environment.Exit(passed == tests.Count ? 0 : 1);

static string TempDir() => Path.Combine(Path.GetTempPath(), "sab-test-" + Guid.NewGuid().ToString("N"));
static string TempFile() => Path.Combine(Path.GetTempPath(), "sab-test-" + Guid.NewGuid().ToString("N") + ".json");

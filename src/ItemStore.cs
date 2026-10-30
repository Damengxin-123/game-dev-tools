using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GameDevTools;

/// <summary>兼容 ItemStudio v1 的物品工作区；保留未知字段、源记录和稳定引用。</summary>
public sealed class ItemStore : IDisposable
{
    /// <summary>三种可编辑的记录集合。</summary>
    public static readonly string[] Kinds = ["items", "affixes", "assets"];
    /// <summary>原工具稀有度顺序。</summary>
    public static readonly string[] Rarities = ["普通", "高级", "稀有", "传说", "史诗", "神话", "未设置"];
    /// <summary>属性数值的运算方式。</summary>
    public static readonly string[] Operations = ["固定增减", "百分比提高", "独立百分比增幅", "取较大值"];
    /// <summary>资源恢复的运算方式。</summary>
    public static readonly string[] RecoveryOperations = ["固定恢复", "按当前值比例恢复", "按上限比例恢复"];
    /// <summary>效果类型及中文名称。</summary>
    public static readonly Dictionary<string, string> Effects = new() { ["AttributeGain"] = "属性数值修改", ["AttributeRecovery"] = "属性恢复", ["AttributeInfluence"] = "属性影响", ["CreateBuff"] = "创建增益", ["GrantSkill"] = "授予技能", ["GrantProfession"] = "赋予职业", ["Damage"] = "造成伤害", ["ChangeState"] = "改变状态", ["DisplayText"] = "显示文本", ["MultiTarget"] = "多目标" };
    /// <summary>数据目录绝对路径。</summary>
    public string DirectoryPath { get; }
    /// <summary>当前已保存快照，编辑前必须复制记录。</summary>
    public JsonObject Database { get; private set; }
    /// <summary>当前工作区修订号。</summary>
    public int Revision => Database["revision"]!.GetValue<int>();
    /// <summary>磁盘读取基线，用于拒绝外部覆盖。</summary>
    private byte[] _disk;
    /// <summary>本实例持有的 Qt 兼容工作区锁。</summary>
    private FileStream? _lock;
    /// <summary>锁文件路径。</summary>
    private readonly string _lockPath;

    /// <summary>读取并验证工作区，持有侧车锁；失败时不改写配置。</summary>
    public ItemStore(string directory)
    {
        DirectoryPath = Path.GetFullPath(directory); _lockPath = Path.Combine(DirectoryPath, ".工作区锁");
        if (!File.Exists(Path.Combine(DirectoryPath, "配置.json"))) throw new IOException("未找到物品配置：" + DirectoryPath);
        try { _lock = new FileStream(_lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read); }
        catch (IOException) { throw new IOException("物品工作区已被其他窗口使用。请关闭另一窗口；异常退出留下的 .工作区锁 需确认无编辑进程后移除。"); }
        try
        {
            _lock.Write(Encoding.UTF8.GetBytes($"{Environment.ProcessId}\n{System.Diagnostics.Process.GetCurrentProcess().ProcessName}\n{Environment.MachineName}\n")); _lock.Flush(true);
            _disk = File.ReadAllBytes(Path.Combine(DirectoryPath, "配置.json"));
            Database = JsonNode.Parse(_disk)?.AsObject() ?? throw new InvalidDataException("物品配置为空。"); Validate(Database);
        }
        catch { Dispose(); throw; }
    }
    /// <summary>读取可选文本，不改变原始 JSON。</summary>
    public static string Text(JsonNode? row, string key, string fallback = "") => row?[key]?.GetValue<string>() ?? fallback;
    /// <summary>取得指定类型的已保存记录。</summary>
    public IEnumerable<JsonObject> Rows(string kind) => Array(Database, kind).OfType<JsonObject>();
    /// <summary>取得完整分类列表。</summary>
    public string[] Categories(string kind) => Array(Database["categories"]!, kind).Select(n => n!.GetValue<string>()).ToArray();
    /// <summary>按稳定标识查找记录。</summary>
    public JsonObject? Find(string kind, string id) => Rows(kind).FirstOrDefault(r => Text(r, "id") == id);
    /// <summary>判断路径属于父分类。</summary>
    public static bool Within(string path, string parent) => path == parent || path.StartsWith(parent + '.', StringComparison.Ordinal);
    /// <summary>原工具固定分类，不允许改名或删除。</summary>
    public static string[] Builtins(string kind) => kind switch
    {
        "items" => ["物品", "物品.装备", "物品.装备.武器", "物品.装备.副手", "物品.装备.头盔", "物品.装备.胸甲", "物品.装备.护腿", "物品.装备.鞋子", "物品.装备.手套", "物品.装备.腰带", "物品.装备.饰品", "物品.装备.未指定", "物品.消耗品", "物品.材料", "物品.技能", "物品.Buff", "物品.其他"],
        "affixes" => ["词条", "词条.属性加成", "词条.资源恢复"],
        "assets" => ["通用", "物品", "物品.装备", "物品.消耗品", "物品.材料", "物品.技能", "物品.Buff", "物品.其他", "词条"], _ => throw new ArgumentException("未知记录类型。")
    };
    /// <summary>获取必需数组，损坏数据给出中文错误。</summary>
    public static JsonArray Array(JsonNode row, string key) => row[key] as JsonArray ?? throw new InvalidDataException("缺少有效列表：" + key);
    /// <summary>严格枚举对象列表，拒绝将损坏记录静默跳过。</summary>
    private static IEnumerable<JsonObject> Objects(JsonNode row, string key) => Array(row, key).Select(n => n as JsonObject ?? throw new InvalidDataException(key + " 包含无效记录。"));
    /// <summary>合并首版内置角色属性与项目导入目录。</summary>
    public static JsonObject[] Attributes(JsonObject db)
    {
        string[] defaults = ["属性.能力值.力量", "属性.能力值.智慧", "属性.能力值.敏捷", "属性.能力值.体质", "属性.资源.生命值", "属性.资源.魔力值", "属性.资源.体力值", "属性.资源.护盾值", "属性.判定.暴击率", "属性.战斗.暴击伤害", "属性.战斗.格挡减伤", "属性.战斗.护甲", "属性.战斗.攻击力", "属性.战斗.攻击速度", "属性.行动.移动速度加成", "属性.行动.负重", "属性.行动.基础移动速度", "属性.掉落.幸运", "属性.元素.火元素亲和", "属性.元素.水元素亲和", "属性.元素.电元素亲和", "属性.信仰.光明之神", "属性.信仰.自然之神"];
        var rows = defaults.ToDictionary(id => id, id => new JsonObject { ["id"] = id, ["name"] = id.Split('.')[^1], ["group"] = id.Split('.')[1], ["unit"] = id == "属性.行动.基础移动速度" ? "米/秒" : new[] { "属性.判定.暴击率", "属性.战斗.暴击伤害", "属性.战斗.格挡减伤", "属性.行动.移动速度加成" }.Contains(id) ? "百分点" : "点", ["targets"] = new JsonArray((id.StartsWith("属性.资源.") ? new[] { "当前值", "上限", "每秒恢复值", "每秒输出值" } : id == "属性.战斗.攻击力" ? new[] { "属性值", "向上浮动比例", "向下浮动比例" } : new[] { "属性值" }).Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()) });
        if (db.ContainsKey("attributeCatalog"))
        {
            var seen = new HashSet<string>();
            foreach (var row in Objects(db, "attributeCatalog"))
            {
                string id = String(row, "id"); Require(seen.Add(id), "角色属性重复。"); String(row, "name"); String(row, "group"); String(row, "unit", true);
                var targets = Array(row, "targets").Select(t => t!.GetValue<string>()).ToArray(); Require(targets.Length > 0 && targets.All(t => !string.IsNullOrWhiteSpace(t)) && targets.Distinct().Count() == targets.Length, "属性字段无效。"); rows[id] = row;
            }
        }
        return rows.Values.ToArray();
    }
    /// <summary>断言业务条件。</summary>
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    /// <summary>校验数值范围与精度。</summary>
    private static void Number(JsonNode row, string key, double min, double max, bool integer = false)
    {
        Require(row[key] is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.Number, "缺少数值：" + key);
        double n = Numeric(row[key]!), scaled = n * (integer ? 1 : 100);
        Require(double.IsFinite(n) && n >= min && n <= max && Math.Abs(scaled - Math.Round(scaled)) < .0001, key + " 超出范围或精度（数量须为整数，其他数值最多两位小数）。");
    }
    /// <summary>统一读取 JSON 解析数值及编辑期间构造的整数或浮点数。</summary>
    public static double Numeric(JsonNode node) => double.Parse(node.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture);
    /// <summary>检查名称、标识和说明的文本约束。</summary>
    private static string String(JsonNode row, string key, bool empty = false, int max = 240)
    {
        Require(row[key] is JsonValue v && v.TryGetValue<string>(out _), "缺少文本：" + key);
        string s = Text(row, key); Require((empty || !string.IsNullOrWhiteSpace(s)) && s.Length <= max && !s.Contains('\0'), key + " 为空、过长或包含无效字符。"); return s;
    }
    /// <summary>验证完整快照中的分类、数值、引用及配方无环性。</summary>
    public static void Validate(JsonObject db)
    {
        Require(db["schemaVersion"]?.GetValue<int>() == 1, "不支持的物品配置版本。"); Number(db, "revision", 0, int.MaxValue - 1, true);
        var indexes = new Dictionary<string, Dictionary<string, JsonObject>>();
        foreach (string kind in Kinds)
        {
            var paths = Array(db["categories"] ?? throw new InvalidDataException("缺少分类。"), kind).Select(n => n!.GetValue<string>()).ToArray();
            Require(paths.Distinct().Count() == paths.Length, "分类重复。");
            foreach (string builtin in Builtins(kind)) Require(paths.Contains(builtin), "缺少固定分类：" + builtin);
            foreach (string path in paths)
            {
                Require(path.Length <= 240 && path.Split('.').Length <= 10 && path.Split('.').All(p => p.Length is > 0 and <= 32 && p.Trim() == p && !Regex.IsMatch(p, "[\\\\/:*?\"<>|\\x00-\\x1f]")), "分类路径无效：" + path);
                int split = path.LastIndexOf('.'); Require(split < 0 ? Builtins(kind).Contains(path) : paths.Contains(path[..split]), "缺少父分类：" + path);
                if (kind == "items" && split >= 0 && path[..split] is "物品" or "物品.装备") Require(Builtins(kind).Contains(path), "物品大类和装备部位为固定分类。");
            }
            var index = new Dictionary<string, JsonObject>(); var names = new HashSet<string>();
            foreach (var value in Array(db, kind))
            {
                var row = value as JsonObject ?? throw new InvalidDataException("记录格式无效。");
                string id = String(row, "id"), name = String(row, "name"), category = String(row, "category");
                Require(paths.Contains(category), name + " 的分类不存在。"); Require(index.TryAdd(id, row), "记录标识重复。"); Require(names.Add(category + '\n' + name), "同一分类内重名：" + name);
                foreach (string key in new[] { "description", "displayTemplate" }) if (row.ContainsKey(key)) String(row, key, true, 10000);
            }
            indexes[kind] = index;
        }
        var attributes = Attributes(db).ToDictionary(r => Text(r, "id"));
        foreach (var row in indexes["affixes"].Values)
        {
            string effect = Text(row, "effectType", "AttributeGain"); Require(Effects.ContainsKey(effect), "词条效果类型无效。"); Require(Rarities.Contains(Text(row, "rarity")), "稀有度无效。");
            if (effect is "AttributeGain" or "AttributeRecovery")
            {
                string attribute = Text(row, "attribute"); Require(attributes.ContainsKey(attribute), "属性不存在：" + attribute);
                var targets = Array(attributes[attribute], "targets").Select(n => n!.GetValue<string>());
                Require(effect == "AttributeRecovery" ? Text(row, "target") == "当前值" : targets.Contains(Text(row, "target")), "属性目标字段无效。");
                Require((effect == "AttributeRecovery" ? RecoveryOperations : Operations).Contains(Text(row, "operation")), "属性运算方式无效。");
                Number(row, "value", Text(row, "operation") == "独立百分比增幅" ? -100 : -1e8, 1e8);
            }
        }
        foreach (var row in indexes["items"].Values)
        {
            Require(Text(row, "category") is not ("物品" or "物品.装备"), "物品需要选择具体分类。"); Require(Rarities.Contains(Text(row, "rarity")), "稀有度无效。");
            Number(row, "valueGold", 0, 1e9); Number(row, "stackLimit", 0, 1e9, true);
            string icon = String(row, "iconId", true); Require(icon == "" || indexes["assets"].ContainsKey(icon), Text(row, "name") + " 引用了不存在的图标。");
            Require(row["allowAffixAdjustment"] is JsonValue b && b.TryGetValue<bool>(out _), "词条调整开关无效。");
            Require(Within(Text(row, "category"), "物品.装备") || !row["allowAffixAdjustment"]!.GetValue<bool>(), "仅装备允许调整词条。");
            var used = new HashSet<string>();
            foreach (var reference in Objects(row, "affixes"))
            {
                string id = Text(reference, "affixId"); Require(indexes["affixes"].ContainsKey(id) && used.Add(id), Text(row, "name") + " 的词条引用缺失或重复。");
                Require(Text(reference, "kind") is "普通" or "基础", "词条身份无效。");
                if (reference.ContainsKey("multiplier")) Number(reference, "multiplier", -1e8, 1e8);
                if (reference.ContainsKey("periodSeconds")) Number(reference, "periodSeconds", 0, 1e8);
            }
            used.Clear();
            foreach (var component in Objects(row, "recipe"))
            {
                string id = Text(component, "itemId"); Require(indexes["items"].ContainsKey(id) && id != Text(row, "id") && used.Add(id), Text(row, "name") + " 的配方引用缺失、自身引用或重复。"); Number(component, "quantity", 1, 1e9, true);
            }
        }
        foreach (var row in indexes["items"].Values.Concat(indexes["affixes"].Values))
        {
            if (!row.ContainsKey("parameters")) continue;
            var keys = new HashSet<string>();
            foreach (var parameter in Objects(row, "parameters"))
            {
                Require(keys.Add(String(parameter, "key")), "业务参数重复。"); String(parameter, "label"); string type = String(parameter, "type");
                Require(new[] { "number", "boolean", "text", "item", "affix", "attribute" }.Contains(type), "业务参数类型无效。");
                if (type == "number") Number(parameter, "value", -1e12, 1e12);
                else if (type == "boolean") Require(parameter["value"] is JsonValue b && b.TryGetValue<bool>(out _), "业务参数开关无效。");
                else
                {
                    string val = String(parameter, "value", true, 10000);
                    Require(val == "" || type switch { "item" => indexes["items"].ContainsKey(val), "affix" => indexes["affixes"].ContainsKey(val), "attribute" => attributes.ContainsKey(val), _ => true }, Text(row, "name") + " 的参数引用不存在。");
                    if (parameter["options"] is JsonArray options && options.Count > 0 && val != "") Require(options.Any(o => (o is JsonObject ? Text(o, "id") : o!.GetValue<string>()) == val), "参数不在候选列表中。");
                }
            }
        }
        var indegree = indexes["items"].ToDictionary(p => p.Key, p => Array(p.Value, "recipe").Count);
        var dependents = indexes["items"].Keys.ToDictionary(id => id, _ => new List<string>());
        foreach (var row in indexes["items"].Values) foreach (var component in Array(row, "recipe")) dependents[Text(component, "itemId")].Add(Text(row, "id"));
        var ready = new Queue<string>(indegree.Where(p => p.Value == 0).Select(p => p.Key)); int visited = 0;
        while (ready.TryDequeue(out string? id)) { visited++; foreach (string parent in dependents[id]) if (--indegree[parent] == 0) ready.Enqueue(parent); }
        Require(visited == indegree.Count, "制作配方存在循环依赖。");
        foreach (var asset in indexes["assets"].Values)
        {
            Require(Regex.IsMatch(Text(asset, "file"), "^assets/[a-f0-9]{64}\\.(png|jpg|jpeg|webp|bmp)$") && Regex.IsMatch(Text(asset, "thumbnail"), "^assets/thumbs/[a-f0-9]{64}\\.png$"), "图标资源路径无效。");
            Number(asset, "width", 1, 16384, true); Number(asset, "height", 1, 16384, true);
        }
    }
    /// <summary>原子写入整个候选快照，同时保留上一版并检查磁盘竞争。</summary>
    private void Commit(JsonObject next)
    {
        string path = Path.Combine(DirectoryPath, "配置.json");
        Require(File.ReadAllBytes(path).SequenceEqual(_disk), "配置已被外部修改，请备份当前草稿并重新打开工作区。");
        next["revision"] = Revision + 1; Validate(next);
        byte[] bytes = Encoding.UTF8.GetBytes(next.ToJsonString(LevelDocument.Options));
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { using (var stream = new FileStream(temp, FileMode.CreateNew)) { stream.Write(bytes); stream.Flush(true); } File.Replace(temp, path, path + ".bak"); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        Database = next; _disk = bytes;
    }
    /// <summary>保存独立草稿，保留所有未编辑的源字段。</summary>
    public void Save(string kind, JsonObject record)
    {
        var next = (JsonObject)Database.DeepClone(); var rows = Array(next, kind); string id = Text(record, "id");
        int index = rows.ToList().FindIndex(r => Text(r, "id") == id);
        if (index >= 0) rows[index] = record.DeepClone(); else rows.Add(record.DeepClone()); Commit(next);
    }
    /// <summary>删除记录前通过完整引用校验，失败保留原数据和文件。</summary>
    public void Delete(string kind, string id)
    {
        var next = (JsonObject)Database.DeepClone(); var rows = Array(next, kind); var row = rows.FirstOrDefault(r => Text(r, "id") == id) ?? throw new InvalidDataException("记录不存在。"); rows.Remove(row); Commit(next);
    }
    /// <summary>添加、重命名或删除分类并联动所有子分类与记录。</summary>
    public void EditCategory(string kind, string action, string path, string name)
    {
        if (action is "add" or "rename") Require(!name.Contains('.') && !string.IsNullOrWhiteSpace(name), "请只填写一个分类层级，名称不能包含点号。");
        var next = (JsonObject)Database.DeepClone(); var paths = Categories(kind).ToList(); Require(paths.Contains(path), "分类不存在。");
        if (action == "add") paths.Add(path + "." + name);
        else
        {
            Require(!Builtins(kind).Contains(path), "不能修改固定分类。");
            if (action == "delete") { Require(!paths.Any(p => p != path && Within(p, path)) && !Rows(kind).Any(r => Within(Text(r, "category"), path)), "请先清空分类中的记录和子分类。"); paths.Remove(path); }
            else if (action == "rename")
            {
                string replacement = path[..(path.LastIndexOf('.') + 1)] + name;
                paths = paths.Select(p => Within(p, path) ? replacement + p[path.Length..] : p).ToList();
                foreach (var row in Array(next, kind).OfType<JsonObject>()) { string category = Text(row, "category"); if (Within(category, path)) row["category"] = replacement + category[path.Length..]; }
            }
            else throw new ArgumentException("未知分类操作。");
        }
        next["categories"]![kind] = new JsonArray(paths.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray()); Commit(next);
    }
    /// <summary>生成新记录的默认值，标识由工具维护。</summary>
    public static JsonObject NewRecord(string kind, string category)
    {
        var row = new JsonObject { ["id"] = Guid.NewGuid().ToString(), ["name"] = "", ["category"] = category, ["description"] = "" };
        if (kind == "items") { row["rarity"] = "普通"; row["valueGold"] = 0; row["stackLimit"] = 99; row["iconId"] = ""; row["allowAffixAdjustment"] = false; row["affixes"] = new JsonArray(); row["recipe"] = new JsonArray(); }
        else { row["rarity"] = "普通"; row["effectType"] = "AttributeGain"; row["attribute"] = "属性.战斗.攻击力"; row["target"] = "属性值"; row["operation"] = "固定增减"; row["value"] = 0; }
        return row;
    }
    /// <summary>在已登记的资源目录内解析路径，并拒绝链接重定向。</summary>
    public string AssetPath(string relative)
    {
        string root = Path.Combine(DirectoryPath, "assets"), full = Path.GetFullPath(Path.Combine(DirectoryPath, relative));
        Require(full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "图标路径越界。");
        for (string? p = full; p != null && !string.Equals(p, DirectoryPath, StringComparison.OrdinalIgnoreCase); p = Path.GetDirectoryName(p))
            if (File.Exists(p) || Directory.Exists(p)) Require((File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0, "图标路径不能使用重定向链接。");
        return full;
    }
    /// <summary>导入图片到当前工作区，内容散列去重；整批校验后一次提交索引。</summary>
    public void ImportImages(IEnumerable<string> paths, string category)
    {
        var inputs = paths.ToArray(); Require(inputs.Length is > 0 and <= 200, "每批导入 1 至 200 张图标。");
        var next = (JsonObject)Database.DeepClone(); var assets = Array(next, "assets");
        foreach (string path in inputs)
        {
            Require(new FileInfo(path).Length <= 20 * 1024 * 1024, "图标不能超过 20 MiB。");
            using var image = Godot.Image.LoadFromFile(path); Require(image != null && !image.IsEmpty(), "无法读取图标：" + path);
            Require((long)image!.GetWidth() * image.GetHeight() <= 32000000, "图标不能超过 3200 万像素。");
            byte[] bytes = image.SavePngToBuffer(); string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
            string file = "assets/" + hash + ".png", thumb = "assets/thumbs/" + hash + ".png";
            if (assets.Any(a => Text(a, "file") == file && Text(a, "category") == category)) continue;
            Directory.CreateDirectory(Path.Combine(DirectoryPath, "assets", "thumbs")); WriteAsset(AssetPath(file), bytes);
            int width = image.GetWidth(), height = image.GetHeight(); double scale = Math.Min(1, 160d / Math.Max(width, height));
            image.Resize(Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)));
            WriteAsset(AssetPath(thumb), image.SavePngToBuffer());
            string name = Path.GetFileNameWithoutExtension(path), unique = name; int suffix = 2;
            while (assets.Any(a => Text(a, "category") == category && Text(a, "name") == unique)) unique = name + " " + suffix++;
            assets.Add(new JsonObject { ["id"] = Guid.NewGuid().ToString(), ["name"] = unique, ["category"] = category, ["file"] = file, ["thumbnail"] = thumb, ["width"] = width, ["height"] = height });
        }
        Commit(next);
    }
    /// <summary>对内容散列命名的新图片采用同目录原子发布，失败不留下半张图片。</summary>
    private static void WriteAsset(string path, byte[] bytes)
    {
        if (File.Exists(path)) return; string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { using (var stream = new FileStream(temp, FileMode.CreateNew)) { stream.Write(bytes); stream.Flush(true); } File.Move(temp, path); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    /// <summary>释放仅由本实例创建的工作区锁。</summary>
    public void Dispose() { if (_lock == null) return; _lock.Dispose(); _lock = null; File.Delete(_lockPath); }
}

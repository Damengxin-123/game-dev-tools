using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Godot;

namespace GameDevTools;

/// <summary>关卡设计分类的 MCP 工具目录与主线程事务实现。</summary>
public sealed class LevelMcpTools
{
    /// <summary>公开工具名称，稳定前缀对应功能分类。</summary>
    public static readonly HashSet<string> Names = new() { "level.get_document", "level.list_objects", "level.get_objects", "level.get_presets", "level.apply", "level.history", "level.view", "level.capture" };
    /// <summary>当前文档会话。</summary>
    private readonly DocumentSession _session;
    /// <summary>当前编辑画布。</summary>
    private readonly LevelCanvas _canvas;
    /// <summary>生成图像内容的主线程回调。</summary>
    private readonly Func<int, Task<byte[]>> _capture;
    /// <summary>初始化关卡分类，不持有网络对象。</summary>
    public LevelMcpTools(DocumentSession session, LevelCanvas canvas, Func<int, Task<byte[]>> capture) { _session = session; _canvas = canvas; _capture = capture; }
    /// <summary>当前文档内容版本，用于避免覆盖 AI 读取后发生的人工修改。</summary>
    public string Revision => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_session.Document.Json())));
    /// <summary>创建参数类型描述。</summary>
    private static object P(string type, string description) => new { type, description };
    /// <summary>创建标准对象参数模式。</summary>
    private static object Schema(Dictionary<string, object> properties, params string[] required) => new { type = "object", properties, required, additionalProperties = false };
    /// <summary>创建带中文标题及读写注解的工具声明。</summary>
    private static object Tool(string name, string title, string description, object schema, bool readOnly = true) => new { name, title, description = "【关卡设计】" + description, inputSchema = schema, annotations = new { readOnlyHint = readOnly, destructiveHint = !readOnly, openWorldHint = false } };
    /// <summary>返回按读取、绘制、编辑和预览分类的工具目录。</summary>
    public static object[] Catalog()
    {
        var ids = new { type = "array", items = new { type = "string" }, minItems = 1, maxItems = 256, description = "对象稳定 ID 列表" };
        var fields = new Dictionary<string, object> {
            ["id"] = P("string", "修改/删除时必填；创建时由软件生成"), ["name"] = P("string", "对象名称"), ["kind"] = new { type = "string", @enum = new[] { "points", "polyline", "polygon" } }, ["preset"] = P("string", "预设名称，先查询 get_presets"),
            ["points"] = new { type = "array", minItems = 1, maxItems = 2048, items = new { type = "array", minItems = 2, maxItems = 2, items = new { type = "number" } }, description = "米制 [x,y] 顶点，折线至少2个、多边形至少3个" },
            ["fill"] = P("string", "填充色 #RRGGBB 或 Qt 顺序 #AARRGGBB"), ["stroke"] = P("string", "描边色 #RRGGBB 或 #AARRGGBB"), ["width"] = P("number", "线宽/道路宽度（米）"), ["diameter"] = P("number", "点/标记直径（米）") };
        return new[] {
            Tool("level.get_document", "读取／当前关卡", "获取标题、版本、单位、坐标方向、范围、对象总数、未保存和草稿状态，可选完整对象。", Schema(new() { ["includeObjects"] = P("boolean", "是否包含全部对象，默认否") })),
            Tool("level.list_objects", "读取／查找对象", "分页查找名称、预设、类型及包围盒；不返回全部顶点。", Schema(new() { ["query"] = P("string", "名称包含的文本"), ["preset"] = P("string", "预设完全匹配"), ["kind"] = P("string", "几何类型"), ["offset"] = P("integer", "起始位置，默认0"), ["limit"] = P("integer", "每页数量1至200，默认50") })),
            Tool("level.get_objects", "读取／对象详情", "按 ID 获取完整顶点、颜色、尺寸及图层顺序。", Schema(new() { ["ids"] = ids }, "ids")),
            Tool("level.get_presets", "读取／绘图预设", "获取所有预设名称、默认类型、颜色和米制尺寸。", Schema(new())),
            Tool("level.apply", "绘制与编辑／批量操作", "原子批量创建、修改、删除对象。先用 get_document 读取 expectedRevision；操作顺序执行，任意失败全部不生效，一次调用一次撤销。新对象追加到顶层；更新只覆盖提供的字段。", Schema(new() { ["expectedRevision"] = P("string", "读取时的关卡版本"), ["operations"] = new { type = "array", minItems = 1, maxItems = 128, items = Schema(new() { ["action"] = new { type = "string", @enum = new[] { "create", "update", "delete" } }, ["object"] = Schema(fields) }, "action", "object") } }, "expectedRevision", "operations"), false),
            Tool("level.history", "编辑／撤销重做", "撤销或重做最近一次完整编辑操作。", Schema(new() { ["expectedRevision"] = P("string", "当前关卡版本"), ["action"] = new { type = "string", @enum = new[] { "undo", "redo" } } }, "expectedRevision", "action"), false),
            Tool("level.view", "预览／定位对象", "显示完整关卡，或按稳定 ID 选中对象供人工检查。", Schema(new() { ["id"] = P("string", "可选要选中的对象 ID"), ["fit"] = P("boolean", "是否显示整个关卡，默认是") })),
            Tool("level.capture", "预览／关卡图像", "返回完整关卡的 PNG 图像内容和范围，用于 AI 看图检查；包含正式对象与名称，不含网格和编辑手柄。", Schema(new() { ["width"] = P("integer", "图片宽度256至2048，默认1280") }))
        };
    }
    /// <summary>将结构化业务数据包装为 MCP 工具结果。</summary>
    private static object Result(object data) => new { content = new[] { new { type = "text", text = JsonSerializer.Serialize(data, McpServer.Json) } }, structuredContent = data, isError = false };
    /// <summary>读取可选文本参数。</summary>
    private static string Str(JsonElement a, string key, string fallback = "") => a.TryGetProperty(key, out var v) ? v.GetString() ?? fallback : fallback;
    /// <summary>返回对象的米制边界。</summary>
    private static object Box(LevelObject o) => new { minX = o.Points.Min(p => p[0]), minY = o.Points.Min(p => p[1]), maxX = o.Points.Max(p => p[0]), maxY = o.Points.Max(p => p[1]) };
    /// <summary>检查编辑竞争和用户正在进行的交互。</summary>
    private void CheckWrite(JsonElement args)
    { if (_canvas.HasDraft || _canvas.IsInteracting) throw new InvalidOperationException("用户正在绘制或拖动，请完成当前操作后重试。"); if (Str(args, "expectedRevision") != Revision) throw new InvalidOperationException("关卡已发生变化，请重新读取当前版本后重试。"); }
    /// <summary>在引擎主线程中执行工具。</summary>
    public async Task<object> Execute(string name, JsonElement args)
    {
        if (args.ValueKind != JsonValueKind.Object) throw new ArgumentException("arguments 必须是对象。");
        var doc = _session.Document;
        if (name == "level.get_document")
            return Result(new { title = doc.Title, revision = Revision, unit = "m", coordinateSystem = "X 向右、Y 向下，顶点为世界坐标；显示两位小数，保存双精度", objectCount = doc.Objects.Count, dirty = _session.Dirty, hasDraft = _canvas.HasDraft, interacting = _canvas.IsInteracting, bounds = doc.Objects.Count == 0 ? null : Box(new LevelObject { Points = doc.Objects.SelectMany(o => o.Points).ToList() }), settings = doc.Settings, objects = args.TryGetProperty("includeObjects", out var inc) && inc.GetBoolean() ? doc.Objects : null });
        if (name == "level.get_presets") return Result(new { unit = "m", presets = Presets.Names.Select(Presets.Make).Select(o => new { o.Preset, o.Kind, o.Fill, o.Stroke, o.Width, o.Diameter }) });
        if (name == "level.list_objects")
        {
            var filtered = doc.Objects.Select((o, i) => new { o.Id, o.Name, o.Kind, o.Preset, layer = i, pointCount = o.Points.Count, bounds = Box(o) }).Where(o => o.Name.Contains(Str(args, "query"), StringComparison.OrdinalIgnoreCase) && (Str(args, "preset") == "" || o.Preset == Str(args, "preset")) && (Str(args, "kind") == "" || o.Kind == Str(args, "kind"))).ToArray();
            int offset = args.TryGetProperty("offset", out var off) ? off.GetInt32() : 0, limit = args.TryGetProperty("limit", out var lim) ? lim.GetInt32() : 50;
            if (offset < 0 || limit < 1 || limit > 200) throw new ArgumentException("分页范围无效。"); return Result(new { revision = Revision, total = filtered.Length, offset, objects = filtered.Skip(offset).Take(limit), nextOffset = offset + limit < filtered.Length ? (int?)(offset + limit) : null });
        }
        if (name == "level.get_objects")
        {
            var ids = args.GetProperty("ids").EnumerateArray().Select(v => v.GetString()!).ToArray(); if (ids.Length is < 1 or > 256) throw new ArgumentException("一次查询1至256个对象。");
            return Result(new { revision = Revision, objects = ids.Select(id => doc.Objects.FirstOrDefault(o => o.Id == id) ?? throw new ArgumentException("对象不存在：" + id)).ToArray() });
        }
        if (name == "level.apply") return Apply(args);
        if (name == "level.history") { CheckWrite(args); string action = Str(args, "action"); if (action is not ("undo" or "redo")) throw new ArgumentException("action 必须是 undo 或 redo。"); _canvas.History(action == "redo"); return Result(new { revision = Revision, objectCount = _session.Document.Objects.Count }); }
        if (name == "level.view")
        { string id = Str(args, "id"); int index = id.Length == 0 ? -1 : doc.Objects.FindIndex(o => o.Id == id); if (id.Length > 0 && index < 0) throw new ArgumentException("对象不存在。"); _canvas.Select(index); if (!args.TryGetProperty("fit", out var fit) || fit.GetBoolean()) _canvas.Fit(); return Result(new { selectedId = index < 0 ? null : id, revision = Revision }); }
        if (name == "level.capture")
        {
            int width = args.TryGetProperty("width", out var w) ? w.GetInt32() : 1280; if (width < 256 || width > 2048) throw new ArgumentException("图片宽度范围256至2048。");
            string revision = Revision; var bytes = await _capture(width); return new { content = new object[] { new { type = "text", text = JsonSerializer.Serialize(new { revision, unit = "m", note = "完整关卡正式图形，颜色与对象名称保留。" }, McpServer.Json) }, new { type = "image", mimeType = "image/png", data = Convert.ToBase64String(bytes) } }, isError = false };
        }
        throw new ArgumentException("未知工具。");
    }
    /// <summary>先在独立文档上完成全部验证，然后一次提交到当前会话。</summary>
    private object Apply(JsonElement args)
    {
        CheckWrite(args); var operations = args.GetProperty("operations"); if (operations.ValueKind != JsonValueKind.Array || operations.GetArrayLength() is < 1 or > 128) throw new ArgumentException("批量操作数必须在1至128之间。");
        var candidate = LevelDocument.Parse(_session.Document.Json()); var created = new List<string>(); int points = 0;
        foreach (var op in operations.EnumerateArray())
        {
            string action = Str(op, "action"); var data = op.GetProperty("object"); string id = Str(data, "id");
            if (action == "delete") { if (data.EnumerateObject().Any(p => p.Name != "id")) throw new ArgumentException("删除操作只接受对象 id。"); int i = candidate.Objects.FindIndex(o => o.Id == id); if (i < 0) throw new ArgumentException("删除目标不存在：" + id); candidate.Objects.RemoveAt(i); continue; }
            LevelObject obj;
            if (action == "create") { if (id.Length > 0) throw new ArgumentException("新对象 ID 由软件分配。"); string preset = Str(data, "preset"); if (preset.Length > 0 && !Presets.Names.Contains(preset)) throw new ArgumentException("未知预设：" + preset); obj = preset.Length > 0 ? Presets.Make(preset) : new LevelObject(); }
            else if (action == "update") obj = candidate.Objects.FirstOrDefault(o => o.Id == id) ?? throw new ArgumentException("修改目标不存在：" + id);
            else throw new ArgumentException("不支持的编辑操作。");
            var node = JsonSerializer.SerializeToNode(obj, LevelDocument.Options)!.AsObject();
            foreach (var field in data.EnumerateObject()) { if (!new[] { "id", "name", "kind", "preset", "points", "fill", "stroke", "width", "diameter" }.Contains(field.Name)) throw new ArgumentException("未知对象字段：" + field.Name); if (field.Name != "id") node[field.Name] = JsonNode.Parse(field.Value.GetRawText()); }
            var updated = node.Deserialize<LevelObject>(LevelDocument.Options)!; if (updated.Points == null || updated.Points.Count > 2048 || (points += updated.Points.Count) > 8192) throw new ArgumentException("单对象最多2048个节点，一次批量最多8192个节点。");
            if (action == "create") { candidate.Objects.Add(updated); created.Add(updated.Id); } else candidate.Objects[candidate.Objects.IndexOf(obj)] = updated;
        }
        candidate = LevelDocument.Parse(candidate.Json()); _session.ReplaceForEdit(candidate); _canvas.Select(-1); _canvas.Commit();
        return Result(new { revision = Revision, createdIds = created, objectCount = candidate.Objects.Count, dirty = _session.Dirty });
    }
}

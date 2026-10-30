using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GameDevTools;

/// <summary>通过套件统一服务提供物品工作区的读取与原子写入接口。</summary>
public sealed class ItemMcpTools
{
    /// <summary>物品工具的稳定名称。</summary>
    public static readonly HashSet<string> Names = ["item.get_catalog", "item.get_snapshot", "item.list_records", "item.get_record", "item.save_record", "item.delete_record", "item.edit_category", "item.import_resources", "item.get_image"];
    /// <summary>当前物品页面及其工作区。</summary>
    private readonly ItemStudioPage _page;
    /// <summary>复用页面打开的数据会话。</summary>
    public ItemMcpTools(ItemStudioPage page) => _page = page;
    /// <summary>生成带中文说明和读写注解的参数声明。</summary>
    private static object Tool(string name, string title, Dictionary<string, object> properties, string[] required, bool write = false) => new { name, title = "物品配置／" + title, description = title + (write ? "。需要最新 expectedRevision；存在人工草稿时拒绝写入；成功后立即保存 JSON。" : "。读取当前物品工作区。"), inputSchema = new { type = "object", properties, required, additionalProperties = false }, annotations = new { readOnlyHint = !write, destructiveHint = write, openWorldHint = false } };
    /// <summary>创建单个参数类型。</summary>
    private static object P(string type, string description) => new { type, description };
    /// <summary>返回独立物品分类下的接口。</summary>
    public static object[] Catalog()
    {
        var kind = new { type = "string", @enum = ItemStore.Kinds }; var revision = P("integer", "当前工作区修订号");
        return [
            Tool("item.get_catalog", "读取分类、稀有度和角色属性", new(), []),
            Tool("item.get_snapshot", "读取完整业务快照", new(), []),
            Tool("item.list_records", "搜索并分页读取记录", new() { ["kind"] = kind, ["search"] = P("string", "名称或分类关键词"), ["category"] = P("string", "分类及其后代"), ["offset"] = P("integer", "偏移量，默认 0"), ["limit"] = P("integer", "数量 1 至 200，默认 50") }, ["kind"]),
            Tool("item.get_record", "读取完整单条记录", new() { ["kind"] = kind, ["id"] = P("string", "稳定标识") }, ["kind", "id"]),
            Tool("item.save_record", "新增或更新物品、词条和资源元数据", new() { ["kind"] = kind, ["record"] = P("object", "新增物品或词条可省略 id；更新只覆盖提供字段；资源只能更新名称和分类"), ["expectedRevision"] = revision }, ["kind", "record", "expectedRevision"], true),
            Tool("item.delete_record", "删除未被引用的记录", new() { ["kind"] = kind, ["id"] = P("string", "稳定标识"), ["expectedRevision"] = revision }, ["kind", "id", "expectedRevision"], true),
            Tool("item.edit_category", "添加、重命名或删除空分类", new() { ["kind"] = kind, ["action"] = new { type = "string", @enum = new[] { "add", "rename", "delete" } }, ["path"] = P("string", "添加时为父路径，否则为目标路径"), ["name"] = P("string", "新分类单层名称"), ["expectedRevision"] = revision }, ["kind", "action", "path", "expectedRevision"], true),
            Tool("item.import_resources", "导入本机图标图片", new() { ["paths"] = new { type = "array", items = P("string", "绝对图片路径"), minItems = 1, maxItems = 200 }, ["category"] = P("string", "资源分类"), ["expectedRevision"] = revision }, ["paths", "category", "expectedRevision"], true),
            Tool("item.get_image", "读取已登记的图标缩略图", new() { ["id"] = P("string", "图标资源稳定标识") }, ["id"])
        ];
    }
    /// <summary>包装标准 MCP 结构化内容。</summary>
    private static object Result(object value) => new { content = new[] { new { type = "text", text = JsonSerializer.Serialize(value, LevelDocument.Options) } }, structuredContent = value, isError = false };
    /// <summary>在引擎主线程执行物品事务，复用界面校验与锁。</summary>
    public object Execute(string name, JsonElement arguments)
    {
        var store = _page.Store ?? throw new InvalidOperationException("物品数据目录尚未打开。");
        var args = JsonNode.Parse(arguments.GetRawText()) as JsonObject ?? throw new ArgumentException("参数必须是对象。");
        string kind = ItemStore.Text(args, "kind"); if (args.ContainsKey("kind") && !ItemStore.Kinds.Contains(kind)) throw new ArgumentException("kind 必须为 items、affixes 或 assets。");
        if (name == "item.get_catalog") return Result(new { revision = store.Revision, categories = store.Database["categories"], attributes = ItemStore.Attributes(store.Database), rarities = ItemStore.Rarities, operations = ItemStore.Operations, recoveryOperations = ItemStore.RecoveryOperations, effectTypes = ItemStore.Effects, itemExample = ItemStore.NewRecord("items", "物品.材料"), affixExample = ItemStore.NewRecord("affixes", "词条.属性加成") });
        if (name == "item.get_snapshot") return Result(store.Database);
        if (name == "item.get_record") return Result(new { revision = store.Revision, record = store.Find(kind, ItemStore.Text(args, "id")) ?? throw new ArgumentException("记录不存在。") });
        if (name == "item.list_records")
        {
            int offset = args["offset"]?.GetValue<int>() ?? 0, limit = args["limit"]?.GetValue<int>() ?? 50; if (offset < 0 || limit is < 1 or > 200) throw new ArgumentException("分页范围无效。");
            string search = ItemStore.Text(args, "search"), category = ItemStore.Text(args, "category"); var rows = store.Rows(kind).Where(r => (category == "" || ItemStore.Within(ItemStore.Text(r, "category"), category)) && (ItemStore.Text(r, "name") + ItemStore.Text(r, "category")).Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
            return Result(new { revision = store.Revision, total = rows.Length, offset, records = rows.Skip(offset).Take(limit).ToArray() });
        }
        if (name == "item.get_image")
        {
            var asset = store.Find("assets", ItemStore.Text(args, "id")) ?? throw new ArgumentException("图标不存在。"); var file = new FileInfo(store.AssetPath(ItemStore.Text(asset, "thumbnail"))); if (file.Length > 20 * 1024 * 1024) throw new IOException("图标文件过大。");
            return new { content = new[] { new { type = "image", mimeType = "image/png", data = Convert.ToBase64String(File.ReadAllBytes(file.FullName)) } }, isError = false };
        }
        if (_page.Dirty) throw new InvalidOperationException("存在尚未保存的物品草稿，请先保存或放弃草稿。");
        if (args["expectedRevision"]?.GetValue<int>() != store.Revision) throw new InvalidOperationException("物品数据版本已变化，请重新读取修订号。");
        string savedId = "";
        if (name == "item.save_record")
        {
            var patch = args["record"] as JsonObject ?? throw new ArgumentException("record 必须为对象。"); string id = ItemStore.Text(patch, "id"); JsonObject row;
            if (id == "") { if (kind == "assets") throw new ArgumentException("新图标请使用导入接口。"); row = ItemStore.NewRecord(kind, ItemStore.Text(patch, "category", kind == "items" ? "物品.其他" : "词条.属性加成")); }
            else row = (JsonObject)(store.Find(kind, id) ?? throw new ArgumentException("更新目标不存在。")).DeepClone();
            foreach (var pair in patch) { if (pair.Key == "id") continue; if (kind == "assets" && pair.Key is not ("name" or "category")) throw new ArgumentException("图标元数据只允许修改名称和分类。"); row[pair.Key] = pair.Value?.DeepClone(); }
            store.Save(kind, row); savedId = ItemStore.Text(row, "id");
        }
        else if (name == "item.delete_record") store.Delete(kind, ItemStore.Text(args, "id"));
        else if (name == "item.edit_category") store.EditCategory(kind, ItemStore.Text(args, "action"), ItemStore.Text(args, "path"), ItemStore.Text(args, "name"));
        else if (name == "item.import_resources") { var paths = ItemStore.Array(args, "paths").Select(n => n!.GetValue<string>()).ToArray(); if (paths.Any(p => !Path.IsPathFullyQualified(p))) throw new ArgumentException("图标路径必须是绝对路径。"); store.ImportImages(paths, ItemStore.Text(args, "category")); }
        else throw new ArgumentException("未知物品工具。");
        _page.ReloadSavedSelection(); return Result(new { revision = store.Revision, id = savedId });
    }
}

using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Threading.Tasks;

namespace GameDevTools;

/// <summary>使用临时副本验证迁移、保存、引用保护及真实原生界面。</summary>
public static class ItemStudioChecks
{
    /// <summary>验证业务不变量并报告具体原因。</summary>
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("物品验证失败：" + message); }
    /// <summary>确认不合法操作被拒绝。</summary>
    private static void Rejected(Action action, string message) { bool failed = false; try { action(); } catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException) { failed = true; } Require(failed, message); }
    /// <summary>仅对临时目录执行写入验证，界面读取正式迁移数据。</summary>
    public static async Task Run(MainUI ui, ItemStudioPage page)
    {
        var source = page.Store ?? throw new InvalidOperationException("物品工作区未加载。");
        ItemStore.Validate(source.Database);
        foreach (var asset in source.Rows("assets")) foreach (string key in new[] { "file", "thumbnail" }) Require(File.Exists(source.AssetPath(ItemStore.Text(asset, key))), "图标缺失：" + ItemStore.Text(asset, "name"));
        string dir = Path.Combine(Path.GetTempPath(), "ItemStudio-check-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "配置.json"); byte[] original = File.ReadAllBytes(Path.Combine(source.DirectoryPath, "配置.json")); File.WriteAllBytes(file, original);
        using (var store = new ItemStore(dir))
        {
            Rejected(() => { using var other = new ItemStore(dir); }, "工作区独占锁");
            var row = (JsonObject)store.Rows("items").First().DeepClone(); string id = ItemStore.Text(row, "id"), name = ItemStore.Text(row, "name"); row["name"] = name + " 验证"; store.Save("items", row);
            Require(File.ReadAllBytes(file + ".bak").SequenceEqual(original), "上一版备份逐字节保留"); Require(JsonNode.DeepEquals(store.Find("items", id)!["sourceRecord"], source.Find("items", id)!["sourceRecord"]), "源记录完整保留");
            string icon = ItemStore.Text(row, "iconId"); if (icon != "") Rejected(() => store.Delete("assets", icon), "图标引用保护");
            var first = ItemStore.NewRecord("items", "物品.材料"); first["name"] = "验证材料甲"; store.Save("items", first);
            var second = ItemStore.NewRecord("items", "物品.材料"); second["name"] = "验证材料乙"; second["recipe"] = new JsonArray(new JsonObject { ["itemId"] = ItemStore.Text(first, "id"), ["quantity"] = 1 }); store.Save("items", second);
            first["recipe"] = new JsonArray(new JsonObject { ["itemId"] = ItemStore.Text(second, "id"), ["quantity"] = 1 }); Rejected(() => store.Save("items", first), "拒绝循环配方");
            Rejected(() => store.Delete("items", ItemStore.Text(first, "id")), "配方删除保护");
            store.EditCategory("items", "add", "物品.材料", "验证分类"); var moved = (JsonObject)store.Find("items", id)!.DeepClone(); moved["category"] = "物品.材料.验证分类"; moved["allowAffixAdjustment"] = false; store.Save("items", moved);
            store.EditCategory("items", "rename", "物品.材料.验证分类", "验证改名"); Require(ItemStore.Text(store.Find("items", id), "category") == "物品.材料.验证改名", "分类联动改名");
            Rejected(() => store.EditCategory("items", "delete", "物品.材料", ""), "固定分类保护");
            using var image = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8); image.Fill(Colors.Red); string png = Path.Combine(dir, "验证图标.png"); image.SavePng(png); int count = store.Rows("assets").Count(); store.ImportImages([png], "通用"); store.ImportImages([png], "通用"); Require(store.Rows("assets").Count() == count + 1, "图片导入与去重");
            File.AppendAllText(file, "\n"); Rejected(() => store.Save("items", row), "外部修改冲突保护");
        }
        Require(!File.Exists(Path.Combine(dir, ".工作区锁")), "关闭释放锁"); using (var reopened = new ItemStore(dir)) Require(reopened.Rows("items").Count() == source.Rows("items").Count() + 2, "保存后重新打开");
        Require(File.ReadAllBytes(Path.Combine(source.DirectoryPath, "配置.json")).SequenceEqual(original), "验证未修改正式数据");
        page.OpenDirectory(dir); var mcp = new ItemMcpTools(page);
        var catalog = JsonSerializer.SerializeToNode(mcp.Execute("item.get_catalog", JsonSerializer.SerializeToElement(new { })))!;
        Require(catalog["structuredContent"]!["attributes"]!.AsArray().Count >= 23, "物品 MCP 属性目录");
        int revision = page.Store!.Revision;
        var created = JsonSerializer.SerializeToNode(mcp.Execute("item.save_record", JsonSerializer.SerializeToElement(new { kind = "items", expectedRevision = revision, record = new { name = "MCP 临时验证", category = "物品.材料" } })))!;
        Require(created["structuredContent"]!["revision"]!.GetValue<int>() == revision + 1, "物品 MCP 新建并保存");
        Rejected(() => mcp.Execute("item.delete_record", JsonSerializer.SerializeToElement(new { kind = "items", expectedRevision = revision, id = created["structuredContent"]!["id"]!.GetValue<string>() })), "MCP 过期修订保护");
        page.NewRecord(); Require(page.Dirty, "原生新建草稿");
        Rejected(() => mcp.Execute("item.save_record", JsonSerializer.SerializeToElement(new { kind = "items", expectedRevision = revision + 1, record = new { name = "不应写入" } })), "MCP 人工草稿保护");
        page.OpenDirectory(source.DirectoryPath);
        ui.ShowItemsForCheck(); page.SelectRecord(ItemStore.Text(source.Rows("items").First(r => ItemStore.Within(ItemStore.Text(r, "category"), "物品.装备") && ItemStore.Text(r, "iconId") != ""), "id"));
        await ui.ToSignal(ui.GetTree(), SceneTree.SignalName.ProcessFrame);
        if (OS.GetCmdlineUserArgs().Contains("--visual-test"))
        {
            await ui.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); string output = ProjectSettings.GlobalizePath("res://build/验证"); Directory.CreateDirectory(output);
            using var screenshot = ui.GetViewport().GetTexture().GetImage(); Require(!screenshot.IsEmpty(), "原生页面渲染"); screenshot.SavePng(Path.Combine(output, "物品配置.png"));
        }
        GD.Print($"PASS：物品 {source.Rows("items").Count()} / 词条 {source.Rows("affixes").Count()} / 图标 {source.Rows("assets").Count()} / 原始字段 / 原子备份 / 锁 / 引用 / 配方环 / 分类 / 图片导入 / 外部冲突 / 原生页面。临时验证目录：{dir}");
    }
}

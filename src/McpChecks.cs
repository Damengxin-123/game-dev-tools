using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using Godot;
using HttpClient = System.Net.Http.HttpClient;

namespace GameDevTools;

/// <summary>通过真实 HTTP 协议验证 AI 到当前场景的完整编辑链路。</summary>
public static class McpChecks
{
    /// <summary>验证条件。</summary>
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException("MCP 验证失败：" + message); }
    /// <summary>发送并解析真实 JSON-RPC 请求。</summary>
    private static async Task<JsonElement> Rpc(HttpClient client, string url, string method, object parameters)
    {
        using var response = await client.PostAsync(url, new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = parameters }), Encoding.UTF8, "application/json"));
        Check(response.IsSuccessStatusCode, "HTTP " + response.StatusCode); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var root = json.RootElement;
        Check(!root.TryGetProperty("error", out _), root.ToString()); return root.GetProperty("result").Clone();
    }
    /// <summary>调用关卡工具。</summary>
    private static Task<JsonElement> Tool(HttpClient client, string url, string name, object arguments) => Rpc(client, url, "tools/call", new { name, arguments });
    /// <summary>运行协议、事务、撤销、读取及图像检查。</summary>
    public static async Task Run(MainUI ui)
    {
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start(); int port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop(); ui.StartMcp(port, false);
        var connection = ui.McpConnection(); using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) }; client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", connection.Token); client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/event-stream");
        try
        {
            using var anonymous = new HttpClient(); using var denied = await anonymous.PostAsync(connection.Url, new StringContent("{}", Encoding.UTF8, "application/json")); Check(denied.StatusCode == HttpStatusCode.Unauthorized, "身份校验");
            using var hostile = new HttpRequestMessage(HttpMethod.Post, connection.Url) { Content = new StringContent("{}", Encoding.UTF8, "application/json") }; hostile.Headers.Add("Origin", "https://example.com"); using var forbidden = await client.SendAsync(hostile); Check(forbidden.StatusCode == HttpStatusCode.Forbidden, "Origin 校验");
            var init = await Rpc(client, connection.Url, "initialize", new { protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "test", version = "1" } }); Check(init.GetProperty("protocolVersion").GetString() == "2025-11-25", "协议协商");
            using var notification = await client.PostAsync(connection.Url, new StringContent("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}", Encoding.UTF8, "application/json")); Check(notification.StatusCode == HttpStatusCode.Accepted, "通知响应");
            var list = await Rpc(client, connection.Url, "tools/list", new { }); Check(list.GetProperty("tools").GetArrayLength() == 8 + ItemMcpTools.Names.Count, "分类工具发现");
            var itemCatalog = await Tool(client, connection.Url, "item.get_catalog", new { }); Check(itemCatalog.GetProperty("structuredContent").GetProperty("attributes").GetArrayLength() >= 23, "物品 HTTP 属性目录");
            var itemRows = await Tool(client, connection.Url, "item.list_records", new { kind = "items", limit = 2 }); Check(itemRows.GetProperty("structuredContent").GetProperty("records").GetArrayLength() == 2, "物品 HTTP 分页");
            var category = ui.FindChildren("*", "CheckBox", true, false).OfType<CheckBox>().First(c => c.Text == "关卡设计");
            category.ButtonPressed = false;
            try { var disabled = await Rpc(client, connection.Url, "tools/list", new { }); Check(disabled.GetProperty("tools").GetArrayLength() == ItemMcpTools.Names.Count, "分类停用"); }
            finally { category.ButtonPressed = true; }
            var state = (await Tool(client, connection.Url, "level.get_document", new { })).GetProperty("structuredContent"); string revision = state.GetProperty("revision").GetString()!; int count = state.GetProperty("objectCount").GetInt32();
            var created = await Tool(client, connection.Url, "level.apply", new { expectedRevision = revision, operations = new[] { new { action = "create", @object = new { preset = "建筑", name = "MCP 测试建筑", points = new[] { new[] { 90d, 10d }, new[] { 100d, 10d }, new[] { 100d, 20d }, new[] { 90d, 20d } } } } } });
            Check(!created.GetProperty("isError").GetBoolean(), "创建图形"); var changed = created.GetProperty("structuredContent"); string id = changed.GetProperty("createdIds")[0].GetString()!; string next = changed.GetProperty("revision").GetString()!; Check(changed.GetProperty("objectCount").GetInt32() == count + 1, "当前会话实时更新");
            var details = await Tool(client, connection.Url, "level.get_objects", new { ids = new[] { id } }); Check(details.GetProperty("structuredContent").GetProperty("objects")[0].GetProperty("points").GetArrayLength() == 4, "读取完整节点");
            var stale = await Tool(client, connection.Url, "level.history", new { expectedRevision = revision, action = "undo" }); Check(stale.GetProperty("isError").GetBoolean(), "拒绝旧版本覆盖");
            var invalid = await Tool(client, connection.Url, "level.apply", new { expectedRevision = next, operations = new object[] { new { action = "update", @object = new { id, name = "不应提交" } }, new { action = "delete", @object = new { id = "missing" } } } }); Check(invalid.GetProperty("isError").GetBoolean(), "拒绝部分成功");
            var after = await Tool(client, connection.Url, "level.get_document", new { }); Check(after.GetProperty("structuredContent").GetProperty("revision").GetString() == next, "失败批次保持原文档");
            var undo = await Tool(client, connection.Url, "level.history", new { expectedRevision = next, action = "undo" }); Check(undo.GetProperty("structuredContent").GetProperty("objectCount").GetInt32() == count, "AI 批次一次撤销");
            var redo = await Tool(client, connection.Url, "level.history", new { expectedRevision = revision, action = "redo" }); Check(redo.GetProperty("structuredContent").GetProperty("objectCount").GetInt32() == count + 1, "重做");
            var updated = await Tool(client, connection.Url, "level.apply", new { expectedRevision = next, operations = new[] { new { action = "update", @object = new { id, name = "AI 修改后的建筑" } } } });
            string updatedRevision = updated.GetProperty("structuredContent").GetProperty("revision").GetString()!;
            var deleted = await Tool(client, connection.Url, "level.apply", new { expectedRevision = updatedRevision, operations = new[] { new { action = "delete", @object = new { id } } } });
            Check(deleted.GetProperty("structuredContent").GetProperty("objectCount").GetInt32() == count, "修改和删除对象");
            await Tool(client, connection.Url, "level.history", new { expectedRevision = deleted.GetProperty("structuredContent").GetProperty("revision").GetString(), action = "undo" });
            await Tool(client, connection.Url, "level.history", new { expectedRevision = updatedRevision, action = "undo" });
            if (DisplayServer.GetName() != "headless") { var imageResult = await Tool(client, connection.Url, "level.capture", new { width = 512 }); var bytes = Convert.FromBase64String(imageResult.GetProperty("content")[1].GetProperty("data").GetString()!); using var image = new Image(); Check(image.LoadPngFromBuffer(bytes) == Error.Ok && image.GetWidth() == 512, "AI 直接获取 PNG 内容"); }
            await Tool(client, connection.Url, "level.history", new { expectedRevision = next, action = "undo" });
            if (DisplayServer.GetName() != "headless")
            {
                ui.FindChildren("*", "Button", true, false).OfType<Button>().First(b => b.Text.Contains("MCP 服务")).EmitSignal(Button.SignalName.Pressed);
                var configuration = ui.FindChildren("*", "TextEdit", true, false).OfType<TextEdit>().First(t => t.Text.Contains("mcpServers"));
                configuration.Text = configuration.Text.Replace(connection.Token, "<本机访问密钥>");
                await ui.ToSignal(ui.GetTree(), SceneTree.SignalName.ProcessFrame); await ui.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var preview = ui.GetViewport().GetTexture().GetImage(); preview.SavePng(ProjectSettings.GlobalizePath("user://MCP服务预览.png"));
            }
            GD.Print("PASS MCP：真实 HTTP / 身份与来源校验 / initialize / 分类工具发现 / 绘图与信息读取 / 版本竞争 / 批量原子性 / 撤销重做" + (DisplayServer.GetName() != "headless" ? " / PNG 图像内容" : ""));
        }
        finally { ui.StopMcp(); }
    }
}

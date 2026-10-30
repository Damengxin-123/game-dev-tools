using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GameDevTools;

/// <summary>本机无状态 Streamable HTTP MCP 服务，网络处理不访问引擎对象。</summary>
public sealed class McpServer : IDisposable
{
    /// <summary>协议与工具输出统一编码。</summary>
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    /// <summary>HTTP 监听器。</summary>
    private readonly HttpListener _listener = new();
    /// <summary>关闭时取消等待和排队操作。</summary>
    private readonly CancellationTokenSource _stop = new();
    /// <summary>串行化整个工具事务，包含异步截图。</summary>
    private readonly SemaphoreSlim _serial = new(1, 1);
    /// <summary>调度到主线程的业务入口。</summary>
    private readonly Func<string, JsonElement, CancellationToken, Task<object>> _call;
    /// <summary>本机访问密钥。</summary>
    private readonly string _token;
    /// <summary>服务地址。</summary>
    public string Url { get; }
    /// <summary>是否开放关卡设计分类。</summary>
    public volatile bool LevelEnabled = true;
    /// <summary>是否开放物品配置分类。</summary>
    public volatile bool ItemEnabled = true;
    /// <summary>配置监听地址并立即启动，端口冲突时向界面报告。</summary>
    public McpServer(int port, string token, Func<string, JsonElement, CancellationToken, Task<object>> call)
    { _token = token; _call = call; Url = $"http://127.0.0.1:{port}/mcp"; _listener.Prefixes.Add($"http://127.0.0.1:{port}/"); _listener.Start(); _ = Task.Run(Listen); }
    /// <summary>接收连接，单连接故障不影响后续请求。</summary>
    private async Task Listen()
    { while (!_stop.IsCancellationRequested) { try { var context = await _listener.GetContextAsync(); _ = Handle(context); } catch (Exception) when (_stop.IsCancellationRequested) { break; } } }
    /// <summary>检查路径、来源、身份和消息大小，再执行 JSON-RPC。</summary>
    private async Task Handle(HttpListenerContext context)
    {
        var request = context.Request; var response = context.Response;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token); timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            if (request.Url?.AbsolutePath != "/mcp") { response.StatusCode = 404; return; }
            if (request.Headers["Origin"] is string origin && origin != Url[..Url.LastIndexOf('/')]) { response.StatusCode = 403; return; }
            if (request.Headers["Authorization"] != "Bearer " + _token) { response.StatusCode = 401; return; }
            if (request.HttpMethod == "GET" || request.HttpMethod == "DELETE") { response.StatusCode = 405; response.Headers["Allow"] = "POST"; return; }
            if (request.HttpMethod != "POST") { response.StatusCode = 405; return; }
            if (request.ContentType?.Split(';')[0].Trim() != "application/json") { response.StatusCode = 415; return; }
            if (!(request.Headers["Accept"]?.Contains("application/json") == true && request.Headers["Accept"]?.Contains("text/event-stream") == true)) { response.StatusCode = 406; return; }
            if (request.Headers["MCP-Protocol-Version"] is string version && version is not ("2025-11-25" or "2025-06-18" or "2025-03-26")) { response.StatusCode = 400; return; }
            using var buffer = new MemoryStream(); var chunk = new byte[8192]; int read;
            while ((read = await request.InputStream.ReadAsync(chunk, timeout.Token)) > 0) { if (buffer.Length + read > 2 * 1024 * 1024) { response.StatusCode = 413; return; } buffer.Write(chunk, 0, read); }
            JsonDocument message;
            try { message = JsonDocument.Parse(buffer.ToArray()); } catch (JsonException) { await Write(response, Error(null, -32700, "JSON 解析失败。")); return; }
            using (message)
            {
                var root = message.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("jsonrpc", out var rpc) || rpc.ValueKind != JsonValueKind.String || rpc.GetString() != "2.0" || !root.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String)
                { await Write(response, Error(null, -32600, "无效的 JSON-RPC 请求。")); return; }
                bool hasId = root.TryGetProperty("id", out var id); object? requestId = hasId ? id.Clone() : null;
                if (hasId && id.ValueKind is not (JsonValueKind.String or JsonValueKind.Number)) { await Write(response, Error(null, -32600, "无效的请求标识。")); return; }
                string name = method.GetString()!;
                if (!hasId) { if (name is "notifications/initialized" or "notifications/cancelled") response.StatusCode = 202; else response.StatusCode = 400; return; }
                var args = root.TryGetProperty("params", out var p) ? p : default;
                object result;
                if (name == "initialize")
                {
                    var requested = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("protocolVersion", out var pv) ? pv.GetString() : "";
                    result = new { protocolVersion = requested is "2025-06-18" or "2025-03-26" ? requested : "2025-11-25", capabilities = new { tools = new { listChanged = false } }, serverInfo = new { name = "game-dev-tools", version = "0.2.0" }, instructions = "关卡设计工具使用米制世界坐标，X 向右、Y 向下。先读取文档版本，再用 level.apply 批量绘图；一次批量编辑对应一次撤销。关卡修改仅作用于当前内存工程，由用户保存。item.* 操作物品工作区，写入需要 expectedRevision，成功后立即保存；人工物品草稿未保存时拒绝写入。" };
                }
                else if (name == "ping") result = new { };
                else if (name == "tools/list") result = new { tools = (LevelEnabled ? LevelMcpTools.Catalog() : Array.Empty<object>()).Concat(ItemEnabled ? ItemMcpTools.Catalog() : Array.Empty<object>()).ToArray() };
                else if (name == "tools/call")
                {
                    if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("name", out var tool) || tool.ValueKind != JsonValueKind.String) { await Write(response, Error(requestId, -32602, "缺少工具名称。")); return; }
                    string toolName = tool.GetString()!;
                    if (!(LevelEnabled && LevelMcpTools.Names.Contains(toolName)) && !(ItemEnabled && ItemMcpTools.Names.Contains(toolName))) { await Write(response, Error(requestId, -32602, "工具不存在或分类已停用。")); return; }
                    var input = args.TryGetProperty("arguments", out var a) ? a.Clone() : JsonSerializer.SerializeToElement(new { });
                    await _serial.WaitAsync(timeout.Token);
                    try { result = await _call(toolName, input, timeout.Token); }
                    catch (Exception e) { result = new { content = new[] { new { type = "text", text = e is OperationCanceledException ? "请求已取消，尚未执行的操作已丢弃。" : e.Message } }, isError = true }; }
                    finally { _serial.Release(); }
                }
                else { await Write(response, Error(requestId, -32601, "不支持的方法。")); return; }
                await Write(response, new { jsonrpc = "2.0", id = requestId, result });
            }
        }
        catch (OperationCanceledException) { response.StatusCode = 503; }
        catch (Exception) { try { response.StatusCode = 500; } catch (ObjectDisposedException) { } }
        finally { try { response.Close(); } catch (Exception) { } }
    }
    /// <summary>构造协议层错误。</summary>
    private static object Error(object? id, int code, string message) => new { jsonrpc = "2.0", id, error = new { code, message } };
    /// <summary>发送 UTF-8 JSON 响应。</summary>
    private static async Task Write(HttpListenerResponse response, object value)
    { var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json); response.ContentType = "application/json"; response.ContentEncoding = Encoding.UTF8; response.ContentLength64 = bytes.Length; await response.OutputStream.WriteAsync(bytes); }
    /// <summary>立即停止接收并取消排队请求。</summary>
    public void Dispose() { _stop.Cancel(); _listener.Close(); }
}

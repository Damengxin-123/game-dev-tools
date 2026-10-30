using Godot;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RandomNumberGenerator = System.Security.Cryptography.RandomNumberGenerator;

namespace GameDevTools;

/// <summary>MCP 功能页和引擎主线程调度。</summary>
public partial class MainUI
{
    /// <summary>当前本机服务。</summary>
    private McpServer? _mcp;
    /// <summary>关卡分类的业务实现。</summary>
    private LevelMcpTools _levelMcp = null!;
    /// <summary>物品配置分类的业务实现。</summary>
    private ItemMcpTools _itemMcp = null!;
    /// <summary>物品服务分类开关。</summary>
    private CheckBox _mcpItems = null!;
    /// <summary>网络到主线程的有限任务队列。</summary>
    private readonly ConcurrentQueue<(Func<Task<object>> Run, TaskCompletionSource<object> Completion)> _mcpQueue = new();
    /// <summary>端口输入。</summary>
    private SpinBox _mcpPort = null!;
    /// <summary>分类开关。</summary>
    private CheckBox _mcpCategory = null!;
    /// <summary>服务状态。</summary>
    private Label _mcpState = null!;
    /// <summary>不包含密钥的调用记录。</summary>
    private Label _mcpActivity = null!;
    /// <summary>客户端配置显示区。</summary>
    private TextEdit _mcpConfig = null!;
    /// <summary>启动按钮。</summary>
    private Button _mcpStart = null!;
    /// <summary>本机身份令牌，存入用户配置目录。</summary>
    private string _mcpToken = "";
    /// <summary>创建分类化 MCP 管理页面。</summary>
    private void BuildMcp()
    {
        SetProcess(false); _itemMcp = new ItemMcpTools(_itemStudio); _levelMcp = new LevelMcpTools(_session, _canvas, CaptureForMcp);
        _mcpToken = (string)_preferences.GetValue("mcp", "token", ""); if (_mcpToken.Length < 32) { _mcpToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); _preferences.SetValue("mcp", "token", _mcpToken); _preferences.Save("user://settings.cfg"); }
        var page = Page("mcp"); page.AddChild(Text("MCP 服务", 29)); page.AddChild(Text("让 AI 连接当前工作空间，编辑关卡布局及物品、词条和图标资源。", 14, "7d8798"));
        var controls = new HBoxContainer(); page.AddChild(controls); controls.AddChild(Text("本机端口", 13)); _mcpPort = Number((int)_preferences.GetValue("mcp", "port", 24680), 1024, 65535, ""); _mcpPort.Step = 1; _mcpPort.SizeFlagsHorizontal = SizeFlags.Fill; controls.AddChild(_mcpPort);
        _mcpStart = Button("启动服务", () => { if (_mcp == null) { try { StartMcp((int)_mcpPort.Value); } catch (Exception e) { Error(e); } } else StopMcp(); }, true); controls.AddChild(_mcpStart);
        _mcpState = Text("服务已停止", 13, "7d8798"); controls.AddChild(_mcpState);
        var auto = new CheckBox { Text = "启动软件时自动开启", ButtonPressed = (bool)_preferences.GetValue("mcp", "autostart", false) }; auto.Toggled += v => { _preferences.SetValue("mcp", "autostart", v); _preferences.Save("user://settings.cfg"); }; page.AddChild(auto);
        var categories = new VBoxContainer(); page.AddChild(Card(categories)); categories.AddChild(Text("功能分类", 18));
        _mcpCategory = new CheckBox { Text = "关卡设计", ButtonPressed = (bool)_preferences.GetValue("mcp", "level_enabled", true) }; categories.AddChild(_mcpCategory);
        _mcpCategory.Toggled += enabled => { if (_mcp != null) _mcp.LevelEnabled = enabled; _preferences.SetValue("mcp", "level_enabled", enabled); _preferences.Save("user://settings.cfg"); };
        categories.AddChild(Text("读取信息   当前关卡 · 查找对象 · 节点与样式详情 · 绘图预设", 13, "576278"));
        categories.AddChild(Text("绘制与编辑   批量创建 / 修改 / 删除 · 撤销 / 重做", 13, "576278")); categories.AddChild(Text("预览   定位对象 · 获取关卡 PNG 图像", 13, "576278"));
        categories.AddChild(Text("AI 的修改实时显示在关卡中，可用一次撤销恢复。保存工程仍由你操作。", 12, "7d8798"));
        _mcpItems = new CheckBox { Text = "物品配置", ButtonPressed = (bool)_preferences.GetValue("mcp", "items_enabled", true) }; categories.AddChild(_mcpItems);
        _mcpItems.Toggled += enabled => { if (_mcp != null) _mcp.ItemEnabled = enabled; _preferences.SetValue("mcp", "items_enabled", enabled); _preferences.Save("user://settings.cfg"); };
        categories.AddChild(Text("读取与编辑   物品 · 词条 · 分类 · 图标；写入立即保存，人工草稿优先。", 13, "576278"));
        page.AddChild(Text("客户端连接配置", 18)); page.AddChild(Text("使用支持 Streamable HTTP 的 MCP 客户端。分类变更后重新连接客户端。", 13, "7d8798"));
        _mcpConfig = new TextEdit { Editable = false, CustomMinimumSize = new Vector2(0, 180), SizeFlagsVertical = SizeFlags.ExpandFill }; page.AddChild(_mcpConfig);
        var actions = new HBoxContainer(); page.AddChild(actions); actions.AddChild(Button("复制连接配置", () => DisplayServer.ClipboardSet(_mcpConfig.Text))); actions.AddChild(Button("重新生成访问密钥", () => { bool running = _mcp != null; StopMcp(); _mcpToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); _preferences.SetValue("mcp", "token", _mcpToken); _preferences.Save("user://settings.cfg"); if (running) { try { StartMcp((int)_mcpPort.Value); } catch (Exception e) { Error(e); } } UpdateMcpConfig(); }));
        _mcpActivity = Text("尚无工具调用", 12, "7d8798"); page.AddChild(_mcpActivity);
        _mcpPort.ValueChanged += _ => UpdateMcpConfig(); UpdateMcpConfig();
        if (auto.ButtonPressed && !OS.GetCmdlineUserArgs().Contains("--mcp-test")) { try { StartMcp((int)_mcpPort.Value); } catch (Exception e) { _mcpState.Text = "启动失败：" + e.Message; } }
    }
    /// <summary>生成当前地址的客户端 JSON 配置。</summary>
    private void UpdateMcpConfig()
    { _mcpConfig.Text = JsonSerializer.Serialize(new { mcpServers = new { game_dev_tools = new { type = "http", url = $"http://127.0.0.1:{(int)_mcpPort.Value}/mcp", headers = new { Authorization = "Bearer " + _mcpToken } } } }, new JsonSerializerOptions { WriteIndented = true }); }
    /// <summary>启动本地服务，只有启动成功才保存端口。</summary>
    public void StartMcp(int port, bool persist = true)
    {
        StopMcp(); _mcp = new McpServer(port, _mcpToken, QueueMcp) { LevelEnabled = _mcpCategory.ButtonPressed, ItemEnabled = _mcpItems.ButtonPressed }; _mcpPort.Value = port; _mcpPort.Editable = false;
        if (persist) { _preferences.SetValue("mcp", "port", port); _preferences.Save("user://settings.cfg"); } _mcpState.Text = "运行中  " + _mcp.Url; _mcpStart.Text = "停止服务"; SetProcess(true);
    }
    /// <summary>关闭监听器并丢弃尚未执行的请求。</summary>
    public void StopMcp()
    { _mcp?.Dispose(); _mcp = null; while (_mcpQueue.TryDequeue(out var item)) item.Completion.TrySetCanceled(); SetProcess(false); if (_mcpState != null) { _mcpState.Text = "服务已停止"; _mcpStart.Text = "启动服务"; _mcpPort.Editable = true; } }
    /// <summary>将一个工具操作排队到 Godot 主线程，超时请求不会延迟执行。</summary>
    private async Task<object> QueueMcp(string name, JsonElement args, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested(); if (_mcpQueue.Count >= 32) throw new InvalidOperationException("工具请求繁忙，请稍后重试。");
        var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancel.Register(() => completion.TrySetCanceled(cancel));
        _mcpQueue.Enqueue((async () => { cancel.ThrowIfCancellationRequested(); bool itemTool = name.StartsWith("item.", StringComparison.Ordinal); if ((itemTool ? _mcp?.ItemEnabled : _mcp?.LevelEnabled) != true) throw new InvalidOperationException("该功能分类已关闭。"); _mcpActivity.Text = $"{DateTime.Now:HH:mm:ss}  执行 {name}"; var result = itemTool ? _itemMcp.Execute(name, args) : await _levelMcp.Execute(name, args); _mcpActivity.Text = $"{DateTime.Now:HH:mm:ss}  已完成 {name}"; return result; }, completion));
        return await completion.Task;
    }
    /// <summary>只在服务运行时处理工具队列，网络线程不直接修改场景。</summary>
    public override void _Process(double delta)
    { if (_mcpQueue.TryDequeue(out var item) && !item.Completion.Task.IsCompleted) ExecuteQueuedMcp(item.Run, item.Completion); }
    /// <summary>保留引擎同步上下文，传播工具错误给调用者。</summary>
    private async void ExecuteQueuedMcp(Func<Task<object>> run, TaskCompletionSource<object> completion)
    { try { completion.TrySetResult(await run()); } catch (Exception e) { _mcpActivity.Text = "工具调用失败：" + e.Message; completion.TrySetException(e); } }
    /// <summary>生成只供返回给 AI 的 PNG，不触碰用户文件。</summary>
    private async Task<byte[]> CaptureForMcp(int width)
    {
        if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("无图形模式无法生成 PNG，请在正常软件窗口中调用。");
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "game-dev-mcp-" + Guid.NewGuid().ToString("N") + ".png");
        try { await ExportPng(path, width, false); return await File.ReadAllBytesAsync(path); } finally { if (File.Exists(path)) File.Delete(path); }
    }
    /// <summary>提供验证使用的本机地址与令牌，不写入日志。</summary>
    public (string Url, string Token) McpConnection() => (_mcp?.Url ?? "", _mcpToken);
}

using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GameDevTools;

/// <summary>独立工具套件主窗口，全部使用 Godot 原生控件。</summary>
public partial class MainUI : Control
{
    /// <summary>物品配置独立页面。</summary>
    private ItemStudioPage _itemStudio = null!;
    /// <summary>当前关卡会话。</summary>
    private readonly DocumentSession _session = new();
    /// <summary>工作区中的矢量画布。</summary>
    private LevelCanvas _canvas = null!;
    /// <summary>所有功能页面。</summary>
    private readonly Dictionary<string, Control> _pages = new();
    /// <summary>侧栏功能按钮。</summary>
    private readonly Dictionary<string, Button> _navigation = new();
    /// <summary>当前页面标识。</summary>
    private string _page = "layout";
    /// <summary>状态提示。</summary>
    private Label _status = null!;
    /// <summary>鼠标坐标状态。</summary>
    private Label _coordinates = null!;
    /// <summary>当前工程标题。</summary>
    private Label _title = null!;
    /// <summary>对象列表。</summary>
    private ItemList _objects = null!;
    /// <summary>名称输入。</summary>
    private LineEdit _name = null!;
    /// <summary>预设下拉框。</summary>
    private OptionButton _preset = null!;
    /// <summary>节点下拉框。</summary>
    private OptionButton _vertices = null!;
    /// <summary>填充颜色输入。</summary>
    private ColorPickerButton _fill = null!;
    /// <summary>轮廓颜色输入。</summary>
    private ColorPickerButton _stroke = null!;
    /// <summary>线宽输入。</summary>
    private SpinBox _width = null!;
    /// <summary>点直径输入。</summary>
    private SpinBox _diameter = null!;
    /// <summary>节点横坐标。</summary>
    private SpinBox _x = null!;
    /// <summary>节点纵坐标。</summary>
    private SpinBox _y = null!;
    /// <summary>节点区域。</summary>
    private VBoxContainer _nodeFields = null!;
    /// <summary>属性标题。</summary>
    private Label _selectionTitle = null!;
    /// <summary>防止属性刷新回写。</summary>
    private bool _refreshing;
    /// <summary>绘图设置控件及文档读取函数。</summary>
    private readonly List<(CheckBox Check, Func<DrawingSettings, bool> Read)> _checks = new();
    /// <summary>网格尺寸控件。</summary>
    private SpinBox _gridStep = null!;
    /// <summary>角度步长控件。</summary>
    private SpinBox _angleStep = null!;
    /// <summary>绘图工具按钮。</summary>
    private readonly Dictionary<string, Button> _tools = new();
    /// <summary>打开项目选择器。</summary>
    private FileDialog _openDialog = null!;
    /// <summary>保存项目选择器。</summary>
    private FileDialog _saveDialog = null!;
    /// <summary>导出文件选择器。</summary>
    private FileDialog _exportDialog = null!;
    /// <summary>导出选项窗口。</summary>
    private ConfirmationDialog _exportOptions = null!;
    /// <summary>导出尺寸。</summary>
    private SpinBox _exportWidth = null!;
    /// <summary>是否输出图例。</summary>
    private CheckBox _legend = null!;
    /// <summary>是否使用预设语义命名。</summary>
    private CheckBox _semanticNames = null!;
    /// <summary>当前导出格式。</summary>
    private bool _svg;
    /// <summary>保存成功之后的待执行操作。</summary>
    private Action? _afterSave;
    /// <summary>未保存确认后的待执行操作。</summary>
    private Action? _afterGuard;
    /// <summary>未保存确认窗口。</summary>
    private ConfirmationDialog _unsaved = null!;
    /// <summary>最近项目列表容器。</summary>
    private VBoxContainer _recentList = null!;
    /// <summary>工具套件本地设置。</summary>
    private readonly ConfigFile _preferences = new();
    /// <summary>功能页容器。</summary>
    private MarginContainer _workspace = null!;

    /// <summary>创建外壳、业务页面和对话框。</summary>
    public override void _Ready()
    {
        GetTree().AutoAcceptQuit = false; GetWindow().MinSize = new Vector2I(1180, 760);
        var factory = GD.Load<GDScript>("res://scripts/界面主题.gd").New().AsGodotObject(); Theme = factory.Call("create_theme").As<Theme>(); factory.Dispose();
        _preferences.Load("user://settings.cfg"); Theme.DefaultFontSize = (int)_preferences.GetValue("ui", "font_size", 14);
        _session.Reset(Presets.Demo());
        var background = new ColorRect { Color = new Color("f7f9fc"), MouseFilter = MouseFilterEnum.Ignore }; AddChild(background); background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var shell = new HBoxContainer(); AddChild(shell); shell.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); shell.AddThemeConstantOverride("separation", 0);
        BuildSidebar(shell); _workspace = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill }; shell.AddChild(_workspace);
        foreach (string edge in new[] { "left", "right", "top", "bottom" }) _workspace.AddThemeConstantOverride("margin_" + edge, 20);
        BuildLayout(); BuildItems(); BuildProjects(); BuildHelp(); BuildSettings(); BuildDialogs(); BuildMcp(); ShowPage("layout"); Refresh();
        _canvas.CallDeferred(nameof(LevelCanvas.Fit));
        var args = OS.GetCmdlineUserArgs();
        if (args.Contains("--items-test") || args.Contains("--smoke") || args.Contains("--visual-test") || args.Contains("--mcp-test")) CallDeferred(nameof(RunChecks));
        else { var path = args.FirstOrDefault(a => a.EndsWith(".level.json", StringComparison.OrdinalIgnoreCase)); if (path != null) OpenProject(path); }
    }
    /// <summary>创建统一文本控件。</summary>
    private static Label Text(string value, int size = 14, string color = "202938")
    { var label = new Label { Text = value }; label.AddThemeFontSizeOverride("font_size", size); label.AddThemeColorOverride("font_color", new Color(color)); return label; }
    /// <summary>创建原生按钮及点击回调。</summary>
    private static Button Button(string text, Action action, bool primary = false)
    {
        var b = new Button { Text = text, MouseDefaultCursorShape = CursorShape.PointingHand }; b.Pressed += action;
        if (primary) { var box = new StyleBoxFlat { BgColor = new Color("315ee7"), CornerRadiusTopLeft = 7, CornerRadiusTopRight = 7, CornerRadiusBottomLeft = 7, CornerRadiusBottomRight = 7, ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 9, ContentMarginBottom = 9 }; b.AddThemeStyleboxOverride("normal", box); b.AddThemeColorOverride("font_color", Colors.White); }
        return b;
    }
    /// <summary>创建内容卡片。</summary>
    private static PanelContainer Card(Control content) { var panel = new PanelContainer { SizeFlagsHorizontal = content.SizeFlagsHorizontal, SizeFlagsVertical = content.SizeFlagsVertical }; panel.AddChild(content); return panel; }
    /// <summary>创建可扩展空白。</summary>
    private static Control Spacer(bool vertical = false) => new() { SizeFlagsHorizontal = vertical ? SizeFlags.Fill : SizeFlags.ExpandFill, SizeFlagsVertical = vertical ? SizeFlags.ExpandFill : SizeFlags.Fill };
    /// <summary>创建两位小数的尺寸输入。</summary>
    private static SpinBox Number(double value, double min, double max, string suffix = " m") => new DecimalSpinBox() { Value = value, MinValue = min, MaxValue = max, Step = .01, Suffix = suffix, CustomMinimumSize = new Vector2(100, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill, UpdateOnTextChanged = false };
    /// <summary>添加属性名称和控件。</summary>
    private static void Field(VBoxContainer parent, string name, Control input) { parent.AddChild(Text(name, 12, "7d8798")); parent.AddChild(input); }
    /// <summary>创建左侧功能栏。</summary>
    private void BuildSidebar(HBoxContainer shell)
    {
        var side = new VBoxContainer { CustomMinimumSize = new Vector2(184, 0) }; var panel = Card(side); shell.AddChild(panel);
        side.AddChild(Text("◈  开发工具", 24, "315ee7")); side.AddChild(Text("GAME DEV WORKSPACE", 10, "98a2b3")); side.AddChild(new HSeparator());
        side.AddChild(Text("工作空间", 11, "98a2b3"));
        foreach (var (id, title) in new[] { ("projects", "▤  项目管理"), ("layout", "◇  关卡布局"), ("items", "▣  物品配置"), ("mcp", "⌘  MCP 服务"), ("help", "?  使用指南") })
        { var b = Button(title, () => ShowPage(id)); b.ToggleMode = true; b.Alignment = HorizontalAlignment.Left; side.AddChild(b); _navigation[id] = b; }
        side.AddChild(Spacer(true)); var settings = Button("⚙  设置", () => ShowPage("settings")); settings.ToggleMode = true; side.AddChild(settings); _navigation["settings"] = settings;
        side.AddChild(new HSeparator()); side.AddChild(Text("●  本地工作空间", 12, "31a27a")); side.AddChild(Text("开发工具集  /  0.1.0", 11, "98a2b3"));
    }
    /// <summary>登记独立功能页。</summary>
    private VBoxContainer Page(string id) { var page = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill }; _workspace.AddChild(page); _pages[id] = page; return page; }
    /// <summary>切换功能，保留画布会话。</summary>
    private void ShowPage(string id) { if (_page == "items" && id != "items" && _itemStudio.Dirty) { _itemStudio.Guard(() => ShowPage(id)); return; } _page = id; foreach (var (key, page) in _pages) page.Visible = key == id; foreach (var (key, button) in _navigation) button.ButtonPressed = key == id; if (id == "projects") RefreshRecent(); }
    /// <summary>创建关卡布局的完整工具页面。</summary>
    private void BuildLayout()
    {
        var page = Page("layout"); var header = new HBoxContainer(); page.AddChild(header);
        var heading = new VBoxContainer(); heading.AddChild(Text("LEVEL DESIGN", 10, "98a2b3")); _title = Text("关卡布局", 25); heading.AddChild(_title); header.AddChild(heading); header.AddChild(Spacer());
        header.AddChild(Button("新建", NewProject)); header.AddChild(Button("打开", () => _openDialog.PopupCenteredRatio(.72f))); header.AddChild(Button("保存", () => Save(false), true)); header.AddChild(Button("另存为", () => Save(true)));
        var bar = new HBoxContainer(); page.AddChild(bar);
        bar.AddChild(Button("撤销", () => _canvas.History(false))); bar.AddChild(Button("重做", () => _canvas.History(true))); bar.AddChild(Button("查看全部", () => _canvas.Fit())); bar.AddChild(Button("重命名项目", RenameProject)); bar.AddChild(Spacer()); bar.AddChild(Button("导出 PNG", () => BeginExport(false))); bar.AddChild(Button("导出 SVG", () => BeginExport(true)));
        var settings = new HBoxContainer(); page.AddChild(settings);
        // 添加直接映射到文档的绘图辅助开关。
        void Toggle(string title, Func<DrawingSettings, bool> read, Action<DrawingSettings, bool> write)
        { var c = new CheckBox { Text = title }; c.Toggled += value => { if (_refreshing) return; write(_session.Document.Settings, value); _canvas.Commit(); }; settings.AddChild(c); _checks.Add((c, read)); }
        Toggle("网格", s => s.Grid, (s, v) => s.Grid = v); Toggle("名称", s => s.ShowNames, (s, v) => s.ShowNames = v); Toggle("标尺", s => s.Ruler, (s, v) => s.Ruler = v); Toggle("十字线", s => s.Crosshair, (s, v) => s.Crosshair = v); Toggle("常用角", s => s.Angles, (s, v) => s.Angles = v); Toggle("角度倍数", s => s.Quantize, (s, v) => s.Quantize = v);
        settings.AddChild(Spacer()); _gridStep = Number(1, .01, 10000); _gridStep.SizeFlagsHorizontal = SizeFlags.Fill; _gridStep.TooltipText = "基本网格间距（米）"; settings.AddChild(_gridStep);
        _angleStep = Number(10, .01, 180, " °"); _angleStep.SizeFlagsHorizontal = SizeFlags.Fill; _angleStep.TooltipText = "最小角度单位"; settings.AddChild(_angleStep);
        _gridStep.ValueChanged += v => { if (!_refreshing) { _session.Document.Settings.GridStep = v; _canvas.Commit(); } }; _angleStep.ValueChanged += v => { if (!_refreshing) { _session.Document.Settings.AngleStep = v; _canvas.Commit(); } };
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; page.AddChild(body);
        var toolScroll = new ScrollContainer { CustomMinimumSize = new Vector2(112, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; body.AddChild(Card(toolScroll)); var tools = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; toolScroll.AddChild(tools); tools.AddChild(Text("绘图工具", 12, "7d8798"));
        foreach (var (id, text) in new[] { ("select", "↖  选择 / 编辑"), ("points", "∴  点集"), ("polyline", "⌁  连续折线"), ("polygon", "◇  多边形") }.Concat(Presets.Names.Select(n => ("preset:" + n, "  " + n))))
        { var button = Button(text, () => { _canvas.SetTool(id); Refresh(); }); button.ToggleMode = true; button.Alignment = HorizontalAlignment.Left; button.AddThemeFontSizeOverride("font_size", 13); tools.AddChild(button); _tools[id] = button; }
        tools.AddChild(new HSeparator()); tools.AddChild(Button("完成图形", () => _canvas.Finish())); tools.AddChild(Button("取消绘制", () => _canvas.Cancel()));
        _canvas = new LevelCanvas { Session = _session, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(380, 300) }; var canvasPanel = Card(_canvas); canvasPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill; body.AddChild(canvasPanel);
        _canvas.Changed += Refresh; _canvas.SelectionChanged += RefreshInspector; _canvas.Message += Status; _canvas.PointerInfo += value => _coordinates.Text = value;
        BuildInspector(body);
        var footer = new HBoxContainer(); page.AddChild(footer); _status = Text("中键平移 · 滚轮缩放 · 左键绘制 · Enter 完成", 12, "7d8798"); _status.SizeFlagsHorizontal = SizeFlags.ExpandFill; _status.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; footer.AddChild(_status); _coordinates = Text("单位：米", 11, "7d8798"); footer.AddChild(_coordinates);
    }
    /// <summary>创建对象列表、样式、层级和节点属性。</summary>
    private void BuildInspector(HBoxContainer body)
    {
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(236, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; body.AddChild(Card(scroll)); var fields = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; scroll.AddChild(fields);
        fields.AddChild(Text("对象与属性", 17)); _objects = new ItemList { CustomMinimumSize = new Vector2(200, 155), SizeFlagsHorizontal = SizeFlags.ExpandFill }; fields.AddChild(_objects); _objects.ItemSelected += i => { if (!_refreshing) _canvas.Select((int)i); };
        var layers = new HBoxContainer(); fields.AddChild(layers); layers.AddChild(Button("↑", () => _canvas.Reorder(_canvas.Selected + 1))); layers.AddChild(Button("↓", () => _canvas.Reorder(_canvas.Selected - 1))); layers.AddChild(Button("置顶", () => _canvas.Reorder(_session.Document.Objects.Count - 1))); layers.AddChild(Button("置底", () => _canvas.Reorder(0)));
        var edit = new HBoxContainer(); fields.AddChild(edit); edit.AddChild(Button("复制", () => _canvas.Duplicate())); edit.AddChild(Button("删除对象", () => _canvas.Delete(true)));
        fields.AddChild(new HSeparator()); _selectionTitle = Text("新建图形样式", 13, "315ee7"); fields.AddChild(_selectionTitle);
        _name = new LineEdit(); Field(fields, "对象名称", _name); _name.TextSubmitted += _ => ApplyInspector(); _name.FocusExited += ApplyInspector;
        _preset = new OptionButton(); _preset.AddItem("无预设"); foreach (var n in Presets.Names) _preset.AddItem(n); Field(fields, "预设语义", _preset); _preset.ItemSelected += _ => ApplyInspector();
        var colors = new HBoxContainer(); fields.AddChild(colors); var fillBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; var strokeBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; colors.AddChild(fillBox); colors.AddChild(strokeBox);
        _fill = new ColorPickerButton { CustomMinimumSize = new Vector2(90, 32), EditAlpha = true }; _stroke = new ColorPickerButton { CustomMinimumSize = new Vector2(90, 32), EditAlpha = true }; Field(fillBox, "填充", _fill); Field(strokeBox, "轮廓", _stroke); _fill.PopupClosed += ApplyInspector; _stroke.PopupClosed += ApplyInspector;
        _width = Number(.2, .01, 1000); Field(fields, "线粗细 / 道路宽度", _width); _width.ValueChanged += _ => ApplyInspector();
        _diameter = Number(.8, .01, 1000); Field(fields, "点 / 标记尺寸", _diameter); _diameter.ValueChanged += _ => ApplyInspector();
        _nodeFields = new VBoxContainer(); fields.AddChild(_nodeFields); _nodeFields.AddChild(new HSeparator()); _vertices = new OptionButton(); Field(_nodeFields, "节点编辑", _vertices); _vertices.ItemSelected += i => { if (_refreshing) return; _canvas.Vertex = (int)i; RefreshCoordinates(); _canvas.RefreshSelected(); };
        _x = Number(0, -1e7, 1e7); _y = Number(0, -1e7, 1e7); Field(_nodeFields, "X 坐标", _x); Field(_nodeFields, "Y 坐标", _y); _x.ValueChanged += _ => ApplyCoordinates(); _y.ValueChanged += _ => ApplyCoordinates();
        _nodeFields.AddChild(Button("删除选中节点", () => _canvas.Delete()));
    }
    /// <summary>同步完整界面状态。</summary>
    private void Refresh()
    {
        if (_title == null || _objects == null) return; _refreshing = true;
        _title.Text = _session.Document.Title + (_session.Dirty || _canvas.HasDraft ? "  •" : ""); GetWindow().Title = _title.Text + " — 游戏开发辅助工具";
        _objects.Clear(); foreach (var o in _session.Document.Objects) _objects.AddItem(o.Name);
        if (_canvas.Selected >= 0 && _canvas.Selected < _objects.ItemCount) _objects.Select(_canvas.Selected);
        foreach (var (check, read) in _checks) check.ButtonPressed = read(_session.Document.Settings);
        _gridStep.Value = _session.Document.Settings.GridStep; _angleStep.Value = _session.Document.Settings.AngleStep;
        foreach (var (key, button) in _tools) button.ButtonPressed = key == _canvas.Tool;
        _refreshing = false; RefreshInspector();
    }
    /// <summary>同步当前选择或默认样式。</summary>
    private void RefreshInspector()
    {
        if (_objects == null) return; _refreshing = true; bool selected = _canvas.Selected >= 0 && _canvas.Selected < _session.Document.Objects.Count;
        var o = selected ? _session.Document.Objects[_canvas.Selected] : _canvas.Style;
        _objects.DeselectAll(); if (selected) _objects.Select(_canvas.Selected);
        _selectionTitle.Text = selected ? $"对象属性 · {o.Points.Count} 个节点" : "新建图形样式"; _name.Text = o.Name; _preset.Select(Math.Max(0, Array.IndexOf(Presets.Names, o.Preset) + 1));
        _fill.Color = LevelObject.ColorOf(o.Fill); _stroke.Color = LevelObject.ColorOf(o.Stroke); _width.Value = o.Width; _diameter.Value = o.Diameter;
        _nodeFields.Visible = selected; _vertices.Clear(); if (selected) { for (int i = 0; i < o.Points.Count; i++) _vertices.AddItem($"节点 {i + 1}"); if (_canvas.Vertex >= 0) _vertices.Select(_canvas.Vertex); else _vertices.Select(-1); }
        _refreshing = false; RefreshCoordinates();
    }
    /// <summary>将属性面板变更提交为一次可撤销操作。</summary>
    private void ApplyInspector()
    {
        if (_refreshing || _name == null) return; bool selected = _canvas.Selected >= 0;
        var o = selected ? _session.Document.Objects[_canvas.Selected] : _canvas.Style.Clone();
        o.Name = _name.Text; o.Preset = _preset.Selected > 0 ? Presets.Names[_preset.Selected - 1] : ""; o.Fill = LevelObject.ColorText(_fill.Color); o.Stroke = LevelObject.ColorText(_stroke.Color); o.Width = _width.Value; o.Diameter = _diameter.Value;
        if (selected) _canvas.Commit(); else _canvas.UpdateStyle(o);
    }
    /// <summary>同步指定顶点的数值。</summary>
    private void RefreshCoordinates()
    {
        if (_x == null) return; _refreshing = true; bool valid = _canvas.Selected >= 0 && _canvas.Vertex >= 0;
        _x.Editable = valid; _y.Editable = valid;
        if (valid) { var p = _session.Document.Objects[_canvas.Selected].Points[_canvas.Vertex]; _x.Value = p[0]; _y.Value = p[1]; }
        _refreshing = false;
    }
    /// <summary>写入米制顶点坐标。</summary>
    private void ApplyCoordinates()
    { if (_refreshing || _canvas.Selected < 0 || _canvas.Vertex < 0) return; _session.Document.Objects[_canvas.Selected].Points[_canvas.Vertex] = new[] { _x.Value, _y.Value }; _canvas.Commit(); }
    /// <summary>显示状态信息。</summary>
    private void Status(string text) { if (_status != null) _status.Text = text; }
    /// <summary>显示可读错误对话框。</summary>
    private void Error(Exception error) { GD.PushError(error.Message); var dialog = new AcceptDialog { Title = "操作未完成", DialogText = error.Message }; AddChild(dialog); dialog.Confirmed += dialog.QueueFree; dialog.Canceled += dialog.QueueFree; dialog.PopupCentered(new Vector2I(560, 190)); }
    /// <summary>建立通用原生文件选择窗口。</summary>
    private FileDialog FilePicker(string title, FileDialog.FileModeEnum mode, string[] filters)
    { var d = new FileDialog { Title = title, FileMode = mode, Access = FileDialog.AccessEnum.Filesystem, Filters = filters, UseNativeDialog = false }; AddChild(d); return d; }
    /// <summary>创建文件和未保存保护对话框。</summary>
    private void BuildDialogs()
    {
        _openDialog = FilePicker("打开关卡工程", FileDialog.FileModeEnum.OpenFile, new[] { "*.level.json ; 关卡工程" }); _openDialog.FileSelected += path => Guard(() => OpenProject(path));
        _saveDialog = FilePicker("保存关卡工程", FileDialog.FileModeEnum.SaveFile, new[] { "*.level.json ; 关卡工程" }); _saveDialog.FileSelected += path => SaveTo(path.EndsWith(".level.json", StringComparison.OrdinalIgnoreCase) ? path : path + ".level.json"); _saveDialog.Canceled += () => _afterSave = null;
        _exportDialog = FilePicker("导出", FileDialog.FileModeEnum.SaveFile, Array.Empty<string>()); _exportDialog.FileSelected += async path => await ExportTo(path);
        _unsaved = new ConfirmationDialog { Title = "保存当前修改？", DialogText = "当前项目或绘图草稿有未保存修改。", OkButtonText = "保存并继续", CancelButtonText = "取消" }; AddChild(_unsaved);
        _unsaved.AddButton("不保存", false, "discard"); _unsaved.Confirmed += () => { _afterSave = _afterGuard; _afterGuard = null; Save(false); }; _unsaved.CustomAction += _ => { var next = _afterGuard; _afterGuard = null; _canvas.Cancel(); next?.Invoke(); }; _unsaved.Canceled += () => _afterGuard = null;
        _exportOptions = new ConfirmationDialog { Title = "导出选项", OkButtonText = "选择保存位置", CancelButtonText = "取消" }; AddChild(_exportOptions); var options = new VBoxContainer { CustomMinimumSize = new Vector2(340, 170) }; _exportOptions.AddChild(options); _exportWidth = Number(2048, 256, 8192, " px"); _exportWidth.Step = 1; Field(options, "图片宽度", _exportWidth); _legend = new CheckBox { Text = "附加预设图例", ButtonPressed = true }; options.AddChild(_legend); _semanticNames = new CheckBox { Text = "SVG 分组使用预设语义命名" }; options.AddChild(_semanticNames);
        _exportOptions.Confirmed += () => { _exportDialog.Filters = new[] { _svg ? "*.svg ; SVG 矢量图" : "*.png ; PNG 图片" }; _exportDialog.CurrentFile = SafeName(_session.Document.Title) + (_svg ? ".svg" : ".png"); _exportDialog.PopupCenteredRatio(.72f); };
    }
    /// <summary>在可能丢失编辑内容的操作前进行保护。</summary>
    private void Guard(Action next)
    { if (!_session.Dirty && !_canvas.HasDraft) { next(); return; } _afterGuard = next; _unsaved.PopupCentered(new Vector2I(500, 180)); }
    /// <summary>创建新的空白项目。</summary>
    private void NewProject() => Guard(() => { _session.Reset(new LevelDocument()); _canvas.Cancel(); _canvas.Select(-1); _canvas.Sync(); _canvas.Fit(); ShowPage("layout"); Refresh(); Status("已创建空白关卡。"); });
    /// <summary>读取旧工程并切换画布。</summary>
    private void OpenProject(string path)
    { try { _session.Open(path); _canvas.Cancel(); _canvas.Select(-1); _canvas.Sync(); _canvas.Fit(); Remember(path); ShowPage("layout"); Refresh(); Status("已打开：" + path); } catch (Exception e) { Error(e); } }
    /// <summary>开始保存，首次保存或另存为时选择位置。</summary>
    private void Save(bool saveAs)
    { ApplyInspector(); if (!_canvas.Finish()) { _afterSave = null; return; } if (saveAs || string.IsNullOrEmpty(_session.Path)) { _saveDialog.CurrentFile = SafeName(_session.Document.Title) + ".level.json"; _saveDialog.PopupCenteredRatio(.72f); } else SaveTo(_session.Path); }
    /// <summary>保存成功后才能继续关闭或切换。</summary>
    private void SaveTo(string path)
    { try { _session.Save(path); Remember(path); Refresh(); Status("已保存：" + path); var next = _afterSave; _afterSave = null; next?.Invoke(); } catch (Exception e) { _afterSave = null; Error(e); } }
    /// <summary>处理项目名称中的非法文件字符。</summary>
    private static string SafeName(string name) { foreach (var c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_'); return string.IsNullOrWhiteSpace(name) ? "关卡工程" : name; }
    /// <summary>提供可撤销的项目重命名。</summary>
    private void RenameProject()
    { var dialog = new ConfirmationDialog { Title = "重命名项目", OkButtonText = "确定", CancelButtonText = "取消" }; var input = new LineEdit { Text = _session.Document.Title, CustomMinimumSize = new Vector2(360, 40) }; dialog.AddChild(input); AddChild(dialog); dialog.Confirmed += () => { if (!string.IsNullOrWhiteSpace(input.Text)) { _session.Document.Title = input.Text.Trim(); _canvas.Commit(); } dialog.QueueFree(); }; dialog.Canceled += dialog.QueueFree; dialog.PopupCentered(); input.GrabFocus(); }
    /// <summary>进入导出流程，拒绝未完成的无效草稿。</summary>
    private void BeginExport(bool svg) { if (!_canvas.Finish()) return; _svg = svg; _semanticNames.Visible = svg; _exportOptions.Title = svg ? "导出 SVG 矢量图" : "导出 PNG 图片"; _exportOptions.PopupCentered(); }
    /// <summary>执行选择的导出。</summary>
    private async Task ExportTo(string path)
    {
        try
        {
            string ext = _svg ? ".svg" : ".png"; if (!path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) path += ext;
            if (_svg) LevelDocument.AtomicWrite(path, VectorExport.Svg(_session.Document, (int)_exportWidth.Value, _legend.ButtonPressed, _semanticNames.ButtonPressed));
            else await ExportPng(path, (int)_exportWidth.Value, _legend.ButtonPressed);
            Status("已导出：" + path);
        }
        catch (Exception e) { Error(e); }
    }
    /// <summary>通过独立视口导出完整关卡，不包含编辑手柄和网格。</summary>
    public async Task ExportPng(string path, int width, bool legend)
    {
        var doc = _session.Document; var bounds = LevelVisual.Bounds(doc); float scale = width / bounds.Size.X;
        var items = legend ? doc.Objects.Where(o => o.Preset.Length > 0).GroupBy(o => o.Preset).Select(g => g.First()).ToArray() : Array.Empty<LevelObject>();
        int drawingHeight = Math.Max(1, (int)Math.Ceiling(bounds.Size.Y * scale)); int height = drawingHeight + (items.Length > 0 ? 40 + items.Length * 28 : 0);
        if (height > 16384 || (long)width * height > 64000000) throw new InvalidOperationException("导出图片过大，请降低宽度或改用 SVG。");
        var viewport = new SubViewport { Size = new Vector2I(width, height), TransparentBg = false, RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Msaa2D = Viewport.Msaa.Msaa4X, Disable3D = true };
        AddChild(viewport); var bg = new ColorRect { Color = Colors.White, Size = new Vector2(width, height) }; viewport.AddChild(bg);
        var drawing = new Node2D { Scale = Vector2.One * scale, Position = -bounds.Position * scale }; viewport.AddChild(drawing);
        try
        {
            foreach (var o in doc.Objects) { var node = new LevelVisual(); drawing.AddChild(node); node.Configure(o, doc.Settings.ShowNames); }
            for (int i = 0; i < items.Length; i++)
            {
                var sample = items[i].Clone(); sample.Width = sample.Kind == "polyline" && sample.Preset == "道路" ? 5 : 1; sample.Diameter = 17;
                double y = drawingHeight + 28 + i * 28;
                sample.Points = sample.Kind == "points" ? new() { new[] { 26d, y } } : sample.Kind == "polyline" ? new() { new[] { 16d, y }, new[] { 36d, y } } : new() { new[] { 16d, y - 8 }, new[] { 36d, y - 8 }, new[] { 36d, y + 8 }, new[] { 16d, y + 8 } };
                var node = new LevelVisual(); viewport.AddChild(node); node.Configure(sample, false); var label = Text(items[i].Preset, 16); label.Position = new Vector2(48, (float)y - 12); viewport.AddChild(label);
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = viewport.GetTexture().GetImage(); if (image == null || image.IsEmpty()) throw new InvalidOperationException("无法获取导出图像，请在图形模式下运行。");
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { if (image.SavePng(temp) != Godot.Error.Ok) throw new IOException("无法写入 PNG 文件。"); if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path); } finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        finally { viewport.QueueFree(); }
    }
    /// <summary>记录最近文件，不复制用户原始工程。</summary>
    private void Remember(string path)
    { var items = _preferences.GetValue("projects", "recent", new string[0]).AsStringArray().Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).Prepend(path).Take(20).ToArray(); _preferences.SetValue("projects", "recent", items); _preferences.Save("user://settings.cfg"); }
    /// <summary>创建项目管理页。</summary>
    private void BuildProjects()
    {
        var page = Page("projects"); page.AddChild(Text("项目管理", 29)); page.AddChild(Text("打开已有的关卡布局，或从空白项目开始。旧版 .level.json 可直接编辑。", 14, "7d8798"));
        var actions = new HBoxContainer(); page.AddChild(actions); actions.AddChild(Button("新建关卡", NewProject, true)); actions.AddChild(Button("打开工程", () => _openDialog.PopupCenteredRatio(.72f))); actions.AddChild(Button("载入海滨街区示例", () => Guard(() => { _session.Reset(Presets.Demo()); _canvas.Cancel(); _canvas.Select(-1); _canvas.Sync(); ShowPage("layout"); _canvas.Fit(); Refresh(); })));
        page.AddChild(new HSeparator()); page.AddChild(Text("最近项目", 18)); var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; page.AddChild(scroll); _recentList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; scroll.AddChild(_recentList);
    }
    /// <summary>刷新最近文件，不删除失效路径对应的磁盘内容。</summary>
    private void RefreshRecent()
    {
        foreach (Node child in _recentList.GetChildren()) { _recentList.RemoveChild(child); child.QueueFree(); }
        var paths = _preferences.GetValue("projects", "recent", new string[0]).AsStringArray();
        if (paths.Length == 0) { _recentList.AddChild(Text("还没有保存的项目。完成绘制后保存工程，它会出现在这里。", 14, "7d8798")); return; }
        foreach (var path in paths)
        { var row = new HBoxContainer(); var detail = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; detail.AddChild(Text(System.IO.Path.GetFileNameWithoutExtension(path), 17)); detail.AddChild(Text(path, 12, "7d8798")); row.AddChild(detail); var open = Button(File.Exists(path) ? "打开编辑" : "文件已移动", () => Guard(() => OpenProject(path))); open.Disabled = !File.Exists(path); row.AddChild(open); _recentList.AddChild(Card(row)); }
    }
    /// <summary>创建使用指南，说明真实可用的操作。</summary>
    private void BuildHelp()
    {
        var page = Page("help"); page.AddChild(Text("使用指南", 29));
        var help = new RichTextLabel { BbcodeEnabled = true, SizeFlagsVertical = SizeFlags.ExpandFill };
        help.Text = "[b]从绘图到工程[/b]\n\n选择左侧点集、折线、多边形或预设，在画布左键逐点绘制。Enter、右键或双击完成；Backspace 回退草稿节点；Esc 取消。NPC 和刷怪点每次点击生成一个独立对象。\n\n[b]编辑与导航[/b]\n\n选择 / 编辑工具：拖动图形移动对象，拖动红色手柄修改节点，双击边插入节点。右侧也可直接输入两位小数的米制坐标。Delete 删除节点（不能少于最小节点数时删除对象），Ctrl+D 复制对象。中键拖动画布，滚轮以鼠标为中心缩放。\n\n[b]项目与导出[/b]\n\nCtrl+S 保存，Ctrl+Shift+S 另存为，Ctrl+O 打开，Ctrl+N 新建。Ctrl+Z 撤销，Ctrl+Y 或 Ctrl+Shift+Z 重做。工程为 .level.json；PNG 是预览图片；SVG 保留对象分组、名称、预设语义、图例和工程元数据。继续编辑请打开工程文件。\n\n[b]单位与辅助[/b]\n\n几何数据以米保存，界面统一显示两位小数。常用角吸附容差为 4.00°，最小角倍数优先。十字线、标尺、网格、名称可独立切换。网格随缩放分级显示。\n\n[b]文件保护[/b]\n\n保存使用同目录临时文件和原子替换；工程通过 .lock 侧车文件防止与旧版同时编辑。程序异常退出留下锁时，确认文件没有被其他程序编辑后再手动移除锁文件。\n\n[b]功能模块[/b]\n\n当前已集成关卡布局和物品配置。物品配置包含物品、词条、资源管理；支持分类、图标、配方和业务参数编辑，Ctrl+S 保存。物品数据位于 数据/物品配置，保存时自动保留 .bak 备份。项目管理、设置和导航由工具套件统一提供，后续模块可作为独立功能页接入。";
        page.AddChild(Card(help));
    }
    /// <summary>创建可保存的界面设置页。</summary>
    private void BuildSettings()
    {
        var page = Page("settings"); page.AddChild(Text("设置", 29)); var fields = new VBoxContainer(); page.AddChild(Card(fields)); fields.AddChild(Text("界面", 18));
        var size = Number(Theme.DefaultFontSize, 12, 20, " px"); size.Step = 1; size.CustomMinimumSize = new Vector2(180, 0); size.SizeFlagsHorizontal = SizeFlags.ShrinkBegin; Field(fields, "默认文字大小", size);
        size.ValueChanged += value => { Theme.DefaultFontSize = (int)value; _preferences.SetValue("ui", "font_size", (int)value); _preferences.Save("user://settings.cfg"); };
        var idle = new CheckBox { Text = "空闲时降低 CPU 占用", ButtonPressed = (bool)_preferences.GetValue("ui", "low_processor", true) }; OS.LowProcessorUsageMode = idle.ButtonPressed; idle.Toggled += enabled => { OS.LowProcessorUsageMode = enabled; _preferences.SetValue("ui", "low_processor", enabled); _preferences.Save("user://settings.cfg"); }; fields.AddChild(idle);
        fields.AddChild(Button("打开设置目录", () => OS.ShellOpen(ProjectSettings.GlobalizePath("user://")))); fields.AddChild(new HSeparator()); fields.AddChild(Text("游戏开发辅助工具  0.1.0", 16)); fields.AddChild(Text("项目文件保存在你选择的位置；最近列表和界面设置保存在本机。", 13, "7d8798"));
    }
    /// <summary>处理不会抢占文本编辑的全局快捷键。</summary>
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey k || !k.Pressed || k.Echo || (!k.CtrlPressed && !k.MetaPressed)) return;
        if (_page == "items") { if (k.Keycode == Key.S) _itemStudio.Save(); else if (k.Keycode == Key.N) _itemStudio.NewRecord(); else return; GetViewport().SetInputAsHandled(); return; }
        if (k.Keycode == Key.S) Save(k.ShiftPressed);
        else if (k.Keycode == Key.O) _openDialog.PopupCenteredRatio(.72f);
        else if (k.Keycode == Key.N) NewProject();
        else if (_page == "layout" && k.Keycode == Key.Z) _canvas.History(k.ShiftPressed);
        else if (_page == "layout" && k.Keycode == Key.Y) _canvas.History(true);
        else if (_page == "layout" && k.Keycode == Key.D) _canvas.Duplicate();
        else return; GetViewport().SetInputAsHandled();
    }
    /// <summary>关闭前检查草稿和修改。</summary>
    public override void _Notification(int what) { if (what == NotificationWMCloseRequest && _canvas != null) _itemStudio.Guard(() => Guard(() => GetTree().Quit())); }
    /// <summary>释放持久化锁。</summary>
    public override void _ExitTree() { StopMcp(); _session.Dispose(); }
    /// <summary>在真实引擎运行中执行必要的集成验证。</summary>
    public async void RunChecks()
    {
        try { if (OS.GetCmdlineUserArgs().Contains("--items-test")) await ItemStudioChecks.Run(this, _itemStudio); else if (OS.GetCmdlineUserArgs().Contains("--mcp-test")) await McpChecks.Run(this); else await SmokeChecks.Run(this, _session, _canvas, OS.GetCmdlineUserArgs().Contains("--visual-test")); GetTree().Quit(0); }
        catch (Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
}

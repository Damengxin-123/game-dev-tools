using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace GameDevTools;

/// <summary>物品配置独立页面，使用原生分类树、列表、表单及资源选择器。</summary>
public partial class ItemStudioPage : VBoxContainer
{
    /// <summary>打开的物品工作区，其他功能通过该实例访问同一份数据。</summary>
    public ItemStore? Store { get; private set; }
    /// <summary>当前独立编辑草稿。</summary>
    private JsonObject? _draft;
    /// <summary>当前草稿是否尚未保存。</summary>
    public bool Dirty { get; private set; }
    /// <summary>当前功能与筛选分类。</summary>
    private string _kind = "items", _category = "", _selected = "";
    /// <summary>分类树。</summary>
    private Tree _tree = null!;
    /// <summary>记录列表。</summary>
    private ItemList _list = null!;
    /// <summary>当前列表中的稳定标识。</summary>
    private readonly List<string> _ids = new();
    /// <summary>名称搜索输入。</summary>
    private LineEdit _search = null!;
    /// <summary>表单滚动内容。</summary>
    private VBoxContainer _form = null!;
    /// <summary>页面状态与错误提示。</summary>
    private Label _status = null!, _heading = null!;
    /// <summary>当前打开目录显示。</summary>
    private Label _directory = null!;
    /// <summary>缩略图缓存，同一资源只读取一次。</summary>
    private readonly Dictionary<string, Texture2D> _textures = new();
    /// <summary>界面重建期间忽略选择信号。</summary>
    private bool _refreshing;
    /// <summary>打开目录和导入图标对话框。</summary>
    private FileDialog _open = null!, _images = null!;
    /// <summary>当前类型按钮。</summary>
    private readonly Dictionary<string, Button> _tabs = new();

    /// <summary>创建页面并打开迁移后的本地数据目录。</summary>
    public override void _Ready()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill;
        var header = new HBoxContainer(); AddChild(header); header.AddChild(Label("物品配置", 27)); header.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        header.AddChild(Button("打开数据目录", () => _open.PopupCenteredRatio(.7f))); header.AddChild(Button("在文件夹中查看", () => { if (Store != null) OS.ShellOpen(Store.DirectoryPath); }));
        _directory = Label("", 12); _directory.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; AddChild(_directory);
        var bar = new HBoxContainer(); AddChild(bar);
        foreach (var (kind, title) in new[] { ("items", "物品配置"), ("affixes", "词条配置"), ("assets", "资源管理") })
        { var button = Button(title, () => Guard(() => { _kind = kind; _category = ""; _selected = ""; _draft = null; Refresh(); })); button.ToggleMode = true; _tabs[kind] = button; bar.AddChild(button); }
        bar.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill }); bar.AddChild(Button("导入图标", () => _images.PopupCenteredRatio(.7f)));
        var body = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SplitOffsets = [0] }; AddChild(body);
        var browse = new VBoxContainer { CustomMinimumSize = new Vector2(330, 0), SizeFlagsHorizontal = SizeFlags.Fill }; body.AddChild(browse);
        _search = new LineEdit { PlaceholderText = "搜索名称或分类…" }; browse.AddChild(_search); _search.TextChanged += _ => RefreshList();
        var split = new VSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SplitOffsets = [220] }; browse.AddChild(split);
        var categories = new VBoxContainer { CustomMinimumSize = new Vector2(0, 130) }; split.AddChild(categories);
        _tree = new ItemCategoryTree { HideRoot = true, SizeFlagsVertical = SizeFlags.ExpandFill, CanMove = (id, path) => Store?.Find(_kind, id) != null && (_kind != "items" || path is not ("物品" or "物品.装备")), MoveRecord = MoveRecord }; categories.AddChild(_tree);
        _tree.ItemSelected += () => { if (!_refreshing) { _category = _tree.GetSelected()?.GetMetadata(0).AsString() ?? ""; RefreshList(); } };
        var actions = new HBoxContainer(); categories.AddChild(actions); actions.AddChild(Button("子分类 +", () => CategoryDialog("add"))); actions.AddChild(Button("改名", () => CategoryDialog("rename"))); actions.AddChild(Button("删除分类", () => CategoryDialog("delete")));
        _list = new ItemRecordList { ResolveId = index => index < _ids.Count ? _ids[index] : "", SizeFlagsVertical = SizeFlags.ExpandFill, FixedIconSize = new Vector2I(36, 36), CustomMinimumSize = new Vector2(0, 150), TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis }; split.AddChild(_list);
        _list.ItemSelected += index => { if (!_refreshing && index >= 0 && index < _ids.Count) { string id = _ids[(int)index]; Guard(() => SelectRecord(id)); RestoreSelection(); } };
        var recordActions = new HBoxContainer(); browse.AddChild(recordActions); recordActions.AddChild(Button("新建", NewRecord)); recordActions.AddChild(Button("复制", Duplicate)); recordActions.AddChild(Button("删除", DeleteRecord));
        var editor = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(430, 0) }; body.AddChild(editor);
        var tools = new HBoxContainer(); editor.AddChild(tools); _heading = Label("选择一条配置", 20); _heading.SizeFlagsHorizontal = SizeFlags.ExpandFill; _heading.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; tools.AddChild(_heading);
        tools.AddChild(Button("放弃修改", () => Guard(() => SelectRecord(_selected)))); tools.AddChild(Button("保存配置", () => Save()));
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; editor.AddChild(scroll);
        _form = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; _form.AddThemeConstantOverride("separation", 9); scroll.AddChild(_form);
        _status = Label("", 12); _status.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; AddChild(_status);
        _open = new FileDialog { Title = "选择 ItemStudio 数据目录", FileMode = FileDialog.FileModeEnum.OpenDir, Access = FileDialog.AccessEnum.Filesystem }; AddChild(_open); _open.DirSelected += path => Guard(() => OpenDirectory(path));
        _images = new FileDialog { Title = "导入物品图标", FileMode = FileDialog.FileModeEnum.OpenFiles, Access = FileDialog.AccessEnum.Filesystem, Filters = ["*.png,*.jpg,*.jpeg,*.webp,*.bmp ; 图标图片"] }; AddChild(_images);
        _images.FilesSelected += paths => { if (Store == null) return; PickCategory("assets", "选择图标导入分类", _kind == "assets" && _category != "" ? _category : "通用", category => Try(() => { Store.ImportImages(paths, category); Refresh(); })); };
        string root = OS.HasFeature("editor") ? ProjectSettings.GlobalizePath("res://") : Path.GetDirectoryName(OS.GetExecutablePath())!;
        string data = Path.Combine(root, "数据", "物品配置");
        if (!OS.HasFeature("editor") && !Directory.Exists(data)) data = Path.Combine(root, "..", "数据", "物品配置");
        var args = OS.GetCmdlineUserArgs(); int arg = System.Array.IndexOf(args, "--item-data"); if (arg >= 0 && arg + 1 < args.Length) data = args[arg + 1];
        OpenDirectory(data);
    }
    /// <summary>构造统一中文文本。</summary>
    private static Label Label(string text, int size = 14) { var label = new Label { Text = text }; label.AddThemeFontSizeOverride("font_size", size); return label; }
    /// <summary>构造原生按钮。</summary>
    private static Button Button(string text, Action action) { var b = new Button { Text = text }; b.Pressed += action; return b; }
    /// <summary>清空布局并延迟释放旧控件。</summary>
    private static void Clear(Node parent) { foreach (Node child in parent.GetChildren()) { parent.RemoveChild(child); child.QueueFree(); } }
    /// <summary>在页面中报告可恢复错误。</summary>
    private void Try(Action action) { try { action(); } catch (Exception e) { Report(e.Message, true); } }
    /// <summary>显示操作结果，错误同时弹出以避免被截断。</summary>
    private void Report(string message, bool error = false)
    {
        _status.Text = message; _status.TooltipText = message;
        if (error) { var dialog = new AcceptDialog { Title = "物品配置", DialogText = message, OkButtonText = "知道了" }; AddChild(dialog); dialog.Confirmed += dialog.QueueFree; dialog.Canceled += dialog.QueueFree; dialog.PopupCentered(new Vector2I(580, 180)); }
    }
    /// <summary>在新目录成功打开后释放旧目录，失败保留原有草稿。</summary>
    public void OpenDirectory(string path)
    {
        Try(() =>
        {
            if (Store != null && string.Equals(Store.DirectoryPath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) return;
            var candidate = new ItemStore(path); Store?.Dispose(); Store = candidate;
            foreach (var texture in _textures.Values) texture.Dispose(); _textures.Clear();
            _draft = null; _selected = ""; _category = ""; Dirty = false; _directory.Text = Store.DirectoryPath; _directory.TooltipText = Store.DirectoryPath; Refresh();
        });
    }
    /// <summary>刷新分类与列表，保留尚未保存的草稿。</summary>
    public void Refresh()
    {
        foreach (var (kind, button) in _tabs) button.ButtonPressed = kind == _kind;
        if (Store == null) return;
        _refreshing = true; _tree.Clear(); var root = _tree.CreateItem(); var all = _tree.CreateItem(root); all.SetText(0, "全部分类"); all.SetMetadata(0, "");
        var nodes = new Dictionary<string, TreeItem>();
        foreach (string path in Store.Categories(_kind).OrderBy(p => p.Count(c => c == '.')).ThenBy(p => p, StringComparer.Ordinal))
        {
            int dot = path.LastIndexOf('.'); var node = _tree.CreateItem(dot < 0 ? root : nodes[path[..dot]]); nodes[path] = node; node.SetText(0, path[(dot + 1)..]); node.SetTooltipText(0, path); node.SetMetadata(0, path);
            node.Collapsed = dot >= 0 && !ItemStore.Within(_category, path); if (_category == path) node.Select(0);
        }
        if (_category == "") all.Select(0); _refreshing = false; RefreshList(); RenderForm();
        Report($"{Store.Rows("items").Count()} 个物品 · {Store.Rows("affixes").Count()} 个词条 · {Store.Rows("assets").Count()} 个图标 · 修订 {Store.Revision}");
    }
    /// <summary>只刷新搜索结果，不触碰当前编辑草稿。</summary>
    private void RefreshList()
    {
        if (Store == null) return; _refreshing = true; _list.Clear(); _ids.Clear();
        foreach (var row in Store.Rows(_kind).Where(r => (_category == "" || ItemStore.Within(ItemStore.Text(r, "category"), _category)) && (ItemStore.Text(r, "name") + ItemStore.Text(r, "category")).Contains(_search.Text, StringComparison.OrdinalIgnoreCase)).OrderBy(r => ItemStore.Text(r, "name"), StringComparer.CurrentCulture))
        {
            string id = ItemStore.Text(row, "id"); _ids.Add(id); _list.AddItem(ItemStore.Text(row, "name"), Icon(_kind == "assets" ? id : ItemStore.Text(row, "iconId"))); _list.SetItemTooltip(_ids.Count - 1, ItemStore.Text(row, "category"));
        }
        RestoreSelection(); _refreshing = false;
    }
    /// <summary>保持列表选中项与当前编辑草稿一致。</summary>
    private void RestoreSelection() { _list.DeselectAll(); int i = _ids.IndexOf(_selected); if (i >= 0) _list.Select(i); }
    /// <summary>从工作区读取缩略图并复用缓存。</summary>
    private Texture2D? Icon(string id)
    {
        if (id == "" || Store == null) return null; if (_textures.TryGetValue(id, out var texture)) return texture;
        var row = Store.Find("assets", id); if (row == null) return null;
        try { string path = Store.AssetPath(ItemStore.Text(row, "thumbnail")); if (!File.Exists(path)) return null; using var image = Image.LoadFromFile(path); if (image == null || image.IsEmpty()) return null; return _textures[id] = ImageTexture.CreateFromImage(image); } catch { return null; }
    }
    /// <summary>选中记录并创建独立编辑副本。</summary>
    public void SelectRecord(string id)
    {
        _selected = id; _draft = Store?.Find(_kind, id)?.DeepClone() as JsonObject; Dirty = false; RenderForm(); RestoreSelection(); _list.EnsureCurrentIsVisible();
    }
    /// <summary>标记当前草稿改动。</summary>
    private void Changed() { Dirty = true; _heading.Text = (ItemStore.Text(_draft, "name") is { Length: > 0 } name ? name : "新配置") + " *"; }
    /// <summary>添加带中文标签的输入行。</summary>
    private void Field(string title, Control control) { _form.AddChild(Label(title, 12)); control.SizeFlagsHorizontal = SizeFlags.ExpandFill; _form.AddChild(control); }
    /// <summary>绑定文本输入，保持未修改字段和源记录完整。</summary>
    private void TextField(JsonObject row, string key, string title, bool multiline = false)
    {
        if (multiline) { var input = new TextEdit { Text = ItemStore.Text(row, key), CustomMinimumSize = new Vector2(0, 90), WrapMode = TextEdit.LineWrappingMode.Boundary }; input.TextChanged += () => { row[key] = input.Text; Changed(); }; Field(title, input); }
        else { var input = new LineEdit { Text = ItemStore.Text(row, key), MaxLength = 240 }; input.TextChanged += value => { row[key] = value; Changed(); }; Field(title, input); }
    }
    /// <summary>绑定双精度数值，统一使用两位小数或整数数量。</summary>
    private Control Number(JsonObject row, string key, double min, double max, bool integer = false, string suffix = "", double fallback = 0)
    {
        var input = new DecimalSpinBox { MinValue = min, MaxValue = max, Step = integer ? 1 : .01, Value = row[key] is JsonNode number ? ItemStore.Numeric(number) : fallback, Suffix = suffix, CustomMinimumSize = new Vector2(100, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill, UpdateOnTextChanged = true };
        input.ValueChanged += value => { row[key] = integer ? Math.Round(value) : Math.Round(value, 2); Changed(); }; return input;
    }
    /// <summary>绑定固定候选列表，不允许写入无效枚举。</summary>
    private OptionButton Choice(string value, IEnumerable<(string Id, string Name)> choices, Action<string> changed)
    {
        var input = new OptionButton { FitToLongestItem = false, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(100, 32) }; var values = choices.ToList();
        foreach (var choice in values) { input.AddItem(choice.Name); if (choice.Id == value) input.Select(input.ItemCount - 1); }
        input.ItemSelected += i => changed(values[(int)i].Id); return input;
    }
    /// <summary>绑定同名枚举。</summary>
    private OptionButton Choice(JsonObject row, string key, IEnumerable<string> values, Action? after = null) => Choice(ItemStore.Text(row, key), values.Select(v => (v, v)), value => { row[key] = value; Changed(); after?.Invoke(); });
    /// <summary>重建当前记录的业务表单。</summary>
    private void RenderForm()
    {
        Clear(_form); _heading.Text = _draft == null ? "选择一条配置" : (ItemStore.Text(_draft, "name") is { Length: > 0 } n ? n : "新配置") + (Dirty ? " *" : "");
        if (_draft == null || Store == null) { _form.AddChild(Label("选择左侧记录，或点击“新建”开始配置。")); return; }
        TextField(_draft, "name", "名称");
        var category = Button(ItemStore.Text(_draft, "category"), () => PickCategory(_kind, "选择所属分类", ItemStore.Text(_draft, "category"), path => { _draft["category"] = path; if (_kind == "items" && !ItemStore.Within(path, "物品.装备")) _draft["allowAffixAdjustment"] = false; Changed(); RenderForm(); })); category.ClipText = true; Field("所属分类", category);
        if (_kind != "assets") Field("稀有度", Choice(_draft, "rarity", ItemStore.Rarities));
        if (_kind == "items") RenderItem(); else if (_kind == "affixes") RenderAffix(); else RenderAsset();
        if (_kind != "assets") { TextField(_draft, "description", "说明", true); if (_kind == "affixes") TextField(_draft, "displayTemplate", "显示文案模板", true); RenderParameters(); }
        if (_draft["source"] is JsonObject source)
        { var details = new RichTextLabel { Text = "来源信息（保留原始字段）\n" + source.ToJsonString(LevelDocument.Options), FitContent = true, SelectionEnabled = true, CustomMinimumSize = new Vector2(0, 70) }; _form.AddChild(details); }
    }
    /// <summary>绘制物品价格、图标、装备词条和制作配方。</summary>
    private void RenderItem()
    {
        var d = _draft!; var preview = new TextureRect { Texture = Icon(ItemStore.Text(d, "iconId")), CustomMinimumSize = new Vector2(100, 100), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered }; _form.AddChild(preview);
        var icons = new HBoxContainer(); _form.AddChild(icons); icons.AddChild(Button("选择图标", () => PickRecord("assets", "选择物品图标", id => { d["iconId"] = id; Changed(); RenderForm(); }, true))); icons.AddChild(Button("移除图标", () => { d["iconId"] = ""; Changed(); RenderForm(); }));
        Field("价值（金币）", Number(d, "valueGold", 0, 1e9)); Field("堆叠上限", Number(d, "stackLimit", 0, 1e9, true));
        if (ItemStore.Within(ItemStore.Text(d, "category"), "物品.装备")) { var check = new CheckBox { Text = "允许调整装备的普通词条", ButtonPressed = d["allowAffixAdjustment"]!.GetValue<bool>() }; check.Toggled += v => { d["allowAffixAdjustment"] = v; Changed(); }; _form.AddChild(check); }
        _form.AddChild(new HSeparator()); _form.AddChild(Label("物品词条", 18));
        var affixes = ItemStore.Array(d, "affixes");
        foreach (var reference in affixes.OfType<JsonObject>().ToArray())
        {
            _form.AddChild(Label(ItemStore.Text(Store!.Find("affixes", ItemStore.Text(reference, "affixId")), "name", "缺失词条")));
            var row = new HBoxContainer(); _form.AddChild(row); row.AddChild(Choice(reference, "kind", ["基础", "普通"])); row.AddChild(Number(reference, "multiplier", -1e8, 1e8, suffix: " 倍", fallback: 1)); row.AddChild(Number(reference, "periodSeconds", 0, 1e8, suffix: " 秒")); row.AddChild(Button("移除", () => { affixes.Remove(reference); Changed(); RenderForm(); }));
        }
        _form.AddChild(Button("添加词条", () => PickRecord("affixes", "选择共享词条", id => { if (affixes.Any(a => ItemStore.Text(a, "affixId") == id)) { Report("该词条已经存在。", true); return; } affixes.Add(new JsonObject { ["affixId"] = id, ["kind"] = "普通", ["multiplier"] = 1, ["periodSeconds"] = 0 }); Changed(); RenderForm(); })));
        _form.AddChild(new HSeparator()); _form.AddChild(Label("制作配方 · 每份产出 1 个当前物品", 18)); var recipe = ItemStore.Array(d, "recipe");
        foreach (var component in recipe.OfType<JsonObject>().ToArray())
        { var row = new HBoxContainer(); _form.AddChild(row); var name = Label(ItemStore.Text(Store!.Find("items", ItemStore.Text(component, "itemId")), "name")); name.SizeFlagsHorizontal = SizeFlags.ExpandFill; name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; row.AddChild(name); row.AddChild(Number(component, "quantity", 1, 1e9, true)); row.AddChild(Button("移除", () => { recipe.Remove(component); Changed(); RenderForm(); })); }
        _form.AddChild(Button("添加制作材料", () => PickRecord("items", "选择配方材料", id => { if (id == ItemStore.Text(d, "id") || recipe.Any(r => ItemStore.Text(r, "itemId") == id)) { Report("不能添加自身或重复材料。", true); return; } recipe.Add(new JsonObject { ["itemId"] = id, ["quantity"] = 1 }); Changed(); RenderForm(); })));
    }
    /// <summary>绘制词条类型及对应的属性效果。</summary>
    private void RenderAffix()
    {
        var d = _draft!; string effect = ItemStore.Text(d, "effectType", "AttributeGain");
        if (Store!.Find("affixes", ItemStore.Text(d, "id")) != null || effect is not ("AttributeGain" or "AttributeRecovery"))
            Field("效果类型（创建其他业务效果时可复制同类型词条）", Label(ItemStore.Effects.GetValueOrDefault(effect, effect)));
        else Field("效果类型", Choice(effect, ItemStore.Effects.Where(p => p.Key is "AttributeGain" or "AttributeRecovery").Select(p => (p.Key, p.Value)), value =>
        {
            d["effectType"] = value;
            if (value is "AttributeGain" or "AttributeRecovery") { d["attribute"] = "属性.战斗.攻击力"; d["target"] = value == "AttributeRecovery" ? "当前值" : "属性值"; d["operation"] = value == "AttributeRecovery" ? "固定恢复" : "固定增减"; d["value"] ??= JsonValue.Create(0); }
            Changed(); RenderForm();
        }));
        if (effect is not ("AttributeGain" or "AttributeRecovery")) return;
        var attributes = ItemStore.Attributes(Store!.Database);
        Field("作用的角色属性", Choice(ItemStore.Text(d, "attribute"), attributes.Select(a => (ItemStore.Text(a, "id"), ItemStore.Text(a, "group") + " / " + ItemStore.Text(a, "name"))), id =>
        { d["attribute"] = id; d["target"] = effect == "AttributeRecovery" ? "当前值" : attributes.First(a => ItemStore.Text(a, "id") == id)["targets"]![0]!.GetValue<string>(); Changed(); RenderForm(); }));
        var attribute = attributes.FirstOrDefault(a => ItemStore.Text(a, "id") == ItemStore.Text(d, "attribute"));
        string[] targets = effect == "AttributeRecovery" ? ["当前值"] : (attribute?["targets"] as JsonArray)?.Select(t => t!.GetValue<string>()).ToArray() ?? ["属性值"];
        Field("修改字段", Choice(d, "target", targets)); Field("修改方式", Choice(d, "operation", effect == "AttributeRecovery" ? ItemStore.RecoveryOperations : ItemStore.Operations));
        Field("效果数值（百分比填写 10 表示 10%）", Number(d, "value", -1e8, 1e8));
    }
    /// <summary>展示本地资源和引用它的物品。</summary>
    private void RenderAsset()
    {
        _form.AddChild(new TextureRect { Texture = Icon(ItemStore.Text(_draft, "id")), CustomMinimumSize = new Vector2(180, 180), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered });
        _form.AddChild(Label($"原图尺寸：{_draft!["width"]} × {_draft["height"]}"));
        _form.AddChild(Button("查看原图", () => Try(() => OS.ShellOpen(Store!.AssetPath(ItemStore.Text(_draft, "file"))))));
        var refs = Store!.Rows("items").Where(r => ItemStore.Text(r, "iconId") == ItemStore.Text(_draft, "id")).Select(r => ItemStore.Text(r, "name"));
        var label = Label("引用物品：" + string.Join("、", refs)); label.AutowrapMode = TextServer.AutowrapMode.WordSmart; _form.AddChild(label);
    }
    /// <summary>按导入的真实参数定义生成业务控件，只修改参数值。</summary>
    private void RenderParameters()
    {
        if (_draft!["parameters"] is not JsonArray parameters) return;
        foreach (var parameter in parameters.OfType<JsonObject>())
        {
            string type = ItemStore.Text(parameter, "type"), title = ItemStore.Text(parameter, "label");
            if (type == "number") Field(title, Number(parameter, "value", -1e12, 1e12));
            else if (type == "boolean") { var check = new CheckBox { Text = title, ButtonPressed = parameter["value"]!.GetValue<bool>() }; check.Toggled += v => { parameter["value"] = v; Changed(); }; _form.AddChild(check); }
            else if (type is "item" or "affix")
            {
                string kind = type == "item" ? "items" : "affixes"; var row = new HBoxContainer(); var button = Button(ItemStore.Text(Store!.Find(kind, ItemStore.Text(parameter, "value")), "name", "选择引用"), () => PickRecord(kind, title, id => { parameter["value"] = id; Changed(); RenderForm(); })); button.SizeFlagsHorizontal = SizeFlags.ExpandFill; button.ClipText = true; row.AddChild(button); row.AddChild(Button("清空", () => { parameter["value"] = ""; Changed(); RenderForm(); })); Field(title, row);
            }
            else if (type == "attribute") Field(title, Choice(ItemStore.Text(parameter, "value"), new[] { ("", "未设置") }.Concat(ItemStore.Attributes(Store!.Database).Select(a => (ItemStore.Text(a, "id"), ItemStore.Text(a, "name")))), id => { parameter["value"] = id; Changed(); }));
            else if (parameter["options"] is JsonArray options && options.Count > 0) Field(title, Choice(ItemStore.Text(parameter, "value"), new[] { ("", "未设置") }.Concat(options.Select(o => o is JsonObject ? (ItemStore.Text(o, "id"), ItemStore.Text(o, "name", ItemStore.Text(o, "id"))) : (o!.GetValue<string>(), o.GetValue<string>()))), v => { parameter["value"] = v; Changed(); }));
            else TextField(parameter, "value", title);
        }
    }
    /// <summary>创建可搜索、可按分类筛选的原生记录选择器。</summary>
    private void PickRecord(string kind, string title, Action<string> selected, bool filterIcons = false)
    {
        if (Store == null) return;
        var dialog = new ConfirmationDialog { Title = title, OkButtonText = "使用选中项", CancelButtonText = "取消" }; AddChild(dialog);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(550, 430) }; dialog.AddChild(box); var search = new LineEdit { PlaceholderText = "搜索名称或分类" }; box.AddChild(search);
        var compatible = new CheckBox { Text = "仅显示当前物品分类及通用图标", ButtonPressed = filterIcons, Visible = filterIcons }; box.AddChild(compatible);
        var list = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill, FixedIconSize = new Vector2I(48, 48) }; box.AddChild(list); var ids = new List<string>();
        // 筛选候选记录时仅更新列表，绝不覆盖当前草稿。
        void Fill()
        {
            list.Clear(); ids.Clear();
            foreach (var row in Store.Rows(kind).Where(r => (ItemStore.Text(r, "name") + ItemStore.Text(r, "category")).Contains(search.Text, StringComparison.OrdinalIgnoreCase)))
            {
                string category = ItemStore.Text(row, "category"); if (compatible.ButtonPressed && !ItemStore.Within(category, "通用") && !ItemStore.Within(ItemStore.Text(_draft, "category"), category)) continue;
                string id = ItemStore.Text(row, "id"); ids.Add(id); list.AddItem(ItemStore.Text(row, "name") + "  ·  " + category, Icon(kind == "assets" ? id : ItemStore.Text(row, "iconId")));
            }
            dialog.GetOkButton().Disabled = true;
        }
        search.TextChanged += _ => Fill(); compatible.Toggled += _ => Fill(); list.ItemSelected += _ => dialog.GetOkButton().Disabled = false;
        dialog.Confirmed += () => { var indexes = list.GetSelectedItems(); if (indexes.Length > 0) selected(ids[indexes[0]]); dialog.QueueFree(); }; dialog.Canceled += dialog.QueueFree; Fill(); dialog.PopupCentered(); search.GrabFocus();
    }
    /// <summary>创建带搜索的分类选择器。</summary>
    private void PickCategory(string kind, string title, string current, Action<string> selected)
    {
        if (Store == null) return; var dialog = new ConfirmationDialog { Title = title, OkButtonText = "选择", CancelButtonText = "取消" }; AddChild(dialog);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(500, 360) }; dialog.AddChild(box); var search = new LineEdit { PlaceholderText = "搜索完整分类路径" }; box.AddChild(search); var list = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill }; box.AddChild(list); var paths = new List<string>();
        // 刷新合法分类候选，同时避免物品落到不可用根节点。
        void Fill() { list.Clear(); paths.Clear(); foreach (string path in Store.Categories(kind).Where(p => p.Contains(search.Text, StringComparison.OrdinalIgnoreCase) && (kind != "items" || p is not ("物品" or "物品.装备")))) { paths.Add(path); list.AddItem(path); if (path == current) list.Select(paths.Count - 1); } dialog.GetOkButton().Disabled = list.GetSelectedItems().Length == 0; }
        search.TextChanged += _ => Fill(); list.ItemSelected += _ => dialog.GetOkButton().Disabled = false; dialog.Confirmed += () => { var indexes = list.GetSelectedItems(); if (indexes.Length > 0) selected(paths[indexes[0]]); dialog.QueueFree(); }; dialog.Canceled += dialog.QueueFree; Fill(); dialog.PopupCentered();
    }
    /// <summary>创建新物品或词条；资源通过导入生成。</summary>
    public void NewRecord()
    {
        if (Store == null) return; if (_kind == "assets") { _images.PopupCenteredRatio(.7f); return; }
        Guard(() => { string category = _category; if (category == "" || (_kind == "items" && category is "物品" or "物品.装备")) category = _kind == "items" ? "物品.其他" : "词条.属性加成"; _draft = ItemStore.NewRecord(_kind, category); _selected = ""; Changed(); RenderForm(); RestoreSelection(); });
    }
    /// <summary>复制完整记录，分配新标识并保留业务参数。</summary>
    private void Duplicate()
    {
        if (_draft == null || Store == null) return; if (_kind == "assets") { Report("图标资源请使用导入功能。"); return; }
        Guard(() => { if (_draft == null) return; _draft = (JsonObject)_draft.DeepClone(); _draft["id"] = Guid.NewGuid().ToString(); string name = ItemStore.Text(_draft, "name") + " 副本", candidate = name; int suffix = 2; while (Store.Rows(_kind).Any(r => ItemStore.Text(r, "name") == candidate && ItemStore.Text(r, "category") == ItemStore.Text(_draft, "category"))) candidate = name + " " + suffix++; _draft["name"] = candidate; _selected = ""; Changed(); RenderForm(); RestoreSelection(); });
    }
    /// <summary>保存整个草稿；校验失败时保持编辑内容。</summary>
    public bool Save()
    {
        if (Store == null || _draft == null) return !Dirty;
        try { Store.Save(_kind, _draft); _selected = ItemStore.Text(_draft, "id"); Dirty = false; Refresh(); Report("已保存配置 · 修订 " + Store.Revision); return true; } catch (Exception e) { Report(e.Message, true); return false; }
    }
    /// <summary>在离开或替换草稿前提供保存、放弃和取消。</summary>
    public void Guard(Action next)
    {
        if (!Dirty) { next(); return; }
        var dialog = new ConfirmationDialog { Title = "保存物品配置修改？", DialogText = "当前物品、词条或资源存在未保存修改。", OkButtonText = "保存并继续", CancelButtonText = "取消" }; dialog.AddButton("放弃修改", false, "discard"); AddChild(dialog);
        dialog.Confirmed += () => { if (Save()) next(); dialog.QueueFree(); }; dialog.CustomAction += _ => { Dirty = false; _draft = Store?.Find(_kind, _selected)?.DeepClone() as JsonObject; RenderForm(); next(); dialog.QueueFree(); }; dialog.Canceled += dialog.QueueFree; dialog.PopupCentered(new Vector2I(500, 180));
    }
    /// <summary>删除记录需要明确确认，引用保护由仓库执行。</summary>
    private void DeleteRecord()
    {
        if (Store == null || _selected == "") return; Guard(() =>
        {
            var dialog = new ConfirmationDialog { Title = "删除配置", DialogText = "删除“" + ItemStore.Text(_draft, "name") + "”？被引用的记录不能删除。", OkButtonText = "删除", CancelButtonText = "取消" }; AddChild(dialog);
            dialog.Confirmed += () => { Try(() => { Store.Delete(_kind, _selected); _draft = null; _selected = ""; Refresh(); }); dialog.QueueFree(); }; dialog.Canceled += dialog.QueueFree; dialog.PopupCentered();
        });
    }
    /// <summary>分类维护沿用仓库校验，并在改名前处理编辑草稿。</summary>
    private void CategoryDialog(string action)
    {
        if (Store == null || _category == "") { Report("请先选择一个具体分类。", true); return; }
        Guard(() =>
        {
            var dialog = new ConfirmationDialog { Title = action == "add" ? "添加子分类" : action == "rename" ? "重命名分类" : "删除空分类", OkButtonText = "确定", CancelButtonText = "取消" }; var input = new LineEdit { PlaceholderText = "只填写这一层的中文名称", Text = action == "rename" ? _category.Split('.')[^1] : "", CustomMinimumSize = new Vector2(400, 40) }; if (action == "delete") dialog.DialogText = "删除空分类：" + _category; else dialog.AddChild(input); AddChild(dialog);
            dialog.Confirmed += () => { Try(() => { Store.EditCategory(_kind, action, _category, input.Text.Trim()); _category = ""; _draft = Store.Find(_kind, _selected)?.DeepClone() as JsonObject; Refresh(); }); dialog.QueueFree(); }; dialog.Canceled += dialog.QueueFree; dialog.PopupCentered();
        });
    }
    /// <summary>服务写入后同步无草稿的选中记录。</summary>
    public void ReloadSavedSelection() { if (!Dirty) _draft = Store?.Find(_kind, _selected)?.DeepClone() as JsonObject; Refresh(); }
    /// <summary>拖动记录到分类后立即保存；移动其他记录时保留当前草稿。</summary>
    private void MoveRecord(string id, string category)
    {
        // 获取最新已保存记录，避免保存提醒完成后使用过期副本。
        void Move() => Try(() => { var row = (JsonObject)(Store!.Find(_kind, id) ?? throw new InvalidOperationException("记录不存在。")).DeepClone(); row["category"] = category; if (_kind == "items" && !ItemStore.Within(category, "物品.装备")) row["allowAffixAdjustment"] = false; Store.Save(_kind, row); ReloadSavedSelection(); });
        if (id == _selected) Guard(Move); else Move();
    }
    /// <summary>释放数据锁与图标缓存。</summary>
    public override void _ExitTree() { Store?.Dispose(); foreach (var texture in _textures.Values) texture.Dispose(); _textures.Clear(); }
}

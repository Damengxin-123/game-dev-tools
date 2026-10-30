using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GameDevTools;

/// <summary>原生二维编辑视口；背景、正式图形和交互覆盖层分别更新。</summary>
public partial class LevelCanvas : Control
{
    /// <summary>所属文档会话。</summary>
    public DocumentSession Session { get; set; } = null!;
    /// <summary>选中对象索引。</summary>
    public int Selected { get; private set; } = -1;
    /// <summary>当前编辑顶点。</summary>
    public int Vertex { get; set; } = -1;
    /// <summary>当前工具。</summary>
    public string Tool { get; private set; } = "select";
    /// <summary>新图形样式。</summary>
    public LevelObject Style { get; private set; } = new();
    /// <summary>独立于正式文档的草稿。</summary>
    public LevelObject Draft { get; private set; } = new();
    /// <summary>是否存在未完成草稿。</summary>
    public bool HasDraft => Draft.Points.Count > 0;
    /// <summary>用户是否正在拖动对象或视口。</summary>
    public bool IsInteracting => _dragging || _panning;
    /// <summary>文档改变后的通知。</summary>
    public event Action? Changed;
    /// <summary>选择改变后的通知。</summary>
    public event Action? SelectionChanged;
    /// <summary>用户提示通知。</summary>
    public event Action<string>? Message;
    /// <summary>状态栏坐标和比例通知。</summary>
    public event Action<string>? PointerInfo;
    /// <summary>本地批量拾取场景。</summary>
    private NativeGeometry _geometry = null!;
    /// <summary>共享视口变换的对象层。</summary>
    private Node2D _world = new();
    /// <summary>独立交互覆盖层。</summary>
    private CanvasOverlay _overlay = null!;
    /// <summary>稳定 ID 对应的显示节点和内容指纹。</summary>
    private readonly Dictionary<string, (LevelVisual Node, string Key)> _visuals = new();
    /// <summary>草稿显示节点。</summary>
    private LevelVisual _preview = new();
    /// <summary>米到屏幕像素的缩放。</summary>
    public float Zoom { get; private set; } = 10;
    /// <summary>原点的屏幕位置。</summary>
    private Vector2 _origin = new(60, 60);
    /// <summary>当前鼠标位置。</summary>
    private Vector2 _mouse;
    /// <summary>拖动起点的世界坐标。</summary>
    private Vector2 _dragStart;
    /// <summary>拖动前的稳定对象数据。</summary>
    private LevelObject? _beforeDrag;
    /// <summary>当前是否拖动画布。</summary>
    private bool _panning;
    /// <summary>当前是否拖动对象。</summary>
    private bool _dragging;
    /// <summary>初始化三层视口。</summary>
    public override void _Ready()
    {
        FocusMode = FocusModeEnum.All; ClipContents = true; MouseDefaultCursorShape = CursorShape.Cross;
        _geometry = new NativeGeometry(); AddChild(_world); _world.AddChild(_preview); _preview.Modulate = new Color(1, 1, 1, .65f);
        _overlay = new CanvasOverlay { Canvas = this, MouseFilter = MouseFilterEnum.Ignore }; AddChild(_overlay); _overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Resized += UpdateTransform; Sync();
    }
    /// <summary>同步文档，仅重新构建内容实际变化的对象。</summary>
    public void Sync()
    {
        var doc = Session.Document; var ids = doc.Objects.Select(o => o.Id).ToHashSet();
        foreach (var id in _visuals.Keys.Where(k => !ids.Contains(k)).ToArray()) { _visuals[id].Node.QueueFree(); _visuals.Remove(id); }
        for (int i = 0; i < doc.Objects.Count; i++)
        {
            var o = doc.Objects[i]; string key = System.Text.Json.JsonSerializer.Serialize(o, LevelDocument.Options) + doc.Settings.ShowNames;
            if (!_visuals.TryGetValue(o.Id, out var entry)) { entry = (new LevelVisual(), ""); _world.AddChild(entry.Node); }
            if (entry.Key != key) { entry.Node.Configure(o, doc.Settings.ShowNames); _visuals[o.Id] = (entry.Node, key); }
            _world.MoveChild(entry.Node, i);
        }
        _world.MoveChild(_preview, _world.GetChildCount() - 1); _geometry.Sync(doc);
        if (Selected >= doc.Objects.Count) Selected = -1;
        UpdateTransform();
    }
    /// <summary>刷新当前对象而不重建整个场景。</summary>
    public void RefreshSelected()
    { if (Selected >= 0 && Selected < Session.Document.Objects.Count) { var o = Session.Document.Objects[Selected]; if (_visuals.TryGetValue(o.Id, out var v)) v.Node.Configure(o, Session.Document.Settings.ShowNames); } _overlay.QueueRedraw(); }
    /// <summary>提交一次编辑操作。</summary>
    public void Commit() { Session.Commit(); Sync(); Changed?.Invoke(); }
    /// <summary>视口坐标转换为世界米制坐标。</summary>
    public Vector2 World(Vector2 p) => (p - _origin) / Zoom;
    /// <summary>世界坐标转换为视口坐标。</summary>
    public Vector2 Screen(Vector2 p) => p * Zoom + _origin;
    /// <summary>同步变换并刷新背景和辅助层。</summary>
    private void UpdateTransform() { _world.Position = _origin; _world.Scale = Vector2.One * Zoom; foreach (var v in _visuals.Values) v.Node.SetViewScale(Zoom); QueueRedraw(); _overlay?.QueueRedraw(); }
    /// <summary>缩放至完整文档范围。</summary>
    public void Fit()
    { var b = LevelVisual.Bounds(Session.Document); Zoom = Math.Clamp(Math.Min((Size.X - 80) / b.Size.X, (Size.Y - 80) / b.Size.Y), .00001f, 800); _origin = Size / 2 - b.GetCenter() * Zoom; UpdateTransform(); }
    /// <summary>选择对象并同步属性面板。</summary>
    public void Select(int index) { Selected = index; Vertex = -1; _overlay?.QueueRedraw(); SelectionChanged?.Invoke(); }
    /// <summary>设置工具，保留无法完成的草稿。</summary>
    public bool SetTool(string tool)
    {
        if (HasDraft && !Finish()) return false;
        Tool = tool; Select(-1);
        if (tool.StartsWith("preset:")) Style = Presets.Make(tool[7..]);
        else if (tool != "select") Style = new LevelObject { Kind = tool, Name = tool == "points" ? "点集" : tool == "polyline" ? "折线" : "多边形" };
        Draft = Style.Clone(); Draft.Points.Clear(); SelectionChanged?.Invoke(); Message?.Invoke(tool == "select" ? "拖动对象或红色节点；双击边插入节点。" : "左键逐点绘制，Enter / 右键完成，Esc 取消。"); return true;
    }
    /// <summary>应用新建样式和草稿外观。</summary>
    public void UpdateStyle(LevelObject style) { Style = style.Clone(); var points = Draft.Points; Draft = style.Clone(); Draft.Points = points; UpdatePreview(); Changed?.Invoke(); }
    /// <summary>提交有效草稿，特殊标记由点击直接完成。</summary>
    public bool Finish()
    {
        if (!HasDraft) return true;
        int minimum = Draft.Kind == "polygon" ? 3 : Draft.Kind == "polyline" ? 2 : 1;
        if (Draft.Points.Count < minimum) { Message?.Invoke($"当前图形至少需要 {minimum} 个节点；按 Esc 可取消。"); return false; }
        var o = Draft.Clone(); o.Id = Guid.NewGuid().ToString(); o.Name = (o.Preset.Length > 0 ? o.Preset : Style.Name) + " " + (Session.Document.Objects.Count + 1);
        Session.Document.Objects.Add(o); Draft = Style.Clone(); Draft.Points.Clear(); _preview.Visible = false; Commit(); return true;
    }
    /// <summary>放弃未提交的草稿。</summary>
    public void Cancel() { Draft = Style.Clone(); Draft.Points.Clear(); _preview.Visible = false; _overlay.QueueRedraw(); Changed?.Invoke(); }
    /// <summary>删除对象或当前选中顶点。</summary>
    public void Delete(bool whole = false)
    {
        if (Selected < 0) return; var o = Session.Document.Objects[Selected];
        int min = o.Kind == "polygon" ? 3 : o.Kind == "polyline" ? 2 : 1;
        if (!whole && Vertex >= 0 && o.Points.Count > min) { o.Points.RemoveAt(Vertex); Vertex = -1; }
        else { Session.Document.Objects.RemoveAt(Selected); Select(-1); }
        Commit(); SelectionChanged?.Invoke();
    }
    /// <summary>复制对象并生成独立标识。</summary>
    public void Duplicate()
    {
        if (Selected < 0) return; var o = Session.Document.Objects[Selected].Clone(); o.Id = Guid.NewGuid().ToString(); o.Name += " 副本";
        foreach (var p in o.Points) { p[0] += 1; p[1] += 1; } Session.Document.Objects.Insert(Selected + 1, o); Selected++; Commit(); SelectionChanged?.Invoke();
    }
    /// <summary>移动对象层级。</summary>
    public void Reorder(int target)
    { if (Selected < 0) return; target = Math.Clamp(target, 0, Session.Document.Objects.Count - 1); var o = Session.Document.Objects[Selected]; Session.Document.Objects.RemoveAt(Selected); Session.Document.Objects.Insert(target, o); Selected = target; Commit(); SelectionChanged?.Invoke(); }
    /// <summary>执行撤销或重做。</summary>
    public void History(bool redo)
    { if (HasDraft) { if (!redo) Cancel(); return; } if (redo ? Session.Redo() : Session.Undo()) { Select(-1); Sync(); Changed?.Invoke(); } }
    /// <summary>按旧工具规则计算顶点两侧夹角吸附。</summary>
    public Vector2 Snap(Vector2 target)
    {
        var s = Session.Document.Settings; if (!HasDraft || (!s.Angles && !s.Quantize)) return target;
        var last = Draft.Points[^1]; var origin = new Vector2((float)last[0], (float)last[1]); var delta = target - origin;
        Vector2 previous = Draft.Points.Count > 1 ? new Vector2((float)Draft.Points[^2][0], (float)Draft.Points[^2][1]) - origin : Vector2.Right;
        double basis = Math.Atan2(previous.Y, previous.X), relative = Math.IEEERemainder((Math.Atan2(delta.Y, delta.X) - basis) * 180 / Math.PI, 360), angle = Math.Abs(relative);
        if (s.Quantize) angle = Math.Min(Math.Floor(180 / s.AngleStep), Math.Round(angle / s.AngleStep, MidpointRounding.AwayFromZero)) * s.AngleStep;
        else { double distance = 4; foreach (double a in new double[] { 0, 30, 45, 60, 90, 120, 135, 150, 180 }) if (Math.Abs(a - Math.Abs(relative)) <= distance) { distance = Math.Abs(a - Math.Abs(relative)); angle = a; } }
        double direction = basis + Math.CopySign(angle, relative) * Math.PI / 180;
        return origin + new Vector2((float)Math.Cos(direction), (float)Math.Sin(direction)) * delta.Length();
    }
    /// <summary>仅在绘制中更新临时图形。</summary>
    private void UpdatePreview()
    {
        _preview.Visible = HasDraft; if (!HasDraft) return;
        var preview = Draft.Clone(); var p = Snap(World(_mouse)); preview.Points.Add(new[] { (double)p.X, p.Y }); _preview.Configure(preview, false); _overlay.QueueRedraw();
    }
    /// <summary>处理鼠标和画布键盘输入。</summary>
    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton button)
        {
            _mouse = button.Position;
            if (button.ButtonIndex == MouseButton.Middle) { _panning = button.Pressed; AcceptEvent(); return; }
            if (button.Pressed && button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            { var anchor = World(_mouse); Zoom = Math.Clamp(Zoom * (button.ButtonIndex == MouseButton.WheelUp ? 1.15f : 1 / 1.15f), .00001f, 800); _origin = _mouse - anchor * Zoom; UpdateTransform(); UpdatePreview(); AcceptEvent(); return; }
            if (button.Pressed && button.ButtonIndex == MouseButton.Right) { Finish(); AcceptEvent(); return; }
            if (button.ButtonIndex != MouseButton.Left) return;
            GrabFocus(); AcceptEvent();
            if (!button.Pressed) { if (_dragging) { _dragging = false; _beforeDrag = null; Commit(); SelectionChanged?.Invoke(); } return; }
            var p = World(_mouse);
            if (Tool != "select")
            {
                if (button.DoubleClick) { Finish(); return; }
                p = Snap(p); Draft.Points.Add(new[] { (double)p.X, p.Y });
                if (Draft.Preset is "NPC" or "刷怪点") Finish(); else { UpdatePreview(); Changed?.Invoke(); } return;
            }
            if (button.DoubleClick && Selected >= 0)
            {
                var o = Session.Document.Objects[Selected]; if (o.Kind == "points") return;
                int edge = NativeGeometry.Nearest(o, p, 10 / Zoom, out var q);
                if (edge >= 0) { o.Points.Insert(edge + 1, q); Vertex = edge + 1; Commit(); SelectionChanged?.Invoke(); } return;
            }
            Vertex = -1;
            if (Selected >= 0) { var o = Session.Document.Objects[Selected]; for (int i = 0; i < o.Points.Count; i++) if (Screen(new Vector2((float)o.Points[i][0], (float)o.Points[i][1])).DistanceTo(_mouse) < 9) { Vertex = i; break; } }
            if (Vertex < 0) Select(_geometry.Hit(p.X, p.Y, 6 / Zoom));
            if (Selected >= 0) { _beforeDrag = Session.Document.Objects[Selected].Clone(); _dragStart = p; _dragging = true; SelectionChanged?.Invoke(); _overlay.QueueRedraw(); }
        }
        else if (input is InputEventMouseMotion motion)
        {
            _mouse = motion.Position;
            if (_panning) { _origin += motion.Relative; UpdateTransform(); }
            else if (_dragging && _beforeDrag != null && Selected >= 0)
            {
                var delta = World(_mouse) - _dragStart; var o = Session.Document.Objects[Selected];
                for (int i = 0; i < o.Points.Count; i++) if (Vertex < 0 || Vertex == i) { o.Points[i][0] = _beforeDrag.Points[i][0] + delta.X; o.Points[i][1] = _beforeDrag.Points[i][1] + delta.Y; }
                RefreshSelected();
            }
            else { UpdatePreview(); _overlay.QueueRedraw(); }
            var p = World(_mouse); PointerInfo?.Invoke($"X {p.X:F2} m    Y {p.Y:F2} m    ·    {Zoom:F2} px/m");
        }
        else if (input is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.Enter) Finish();
            else if (key.Keycode == Key.Escape) { Cancel(); SetTool("select"); }
            else if (key.Keycode == Key.Backspace && HasDraft) { Draft.Points.RemoveAt(Draft.Points.Count - 1); UpdatePreview(); Changed?.Invoke(); }
            else if (key.Keycode == Key.Delete) Delete();
            else return;
            AcceptEvent();
        }
    }
    /// <summary>绘制有密度限制的分级网格。</summary>
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color("fafbf9"));
        if (!Session.Document.Settings.Grid) return;
        double spacing = Session.Document.Settings.GridStep; while (spacing * Zoom < 12) spacing *= 10;
        var a = World(Vector2.Zero); var b = World(Size);
        for (long x = (long)Math.Floor(a.X / spacing); x <= Math.Ceiling(b.X / spacing); x++) { float px = Screen(new Vector2((float)(x * spacing), 0)).X; DrawLine(new Vector2(px, 0), new Vector2(px, Size.Y), new Color(x % 10 == 0 ? "bac8cc" : x % 5 == 0 ? "d3dce0" : "e9eef0"), x % 10 == 0 ? 1.3f : 1); }
        for (long y = (long)Math.Floor(a.Y / spacing); y <= Math.Ceiling(b.Y / spacing); y++) { float py = Screen(new Vector2(0, (float)(y * spacing))).Y; DrawLine(new Vector2(0, py), new Vector2(Size.X, py), new Color(y % 10 == 0 ? "bac8cc" : y % 5 == 0 ? "d3dce0" : "e9eef0"), y % 10 == 0 ? 1.3f : 1); }
    }
    /// <summary>在屏幕坐标中绘制恒定尺寸的编辑手柄与标尺。</summary>
    public void DrawOverlay(Control target)
    {
        var s = Session.Document.Settings;
        if (s.Crosshair) { target.DrawLine(new Vector2(_mouse.X, 0), new Vector2(_mouse.X, Size.Y), new Color("85a5bd")); target.DrawLine(new Vector2(0, _mouse.Y), new Vector2(Size.X, _mouse.Y), new Color("85a5bd")); }
        if (Selected >= 0 && Selected < Session.Document.Objects.Count)
        {
            var o = Session.Document.Objects[Selected];
            for (int i = 0; i < o.Points.Count; i++) { var p = Screen(new Vector2((float)o.Points[i][0], (float)o.Points[i][1])); if (!new Rect2(Vector2.Zero, Size).Grow(8).HasPoint(p)) continue; target.DrawCircle(p, i == Vertex ? 6 : 4.5f, new Color("ef4444"), true, -1, true); target.DrawArc(p, i == Vertex ? 6 : 4.5f, 0, Mathf.Tau, 20, Colors.White, 1, true); }
        }
        foreach (var point in Draft.Points) target.DrawCircle(Screen(new Vector2((float)point[0], (float)point[1])), 4, new Color("ef4444"), true, -1, true);
        if (HasDraft && s.Ruler)
        {
            var a = new Vector2((float)Draft.Points[^1][0], (float)Draft.Points[^1][1]); var b = Snap(World(_mouse)); var label = $"{a.DistanceTo(b):F2} m";
            if (Draft.Points.Count > 1) { var prev = new Vector2((float)Draft.Points[^2][0], (float)Draft.Points[^2][1]); label += $"   {Math.Abs((prev - a).AngleTo(b - a) * 180 / Math.PI):F2}°"; }
            var at = _mouse + new Vector2(14, -18); var size = LevelVisual.LabelFont.GetStringSize(label, HorizontalAlignment.Left, -1, 14);
            target.DrawRect(new Rect2(at - new Vector2(5, 17), size + new Vector2(10, 6)), new Color(1, 1, 1, .94f)); target.DrawString(LevelVisual.LabelFont, at, label, HorizontalAlignment.Left, -1, 14, new Color("315ee7"));
        }
    }
    /// <summary>退出时归还本地缓存。</summary>
    public override void _ExitTree() => _geometry?.Dispose();
}

/// <summary>不接收输入的编辑覆盖层。</summary>
public partial class CanvasOverlay : Control
{
    /// <summary>所属画布。</summary>
    public LevelCanvas Canvas { get; set; } = null!;
    /// <summary>委托画布绘制交互辅助。</summary>
    public override void _Draw() => Canvas.DrawOverlay(this);
}

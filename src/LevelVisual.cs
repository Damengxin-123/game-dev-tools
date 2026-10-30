using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GameDevTools;

/// <summary>单对象持久渲染节点，几何变化才重建网格，平移缩放复用绘制命令。</summary>
public partial class LevelVisual : Node2D
{
    /// <summary>当前对象的不可变显示副本。</summary>
    private LevelObject _object = new();
    /// <summary>主填充的缓存三角网格。</summary>
    private ArrayMesh? _mesh;
    /// <summary>纹样缓存，不随鼠标移动生成。</summary>
    private Texture2D? _pattern;
    /// <summary>描边顶点。</summary>
    private Vector2[] _points = Array.Empty<Vector2>();
    /// <summary>缓存的虚线线段。</summary>
    private Vector2[] _dashes = Array.Empty<Vector2>();
    /// <summary>所有对象共享的纹样资源。</summary>
    private static readonly Dictionary<string, Texture2D> Patterns = new();
    /// <summary>显示标签的中文字体。</summary>
    public static readonly SystemFont LabelFont = new() { FontNames = new[] { "Microsoft YaHei", "Noto Sans CJK SC", "sans-serif" }, MultichannelSignedDistanceField = true };
    /// <summary>独立于几何重建的标签层。</summary>
    private ObjectLabels? _labels;
    /// <summary>更新指定对象的绘制缓存。</summary>
    public void Configure(LevelObject obj, bool names)
    {
        _object = obj.Clone();
        _points = obj.Points.Select(p => new Vector2((float)p[0], (float)p[1])).ToArray();
        _mesh?.Dispose(); _mesh = null; _pattern = null;
        if (obj.Kind == "polygon" && _points.Length >= 3)
        {
            var triangles = NativeGeometry.Tessellate(obj);
            if (triangles.Length > 0)
            {
                var vertices = new Vector3[triangles.Length / 2]; var uv = new Vector2[vertices.Length];
                for (int i = 0; i < vertices.Length; i++) { vertices[i] = new Vector3((float)triangles[i * 2], (float)triangles[i * 2 + 1], 0); uv[i] = new Vector2(vertices[i].X / 2, vertices[i].Y / 2); }
                var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
                arrays[(int)Mesh.ArrayType.Vertex] = vertices; arrays[(int)Mesh.ArrayType.TexUV] = uv;
                _mesh = new ArrayMesh(); _mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                _pattern = Pattern(obj.Preset);
            }
        }
        _dashes = Array.Empty<Vector2>();
        if (obj.Kind == "polyline" && _points.Length > 1 && obj.Preset is "道路" or "移动路线")
        {
            double w = obj.Preset == "道路" ? Math.Min(.12, obj.Width / 5) : obj.Width;
            var data = NativeGeometry.Dashes(obj, w * (obj.Preset == "道路" ? 8 : 6), w * (obj.Preset == "道路" ? 6 : 4));
            _dashes = Enumerable.Range(0, data.Length / 2).Select(i => new Vector2((float)data[i * 2], (float)data[i * 2 + 1])).ToArray();
        }
        if (_labels == null) { _labels = new ObjectLabels(); AddChild(_labels); }
        _labels.Object = _object; _labels.Visible = names; _labels.QueueRedraw();
        TextureRepeat = TextureRepeatEnum.Enabled; QueueRedraw();
    }
    /// <summary>仅刷新屏幕字号，不重新计算几何。</summary>
    public void SetViewScale(float scale) { if (_labels != null) { _labels.ViewScale = scale; _labels.QueueRedraw(); } }
    /// <summary>按预设共享纹样贴图，源定义同样用于 SVG 导出。</summary>
    private static Texture2D? Pattern(string preset)
    {
        var path = VectorExport.PatternPath(preset); if (path.Length == 0) return null;
        if (Patterns.TryGetValue(preset, out var texture)) return texture;
        var image = new Image();
        image.LoadSvgFromString($"<svg xmlns='http://www.w3.org/2000/svg' width='96' height='96' viewBox='0 0 2 2'><path d='{path}' fill='none' stroke='{VectorExport.PatternColor(preset)}' stroke-width='.04' stroke-linecap='round' stroke-linejoin='round'/></svg>");
        texture = ImageTexture.CreateFromImage(image); image.Dispose(); Patterns.Add(preset, texture); return texture;
    }
    /// <summary>重放缓存图形，正常空闲时不会被调用。</summary>
    public override void _Draw()
    {
        if (_points.Length == 0) return;
        var fill = LevelObject.ColorOf(_object.Fill); var stroke = LevelObject.ColorOf(_object.Stroke); float width = (float)_object.Width;
        if (_object.Kind == "polygon" && _mesh != null)
        {
            DrawMesh(_mesh, null, Transform2D.Identity, fill);
            if (_pattern != null) DrawMesh(_mesh, _pattern, Transform2D.Identity, Colors.White);
        }
        if (_object.Kind == "points")
        {
            foreach (var p in _points) DrawMarker(p, _object, fill, stroke);
        }
        else if (_points.Length > 1)
        {
            if (_object.Preset != "移动路线")
            {
                var points = _object.Kind == "polygon" ? _points.Concat(new[] { _points[0] }).ToArray() : _points;
                DrawPolyline(points, stroke, width, false);
                // 圆角描边接头及端点与旧工具保持一致。
                foreach (var p in _points) DrawCircle(p, width / 2, stroke, true);
            }
            if (_dashes.Length > 0) DrawMultiline(_dashes, _object.Preset == "道路" ? new Color("fff4c7") : stroke, _object.Preset == "道路" ? (float)Math.Min(.12, _object.Width / 5) : width, false);
        }
    }
    /// <summary>绘制纯矢量专用标记，避免缩放后图标模糊。</summary>
    private void DrawMarker(Vector2 center, LevelObject obj, Color fill, Color stroke)
    {
        float d = (float)obj.Diameter, w = (float)obj.Width;
        if (obj.Preset == "NPC")
        {
            DrawCircle(center + new Vector2(0, -.31f) * d, .17f * d, fill, true);
            DrawArc(center + new Vector2(0, -.31f) * d, .17f * d, 0, Mathf.Tau, 48, stroke, w);
            var body = new List<Vector2> { center + new Vector2(-.4f, .46f) * d, center + new Vector2(-.4f, .27f) * d };
            for (int i = 1; i <= 32; i++) { float t = i / 32f; var p = new Vector2(-.4f, .27f).BezierInterpolate(new Vector2(-.4f, -.15f), new Vector2(.4f, -.15f), new Vector2(.4f, .27f), t); body.Add(center + p * d); }
            body.Add(center + new Vector2(.4f, .46f) * d); DrawColoredPolygon(body.ToArray(), fill); body.Add(body[0]); DrawPolyline(body.ToArray(), stroke, w);
        }
        else
        {
            DrawCircle(center, d / 2, fill, true); DrawArc(center, d / 2, 0, Mathf.Tau, 64, stroke, w);
            if (obj.Preset == "刷怪点")
            {
                DrawLine(center + new Vector2(0, -.5f) * d, center + new Vector2(0, .5f) * d, stroke, w);
                foreach (float sign in new[] { -1f, 1f })
                {
                    var curve = new Vector2[33]; for (int i = 0; i <= 32; i++) curve[i] = center + new Vector2(.2f * sign, -.458f).BezierInterpolate(new Vector2(.38f * sign, -.2f), new Vector2(.38f * sign, .2f), new Vector2(.2f * sign, .458f), i / 32f) * d;
                    DrawPolyline(curve, stroke, w);
                }
                var ring = Enumerable.Range(0, 65).Select(i => center + new Vector2(Mathf.Cos(i * Mathf.Tau / 64) * .48f, Mathf.Sin(i * Mathf.Tau / 64) * .17f) * d).ToArray(); DrawPolyline(ring, stroke, w);
            }
        }
    }
    /// <summary>为画布与导出提供相同的标签基线。</summary>
    public static Vector2[] NameOrigins(LevelObject o)
    {
        if (o.Points.Count == 0) return Array.Empty<Vector2>();
        var points = o.Points.Select(p => new Vector2((float)p[0], (float)p[1])).ToArray();
        var size = LabelFont.GetStringSize(o.Name, HorizontalAlignment.Left, -1, 100) * .0075f;
        if (o.Kind == "points") return points.Select(p => p + new Vector2((float)((o.Diameter + o.Width) / 2 + .25), .25f)).ToArray();
        if (o.Kind == "polyline") return new[] { points[0] + new Vector2(0, (float)(-o.Width / 2 - .25)) };
        var box = new Rect2(points[0], Vector2.Zero); foreach (var p in points) box = box.Expand(p);
        var center = box.GetCenter();
        if (!Geometry2D.IsPointInPolygon(center, points))
        {
            var crossings = new List<float>();
            for (int i = 0; i < points.Length; i++) { var a = points[i]; var b = points[(i + 1) % points.Length]; if ((a.Y > center.Y) != (b.Y > center.Y)) crossings.Add(a.X + (center.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y)); }
            crossings.Sort(); float longest = -1;
            for (int i = 1; i < crossings.Count; i += 2) if (crossings[i] - crossings[i - 1] > longest) { longest = crossings[i] - crossings[i - 1]; center.X = (crossings[i] + crossings[i - 1]) / 2; }
        }
        return new[] { center + new Vector2(-size.X / 2, .25f) };
    }
    /// <summary>返回包含图形、描边和名称的导出范围。</summary>
    public static Rect2 Bounds(LevelDocument doc)
    {
        Rect2? result = null;
        foreach (var o in doc.Objects)
        {
            if (o.Points.Count == 0) continue;
            var box = new Rect2(new Vector2((float)o.Points[0][0], (float)o.Points[0][1]), Vector2.Zero);
            foreach (var p in o.Points) box = box.Expand(new Vector2((float)p[0], (float)p[1]));
            box = box.Grow((float)Math.Max(o.Width, o.Diameter) / 2);
            if (doc.Settings.ShowNames) foreach (var origin in NameOrigins(o)) box = box.Merge(new Rect2(origin - new Vector2(0, .8f), LabelFont.GetStringSize(o.Name, HorizontalAlignment.Left, -1, 100) * .0075f));
            result = result == null ? box : result.Value.Merge(box);
        }
        return (result ?? new Rect2(0, 0, 50, 30)).Grow(2);
    }
    /// <summary>节点离开时释放网格资源。</summary>
    public override void _ExitTree() { _mesh?.Dispose(); _mesh = null; }
}

/// <summary>独立标签层，视口中保持可读字号，导出时保持米制尺寸。</summary>
public partial class ObjectLabels : Node2D
{
    /// <summary>标签所属对象。</summary>
    public LevelObject Object { get; set; } = new();
    /// <summary>编辑视口缩放，零表示按原尺寸导出。</summary>
    public float ViewScale { get; set; }
    /// <summary>使用 MSDF 字体在缩放中保持清晰。</summary>
    public override void _Draw()
    {
        if (string.IsNullOrWhiteSpace(Object.Name)) return;
        float factor = ViewScale > 0 ? Math.Max(.0075f, 12 / (100 * ViewScale)) : .0075f;
        var color = LevelObject.ColorOf(Object.Kind == "polyline" ? Object.Stroke : Object.Fill).Darkened(.42f); color.A = 1;
        var width = LevelVisual.LabelFont.GetStringSize(Object.Name, HorizontalAlignment.Left, -1, 100).X;
        foreach (var baseline in LevelVisual.NameOrigins(Object))
        {
            var origin = baseline;
            if (Object.Kind == "polygon") origin.X -= width * (factor - .0075f) / 2;
            DrawSetTransform(origin, 0, Vector2.One * factor); DrawString(LevelVisual.LabelFont, Vector2.Zero, Object.Name, HorizontalAlignment.Left, -1, 100, color);
        }
    }
}

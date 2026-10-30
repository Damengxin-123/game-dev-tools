using Godot;
using System;
using System.Linq;
using System.Xml.Linq;
using System.Globalization;

namespace GameDevTools;

/// <summary>从文档直接生成可编辑 SVG；纹样与画布共用几何定义。</summary>
public static class VectorExport
{
    /// <summary>SVG 命名空间。</summary>
    private static readonly XNamespace Ns = "http://www.w3.org/2000/svg";
    /// <summary>独立于系统区域设置的浮点格式。</summary>
    private static string N(double n) => n.ToString("0.########", CultureInfo.InvariantCulture);
    /// <summary>创建 SVG 元素。</summary>
    private static XElement E(string name, params object[] attributes) => new(Ns + name, attributes);
    /// <summary>创建 SVG 属性。</summary>
    private static XAttribute A(string name, object value) => new(name, value);
    /// <summary>返回每个面预设的两米矢量纹样。</summary>
    public static string PatternPath(string preset) => preset switch
    {
        "农田" => "M0 .65H2 M0 1.65H2 M.5 .65V.3 M.34 .23L.5 .41L.66 .23 M1.5 1.65V1.3 M1.34 1.23L1.5 1.41L1.66 1.23",
        "山地" => "M.12 1.6L.78 .42L1.2 1.1L1.48 .72L1.9 1.6 M.78 .42V1.12L1.05 1.48",
        "沙漠" => "M.25 .28h.05 M1.12 .18h.05 M1.73 .58h.05 M.65 .83h.05 M1.28 1.16h.05 M.2 1.52h.05 M.8 1.76h.05 M1.74 1.78h.05",
        "森林" => "M.55 .22L.3 .56H.42L.21 .85H.89L.68 .56H.8Z M.55 .85V1.05 M1.52 1.06L1.27 1.4H1.39L1.18 1.69H1.86L1.65 1.4H1.77Z M1.52 1.69V1.89",
        "普通平原" => "M.2 .5H.78 M1.18 1.5H1.76",
        "家具" => "M0 .25H2 M0 .5H2 M0 .75H2 M0 1H2 M0 1.25H2 M0 1.5H2 M0 1.75H2",
        "绿化" => "M.3 .6l.12-.3 .12.3 M.42 .3v.5 M1.2 1.5l.15-.35 .15.35 M1.35 1.15v.5",
        _ => ""
    };
    /// <summary>返回共享纹样颜色。</summary>
    public static string PatternColor(string preset) => preset switch { "农田" => "#b58b31", "沙漠" => "#b38c37", "森林" => "#9bc69d", "普通平原" => "#9b8062", "家具" => "#8c6948", "绿化" => "#548466", _ => "#78523a" };
    /// <summary>写入对象颜色和圆角描边。</summary>
    private static void Style(XElement element, LevelObject o, bool filled = true)
    {
        var fill = LevelObject.ColorOf(o.Fill); var stroke = LevelObject.ColorOf(o.Stroke);
        element.Add(A("fill", filled ? "#" + fill.ToHtml(false) : "none"), A("fill-opacity", N(fill.A)), A("stroke", "#" + stroke.ToHtml(false)), A("stroke-opacity", N(stroke.A)), A("stroke-width", N(o.Width)), A("stroke-linecap", "round"), A("stroke-linejoin", "round"));
    }
    /// <summary>输出单个对象，预设语义与真实名称分别保存。</summary>
    private static XElement ObjectElement(LevelObject o, bool showNames, bool presetNames)
    {
        var group = E("g", A("id", "object-" + o.Id), A("data-name", presetNames && o.Preset.Length > 0 ? o.Preset : o.Name), A("data-preset", o.Preset), A("data-unit", "m"), E("title", o.Name));
        if (o.Kind == "points") foreach (var p in o.Points)
        {
            if (o.Preset is "NPC" or "刷怪点")
            {
                var marker = E("g", A("transform", $"translate({N(p[0])} {N(p[1])}) scale({N(o.Diameter)})"));
                var body = o.Preset == "NPC" ? E("path", A("d", "M-.17 -.31a.17 .17 0 1 0 .34 0a.17 .17 0 1 0 -.34 0 M-.4 .46V.27C-.4 -.15 .4 -.15 .4 .27V.46Z")) : E("circle", A("r", ".5"));
                var sample = o.Clone(); sample.Width /= o.Diameter; Style(body, sample); marker.Add(body);
                if (o.Preset == "刷怪点") { var lines = E("path", A("d", "M0 -.5V.5 M-.2 -.458C-.38 -.2 -.38 .2 -.2 .458 M.2 -.458C.38 -.2 .38 .2 .2 .458 M-.48 0a.48 .17 0 1 0 .96 0a.48 .17 0 1 0 -.96 0")); Style(lines, sample, false); marker.Add(lines); }
                group.Add(marker);
            }
            else { var circle = E("circle", A("cx", N(p[0])), A("cy", N(p[1])), A("r", N(o.Diameter / 2))); Style(circle, o); group.Add(circle); }
        }
        else
        {
            var d = string.Join(" ", o.Points.Select((p, i) => $"{(i == 0 ? "M" : "L")}{N(p[0])} {N(p[1])}")) + (o.Kind == "polygon" ? " Z" : "");
            var path = E("path", A("d", d), A("fill-rule", "evenodd")); Style(path, o, o.Kind == "polygon");
            if (o.Preset == "移动路线") path.Add(A("stroke-dasharray", $"{N(o.Width * 6)} {N(o.Width * 4)}")); group.Add(path);
            if (o.Kind == "polygon" && PatternPath(o.Preset).Length > 0) group.Add(E("path", A("d", d), A("fill", $"url(#pattern-{Array.IndexOf(Presets.Names, o.Preset)})"), A("fill-rule", "evenodd")));
            if (o.Kind == "polyline" && o.Preset == "道路") { double w = Math.Min(.12, o.Width / 5); group.Add(E("path", A("d", d), A("fill", "none"), A("stroke", "#fff4c7"), A("stroke-width", N(w)), A("stroke-dasharray", $"{N(w * 8)} {N(w * 6)}"))); }
        }
        if (showNames) foreach (var origin in LevelVisual.NameOrigins(o))
        {
            var color = LevelObject.ColorOf(o.Kind == "polyline" ? o.Stroke : o.Fill).Darkened(.42f);
            group.Add(E("text", A("class", "object-name"), A("x", N(origin.X)), A("y", N(origin.Y)), A("font-family", "Microsoft YaHei, sans-serif"), A("font-size", ".75"), A("fill", "#" + color.ToHtml(false)), o.Name));
        }
        return group;
    }
    /// <summary>生成包含完整工程元数据、图例及独立对象分组的 SVG。</summary>
    public static string Svg(LevelDocument doc, int width, bool legend, bool presetNames)
    {
        var bounds = LevelVisual.Bounds(doc); double scale = width / bounds.Size.X;
        var items = legend ? doc.Objects.Where(o => o.Preset.Length > 0).GroupBy(o => o.Preset).Select(g => g.First()).ToArray() : Array.Empty<LevelObject>();
        double h = Math.Ceiling(bounds.Size.Y * scale), total = h + (items.Length > 0 ? 40 + items.Length * 28 : 0);
        var root = E("svg", A("width", width), A("height", N(total)), A("viewBox", $"0 0 {width} {N(total)}"), E("metadata", doc.Json()));
        var defs = E("defs"); foreach (var key in Presets.Names.Where(k => PatternPath(k).Length > 0)) defs.Add(E("pattern", A("id", "pattern-" + Array.IndexOf(Presets.Names, key)), A("patternUnits", "userSpaceOnUse"), A("width", 2), A("height", 2), E("path", A("d", PatternPath(key)), A("fill", "none"), A("stroke", PatternColor(key)), A("stroke-width", ".04")))); root.Add(defs);
        root.Add(E("rect", A("width", "100%"), A("height", "100%"), A("fill", "white")));
        var drawing = E("g", A("transform", $"scale({N(scale)}) translate({N(-bounds.Position.X)} {N(-bounds.Position.Y)})"));
        foreach (var o in doc.Objects) drawing.Add(ObjectElement(o, doc.Settings.ShowNames, presetNames)); root.Add(drawing);
        for (int i = 0; i < items.Length; i++)
        {
            var sample = items[i].Clone(); sample.Id = "legend-" + i; sample.Width = sample.Kind == "polyline" && sample.Preset == "道路" ? 5 : 1; sample.Diameter = 17;
            sample.Points = sample.Kind == "points" ? new() { new[] { 26d, h + 28 + i * 28 } } : sample.Kind == "polyline" ? new() { new[] { 16d, h + 28 + i * 28 }, new[] { 36d, h + 28 + i * 28 } } : new() { new[] { 16d, h + 20 + i * 28 }, new[] { 36d, h + 20 + i * 28 }, new[] { 36d, h + 36 + i * 28 }, new[] { 16d, h + 36 + i * 28 } };
            root.Add(ObjectElement(sample, false, false)); root.Add(E("text", A("x", 48), A("y", N(h + 33 + i * 28)), A("font-size", 16), A("font-family", "Microsoft YaHei, sans-serif"), items[i].Preset));
        }
        return new XDocument(new XDeclaration("1.0", "utf-8", null), root).ToString();
    }
}

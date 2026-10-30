using Godot;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace GameDevTools;

/// <summary>少量有意义的跨语言集成检查，使用临时目录而非用户工程。</summary>
public static class SmokeChecks
{
    /// <summary>检查不变量，失败立即报告具体原因。</summary>
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("验证失败：" + message); }
    /// <summary>从三角顶点求面积，检查剖分是否丢失或重叠。</summary>
    private static double Area(double[] triangles)
    { double area = 0; for (int i = 0; i < triangles.Length; i += 6) area += Math.Abs((triangles[i + 2] - triangles[i]) * (triangles[i + 5] - triangles[i + 1]) - (triangles[i + 3] - triangles[i + 1]) * (triangles[i + 4] - triangles[i])) / 2; return area; }
    /// <summary>验证旧格式、原生算法、历史、锁、真实输入及导出。</summary>
    public static async Task Run(MainUI ui, DocumentSession session, LevelCanvas canvas, bool visual)
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GameDevTools-check-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        GD.Print("验证目录：" + dir);
        var original = Presets.Demo(); var json = original.Json(); Require(LevelDocument.Parse(json).Json() == json, "旧格式 JSON 往返");
        bool rejected = false; try { LevelDocument.Parse(json.Replace("\"kind\":", "\"missingKind\":")); } catch (InvalidDataException) { rejected = true; } Require(rejected, "拒绝缺失必需字段");
        var color = LevelObject.ColorOf("#aa0cb450"); Require(color.R8 == 12 && color.G8 == 180 && color.B8 == 80 && color.A8 == 170, "ARGB 通道转换");
        var bow = new LevelObject { Points = new() { new[] { 0d, 0d }, new[] { 4d, 4d }, new[] { 0d, 4d }, new[] { 4d, 0d } } };
        Require(Math.Abs(Area(NativeGeometry.Tessellate(bow)) - 8) < 1e-6, "自交多边形奇偶填充");
        var concave = new LevelObject { Points = new() { new[] { 0d, 0d }, new[] { 4d, 0d }, new[] { 4d, 1d }, new[] { 1d, 1d }, new[] { 1d, 4d }, new[] { 0d, 4d } } };
        Require(Math.Abs(Area(NativeGeometry.Tessellate(concave)) - 7) < 1e-6, "凹多边形剖分");
        using (var native = new NativeGeometry()) { var doc = new LevelDocument(); doc.Objects.Add(concave); native.Sync(doc); Require(native.Hit(.5, 3, .01) == 0 && native.Hit(3, 3, .01) == -1, "凹面拾取"); }
        string path = System.IO.Path.Combine(dir, "兼容关卡.level.json");
        using (var first = new DocumentSession())
        {
            first.Reset(LevelDocument.Parse(json)); first.Save(path); Require(File.Exists(path + ".lock"), "工程锁");
            using var second = new DocumentSession(); bool blocked = false; try { second.Open(path); } catch (IOException) { blocked = true; } Require(blocked, "阻止重复编辑");
            first.Document.Title = "修改"; first.Commit(); Require(first.Dirty, "修改状态"); first.Undo(); Require(first.Document.Title == original.Title && !first.Dirty, "撤销回到保存点"); first.Redo(); Require(first.Document.Title == "修改", "重做"); first.Save(path);
        }
        Require(!File.Exists(path + ".lock"), "释放文件锁");
        using (var reopened = new DocumentSession()) { reopened.Open(path); Require(reopened.Document.Title == "修改", "重新打开保存文件"); }
        session.Reset(new LevelDocument()); canvas.Cancel(); canvas.Select(-1); canvas.Sync(); canvas.SetTool("polyline");
        foreach (var pos in new[] { new Vector2(120, 110), new Vector2(260, 110), new Vector2(260, 220) })
        { canvas._GuiInput(new InputEventMouseButton { Position = pos, ButtonIndex = MouseButton.Left, Pressed = true }); canvas._GuiInput(new InputEventMouseButton { Position = pos, ButtonIndex = MouseButton.Left, Pressed = false }); }
        Require(canvas.Finish() && session.Document.Objects.Count == 1 && session.Document.Objects[0].Points.Count == 3, "实际输入创建折线");
        canvas.SetTool("select"); canvas.Select(0); var before = session.Document.Objects[0].Points[1].ToArray();
        canvas._GuiInput(new InputEventMouseButton { Position = new Vector2(260, 110), ButtonIndex = MouseButton.Left, Pressed = true });
        canvas._GuiInput(new InputEventMouseMotion { Position = new Vector2(290, 130), Relative = new Vector2(30, 20) });
        canvas._GuiInput(new InputEventMouseButton { Position = new Vector2(290, 130), ButtonIndex = MouseButton.Left, Pressed = false });
        Require(!session.Document.Objects[0].Points[1].SequenceEqual(before), "节点拖动"); canvas.History(false); Require(session.Document.Objects[0].Points[1].SequenceEqual(before), "拖动一次撤销");
        canvas.Select(0); canvas.Duplicate(); Require(session.Document.Objects.Count == 2 && session.Document.Objects[0].Id != session.Document.Objects[1].Id, "复制独立对象");
        canvas.Reorder(0); canvas.History(false); canvas.Delete(true);
        session.Reset(Presets.Demo()); canvas.Cancel(); canvas.Select(-1); canvas.Sync(); canvas.Fit();
        string svg = VectorExport.Svg(session.Document, 1024, true, true); var xml = XDocument.Parse(svg); XNamespace ns = "http://www.w3.org/2000/svg";
        Require(LevelDocument.Parse(xml.Root!.Element(ns + "metadata")!.Value).Json() == session.Document.Json(), "SVG 完整元数据");
        Require(xml.Descendants(ns + "g").Any(g => (string?)g.Attribute("data-preset") == "NPC") && xml.Descendants(ns + "pattern").Any(), "SVG 标记和纹样");
        LevelDocument.AtomicWrite(System.IO.Path.Combine(dir, "关卡.svg"), svg);
        if (visual)
        {
            string output = ProjectSettings.GlobalizePath("res://build/验证"); Directory.CreateDirectory(output);
            await ui.ExportPng(System.IO.Path.Combine(output, "关卡导出.png"), 1400, true);
            canvas.Select(6);
            await ui.ToSignal(ui.GetTree(), SceneTree.SignalName.ProcessFrame); await ui.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var screen = ui.GetViewport().GetTexture().GetImage(); Require(!screen.IsEmpty(), "原生界面渲染"); screen.SavePng(System.IO.Path.Combine(output, "主界面.png"));
            LevelDocument.AtomicWrite(System.IO.Path.Combine(output, "关卡导出.svg"), svg);
        }
        GD.Print("PASS：JSON / ARGB / C++ 凹面和自交剖分 / 拾取 / 锁 / 历史 / 实际输入 / SVG" + (visual ? " / PNG / 界面截图" : ""));
    }
}

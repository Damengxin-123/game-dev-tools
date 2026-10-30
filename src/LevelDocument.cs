using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using FileAccess = System.IO.FileAccess;
using Environment = System.Environment;

namespace GameDevTools;

/// <summary>保持旧工程精度的双精度米制点。</summary>
public record struct MeterPoint(double X, double Y)
{
    /// <summary>只在提交显示数据时转换为引擎向量。</summary>
    public Vector2 Vector => new((float)X, (float)Y);
}

/// <summary>独立矢量对象，持久化格式与 DesktopTool 兼容。</summary>
public sealed class LevelObject
{
    /// <summary>稳定对象标识。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>用户可编辑名称。</summary>
    public string Name { get; set; } = "未命名对象";
    /// <summary>点集、折线或多边形。</summary>
    public string Kind { get; set; } = "polygon";
    /// <summary>预设语义名称。</summary>
    public string Preset { get; set; } = "";
    /// <summary>双精度米制节点。</summary>
    public List<double[]> Points { get; set; } = new();
    /// <summary>旧格式 ARGB 填充颜色。</summary>
    public string Fill { get; set; } = "#ffa9d4bb";
    /// <summary>旧格式 ARGB 描边颜色。</summary>
    public string Stroke { get; set; } = "#ff355b50";
    /// <summary>米制线宽。</summary>
    public double Width { get; set; } = 0.20;
    /// <summary>米制点直径。</summary>
    public double Diameter { get; set; } = 0.80;
    /// <summary>用于原生批处理的连续双精度坐标。</summary>
    [JsonIgnore] public double[] Flat => Points.SelectMany(p => p).ToArray();
    /// <summary>生成完全独立的对象副本。</summary>
    public LevelObject Clone() => JsonSerializer.Deserialize<LevelObject>(JsonSerializer.Serialize(this, LevelDocument.Options), LevelDocument.Options)!;
    /// <summary>将 Qt ARGB 或 RGB 转换为引擎颜色。</summary>
    public static Color ColorOf(string value) => value.Length == 9
        ? new Color(value[3..] + value.Substring(1, 2)) : new Color(value);
    /// <summary>保留旧工程的颜色通道顺序。</summary>
    public static string ColorText(Color color) => $"#{color.A8:x2}{color.R8:x2}{color.G8:x2}{color.B8:x2}";
}

/// <summary>项目辅助绘制设置。</summary>
public sealed class DrawingSettings
{
    /// <summary>显示对象名称。</summary>
    public bool ShowNames { get; set; } = true;
    /// <summary>显示分级网格。</summary>
    public bool Grid { get; set; } = true;
    /// <summary>启用常用角度吸附。</summary>
    public bool Angles { get; set; }
    /// <summary>强制最小角度倍数。</summary>
    public bool Quantize { get; set; }
    /// <summary>显示长度与角度标尺。</summary>
    public bool Ruler { get; set; } = true;
    /// <summary>显示十字辅助线。</summary>
    public bool Crosshair { get; set; }
    /// <summary>角度步长，单位度。</summary>
    public double AngleStep { get; set; } = 10.00;
    /// <summary>网格步长，单位米。</summary>
    public double GridStep { get; set; } = 1.00;
    /// <summary>兼容旧项目显示单位；新界面统一米。</summary>
    public string Unit { get; set; } = "m";
}

/// <summary>可独立保存的关卡文档。</summary>
public sealed class LevelDocument
{
    /// <summary>兼容旧项目的格式标识。</summary>
    public string Format { get; set; } = "DesktopTool.LevelLayout";
    /// <summary>工程文件版本。</summary>
    public int Version { get; set; } = 1;
    /// <summary>项目名称。</summary>
    public string Title { get; set; } = "未命名关卡";
    /// <summary>按底层到顶层排列的对象。</summary>
    public List<LevelObject> Objects { get; set; } = new();
    /// <summary>辅助设置。</summary>
    public DrawingSettings Settings { get; set; } = new();
    /// <summary>所有文件统一使用的小写驼峰 JSON 编码。</summary>
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    /// <summary>返回完整快照，用于保存及操作历史。</summary>
    public string Json() => JsonSerializer.Serialize(this, Options);
    /// <summary>严格校验数据，损坏文件不得覆盖当前工作区。</summary>
    public static LevelDocument Parse(string text)
    {
        using var raw = JsonDocument.Parse(text);
        var root = raw.RootElement;
        if (!root.TryGetProperty("format", out var f) || f.GetString() != "DesktopTool.LevelLayout" ||
            !root.TryGetProperty("version", out var v) || v.GetInt32() != 1 ||
            !root.TryGetProperty("objects", out var entries) || entries.ValueKind != JsonValueKind.Array ||
            !root.TryGetProperty("settings", out var settings) || settings.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("title", out var title) || title.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("不是受支持的关卡工程。请选择 .level.json 文件。");
        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) throw new InvalidDataException("对象结构无效。");
            foreach (var key in new[] { "id", "kind", "fill", "stroke" })
                if (!entry.TryGetProperty(key, out var item) || item.ValueKind != JsonValueKind.String) throw new InvalidDataException("对象缺少必要字段：" + key);
            foreach (var key in new[] { "width", "diameter" })
                if (!entry.TryGetProperty(key, out var item) || item.ValueKind != JsonValueKind.Number) throw new InvalidDataException("对象缺少必要尺寸：" + key);
            if (!entry.TryGetProperty("points", out var pts) || pts.ValueKind != JsonValueKind.Array) throw new InvalidDataException("对象缺少顶点数据。");
        }
        var doc = JsonSerializer.Deserialize<LevelDocument>(text, Options) ?? throw new InvalidDataException("工程内容为空。");
        if (string.IsNullOrWhiteSpace(doc.Title) || doc.Objects == null || doc.Settings == null) throw new InvalidDataException("工程缺少必要字段。");
        var ids = new HashSet<string>();
        foreach (var o in doc.Objects)
        {
            if (o == null || string.IsNullOrEmpty(o.Id) || !ids.Add(o.Id) || o.Name == null || o.Preset == null ||
                o.Kind is not ("points" or "polyline" or "polygon") || o.Points == null ||
                o.Points.Count < (o.Kind == "polygon" ? 3 : o.Kind == "polyline" ? 2 : 1) ||
                !Finite(o.Width, .01, 1000) || !Finite(o.Diameter, .01, 1000)) throw new InvalidDataException("对象数据不完整、标识重复或尺寸无效。");
            foreach (var p in o.Points) if (p == null || p.Length != 2 || !Finite(p[0], -1e7, 1e7) || !Finite(p[1], -1e7, 1e7)) throw new InvalidDataException("对象包含无效坐标。");
            foreach (var c in new[] { o.Fill, o.Stroke }) if (c == null || c.Length is not (7 or 9) || c[0] != '#' || !ulong.TryParse(c[1..], System.Globalization.NumberStyles.HexNumber, null, out _)) throw new InvalidDataException("对象包含无效颜色。");
        }
        if (!Finite(doc.Settings.GridStep, .01, 10000) || !Finite(doc.Settings.AngleStep, .01, 180) || doc.Settings.Unit is not ("m" or "cm" or "km")) throw new InvalidDataException("绘图辅助设置无效。");
        return doc;
    }
    /// <summary>判断数值在有限范围内。</summary>
    private static bool Finite(double x, double min, double max) => double.IsFinite(x) && x >= min && x <= max;
    /// <summary>同目录临时文件加原子替换，失败时保留原工程。</summary>
    public static void AtomicWrite(string path, string text)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { var bytes = System.Text.Encoding.UTF8.GetBytes(text); stream.Write(bytes); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

/// <summary>项目锁、历史和文件事务；界面不直接操作工程文件。</summary>
public sealed class DocumentSession : IDisposable
{
    /// <summary>替换已验证的编辑副本，保留文件锁、保存点和历史。</summary>
    public void ReplaceForEdit(LevelDocument document) => Document = document;
    /// <summary>当前文档。</summary>
    public LevelDocument Document { get; private set; } = new();
    /// <summary>当前文件路径。</summary>
    public string Path { get; private set; } = "";
    /// <summary>已保存的比较基线。</summary>
    private string _saved = "";
    /// <summary>当前操作历史。</summary>
    private readonly List<string> _history = new();
    /// <summary>历史游标。</summary>
    private int _position;
    /// <summary>自动随进程退出释放的操作系统锁。</summary>
    private FileStream? _lock;
    /// <summary>记录持有的 Qt 兼容锁文件。</summary>
    private string _lockPath = "";
    /// <summary>是否存在正式文档修改。</summary>
    public bool Dirty => Document.Json() != _saved;
    /// <summary>初始化空项目。</summary>
    public DocumentSession() => Reset(new LevelDocument());
    /// <summary>替换文档并重新建立历史。</summary>
    public void Reset(LevelDocument document)
    { ReleaseLock(); Path = ""; Document = document; _saved = document.Json(); _history.Clear(); _history.Add(_saved); _position = 0; }
    /// <summary>完成一次编辑，保留最近一百个操作。</summary>
    public void Commit()
    {
        var json = Document.Json(); if (_history[_position] == json) return;
        _history.RemoveRange(_position + 1, _history.Count - _position - 1); _history.Add(json);
        if (_history.Count > 101) _history.RemoveAt(0); _position = _history.Count - 1;
    }
    /// <summary>恢复前一次操作。</summary>
    public bool Undo() { if (_position == 0) return false; Document = LevelDocument.Parse(_history[--_position]); return true; }
    /// <summary>恢复后一次操作。</summary>
    public bool Redo() { if (_position + 1 == _history.Count) return false; Document = LevelDocument.Parse(_history[++_position]); return true; }
    /// <summary>在验证成功并获得锁后切换项目。</summary>
    public void Open(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        var doc = LevelDocument.Parse(File.ReadAllText(path));
        if (!string.Equals(path, Path, StringComparison.OrdinalIgnoreCase)) Acquire(path);
        Path = path; Document = doc; _saved = doc.Json(); _history.Clear(); _history.Add(_saved); _position = 0;
    }
    /// <summary>保存到当前文件或新路径，失败不丢弃原项目的锁。</summary>
    public void Save(string path)
    {
        path = System.IO.Path.GetFullPath(path); var json = Document.Json(); LevelDocument.Parse(json);
        if (string.Equals(path, Path, StringComparison.OrdinalIgnoreCase)) LevelDocument.AtomicWrite(path, json);
        else
        {
            var oldLock = _lock; var oldLockPath = _lockPath; _lock = null; _lockPath = "";
            try { Acquire(path); LevelDocument.AtomicWrite(path, json); }
            catch { ReleaseLock(); _lock = oldLock; _lockPath = oldLockPath; throw; }
            oldLock?.Dispose(); if (!string.IsNullOrEmpty(oldLockPath) && File.Exists(oldLockPath)) File.Delete(oldLockPath);
        }
        Path = path; _saved = json;
    }
    /// <summary>创建 Qt QLockFile 兼容的侧车文件，同时持有独占文件句柄。</summary>
    private void Acquire(string path)
    {
        var lockPath = path + ".lock";
        FileStream candidate;
        try { candidate = new FileStream(lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read); }
        catch (IOException) { throw new IOException("该工程已有编辑锁，请关闭另一个编辑窗口。若此前异常退出，请确认没有程序编辑后删除对应 .lock 文件。"); }
        var bytes = System.Text.Encoding.UTF8.GetBytes($"{Environment.ProcessId}\n{System.Diagnostics.Process.GetCurrentProcess().ProcessName}\n{Environment.MachineName}\n");
        try { candidate.Write(bytes); candidate.Flush(true); } catch { candidate.Dispose(); File.Delete(lockPath); throw; }
        ReleaseLock(); _lock = candidate; _lockPath = lockPath;
    }
    /// <summary>关闭句柄后删除本实例创建的锁。</summary>
    private void ReleaseLock() { _lock?.Dispose(); _lock = null; if (_lockPath.Length > 0) { try { File.Delete(_lockPath); } catch (IOException) { } _lockPath = ""; } }
    /// <summary>退出时释放项目锁。</summary>
    public void Dispose() => ReleaseLock();
}

/// <summary>与旧编辑器一致的预设样式目录。</summary>
public static class Presets
{
    /// <summary>对用户显示的预设顺序。</summary>
    public static readonly string[] Names = { "绿化", "农田", "山地", "沙漠", "森林", "普通平原", "道路", "家具", "水域", "建筑", "刷怪点", "NPC", "移动路线" };
    /// <summary>创建仅包含样式的新对象，不预置几何。</summary>
    public static LevelObject Make(string key)
    {
        var o = new LevelObject { Name = key, Preset = key };
        var pair = key switch { "农田" => ("f1d775", "b58b31"), "山地" => ("bd926f", "77513b"), "沙漠" => ("efd181", "b38c37"), "森林" => ("356b48", "244d33"), "普通平原" => ("d7bea0", "9b8062"), "家具" => ("c9a477", "70533a"), "水域" => ("93d4e9", "4798b3"), "建筑" => ("dedbd0", "86847b"), "刷怪点" => ("f9dddd", "d77b82"), "NPC" => ("c4dcec", "42718b"), _ => ("a9d4bb", "355b50") };
        o.Fill = "#ff" + pair.Item1; o.Stroke = "#ff" + pair.Item2;
        if (key == "道路") { o.Kind = "polyline"; o.Stroke = "#ffa8acb1"; o.Width = 4; }
        if (key == "移动路线") { o.Kind = "polyline"; o.Stroke = "#ffed8a29"; }
        if (key is "刷怪点" or "NPC") { o.Kind = "points"; o.Diameter = key == "NPC" ? 2 : 3; o.Width = key == "NPC" ? .08 : .10; }
        if (key == "家具") o.Width = .10;
        return o;
    }
    /// <summary>提供真实可编辑的海滨街区示例，涵盖常见面和标记。</summary>
    public static LevelDocument Demo()
    {
        var doc = new LevelDocument { Title = "海滨街区 · 布局草案" };
        // 追加一个带指定轮廓的语义对象。
        void Add(string preset, string name, params double[] xy) { var o = Make(preset); o.Name = name; for (int i = 0; i < xy.Length; i += 2) o.Points.Add(new[] { xy[i], xy[i + 1] }); doc.Objects.Add(o); }
        Add("水域", "海湾", 54,-6, 82,-6, 82,52, 65,52, 61,38, 62,21);
        Add("普通平原", "滨海公共空间", -8,-6, 54,-6, 60,19, 59,38, 63,52, -8,52);
        Add("道路", "海风大道", -5,24, 48,24, 56,34);
        Add("道路", "商店街", 23,-3, 23,45);
        Add("绿化", "街角花园", -3,29, 15,29, 15,44, -3,44);
        Add("森林", "观景林地", 38,33, 50,33, 56,44, 36,47);
        Add("建筑", "冒险者公会", 0,0, 15,0, 15,17, 0,17);
        Add("建筑", "集市商铺", 31,0, 48,0, 48,16, 31,16);
        Add("建筑", "工匠作坊", 26,31, 32,31, 32,44, 26,44);
        Add("家具", "长椅", 3,32, 9,32, 9,33, 3,33);
        Add("NPC", "公会向导", 12,21); Add("NPC", "港口商人", 45,20);
        Add("移动路线", "巡逻路线", 4,24, 23,24, 23,40, 34,40);
        Add("刷怪点", "训练点", 48,39); return doc;
    }
}

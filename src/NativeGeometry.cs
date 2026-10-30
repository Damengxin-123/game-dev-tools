using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Godot;

namespace GameDevTools;

/// <summary>C++ 几何库的批量边界；所有数组跨边界仅传输一次。</summary>
public sealed class NativeGeometry : IDisposable
{
    /// <summary>本地场景所有权。</summary>
    private IntPtr _scene;
    /// <summary>加载项目或导出程序旁的原生库。</summary>
    static NativeGeometry()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeGeometry).Assembly, Resolve);
    }
    /// <summary>在编辑器与独立导出中定位本地二进制。</summary>
    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? search)
    {
        if (name != "level_geometry") return IntPtr.Zero;
        string filename = OperatingSystem.IsWindows() ? "level_geometry.dll" : "liblevel_geometry.so";
        foreach (var dir in new[] { ProjectSettings.GlobalizePath("res://bin"), System.IO.Path.Combine(AppContext.BaseDirectory, "bin"), System.IO.Path.GetDirectoryName(OS.GetExecutablePath())! })
            if (NativeLibrary.TryLoad(System.IO.Path.Combine(dir, filename), out var handle)) return handle;
        throw new DllNotFoundException("缺少 C++ 几何库，请先运行 构建.ps1。");
    }
    /// <summary>创建场景并校验接口版本。</summary>
    public NativeGeometry() { if (Version() != 1) throw new InvalidOperationException("几何库版本不一致。"); _scene = Create(); }
    /// <summary>按文档顺序构建拾取索引，只在对象变化时执行。</summary>
    public void Sync(LevelDocument document)
    { Clear(_scene); foreach (var o in document.Objects) Add(_scene, o.Flat, o.Points.Count, o.Kind == "points" ? 0 : o.Kind == "polyline" ? 1 : 2, o.Width, o.Diameter); }
    /// <summary>批量命中，返回最上层对象索引。</summary>
    public int Hit(double x, double y, double tolerance) => HitNative(_scene, x, y, tolerance);
    /// <summary>查找用于插入节点的最近边。</summary>
    public static int Nearest(LevelObject o, Vector2 p, double tolerance, out double[] result)
    { result = new double[2]; return NearestNative(o.Flat, o.Points.Count, o.Kind == "polygon" ? 1 : 0, p.X, p.Y, tolerance, result); }
    /// <summary>获取兼容奇偶填充的三角形顶点。</summary>
    public static double[] Tessellate(LevelObject o) => Take(TessellateNative(o.Flat, o.Points.Count, out int count), count);
    /// <summary>生成跨转角连续的虚线段。</summary>
    public static double[] Dashes(LevelObject o, double dash, double gap) => Take(DashesNative(o.Flat, o.Points.Count, dash, gap, out int count), count);
    /// <summary>转移本地数组并在异常路径释放内存。</summary>
    private static double[] Take(IntPtr data, int count)
    { if (data == IntPtr.Zero) return Array.Empty<double>(); try { var result = new double[count]; Marshal.Copy(data, result, 0, count); return result; } finally { Free(data); } }
    /// <summary>销毁几何索引。</summary>
    public void Dispose() { if (_scene != IntPtr.Zero) { Destroy(_scene); _scene = IntPtr.Zero; } }
    /// <summary>读取本地接口版本。</summary>
    [DllImport("level_geometry", EntryPoint = "vg_version", CallingConvention = CallingConvention.Cdecl)] private static extern int Version();
    /// <summary>创建本地场景。</summary>
    [DllImport("level_geometry", EntryPoint = "vg_create", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr Create();
    /// <summary>释放本地场景。</summary>
    [DllImport("level_geometry", EntryPoint = "vg_destroy", CallingConvention = CallingConvention.Cdecl)] private static extern void Destroy(IntPtr scene);
    /// <summary>清空本地索引。</summary>
    [DllImport("level_geometry", EntryPoint = "vg_clear", CallingConvention = CallingConvention.Cdecl)] private static extern void Clear(IntPtr scene);
    /// <summary>追加几何对象。</summary>
    [DllImport("level_geometry", EntryPoint = "vg_add", CallingConvention = CallingConvention.Cdecl)] private static extern void Add(IntPtr scene, double[] xy, int count, int kind, double width, double diameter);
    /// <summary>调用本地拾取。</summary>
    [DllImport("level_geometry", EntryPoint = "vg_hit", CallingConvention = CallingConvention.Cdecl)] private static extern int HitNative(IntPtr scene, double x, double y, double tolerance);
    /// <summary>调用边投影。</summary>
    [DllImport("level_geometry", EntryPoint = "vg_nearest", CallingConvention = CallingConvention.Cdecl)] private static extern int NearestNative(double[] xy, int count, int closed, double x, double y, double tolerance, double[] result);
    /// <summary>调用奇偶三角剖分。</summary>
    [DllImport("level_geometry", EntryPoint = "vg_tessellate", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr TessellateNative(double[] xy, int count, out int size);
    /// <summary>调用虚线细分。</summary>
    [DllImport("level_geometry", EntryPoint = "vg_dashes", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr DashesNative(double[] xy, int count, double dash, double gap, out int size);
    /// <summary>归还原生数组。</summary>
    [DllImport("level_geometry", EntryPoint = "vg_free", CallingConvention = CallingConvention.Cdecl)] private static extern void Free(IntPtr data);
}

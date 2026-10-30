# 优先运行已导出的独立软件；未导出时使用本地 .NET 引擎。
param([switch]$Editor)
$ErrorActionPreference = 'Stop'
$appPath = Join-Path $PSScriptRoot 'dist/游戏开发辅助工具.exe'
if (!$Editor -and (Test-Path -LiteralPath $appPath)) {
    # 恢复同一路径已启动的软件窗口，避免重复进程争用物品数据目录。
    $running = Get-Process -Name '游戏开发辅助工具' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $appPath }
    if ($running) {
        Add-Type -TypeDefinition @"
using System;
using System.Text;
using System.Runtime.InteropServices;
/// <summary>恢复工具套件已经存在的主窗口，保留其中尚未保存的编辑。</summary>
public static class GameDevToolsStartupWindow
{
    /// <summary>顶层窗口枚举回调。</summary>
    private delegate bool WindowCallback(IntPtr window, IntPtr state);
    /// <summary>枚举当前桌面顶层窗口。</summary>
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr state);
    /// <summary>取得窗口所属进程。</summary>
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    /// <summary>读取窗口标题以排除输入法等辅助窗口。</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int max);
    /// <summary>恢复隐藏或最小化的窗口。</summary>
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
    /// <summary>请求将已有窗口切换到前台。</summary>
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    /// <summary>显示指定进程的工具主窗口。</summary>
    public static bool Restore(uint processId)
    {
        bool found = false;
        EnumWindows((window, state) => {
            GetWindowThreadProcessId(window, out uint owner);
            if (owner != processId) return true;
            var title = new StringBuilder(512); GetWindowText(window, title, title.Capacity);
            if (!title.ToString().Contains("游戏开发辅助工具")) return true;
            ShowWindowAsync(window, 9); SetForegroundWindow(window); found = true; return false;
        }, IntPtr.Zero);
        return found;
    }
}
"@
        foreach ($instance in $running) {
            if ([GameDevToolsStartupWindow]::Restore([uint32]$instance.Id)) {
                Write-Output '已显示正在运行的软件窗口。'
                exit 0
            }
        }
        throw '软件进程正在启动，但尚未发现主窗口。请稍后重试；不会重复启动或终止已有编辑进程。'
    }
    # 此处启动的是用户需要操作的主界面，必须正常显示窗口。
    Start-Process -FilePath $appPath -WorkingDirectory $PSScriptRoot -WindowStyle Normal
    exit 0
}
$engine = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '.tools') -Recurse -Filter '*mono_win64.exe' | Select-Object -First 1
if (!$engine) { throw '未找到 Godot 4.6 .NET 引擎，请先安装到 .tools，或用 Godot .NET 导入 project.godot。' }
$arguments = @('--path', ('"' + $PSScriptRoot + '"'))
if ($Editor) { $arguments += '--editor' }
Start-Process -FilePath $engine.FullName -ArgumentList $arguments -WorkingDirectory $PSScriptRoot -WindowStyle Normal

# 构建原生几何库及 Godot C# 业务程序集。
param([switch]$Export)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$cmakeCommand = Get-Command cmake -ErrorAction SilentlyContinue
$cmakePath = if ($cmakeCommand) { $cmakeCommand.Source } else { 'I:\TOOL\VisualStudio2026\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe' }
if (!(Test-Path -LiteralPath $cmakePath)) { throw '未找到 CMake，请安装 Visual Studio C++ 桌面工作负载，并将 CMake 加入 PATH。' }
& $cmakePath -S native -B build/native -A x64
if ($LASTEXITCODE -ne 0) { throw 'C++ 配置失败。' }
& $cmakePath --build build/native --config Release
if ($LASTEXITCODE -ne 0) { throw 'C++ 编译失败。' }
dotnet build GameDevTools.csproj --nologo
if ($LASTEXITCODE -ne 0) { throw 'C# 编译失败。' }
if ($Export) {
    $engine = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '.tools') -Recurse -Filter '*mono*console.exe' | Select-Object -First 1
    if (!$engine) { throw '未找到 Godot .NET，请把 4.6 .NET 引擎解压到 .tools。' }
    New-Item -ItemType Directory -Path 'dist' -Force | Out-Null
    & $engine.FullName --headless --path $PSScriptRoot --export-release 'Windows 桌面' (Join-Path $PSScriptRoot 'dist/游戏开发辅助工具.exe') 2>&1 | Tee-Object -FilePath 'build/export.log'
    if ($LASTEXITCODE -ne 0 -or (Select-String -LiteralPath 'build/export.log' -Pattern '^ERROR:' -Quiet)) { throw 'Godot 导出失败，请查看 build/export.log。' }
    Copy-Item -LiteralPath 'bin/level_geometry.dll' -Destination 'dist/level_geometry.dll' -Force
}
Write-Output '构建完成。运行 启动.ps1 打开工具套件。'

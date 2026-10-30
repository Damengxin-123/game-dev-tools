# 执行跨语言核心流程验证；Visual 额外检查实际图形渲染和 PNG 导出。
param([switch]$Visual, [switch]$Mcp, [switch]$Items)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$engine = Get-ChildItem -LiteralPath '.tools' -Recurse -Filter '*mono*console.exe' | Select-Object -First 1
if (!$engine) { throw '请将 Godot 4.6 .NET 解压到 .tools。' }
if ($Items) {
    if ($Visual) { & $engine.FullName --path $PSScriptRoot -- --items-test --visual-test }
    else { & $engine.FullName --headless --path $PSScriptRoot -- --items-test }
} elseif ($Mcp) {
    if ($Visual) { & $engine.FullName --path $PSScriptRoot -- --mcp-test }
    else { & $engine.FullName --headless --path $PSScriptRoot -- --mcp-test }
} elseif ($Visual) {
    & $engine.FullName --path $PSScriptRoot -- --visual-test
} else {
    & $engine.FullName --headless --path $PSScriptRoot -- --smoke
}
if ($LASTEXITCODE -ne 0) { throw '验证失败，请检查以上输出。' }

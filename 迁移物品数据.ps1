# 将 ItemStudio 工作区完整复制到工具套件，保留源文件并逐文件校验。
param([string]$Source = 'I:\PROJECT\UnrealEngine\LxARPG\Tools\ItemStudio')
$ErrorActionPreference = 'Stop'
$sourceData = Join-Path $Source '数据'
$targetData = Join-Path $PSScriptRoot '数据/物品配置'
if (Test-Path -LiteralPath $targetData) { throw '目标物品数据目录已存在，拒绝覆盖。' }
$staging = Join-Path $PSScriptRoot ('数据/物品迁移-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $PSScriptRoot '数据/.gdignore'), '', [Text.UTF8Encoding]::new($false))
$before = (Get-FileHash -LiteralPath (Join-Path $sourceData '配置.json') -Algorithm SHA256).Hash
$files = Get-ChildItem -LiteralPath $sourceData -Recurse -File | Where-Object { $_.Name -ne '.工作区锁' }
$manifest = foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath($sourceData, $file.FullName)
    $target = Join-Path $staging $relative
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $hash) { throw "迁移校验失败：$relative" }
    [ordered]@{ file = $relative; sha256 = $hash; bytes = $file.Length }
}
if ((Get-FileHash -LiteralPath (Join-Path $sourceData '配置.json') -Algorithm SHA256).Hash -ne $before) { throw '迁移期间源配置发生变化，请重新迁移。临时副本已保留。' }
$db = Get-Content -LiteralPath (Join-Path $staging '配置.json') -Raw | ConvertFrom-Json
foreach ($asset in $db.assets) {
    foreach ($relative in @($asset.file, $asset.thumbnail)) {
        if (!(Test-Path -LiteralPath (Join-Path $staging $relative))) { throw "缺少图标：$relative" }
    }
}
$report = [ordered]@{ source = $sourceData; migratedAt = [DateTimeOffset]::Now.ToString('o'); items = $db.items.Count; affixes = $db.affixes.Count; assets = $db.assets.Count; attributes = $db.attributeCatalog.Count; revision = $db.revision; files = @($manifest) }
[IO.File]::WriteAllText((Join-Path $staging '迁移报告.json'), ($report | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
# 暂存目录与最终目录都固定在当前工程的数据目录中，整包完成后再发布。
[IO.Directory]::Move($staging, $targetData)
Write-Output "已迁移：$($db.items.Count) 个物品、$($db.affixes.Count) 个词条、$($db.assets.Count) 个图标，校验 $($files.Count) 个文件。"

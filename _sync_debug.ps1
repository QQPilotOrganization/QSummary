$ErrorActionPreference = 'Stop'

# 源路径
$srcUiBin    = 'c:\Users\Develop\QSummary\QsummaryUI\bin\Debug\net10.0'
$srcUiWww    = 'c:\Users\Develop\QSummary\QsummaryUI\wwwroot'
$srcUiCfg    = 'c:\Users\Develop\QSummary\QsummaryUI\config.ini'
$srcUiSys    = 'c:\Users\Develop\QSummary\QsummaryUI\system.txt'
$srcCoreBin  = 'c:\Users\Develop\QSummary\QQPilot4\bin\Debug\net10.0'
$srcGroupsDb = 'c:\Users\Develop\QSummary\QQPilot4\bin\Release\net10.0\groups.sqlite3'

# 目标路径（用户实际在跑的两个目录）
$targets = @(
    'c:\Users\Develop\QSummary\QQPilot4\bin\Debug\net10.0',
    'c:\Users\Develop\QSummary\Qsummary1.0\net10.0'
)

foreach ($dst in $targets) {
    Write-Host "`n=== 同步到: $dst ===" -ForegroundColor Cyan
    if (-not (Test-Path $dst)) { New-Item -ItemType Directory -Path $dst -Force | Out-Null }

    # 1) 从 QsummaryUI Debug 输出 复制 程序集
    foreach ($f in @('QsummaryUI.exe','QsummaryUI.dll','QsummaryUI.deps.json','QsummaryUI.runtimeconfig.json','QsummaryUI.staticwebassets.endpoints.json','QsummaryUI.staticwebassets.runtime.json')) {
        $s = Join-Path $srcUiBin $f
        if (Test-Path $s) {
            Copy-Item $s $dst -Force
            Write-Host "  [UI] $f"
        }
    }
    foreach ($f in @('QsummaryCore.exe','QsummaryCore.dll','QsummaryCore.deps.json','QsummaryCore.runtimeconfig.json')) {
        $s = Join-Path $srcCoreBin $f
        $d = Join-Path $dst $f
        if ((Test-Path $s) -and -not ([IO.Path]::GetFullPath($s) -ieq [IO.Path]::GetFullPath($d))) {
            Copy-Item $s $dst -Force
            Write-Host "  [Core] $f"
        }
    }

    # 2) 复制 wwwroot/ 整个目录（index.html / main.js / style.css 都是最新改的）
    $dstWww = Join-Path $dst 'wwwroot'
    if (-not (Test-Path $dstWww)) { New-Item -ItemType Directory -Path $dstWww -Force | Out-Null }
    Copy-Item (Join-Path $srcUiWww '*') $dstWww -Recurse -Force
    Write-Host "  [wwwroot] 已同步 $(Get-ChildItem $srcUiWww -Recurse -File | Measure-Object | Select-Object -ExpandProperty Count) 个文件"

    # 3) 复制 config.ini（含 [rag] 段新默认值）
    if (Test-Path $srcUiCfg) {
        Copy-Item $srcUiCfg $dst -Force
        Write-Host "  [INI] config.ini ($([IO.File]::ReadAllLines($srcUiCfg).Count) 行)"
    }

    # 4) 复制 system.txt（如果存在）
    if (Test-Path $srcUiSys) {
        Copy-Item $srcUiSys $dst -Force
        Write-Host "  [TXT] system.txt"
    }

    # 5) 复制测试用 groups.sqlite3（如果目标还没有或源更新）
    if (Test-Path $srcGroupsDb) {
        Copy-Item $srcGroupsDb $dst -Force
        $fi = Get-Item (Join-Path $dst 'groups.sqlite3')
        Write-Host "  [DB ] groups.sqlite3 ($($fi.Length) bytes)"
    }

    # 6) 确保 summaries 文件夹存在
    $sumDir = Join-Path $dst 'summaries'
    if (-not (Test-Path $sumDir)) { New-Item -ItemType Directory -Path $sumDir -Force | Out-Null; Write-Host "  [DIR] summaries/" }
}

Write-Host "`n✅ 同步完成" -ForegroundColor Green

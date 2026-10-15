<#
.SYNOPSIS
构建天气查询助手 → 单文件自包含 exe → 复制到 dist\ 目录。
用法：在仓库根目录运行 .\build\deploy.ps1
#>
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root "dist"

# 清理旧构建
if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }
New-Item -ItemType Directory -Force -Path $dist | Out-Null

# 发布单文件自包含 exe
Write-Host ">>> 正在发布单文件 exe …" -ForegroundColor Cyan
$publishArgs = @(
    "publish", (Join-Path $root "src\WeatherHelper.App\WeatherHelper.App.csproj"),
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "-p", "PublishSingleFile=true",
    "-o", $dist
)
& "dotnet" $publishArgs
# 验证
$exe = Join-Path $dist "WeatherHelper.exe"
if (Test-Path $exe) {
    $size = (Get-Item $exe).Length
    Write-Host ">>> 成功！$exe ($([math]::Round($size / 1MB, 1)) MB)" -ForegroundColor Green
    # 删掉单文件发布时附带的多余文件（runtimeconfig、deps 等），只保留 exe
    Get-ChildItem $dist -Exclude "WeatherHelper.exe" | Remove-Item -Recurse -Force
    $size2 = (Get-Item $exe).Length
    Write-Host ">>> 最终：$exe ($([math]::Round($size2 / 1MB, 1)) MB)" -ForegroundColor Green
} else {
    Write-Host ">>> 发布失败，未找到 WeatherHelper.exe" -ForegroundColor Red
    exit 1
}

# 复制 settings.json 模板（首次运行自动生成，这里只是标记）
Write-Host ">>> 完成。可在 $dist 找到 WeatherHelper.exe" -ForegroundColor Cyan
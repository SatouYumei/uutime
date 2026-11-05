# 发布新版本：版本号加一（或用 -Version 指定）→ 打包 → 算 SHA-256 → 提交、打 tag、推送 → 创建 GitHub Release。
# 客户端「检查更新」从 Release 下载安装包并用 .sha256 校验，所以两个文件都要传。
# 用法：powershell -ExecutionPolicy Bypass -File release.ps1 [-Version 1.2.0] [-Notes "更新说明"]
param([string]$Version = "", [string]$Notes = "")
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$csproj = "$root\uutime.csproj"

if (git -C $root status --porcelain) { throw "还有没提交的改动，先提交再发布" }

$text = [IO.File]::ReadAllText($csproj)
$current = [regex]::Match($text, '<Version>([^<]+)</Version>').Groups[1].Value
if (-not $Version) {
    $v = [version]$current
    $Version = "$($v.Major).$($v.Minor).$($v.Build + 1)"
}
if ([version]$Version -le [version]$current) { throw "新版本 $Version 必须大于当前的 $current" }
[IO.File]::WriteAllText($csproj, $text.Replace("<Version>$current</Version>", "<Version>$Version</Version>"), (New-Object Text.UTF8Encoding $false))

& "$root\build.ps1"
if ($LASTEXITCODE -ne 0) { throw "打包失败" }

$setup = "$root\dist\uutime-Setup-$Version.exe"
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
$sha = "$setup.sha256"
[IO.File]::WriteAllText($sha, "$hash  uutime-Setup-$Version.exe`n")

git -C $root commit -q -m "v$Version" -- uutime.csproj
git -C $root tag "v$Version"
git -C $root push -q origin HEAD --tags
if (-not $Notes) { $Notes = "uutime $Version" }
gh release create "v$Version" $setup $sha --repo SatouYumei/uutime --title "uutime $Version" --notes $Notes
Write-Host "已发布 v$Version，用户点「检查更新」就能看到并更新"

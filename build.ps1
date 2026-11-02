# 打包：发布程序，再用 Inno Setup 生成安装包，输出到 dist\
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

[xml]$proj = Get-Content "$root\uutime.csproj"
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version

$publish = "$root\build\publish"
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish "$root\uutime.csproj" -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败" }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "没找到 Inno Setup 6（ISCC.exe）" }

& $iscc "/DAppVersion=$version" "$root\installer\uutime.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup 编译失败" }
Write-Host "安装包：$root\dist\uutime-Setup-$version.exe"

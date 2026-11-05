# uutime

Windows 11 上的应用使用时间统计，常驻托盘，占用很低。

- **只记你添加的应用**：默认什么都不监控。
- **区分前台和后台**：窗口在前台、并且你在操作时算前台时间；进程开着但不在前台（或你离开了）算后台时间。
- **空闲判定**：一段时间没有键盘、鼠标或手柄（XInput）操作，就不再算前台，时长可调或关闭。
- **低占用**：前台切换靠系统事件推送，进程每 5 秒扫一次；托盘待机约 10~15MB 内存，CPU 几乎为零。窗口关掉或最小化后会释放内存。
- 今天 / 昨天 / 7 天 / 30 天统计，单个应用最近 14 天的前后台柱状图。
- 20 套配色主题，开机自启。

## 安装

到 [Releases](../../releases) 下载 `uutime-Setup-x.y.z.exe`。需要 [.NET 10 桌面运行时](https://dotnet.microsoft.com/download/dotnet/10.0)（x64），没装的话安装包会提示。

装在 `%LOCALAPPDATA%\Programs\uutime`，不需要管理员权限。记录保存在 `%LOCALAPPDATA%\uutime`，卸载时保留。

## 从源码编译

需要 .NET 10 SDK。

```bash
dotnet build -c Release
```

打安装包还需要 [Inno Setup 6](https://jrsoftware.org/isinfo.php)：

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
```

版本号在 `uutime.csproj` 的 `<Version>`，安装包输出到 `dist\`。

发布新版本（版本号加一、打包、算 SHA-256、打 tag、创建 GitHub Release）：

```bash
powershell -ExecutionPolicy Bypass -File release.ps1 -Notes "更新说明"
```

uutime 平时不联网。点侧边栏的「检查更新」才去 Releases 看最新版本；有新版本时按钮变成「更新」，再点才下载、校验 SHA-256，然后静默安装并重新启动。

设置环境变量 `UUTIME_DATA` 可以把数据目录指到别处，方便测试。

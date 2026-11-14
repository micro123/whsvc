# Wallhaven 壁纸服务（WinUI 3）

一个基于 **WinUI 3 / Windows App SDK** 的 Windows 桌面壁纸自动轮换工具。程序会从 Wallhaven 搜索图片、下载到本地、设为桌面壁纸，并在窗口关闭后继续驻留系统托盘。

## 技术栈

- .NET 10
- WinUI 3
- Windows App SDK 1.8
- 非打包桌面应用（unpackaged）
- x64

## 功能

- Wallhaven API 搜索与下载
- SFW / Sketchy / NSFW 内容纯度过滤
- General / Anime / People 分类过滤
- 最低分辨率、图片比例与关键词设置
- 定时自动轮换和立即抓取
- 当前壁纸预览、页面链接复制与浏览器打开
- 将当前壁纸保存到“图片”目录
- Windows 应用通知
- 系统托盘菜单：打开设置、立即抓取、保存当前图片、退出
- 缓存和设置持久化

## 构建

要求：

- Windows 10 1809 或更高版本
- .NET 10 SDK

```powershell
dotnet restore
dotnet build .\WallhavenService.csproj -c Debug -p:Platform=x64
```

生成目录：

```text
bin\x64\Debug\net10.0-windows10.0.19041.0\
```

## 运行

```powershell
dotnet run --project .\WallhavenService.csproj -c Debug -p:Platform=x64
```

或者直接运行生成目录中的 `WallhavenService.exe`。

项目配置了 `WindowsAppSDKSelfContained=true`，发布/复制时无需目标机器预先安装 Windows App Runtime。

## 使用说明

1. 输入一个或多个关键词（每行一个）。
2. 选择内容纯度、壁纸分类、最低分辨率和比例。
3. 如需 NSFW 内容，请配置 Wallhaven API Key。
4. 点击“保存设置”应用自动轮换配置，或点击“立即抓取”。
5. 关闭主窗口只会隐藏到系统托盘；需要完全退出时，请使用托盘菜单中的“退出”。

## 数据目录

应用数据保存在：

```text
%APPDATA%\WallhavenService\
```

其中包含设置、当前壁纸缓存和下载文件。

启动线程、窗口激活以及未处理的异常堆栈会记录到
`%LOCALAPPDATA%\WallhavenService\diagnostics.log`，日志达到 1 MiB 时保留一份上次日志。
原生组件直接终止进程时，还需结合 Windows 应用事件和崩溃转储排查。

程序入口必须保持同步的 `[STAThread] void Main`。不要改为 `async Task Main`，
否则编译器生成的实际入口不会携带 STA 标记，可能在 UI Automation 查询窗口时崩溃。
单实例激活重定向在工作线程执行，并通过 COM 等待完成，保持主线程的 STA 模型。
## GitHub Actions 安装包

项目中的 `.github/workflows/build-windows.yml` 会自动编译 Windows x64 self-contained 包，并生成两种 Artifacts：

- `WallhavenService-win-x64-数字`：绿色免安装 ZIP 包
- `WallhavenService-installer-数字`：Inno Setup 安装程序

在安装向导的“附加选项”页面可以选择“开机时自动启动 Wallhaven 壁纸服务”。该选项使用当前用户的启动目录，不需要管理员权限。

升级或重复安装时，安装程序会按完整安装路径检查并结束旧进程（包括驻留托盘的程序），
确认进程退出后才替换文件；即使 Windows Restart Manager 不可用也会执行此检查。
其他目录中的同名程序不会被结束。无法关闭时会阻止安装，提示先退出旧程序后重试。
完成后可勾选“启动 Wallhaven 壁纸服务”重新运行。尚未保存的界面设置需要在升级前保存。
安装日志默认保存在 `%TEMP%\Setup Log 日期*.txt`。

触发 GitHub Actions 后，在仓库的 **Actions → Build Windows self-contained** 运行记录底部即可下载。

# APISwitch

APISwitch 是一个基于 .NET 8 + WPF 的 Windows 桌面应用，用于可视化管理 Codex 和 Claude Code 的 API 供应商，并支持一键切换激活配置。

## 功能说明

- 供应商管理：按工具类型（Codex / Claude Code）分别管理供应商
- 数据持久化：使用 SQLite 存储 Name、BaseUrl、ApiKey、激活状态与排序
- 配置写入：
  - Codex：写入 `~/.codex/config.toml` 的 `[model_providers.OpenAI]` 段下 `base_url`，并写入 `~/.codex/auth.json` 的 `OPENAI_API_KEY`
  - Claude Code：写入 `~/.claude/settings.json` 的 `env.ANTHROPIC_AUTH_TOKEN` 与 `env.ANTHROPIC_BASE_URL`
- 自动备份：写入前自动生成 `.bak` 备份
- 系统托盘：关闭窗口后隐藏到托盘，支持托盘菜单显示主窗口与退出

## 技术栈

- .NET 8 (WPF, net8.0-windows)
- Microsoft.Data.Sqlite
- System.Text.Json

## 项目结构

```text
src/APISwitch/
  App.xaml(.cs)
  MainWindow.xaml(.cs)
  Models/Provider.cs
  Services/DatabaseService.cs
  Services/ConfigWriterService.cs
  Views/ProviderDialog.xaml(.cs)
  Assets/app.ico
```

## 构建与运行

1. 恢复依赖

```powershell
dotnet restore APISwitch.sln
```

2. 构建

```powershell
dotnet build APISwitch.sln
```

3. 运行

```powershell
dotnet run --project src/APISwitch/APISwitch.csproj
```

## 发布

执行根目录脚本：

```bat
repack.bat
```

发布结果：

- 产物路径：`.\Release\APISwitch.exe`
- 发布模式：单文件、`win-x64`、非自包含
- 自动清理：脚本结束后会删除 `src\APISwitch\bin` 和 `src\APISwitch\obj`

## 配置文件路径

- Codex: `~/.codex/config.toml`, `~/.codex/auth.json`
- Claude Code: `~/.claude/settings.json`
- APISwitch 数据库: `~/.APISwitch/apiswitch.db`

## 说明

- 若激活 Codex 供应商时缺少 `config.toml`，应用会提示“请先安装 Codex”，且不会创建该文件。
- `auth.json` 与 `settings.json` 在不存在时会自动创建。
- 当前发布为非自包含，目标机器需安装 `.NET 8 Desktop Runtime`。

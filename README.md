# APISwitch

APISwitch 用于可视化管理 **Codex**、**Claude Code** 和 **Grok Build** 的 API 供应商，并支持一键切换激活配置。  
当前仓库包含两套前端：

- 新版：`src/UI`（Avalonia，跨平台）
- 旧版：`src/WPF`（WPF/WinForms，仅 Windows）
- 共享核心：`src/Core`

## 功能说明

- 供应商管理：按工具类型（Codex / Claude Code / Grok）分别管理供应商，激活状态与排序按分类隔离
- 数据持久化：使用 SQLite 存储 Name、BaseUrl、ApiKey、激活状态、排序与测试状态（`TestStatus`）
- 供应商连通性测试：卡片支持“测试”按钮，分别请求 Codex / Claude / Grok 对应接口
- 测试状态点：供应商名称前显示状态点（绿色=可用，红色=失败，无文字），状态持久化到数据库
- 配置写入：
  - Codex：写入 `~/.codex/config.toml` 的 `[model_providers.OpenAI]` 段下 `base_url`，并写入 `~/.codex/auth.json` 的 `OPENAI_API_KEY`
  - Claude Code：写入 `~/.claude/settings.json` 的 `env.ANTHROPIC_AUTH_TOKEN` 与 `env.ANTHROPIC_BASE_URL`
  - Grok Build：写入 `~/.grok/config.toml` 的 `endpoints.models_base_url`、`endpoints.xai_api_base_url`，以及 `model."grok-4.5".api_key`
- 会话管理：扫描并管理 Codex / Claude / Grok 会话（列表、详情、恢复、删除）；Grok 会话目录为 `~/.grok/sessions`
- 设置页：可分别为三类工具配置默认测试模型、端点路径、Prompt 与客户端版本，并打开对应配置目录
- 自动备份：写入前自动生成 `.bak` 备份
- 系统托盘：关闭窗口后隐藏到托盘，支持托盘菜单显示主窗口与退出；托盘提示展示当前激活的供应商（含 Grok）

## 技术栈

- .NET 8
- 新版 UI：Avalonia 11（`net8.0`）
- 旧版 UI：WPF/WinForms（`net8.0-windows`）
- Core：`Microsoft.Data.Sqlite`

## 项目结构

```text
src/
  Core/                  # 共享业务与数据访问
  UI/                    # 新版 Avalonia 前端（跨平台）
  WPF/                   # 旧版 WPF 前端（Windows）
```

## 构建与运行

### 日常构建

直接构建（SDK 会在需要时自动还原 NuGet 包）：

```powershell
dotnet build APISwitch.sln
```

### 显式还原后再构建

首次克隆、清理 `obj`/`bin`、换机，或需要使用 `--no-restore` 时，**必须先还原**：

```powershell
dotnet restore APISwitch.sln
dotnet build APISwitch.sln --no-restore
```

不要跳过还原直接执行 `dotnet build ... --no-restore`。否则常见失败：

| 错误 | 含义 |
|------|------|
| `NETSDK1004` 找不到 `project.assets.json` | 该项目从未成功还原（例如 `src/WPF`） |
| `NU1101` 找不到 Avalonia / Microsoft.Data.Sqlite 等包 | 上次还原失败，留下了残缺的 assets |
| `NU1801` 无法加载 `https://api.nuget.org/v3/index.json` | 访问 nuget.org 失败（代理/防火墙/DNS）；源通后再 `dotnet restore` |

说明：`NU1101` 在 `NU1801` 之后出现时，多半是源不可达导致的误报，不是包不存在。网络恢复后重新 `dotnet restore APISwitch.sln` 即可。

### 运行

新版（推荐）：

```powershell
dotnet run --project src/UI/UI.csproj
```

旧版（仅 Windows）：

```powershell
dotnet run --project src/WPF/WPF.csproj
```

## 发布

新版 Avalonia 发布以 CI 为准

- 工作流：`.github/workflows/publish-cross-platform.yml`
- 触发方式：推送分支，或在 GitHub Actions 页面手动运行 `Publish Cross Platform`
- macOS：发布 `osx-x64` 自包含，打包为 `.app` 并生成 `.dmg`（含 Applications 快捷方式，可拖拽安装）
- Windows：发布 `win-x64` 单文件、非自包含，并打 zip
- 产物：workflow artifact 与 GitHub Release 附件

旧版 UI（WPF）如需本地发布：

```bat
repack.bat
```

- 项目：`src\WPF\WPF.csproj`
- 输出：`.\Release\APISwitch.exe`
- 参数：`win-x64`、单文件、`--self-contained false`

## PublishSingleFile 说明

- `PublishSingleFile=true`：输出以单个 `APISwitch.exe` 为主，便于分发
- 不设置该参数：输出为多文件目录（exe + 多个 dll），部署时需整体拷贝目录

## JSON 序列化约束

Avalonia 版存在裁剪/AOT 发布场景，默认反射序列化可能被禁用。新增或修改 JSON 代码时必须遵守：

- 不要在 `src/UI` 或 `src/Core` 的 Avalonia 运行路径中使用依赖默认反射的 `JsonSerializer.Serialize(obj)`、`JsonSerializer.Deserialize<T>(json)`、无参 `JsonNode.ToJsonString()`。
- 优先使用 `JsonSerializerContext` 源生成，并显式传入 `JsonTypeInfo` 或 `JsonSerializerContext.Default.Options`。
- `JsonNode` / `JsonObject` 生成字符串时，也必须传入包含相关值类型的源生成 options，例如字符串、布尔值、整数等。
- 否则裁剪/AOT 环境可能出现 `EmptyJsonTypeInfoResolver` 元数据缺失异常。

## 配置文件路径

- Codex: `~/.codex/config.toml`, `~/.codex/auth.json`
- Claude Code: `~/.claude/settings.json`
- Grok Build: `~/.grok/config.toml`；会话：`~/.grok/sessions`
- APISwitch 数据库: `~/.APISwitch/apiswitch.db`

## 说明

- 若激活 Codex 供应商时缺少 `config.toml`，应用会提示“请先安装 Codex”，且不会创建该文件。
- `auth.json`、`settings.json` 与 Grok 的 `config.toml` 在不存在时会自动创建（写入前会创建父目录）。
- 供应商配置被编辑后，测试状态会重置为未知（不显示状态点）。
- Grok 默认测试模型为 `grok-4.5`，测试请求对齐 grok-shell 的 Responses API；可在设置页或供应商级 TestModel 覆盖。
- Windows 非自包含发布需要目标机安装 .NET 运行时。

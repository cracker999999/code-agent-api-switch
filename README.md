# APISwitch

APISwitch 用于可视化管理 Codex 和 Claude Code 的 API 供应商，并支持一键切换激活配置。  
当前仓库包含两套前端：

- 新版：`src/UI`（Avalonia，跨平台）
- 旧版：`src/APISwitch`（WPF/WinForms，仅 Windows）
- 共享核心：`src/Core`

## 功能说明

- 供应商管理：按工具类型（Codex / Claude Code）分别管理供应商
- 数据持久化：使用 SQLite 存储 Name、BaseUrl、ApiKey、激活状态、排序与测试状态（`TestStatus`）
- 供应商连通性测试：卡片支持“测试”按钮，分别请求 Codex/Claude 对应接口
- 测试状态点：供应商名称前显示状态点（绿色=可用，红色=失败，无文字），状态持久化到数据库
- 配置写入：
  - Codex：写入 `~/.codex/config.toml` 的 `[model_providers.OpenAI]` 段下 `base_url`，并写入 `~/.codex/auth.json` 的 `OPENAI_API_KEY`
  - Claude Code：写入 `~/.claude/settings.json` 的 `env.ANTHROPIC_AUTH_TOKEN` 与 `env.ANTHROPIC_BASE_URL`
- 自动备份：写入前自动生成 `.bak` 备份
- 系统托盘：关闭窗口后隐藏到托盘，支持托盘菜单显示主窗口与退出

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
  APISwitch/             # 旧版 WPF 前端（Windows）
```

## 构建与运行

1. 恢复依赖

```powershell
dotnet restore APISwitch.sln
```

2. 构建（会尝试构建新旧两版）

```powershell
dotnet build APISwitch.sln
```

3. 运行新版（推荐）

```powershell
dotnet run --project src/UI/UI.csproj
```

4. 运行旧版（仅 Windows）

```powershell
dotnet run --project src/APISwitch/APISwitch.csproj
```

## 发布

新版 Avalonia 发布以 CI 为准

- 工作流：`.github/workflows/publish-cross-platform.yml`
- 触发方式：推送分支，或在 GitHub Actions 页面手动运行 `Publish Cross Platform`
- macOS：发布 `osx-x64` 自包含，并打包 `.app` + zip
- Windows：发布 `win-x64` 单文件、非自包含，并打 zip
- 产物：workflow artifact 与 GitHub Release 附件

旧版 UI（WPF）如需本地发布：

```bat
repack.bat
```

- 项目：`src\APISwitch\APISwitch.csproj`
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
- APISwitch 数据库: `~/.APISwitch/apiswitch.db`

## 说明

- 若激活 Codex 供应商时缺少 `config.toml`，应用会提示“请先安装 Codex”，且不会创建该文件。
- `auth.json` 与 `settings.json` 在不存在时会自动创建。
- 供应商配置被编辑后，测试状态会重置为未知（不显示状态点）。
- Windows 非自包含发布需要目标机安装 .NET 运行时。

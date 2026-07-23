## Why

APISwitch 当前只支持 Codex 和 Claude Code，用户无法在 Grok Build 的 API 供应商之间做同样的一键切换、测试和会话管理。

## What Changes

- 新增 Grok 分类，使用 `ToolType=2`，在 WPF 和 Avalonia 主窗口中作为第三个供应商标签展示。
- Grok 激活时写入 `%USERPROFILE%\.grok\config.toml`。
- Grok 配置写入 `models_base_url`、`xai_api_base_url`、`api_key` 三个 TOML 字段。
- Grok 测试请求使用默认模型 `grok-4.5`，默认客户端版本号 `0.2.111`，并支持供应商级 TestModel 覆盖。
- WPF 和 Avalonia 的设置页、供应商弹窗、托盘提示、会话管理窗口同步支持 Grok。
- Grok 会话管理扫描 `~\.grok\sessions`，支持列表、详情、恢复和删除。

## Capabilities

### New Capabilities
- `settings-dialog`: 设置页支持 Codex、Claude Code、Grok 三类工具的默认测试参数和配置目录入口。

### Modified Capabilities
- `provider-management`: Provider 的 ToolType 范围新增 Grok，并保持激活状态与排序按分类隔离。
- `config-writer`: 激活 Grok 供应商时写入 Grok Build 配置文件。
- `api-test`: 测试服务新增 Grok Responses API 请求。
- `main-window`: 主窗口新增 Grok 标签页并按当前标签新增、展示和排序供应商。
- `provider-dialog`: 新增/编辑供应商对话框支持 Grok 标题和默认测试模型占位。
- `session-service`: 会话服务新增 Grok 会话扫描、消息加载、恢复命令和删除。
- `session-window`: 会话管理窗口新增 Grok 标签页。
- `system-tray`: 托盘提示展示 Grok 当前激活供应商。

## Impact

- 影响 `src/Core` 的配置写入、测试请求、应用设置、会话服务和解析器。
- 影响 `src/WPF` 与 `src/UI` 的主窗口、设置页、供应商弹窗、会话窗口和托盘提示。
- 不改变现有 SQLite 表结构；历史数据库通过 `ToolType=2` 自然存储 Grok 供应商。

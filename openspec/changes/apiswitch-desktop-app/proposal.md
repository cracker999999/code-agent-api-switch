## Why

Codex CLI 和 Claude Code 用户经常需要在多个 API 供应商之间切换（如官方、中转站等），目前只能手动编辑配置文件（TOML/JSON），操作繁琐且容易出错。需要一个常驻系统托盘的桌面工具，提供可视化的供应商管理和一键切换能力。

## What Changes

- 新建 .NET 8 + WPF 桌面应用，包含完整的项目结构和解决方案文件
- 实现 SQLite 数据库存储供应商信息（名称、BaseUrl、ApiKey、工具类型、激活状态）
- 实现供应商的增删改查 UI（卡片列表 + 对话框）
- 实现 Codex 配置写入：正则替换 `config.toml` 中的 `base_url`，JSON 写入 `auth.json` 中的 `OPENAI_API_KEY`
- 实现 Claude Code 配置写入：修改 `settings.json` 中的 `ANTHROPIC_AUTH_TOKEN` 和 `ANTHROPIC_BASE_URL`
- 实现系统托盘常驻：关闭窗口最小化到托盘，托盘右键菜单控制显示/退出
- 写入前自动备份原配置文件（`.bak` 后缀）

## Capabilities

### New Capabilities
- `provider-management`: 供应商数据的 SQLite 存储与 CRUD 操作，包含数据模型和数据库服务
- `config-writer`: 配置文件写入逻辑，支持 Codex（TOML + JSON）和 Claude Code（JSON）两种格式，含备份机制
- `main-window`: 主窗口 UI，包含标签页切换、供应商卡片列表、启用/编辑/删除操作
- `provider-dialog`: 新增/编辑供应商的对话框
- `system-tray`: 系统托盘常驻，含右键菜单、最小化到托盘、双击恢复窗口

### Modified Capabilities

## Impact

- 新增 NuGet 依赖：Microsoft.Data.Sqlite
- 运行时读写用户目录下的配置文件：`~/.codex/config.toml`、`~/.codex/auth.json`、`~/.claude/settings.json`
- 运行时在应用目录创建 SQLite 数据库文件

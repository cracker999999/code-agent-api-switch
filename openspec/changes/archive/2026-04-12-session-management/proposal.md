## Why

Codex CLI 和 Claude Code 的对话会话以 `.jsonl` 文件散落在本地目录中，用户无法直观地浏览历史对话、查看聊天内容或清理过期会话。需要一个会话管理窗口，提供会话列表浏览、聊天详情查看和删除能力。

## What Changes

- 新增 `Models/SessionMeta.cs` 和 `Models/SessionMessage.cs` 数据模型
- 新增 `Services/SessionService.cs`，实现 Codex 和 Claude 会话的扫描、解析、删除
- 新增 `Views/SessionWindow.xaml(.cs)`，左右分栏布局：左侧会话列表 + 右侧聊天详情
- MainWindow 右上角增加"会话管理"按钮，点击打开 SessionWindow

## Capabilities

### New Capabilities
- `session-service`: 会话数据的扫描、JSONL 解析（Codex/Claude 两种格式）、删除操作
- `session-window`: 会话管理窗口 UI，包含标签切换、会话列表、聊天详情、删除交互

### Modified Capabilities
- `main-window`: 右上角新增"会话管理"按钮入口

## Impact

- 新增文件：`Models/SessionMeta.cs`、`Models/SessionMessage.cs`、`Services/SessionService.cs`、`Views/SessionWindow.xaml`、`Views/SessionWindow.xaml.cs`
- 修改文件：`MainWindow.xaml`（添加按钮）、`MainWindow.xaml.cs`（按钮事件）
- 运行时读取用户目录下的 `.jsonl` 文件：`~/.codex/sessions/`、`~/.claude/projects/`
- 删除操作涉及文件系统写操作（删除 .jsonl 文件及 Claude sidecar 目录）

## Why

当前供应商列表只能按既有顺序展示，用户无法按个人习惯调整常用供应商位置。需要提供直观的上移/下移能力，快速管理显示顺序。

## What Changes

- 在供应商卡片操作区新增“上移(↑)”和“下移(↓)”按钮
- 卡片操作按钮顺序明确为：`启用 | 测试 | 编辑 | 删除 | ↑ | ↓`
- 用户点击后在当前工具类型列表内调整顺序（Codex 与 Claude Code 各自独立）
- 边界项按钮禁用：首项不可上移，末项不可下移
- 调整后持久化 `SortOrder`，重启后顺序保持一致

## Capabilities

### New Capabilities


### Modified Capabilities
- `main-window`: 供应商卡片操作区新增上移/下移按钮与交互规则
- `provider-management`: 新增供应商顺序重排操作并持久化 `SortOrder`

## Impact

- 受影响代码：`MainWindow.xaml`、`MainWindow.xaml.cs`、`Services/DatabaseService.cs`
- 数据影响：复用现有 `SortOrder` 字段，无新增表结构
- 无新增第三方依赖

## ADDED Requirements

### Requirement: 供应商顺序重排
系统 SHALL 支持在同一 ToolType 内按记录顺序执行上移/下移重排。

#### Scenario: 上移重排
- **WHEN** 用户对某供应商执行上移操作
- **THEN** 系统 MUST 在同一 ToolType 范围内将该供应商与前一条记录交换 `SortOrder`

#### Scenario: 下移重排
- **WHEN** 用户对某供应商执行下移操作
- **THEN** 系统 MUST 在同一 ToolType 范围内将该供应商与后一条记录交换 `SortOrder`

#### Scenario: 跨 ToolType 隔离
- **WHEN** 用户在 Codex 列表执行顺序调整
- **THEN** 系统 MUST 不影响 Claude Code 列表记录的 `SortOrder`

### Requirement: 顺序调整持久化
顺序调整结果 SHALL 持久化到数据库并在重启后保持一致。

#### Scenario: 重启后顺序保持
- **WHEN** 用户完成顺序调整并重启应用
- **THEN** 系统按调整后的 `SortOrder` 展示供应商列表

## MODIFIED Requirements

### Requirement: 标签页切换
主窗口顶部 SHALL 显示 "Codex"、"Claude Code" 和 "Grok" 三个标签，点击切换显示对应 ToolType 的供应商列表。

#### Scenario: 切换到 Codex 标签
- **WHEN** 用户点击 "Codex" 标签
- **THEN** 列表显示所有 ToolType=0 的供应商卡片

#### Scenario: 切换到 Claude Code 标签
- **WHEN** 用户点击 "Claude Code" 标签
- **THEN** 列表显示所有 ToolType=1 的供应商卡片

#### Scenario: 切换到 Grok 标签
- **WHEN** 用户点击 "Grok" 标签
- **THEN** 列表显示所有 ToolType=2 的供应商卡片

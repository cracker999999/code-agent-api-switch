## ADDED Requirements

### Requirement: 托盘提示展示 Grok 激活供应商
托盘提示 SHALL 同时展示 Codex、Claude Code 和 Grok 的当前激活供应商名称。

#### Scenario: 刷新托盘提示
- **WHEN** 任一工具分类的供应商列表刷新
- **THEN** 托盘提示包含 Grok 当前激活供应商；若未启用则显示"未启用"

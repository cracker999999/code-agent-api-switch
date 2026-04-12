## ADDED Requirements

### Requirement: 会话管理按钮
主窗口右上角 SHALL 显示"会话管理"按钮，点击打开 SessionWindow。

#### Scenario: 点击会话管理按钮
- **WHEN** 用户点击右上角"会话管理"按钮
- **THEN** 系统以 `new SessionWindow { Owner = this }.Show()` 打开会话管理窗口

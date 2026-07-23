## MODIFIED Requirements

### Requirement: 会话管理窗口布局
SessionWindow SHALL 为 ~900x640 尺寸，CenterOwner 定位，顶栏包含标题和 Codex/Claude/Grok 选项卡。

#### Scenario: 打开会话管理窗口
- **WHEN** 用户点击主窗口“会话管理”按钮
- **THEN** 窗口以 ~900x640 尺寸居中于 MainWindow 显示，顶栏显示"会话管理"标题和 Codex/Claude/Grok 选项卡

## ADDED Requirements

### Requirement: Grok 会话标签切换
切换 Grok 选项卡 SHALL 重新扫描 Grok 会话目录并刷新列表。

#### Scenario: 切换到 Grok 选项卡
- **WHEN** 用户点击 Grok 选项卡
- **THEN** 系统调用 ScanGrokSessions() 并刷新左侧会话列表

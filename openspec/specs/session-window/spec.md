# session-window Specification

## Purpose
TBD - created by archiving change session-management. Update Purpose after archive.
## Requirements
### Requirement: 窗口基本属性
SessionWindow SHALL 为 ~900x640 尺寸，CenterOwner 定位，顶栏包含标题和 Codex/Claude/Grok 选项卡。

#### Scenario: 打开会话管理窗口
- **WHEN** 用户点击主窗口“会话管理”按钮
- **THEN** 窗口以 ~900x640 尺寸居中于 MainWindow 显示，顶栏显示"会话管理"标题和 Codex/Claude/Grok 选项卡

### Requirement: 左右分栏布局
SessionWindow SHALL 采用左右分栏布局，左侧 ~300px 显示会话列表，右侧显示聊天详情。

#### Scenario: 分栏渲染
- **WHEN** SessionWindow 加载完成
- **THEN** 左侧面板宽度约 300px 显示会话列表，右侧面板占据剩余空间显示聊天详情

### Requirement: 会话列表显示
左侧面板 SHALL 显示会话计数页眉和使用 VirtualizingStackPanel 的会话列表，每项显示标题和相对时间。

#### Scenario: 会话列表加载
- **WHEN** 选项卡切换或窗口首次加载
- **THEN** 左侧面板页眉显示会话总数，列表按 LastActiveAt 降序显示会话，每项显示标题和相对时间

#### Scenario: 无会话
- **WHEN** 扫描结果为空
- **THEN** 列表区域显示空状态

### Requirement: Codex 子代理分组
WPF 与 Avalonia 的 Codex 会话列表 SHALL 根据 ParentSessionId 将子代理折叠到对应主会话下，Claude Code 与 Grok 的列表行为保持不变。

#### Scenario: 主会话包含子代理
- **WHEN** Codex 扫描结果中存在 ParentSessionId 指向主会话 SessionId 的子代理
- **THEN** 列表默认只显示主会话，主会话摘要显示子代理数量，点击展开按钮后按缩进层级显示子代理

#### Scenario: 显示子代理身份
- **WHEN** 用户展开主会话的子代理
- **THEN** 子代理项使用 agent_path 的末级任务名作为标题，并在摘要中显示“subagent”和可用的 agent_nickname

#### Scenario: 子代理活动更新主会话排序
- **WHEN** 子代理的 LastActiveAt 晚于其主会话
- **THEN** 主会话使用自身及全部后代中的最新活动时间参与排序和相对时间显示

#### Scenario: 父会话不存在
- **WHEN** 子代理的 ParentSessionId 在当前扫描结果中找不到对应会话
- **THEN** 该子代理作为独立顶层项显示，不从列表中隐藏

#### Scenario: 非 Codex 会话列表
- **WHEN** 用户查看 Claude Code 或 Grok 选项卡
- **THEN** 会话继续按原有项目分组和平铺方式显示，不启用父子折叠

### Requirement: 选项卡切换重新扫描
切换 Codex/Claude 选项卡 SHALL 重新扫描对应目录并刷新列表。

#### Scenario: 切换到 Codex 选项卡
- **WHEN** 用户点击 Codex 选项卡
- **THEN** 系统调用 ScanCodexSessions() 并刷新左侧会话列表

#### Scenario: 切换到 Claude 选项卡
- **WHEN** 用户点击 Claude 选项卡
- **THEN** 系统调用 ScanClaudeSessions() 并刷新左侧会话列表

### Requirement: 选中会话异步加载消息
选中会话列表项 SHALL 通过 Task.Run 异步加载该会话的全部消息并在右侧显示。

#### Scenario: 选中会话
- **WHEN** 用户点击左侧列表中的某个会话
- **THEN** 右侧页眉显示会话标题和删除按钮，通过 Task.Run 异步加载消息并以聊天气泡形式显示

### Requirement: 聊天气泡样式
右侧详情面板 SHALL 以不同样式显示用户、助手、工具三种角色的消息。

#### Scenario: 用户消息样式
- **WHEN** 渲染 role=user 的消息
- **THEN** 显示蓝色背景、白色文字、靠右对齐的气泡

#### Scenario: 助手消息样式
- **WHEN** 渲染 role=assistant 的消息
- **THEN** 显示浅灰色背景、靠左对齐的气泡

#### Scenario: 工具消息样式
- **WHEN** 渲染 role=tool 的消息
- **THEN** 默认折叠显示，点击可展开查看内容

### Requirement: 删除会话交互
右侧页眉的删除按钮 SHALL 弹出确认对话框，确认后删除会话文件并刷新列表。

#### Scenario: 确认删除会话
- **WHEN** 用户点击删除按钮并在确认对话框中确认
- **THEN** 系统调用 DeleteSession 删除文件，Codex 会话同时删除其全部后代子代理，然后刷新左侧会话列表并清空右侧详情面板

#### Scenario: 取消删除
- **WHEN** 用户点击删除按钮后在确认对话框中取消
- **THEN** 不执行任何操作

### Requirement: 相对时间显示
会话列表中的时间 SHALL 以相对时间格式显示。

#### Scenario: 不足 1 分钟
- **WHEN** 会话 LastActiveAt 距当前不足 1 分钟
- **THEN** 显示"刚刚"

#### Scenario: 不足 60 分钟
- **WHEN** 会话 LastActiveAt 距当前不足 60 分钟
- **THEN** 显示"X 分钟前"

#### Scenario: 不足 24 小时
- **WHEN** 会话 LastActiveAt 距当前不足 24 小时
- **THEN** 显示"X 小时前"

#### Scenario: 不足 30 天
- **WHEN** 会话 LastActiveAt 距当前不足 30 天
- **THEN** 显示"X 天前"

#### Scenario: 超过 30 天
- **WHEN** 会话 LastActiveAt 距当前超过 30 天
- **THEN** 显示"YYYY/MM/DD"格式日期

### Requirement: 关闭窗口
关闭 SessionWindow SHALL 返回 MainWindow，不影响主程序运行。

#### Scenario: 关闭会话管理窗口
- **WHEN** 用户关闭 SessionWindow
- **THEN** 窗口关闭，MainWindow 保持正常运行

### Requirement: Grok 会话标签切换
切换 Grok 选项卡 SHALL 重新扫描 Grok 会话目录并刷新列表。

#### Scenario: 切换到 Grok 选项卡
- **WHEN** 用户点击 Grok 选项卡
- **THEN** 系统调用 ScanGrokSessions() 并刷新左侧会话列表

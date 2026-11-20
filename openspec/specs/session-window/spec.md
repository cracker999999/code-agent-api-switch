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

### Requirement: 会话搜索
WPF 与 Avalonia 的会话列表 SHALL 提供与主窗口供应商搜索框样式一致的搜索框，并按会话标题或完整 `ProjectDir` 进行不区分大小写的本地过滤。

#### Scenario: 按会话标题搜索
- **WHEN** 用户输入会话标题的部分文本
- **THEN** 列表仅显示标题包含该文本的会话

#### Scenario: 按项目目录搜索
- **WHEN** 用户输入 `ProjectDir` 的部分路径
- **THEN** 列表仅显示项目目录包含该路径的会话

#### Scenario: 清空搜索
- **WHEN** 用户清空搜索框
- **THEN** 列表恢复显示当前选项卡下的全部会话

### Requirement: 选中会话异步加载消息
选中会话列表项 SHALL 通过 Task.Run 异步加载该会话的全部消息并在右侧显示。

#### Scenario: 选中会话
- **WHEN** 用户点击左侧列表中的某个会话
- **THEN** 右侧页眉显示会话标题和删除按钮，通过 Task.Run 异步加载消息并以聊天气泡形式显示

### Requirement: 聊天气泡样式
右侧详情面板 SHALL 以不同样式显示用户、助手、工具和错误消息。

#### Scenario: 用户消息样式
- **WHEN** 渲染 role=user 的消息
- **THEN** 显示蓝色背景、白色文字、靠右对齐的气泡

#### Scenario: 助手消息样式
- **WHEN** 渲染 role=assistant 的消息
- **THEN** 显示浅灰色背景、靠左对齐的气泡

#### Scenario: 工具消息样式
- **WHEN** 渲染 role=tool 的消息
- **THEN** 默认折叠显示，点击可展开查看内容

#### Scenario: 错误消息样式
- **WHEN** WPF 或 Avalonia 渲染 role=error 的消息，包括 Codex 任务完成事件中的错误
- **THEN** 在原消息位置直接显示红色错误文字，并保留完整错误正文和发生时间

### Requirement: 助手回复 Markdown 渲染
WPF 与 Avalonia 的会话详情 SHALL 渲染助手回复中的标题、强调、列表、引用、代码块、链接和表格，并支持选择与复制文本。

#### Scenario: Markdown 回复
- **WHEN** role=assistant 的正文包含 Markdown 格式
- **THEN** 使用富文本呈现格式，保留消息角色、时间和已有图片附件

#### Scenario: 纯文本及其他消息
- **WHEN** 助手回复为纯文本，或消息角色为 user、tool、developer、error
- **THEN** 继续使用原有文本显示方式，保留原始正文、换行与空白，不修改会话文件

#### Scenario: 打开回复中的链接
- **WHEN** 用户点击 Markdown 链接
- **THEN** 仅允许打开 HTTP 或 HTTPS 网页，禁止启动本地文件或自定义协议，打开失败时提示错误

### Requirement: Codex 计划更新显示
WPF 与 Avalonia SHALL 在会话详情中按原始消息顺序直接显示计划更新，使用浅色背景和 Updated Plan 标题，文本支持选择与复制。

#### Scenario: 显示计划说明与步骤
- **WHEN** 计划包含 explanation 和多个步骤
- **THEN** 在标题下以斜体显示说明，再以缩进列表显示步骤；pending 使用空复选框，in_progress 使用空复选框和蓝色加粗文字，completed 使用勾选框
- **AND** 保留计划时间供悬停查看，缺少说明时不保留空说明行

#### Scenario: 多次更新计划
- **WHEN** 会话中多次调用 update_plan
- **THEN** 每次更新在对应位置显示当时的完整计划，不覆盖历史计划，也不折叠为普通工具结果

### Requirement: 以管理员身份恢复会话
WPF 与 Avalonia 在 Windows 上恢复 Codex、Claude Code 或 Grok 会话时 SHALL 请求管理员权限，使用 `-w 0 new-tab` 在最近使用的管理员 Windows Terminal 窗口中新增 PowerShell 标签页并执行对应恢复命令。

#### Scenario: 恢复三个 Agent 的会话
- **WHEN** 用户选中 Codex、Claude Code 或 Grok 会话并点击“恢复”
- **THEN** Windows 请求管理员授权，授权后在最近使用的管理员终端窗口中新增标签页并执行该 Agent 的恢复命令
- **AND** 保留所选会话的 Session ID 和项目工作目录

#### Scenario: 没有管理员终端窗口
- **WHEN** 当前没有可复用的管理员 Windows Terminal 窗口
- **THEN** Windows Terminal 新建管理员窗口承载该标签页，不将管理员会话加入普通权限窗口

#### Scenario: 拒绝管理员授权或启动失败
- **WHEN** 用户取消 Windows 管理员授权或终端启动失败
- **THEN** 系统显示恢复失败原因，不降级为普通权限启动

#### Scenario: 使用 Windows Terminal 应用别名
- **WHEN** Windows Terminal 在当前用户的 WindowsApps 目录中提供 wt.exe 应用别名
- **THEN** 系统使用该别名的完整路径请求管理员启动，避免名称解析到 App Paths 中的安装路径后未执行命令

#### Scenario: 在非 Windows 平台恢复会话
- **WHEN** 用户在 macOS 或 Linux 上点击“恢复”
- **THEN** 保持原有终端启动方式和恢复命令，不请求 Windows 管理员权限

### Requirement: 删除会话交互
右侧页眉的“删除”按钮 SHALL 弹出确认对话框，确认后删除会话文件并刷新列表。Codex 的确认文案 SHALL 说明同时删除全部子代理和清洗备份。

#### Scenario: 确认删除会话
- **WHEN** 用户点击删除按钮并在确认对话框中确认
- **THEN** 系统调用 DeleteSession 删除文件，Codex 会话同时删除其全部后代子代理及对应清洗备份，然后刷新左侧会话列表并清空右侧详情面板

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

### Requirement: 清洗会话交互
WPF 与 Avalonia 的会话详情操作区 SHALL 在选中 Codex 会话时显示“清洗”按钮。

#### Scenario: 清洗所选 Codex 会话
- **WHEN** 用户点击“清洗”
- **THEN** 系统异步清洗点击时选中的会话原文件，期间禁用详情操作按钮，完成后恢复按钮并显示清洗记录数及备份路径
- **AND** 保持选中的会话和现有对话详情，用户可继续点击“恢复”

#### Scenario: 无需清洗或清洗失败
- **WHEN** 会话没有加密记录或清洗发生错误
- **THEN** 系统分别显示无需清洗的提示或失败原因，并恢复详情操作按钮

#### Scenario: 隐藏不适用的清洗按钮
- **WHEN** 未选中会话或选中 Claude Code、Grok 会话
- **THEN** 不显示“清洗”按钮

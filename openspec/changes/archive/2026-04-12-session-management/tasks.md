## 1. 数据模型

- [x] 1.1 创建 `Models/SessionMeta.cs`（ProviderId, SessionId, Title, ProjectDir, CreatedAt, LastActiveAt, SourcePath）
- [x] 1.2 创建 `Models/SessionMessage.cs`（Role, Content, Timestamp）

## 2. SessionService 扫描与解析

- [x] 2.1 创建 `Services/SessionService.cs` 基础结构，实现头尾读取工具方法（前 10 行 + 后 30 行）
- [x] 2.2 实现 `ScanCodexSessions()`：遍历 `~/.codex/sessions/*.jsonl`，解析 `session_meta` 提取元数据，会话 ID 回退到文件名 UUID，标题取 cwd 基名，按 LastActiveAt 降序
- [x] 2.3 实现 Codex 消息解析：`response_item` 的 message/function_call/function_call_output 三种类型
- [x] 2.4 实现 `ScanClaudeSessions()`：递归扫描 `~/.claude/projects/**/*.jsonl`，跳过 `agent-` 开头文件，头部提取 sessionId/cwd/timestamp，尾部提取 last_active_at/summary/custom-title，标题优先级 custom-title > 目录基名
- [x] 2.5 实现 Claude 消息解析：角色重分类（全 tool_result 的 user → tool）、跳过 isMeta=true 条目
- [x] 2.6 实现解析容错：JSON 无效或格式损坏跳过该文件，无消息的文件跳过，目录不存在返回空列表

## 3. SessionService 删除

- [x] 3.1 实现 `DeleteSession()`：Codex 删除 .jsonl 文件，Claude 删除 .jsonl 文件 + 同名 sidecar 目录

## 4. SessionWindow UI

- [x] 4.1 创建 `Views/SessionWindow.xaml` 布局：~900x640 CenterOwner，顶栏标题 + Codex/Claude 选项卡，左右分栏 Grid
- [x] 4.2 实现左侧面板：会话计数页眉 + 带 VirtualizingStackPanel 的 ListBox，列表项显示标题和相对时间
- [x] 4.3 实现右侧面板：页眉（会话标题 + 删除按钮）+ 带聊天气泡的 ScrollViewer
- [x] 4.4 实现聊天气泡样式：用户蓝色背景白色文字靠右、助手浅灰靠左、工具默认折叠点击展开
- [x] 4.5 实现相对时间显示：刚刚 / X 分钟前 / X 小时前 / X 天前 / YYYY/MM/DD

## 5. SessionWindow 交互逻辑

- [x] 5.1 实现 `SessionWindow.xaml.cs`：选项卡切换触发重新扫描并刷新列表
- [x] 5.2 实现选中会话时通过 Task.Run 异步加载消息并渲染聊天气泡
- [x] 5.3 实现删除按钮：确认对话框 → 调用 DeleteSession → 刷新列表 → 清空详情面板

## 6. MainWindow 入口

- [x] 6.1 在 `MainWindow.xaml` 右上角添加"会话管理"按钮
- [x] 6.2 在 `MainWindow.xaml.cs` 中实现按钮点击事件：`new SessionWindow { Owner = this }.Show()`

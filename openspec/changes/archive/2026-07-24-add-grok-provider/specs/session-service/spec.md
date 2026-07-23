## ADDED Requirements

### Requirement: Grok 会话扫描
系统 SHALL 扫描 `~/.grok/sessions/` 目录下的本地会话目录，提取会话元数据。

#### Scenario: 扫描 Grok 会话目录
- **WHEN** 调用 `ScanGrokSessions()`
- **THEN** 系统遍历 Grok sessions 目录下包含 `chat_history.jsonl` 的会话目录，返回按 `LastActiveAt` 降序排列的 `List<SessionMeta>`

#### Scenario: Grok 目录不存在
- **WHEN** Grok sessions 目录不存在
- **THEN** 返回空列表，不报错

### Requirement: Grok JSONL 解析
系统 SHALL 从 Grok 会话目录中的 `chat_history.jsonl`、`summary.json` 和 `prompt_context.json` 解析会话 ID、项目目录、标题、时间和消息。

#### Scenario: 解析 Grok 元数据
- **WHEN** 会话目录包含 `summary.json` 或 `prompt_context.json`
- **THEN** 系统提取 session ID、工作目录、标题、时间并映射到 `SessionMeta`

#### Scenario: 解析 Grok 消息
- **WHEN** `chat_history.jsonl` 包含 role/content/text/message/payload 等常见消息字段
- **THEN** 系统提取用户、AI、工具和错误消息并映射到 `SessionMessage`

### Requirement: 恢复 Grok 会话
系统 SHALL 为 Grok 会话生成恢复命令。

#### Scenario: 生成 Grok 恢复命令
- **WHEN** 用户点击 Grok 会话的“恢复会话”
- **THEN** 系统启动终端执行 `grok -r <sessionId>`，工作目录为会话 ProjectDir

### Requirement: 删除 Grok 会话
系统 SHALL 删除 Grok 会话目录。

#### Scenario: 删除 Grok 会话
- **WHEN** 调用 `DeleteSession("grok", sessionId, sourcePath)`
- **THEN** 删除 `sourcePath` 所在的 Grok 会话目录

## ADDED Requirements

### Requirement: Codex 会话扫描
系统 SHALL 扫描 `~/.codex/sessions/` 目录下的 `.jsonl` 文件，提取会话元数据。

#### Scenario: 扫描 Codex 会话目录
- **WHEN** 调用 `ScanCodexSessions()`
- **THEN** 系统遍历 `~/.codex/sessions/` 下所有 `.jsonl` 文件，每文件仅读取前 10 行和后 30 行提取元数据，返回按 `LastActiveAt` 降序排列的 `List<SessionMeta>`

#### Scenario: Codex 目录不存在
- **WHEN** `~/.codex/sessions/` 目录不存在
- **THEN** 返回空列表，不报错

### Requirement: Claude 会话扫描
系统 SHALL 递归扫描 `~/.claude/projects/` 目录下的 `.jsonl` 文件，跳过以 `agent-` 开头的文件。

#### Scenario: 扫描 Claude 会话目录
- **WHEN** 调用 `ScanClaudeSessions()`
- **THEN** 系统递归扫描 `~/.claude/projects/` 下所有 `.jsonl` 文件，跳过文件名以 `agent-` 开头的文件，每文件仅读取前 10 行和后 30 行提取元数据，返回按 `LastActiveAt` 降序排列的 `List<SessionMeta>`

#### Scenario: Claude 目录不存在
- **WHEN** `~/.claude/projects/` 目录不存在
- **THEN** 返回空列表，不报错

### Requirement: Codex JSONL 解析
系统 SHALL 按照 codex.rs 规格解析 Codex 的 `.jsonl` 文件。

#### Scenario: 解析 session_meta 行
- **WHEN** 遇到 `type=session_meta` 的行
- **THEN** 提取 `payload.id` 作为 SessionId、`payload.cwd` 作为 ProjectDir、`payload.timestamp` 作为 CreatedAt

#### Scenario: 解析 response_item message
- **WHEN** 遇到 `type=response_item` 且 `payload.type=message` 的行
- **THEN** 角色从 `payload.role` 获取，内容从 `payload.content` 获取

#### Scenario: 解析 response_item function_call
- **WHEN** 遇到 `type=response_item` 且 `payload.type=function_call` 的行
- **THEN** 角色设为 assistant，内容设为 `[Tool: {name}]`

#### Scenario: 解析 response_item function_call_output
- **WHEN** 遇到 `type=response_item` 且 `payload.type=function_call_output` 的行
- **THEN** 角色设为 tool，内容从 `payload.output` 获取

#### Scenario: 会话 ID 回退
- **WHEN** 未找到 `session_meta` 行
- **THEN** 从文件名匹配 UUID 正则作为 SessionId

#### Scenario: 标题提取
- **WHEN** 提取 Codex 会话标题
- **THEN** 从 `cwd` 获取目录基名作为标题

### Requirement: Claude JSONL 解析
系统 SHALL 按照 claude.rs 规格解析 Claude 的 `.jsonl` 文件。

#### Scenario: 解析头部行
- **WHEN** 读取文件前 10 行
- **THEN** 提取 `sessionId`、`cwd`、`timestamp`

#### Scenario: 解析尾部行
- **WHEN** 读取文件后 30 行（反向）
- **THEN** 提取 `last_active_at`、`summary`、`custom-title`

#### Scenario: 标题优先级
- **WHEN** 确定 Claude 会话标题
- **THEN** 按 custom-title > 目录基名 的优先级选取

#### Scenario: 角色重分类
- **WHEN** 遇到 role=user 但内容项全为 `tool_result` 的消息
- **THEN** 将该消息角色归类为 tool

#### Scenario: 跳过 isMeta 条目
- **WHEN** 遇到 `isMeta=true` 的条目
- **THEN** 跳过该条目，不作为消息处理

### Requirement: JSONL 解析容错
系统 SHALL 在解析失败时跳过该文件。

#### Scenario: 解析失败
- **WHEN** 某 `.jsonl` 文件解析出错（格式损坏、JSON 无效等）
- **THEN** 跳过该文件，继续处理其他文件

#### Scenario: 无消息的文件
- **WHEN** 某 `.jsonl` 文件解析后无有效消息
- **THEN** 跳过该文件

### Requirement: SourcePath 绝对路径约束
系统 SHALL 在 `SessionMeta.SourcePath` 中保存 `.jsonl` 文件的绝对路径。

#### Scenario: 生成会话元数据
- **WHEN** 扫描并生成 `SessionMeta`
- **THEN** `SourcePath` MUST 为对应 `.jsonl` 文件的绝对路径

### Requirement: 删除 Codex 会话
系统 SHALL 删除 Codex 会话的 `.jsonl` 文件。

#### Scenario: 删除 Codex 会话
- **WHEN** 调用 `DeleteSession("codex", sessionId, sourcePath)`
- **THEN** 删除 `sourcePath` 指向的 `.jsonl` 文件

### Requirement: 删除 Claude 会话
系统 SHALL 删除 Claude 会话的 `.jsonl` 文件及同名 sidecar 目录。

#### Scenario: 删除 Claude 会话
- **WHEN** 调用 `DeleteSession("claude", sessionId, sourcePath)`
- **THEN** 删除 `sourcePath` 指向的 `.jsonl` 文件，并删除同名的 sidecar 目录（subagents、tool-results）

# session-service Specification

## Purpose
TBD - created by archiving change session-management. Update Purpose after archive.
## Requirements
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
- **WHEN** 遇到文件中的首个 `type=session_meta` 行
- **THEN** 提取 `payload.id` 作为 SessionId、`payload.cwd` 作为 ProjectDir、`payload.timestamp` 作为 CreatedAt

#### Scenario: 解析 Codex 子代理元数据
- **WHEN** 首个 `session_meta` 的 `payload.thread_source` 为 `subagent`，且 `payload.source.subagent.thread_spawn` 包含父线程信息
- **THEN** 系统提取 `parent_thread_id`、`agent_path`、`agent_nickname`，并将该会话标记为子代理

#### Scenario: 忽略子代理继承的主会话元数据
- **WHEN** 子代理 JSONL 在首个 `session_meta` 后包含继承自主会话的其他 `session_meta`
- **THEN** 后续元数据不得覆盖子代理自身的 SessionId 和父子关系

#### Scenario: 解析 response_item message
- **WHEN** 遇到 `type=response_item` 且 `payload.type=message` 的行
- **THEN** 角色从 `payload.role` 获取，内容从 `payload.content` 获取

#### Scenario: 解析 response_item function_call
- **WHEN** 遇到 `type=response_item` 且 `payload.type=function_call` 的行
- **THEN** 角色设为 assistant，内容设为 `[Tool: {name}]`

#### Scenario: 解析 response_item function_call_output
- **WHEN** 遇到 `type=response_item` 且 `payload.type=function_call_output` 的行
- **THEN** 角色设为 tool，内容从 `payload.output` 获取

#### Scenario: 解析 event_msg context_compacted
- **WHEN** 遇到 `type=event_msg` 且 `payload.type=context_compacted` 的行
- **THEN** 在消息流的对应位置插入一条角色为 assistant、内容为 `Context compacted` 的消息

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
- **THEN** 提取 `last_active_at`、`summary`、`custom-title`、`ai-title`

#### Scenario: 标题优先级
- **WHEN** 确定 Claude 会话标题
- **THEN** 按 custom-title > ai-title > 目录基名 的优先级选取

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
系统 SHALL 删除 Codex 会话的 `.jsonl` 文件及对应清洗备份，并递归删除该会话的全部后代子代理文件及其清洗备份。

#### Scenario: 删除 Codex 会话
- **WHEN** 调用 `DeleteSession("codex", sessionId, sourcePath)`
- **THEN** 删除 `sourcePath` 指向的 `.jsonl` 文件，以及 ParentSessionId 直接或间接指向该会话的全部子代理 `.jsonl` 文件，并删除这些文件对应的全部清洗备份

#### Scenario: 删除包含后代的子代理
- **WHEN** 删除的 Codex 子代理自身仍包含下级子代理
- **THEN** 系统仅递归删除该子代理及其后代和对应清洗备份，不删除其父会话、兄弟子代理及其备份

#### Scenario: 精确匹配清洗备份
- **WHEN** 删除指定 Codex 会话文件的备份
- **THEN** 系统仅删除同目录下由本工具生成的 `原文件名.jsonl.<32 位 GUID>.bak`，保留其他会话及相近文件名的文件

#### Scenario: 原文件已消失
- **WHEN** 删除的会话原文件已不存在，但同目录仍有对应清洗备份
- **THEN** 系统仍清理这些备份

#### Scenario: 备份删除失败
- **WHEN** 任一清洗备份因占用或权限等原因无法删除
- **THEN** 系统报告错误并保留对应会话原文件，允许用户重试

### Requirement: 删除 Claude 会话
系统 SHALL 删除 Claude 会话的 `.jsonl` 文件及同名 sidecar 目录。

#### Scenario: 删除 Claude 会话
- **WHEN** 调用 `DeleteSession("claude", sessionId, sourcePath)`
- **THEN** 删除 `sourcePath` 指向的 `.jsonl` 文件，并删除同名的 sidecar 目录（subagents、tool-results）

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

### Requirement: 清洗 Codex 会话
系统 SHALL 仅清洗指定 Codex 会话的原始 JSONL 文件，移除影响切换模型供应商后恢复会话的加密记录。

#### Scenario: 清洗加密推理与压缩记录
- **WHEN** 调用 `CleanSession("codex", sourcePath)`
- **THEN** 系统移除含 `encrypted_content` 属性的 `response_item` 推理或压缩记录，以及含该属性的 `compacted` 快照，包含嵌套历史中的加密内容
- **AND** 保留原始用户消息、助手消息、工具调用、纯摘要推理、空行及其他非加密记录，不因正文提到 `encrypted_content` 而删除记录
- **AND** 删除加密压缩快照后，恢复会话使用前文保留的原始历史

#### Scenario: 备份与安全写回
- **WHEN** 会话包含需要清洗的记录
- **THEN** 系统先完整生成 UTF-8 临时文件，再备份并原子替换原文件，返回删除记录数和独立的 `.bak` 备份路径
- **AND** 保留原文件的修改时间，不覆盖已有备份，不清洗其他会话或子代理文件

#### Scenario: 会话无需清洗
- **WHEN** 会话不包含需要清洗的加密记录
- **THEN** 返回零条记录，不改写原文件，不创建备份

#### Scenario: 无效内容或文件访问失败
- **WHEN** 文件不存在、被占用、包含无效 JSON 或无效 UTF-8，或清洗期间检测到文件发生变化
- **THEN** 系统报告错误，不用部分清洗结果覆盖原文件，并清理临时文件

#### Scenario: 不支持的会话类型
- **WHEN** 对非 Codex 会话调用清洗操作
- **THEN** 系统报告不支持该操作，不修改文件

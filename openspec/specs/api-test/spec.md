# api-test Specification

## Purpose
TBD - created by archiving change test-model. Update Purpose after archive.
## Requirements
### Requirement: Codex 供应商测试请求
系统 SHALL 向 `{BaseUrl}/responses` 发送流式 POST 请求验证 Codex 供应商连通性，使用供应商配置的 TestModel 作为模型 ID。

#### Scenario: Codex 测试请求使用自定义模型
- **WHEN** 用户测试一个 ToolType=0（Codex）的供应商且 TestModel 不为空
- **THEN** 系统发送 POST 请求，body 中 `model` 字段使用 `provider.TestModel` 的值

#### Scenario: Codex 测试请求使用默认模型
- **WHEN** 用户测试一个 ToolType=0（Codex）的供应商且 TestModel 为空
- **THEN** 系统发送 POST 请求，body 中 `model` 字段使用默认值 `gpt-5.3-codex`

### Requirement: Claude Code 供应商测试请求
系统 SHALL 向 `{BaseUrl}/v1/messages` 发送流式 POST 请求验证 Claude Code 供应商连通性，使用供应商配置的 TestModel 作为模型 ID。

#### Scenario: Claude Code 测试请求使用自定义模型
- **WHEN** 用户测试一个 ToolType=1（Claude Code）的供应商且 TestModel 不为空
- **THEN** 系统发送 POST 请求，body 中 `model` 字段使用 `provider.TestModel` 的值

#### Scenario: Claude Code 测试请求使用默认模型
- **WHEN** 用户测试一个 ToolType=1（Claude Code）的供应商且 TestModel 为空
- **THEN** 系统发送 POST 请求，body 中 `model` 字段使用默认值 `claude-opus-4-6`

### Requirement: 流式判定成功
系统 SHALL 使用 `HttpCompletionOption.ResponseHeadersRead` + `ReadAsStreamAsync` 实现流式读取，收到首个 stream chunk 即判定成功。

#### Scenario: 收到首个 chunk
- **WHEN** 请求返回 HTTP 2xx 且收到首个 stream chunk
- **THEN** 判定为成功，记录从请求发出到收到首个 chunk 的响应时间（ms）

### Requirement: 超时 30 秒
系统 SHALL 设置 30 秒请求超时。

#### Scenario: 请求超时
- **WHEN** 请求在 30 秒内未收到响应
- **THEN** 判定为失败，返回超时错误信息

### Requirement: HTTP 错误处理
系统 SHALL 在收到非 2xx 状态码时判定失败。

#### Scenario: HTTP 状态码非 2xx
- **WHEN** 请求返回非 2xx HTTP 状态码
- **THEN** 判定为失败，返回 HTTP 状态码和错误内容

### Requirement: ApiTestResult 返回结构
`TestProviderAsync` 方法 SHALL 返回 `ApiTestResult`，包含 `bool Success`、`string Message`、`long? ResponseTimeMs`。

#### Scenario: 成功结果
- **WHEN** 测试成功
- **THEN** 返回 `Success=true`、`Message` 为空或描述信息、`ResponseTimeMs` 为响应时间

#### Scenario: 失败结果
- **WHEN** 测试失败
- **THEN** 返回 `Success=false`、`Message` 为错误描述、`ResponseTimeMs=null`

### Requirement: 测试结果状态映射
系统 SHALL 将测试结果映射为可持久化状态码，供供应商卡片状态点展示使用。

#### Scenario: 测试成功映射
- **WHEN** `TestProviderAsync` 返回 `Success=true`
- **THEN** 调用方 MUST 将该供应商状态写入 `TestStatus=1`（可用）

#### Scenario: 测试失败映射
- **WHEN** `TestProviderAsync` 返回 `Success=false`
- **THEN** 调用方 MUST 将该供应商状态写入 `TestStatus=2`（失败）


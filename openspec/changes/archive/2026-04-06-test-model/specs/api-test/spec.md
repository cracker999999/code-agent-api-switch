## ADDED Requirements

### Requirement: Codex 供应商测试请求
系统 SHALL 向 `{BaseUrl}/responses` 发送流式 POST 请求验证 Codex 供应商连通性。

#### Scenario: Codex 测试请求格式
- **WHEN** 用户测试一个 ToolType=0（Codex）的供应商
- **THEN** 系统发送 POST 请求到 `{BaseUrl}/responses`，携带以下 headers：`authorization: Bearer {ApiKey}`、`content-type: application/json`、`accept: text/event-stream`、`accept-encoding: identity`、`user-agent: codex-tui/0.118.0 (Windows 10.0.19045; x86_64) WindowsTerminal (codex-tui; 0.118.0)`、`originator: codex_tui`，body 为 `{"model":"gpt-5.3-codex","input":[{"role":"user","content":"你是什么模型"}],"stream":true}`

### Requirement: Claude Code 供应商测试请求
系统 SHALL 向 `{BaseUrl}/v1/messages` 发送流式 POST 请求验证 Claude Code 供应商连通性。

#### Scenario: Claude Code 测试请求格式
- **WHEN** 用户测试一个 ToolType=1（Claude Code）的供应商
- **THEN** 系统发送 POST 请求到 `{BaseUrl}/v1/messages`，携带以下 headers：`authorization: Bearer {ApiKey}`、`x-api-key: {ApiKey}`、`anthropic-version: 2023-06-01`、`anthropic-beta: claude-code-20250219,interleaved-thinking-2025-05-14`、`anthropic-dangerous-direct-browser-access: true`、`content-type: application/json`、`accept: application/json`、`accept-encoding: identity`、`accept-language: *`、`user-agent: claude-cli/2.1.77 (external, cli)`、`x-app: cli`，body 为 `{"model":"claude-opus-4-6","max_tokens":1,"messages":[{"role":"user","content":"你是什么模型"}],"stream":true}`

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


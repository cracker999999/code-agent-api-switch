# 测试模型功能设计

## 用途

在供应商卡片上提供"测试"按钮，发送轻量级流式请求验证 API 和端点是否有效。

## 请求格式

### Codex（Responses API, ToolType=0）

- URL：`{BaseUrl}/responses`
- Headers：
  - `authorization: Bearer {ApiKey}`
  - `content-type: application/json`
  - `accept: text/event-stream`
  - `accept-encoding: identity`
  - `user-agent: codex-tui/0.118.0 (Windows 10.0.19045; x86_64) WindowsTerminal (codex-tui; 0.118.0)`
  - `originator: codex_tui`
- Body：
```json
{
  "model": "gpt-5.3-codex",
  "input": [{"role": "user", "content": "你是什么模型"}],
  "stream": true
}
```

### Claude Code（Anthropic Messages API, ToolType=1）

- URL：`{BaseUrl}/v1/messages`
- Headers：
  - `authorization: Bearer {ApiKey}`
  - `x-api-key: {ApiKey}`
  - `anthropic-version: 2023-06-01`
  - `anthropic-beta: claude-code-20250219,interleaved-thinking-2025-05-14`
  - `anthropic-dangerous-direct-browser-access: true`
  - `content-type: application/json`
  - `accept: application/json`
  - `accept-encoding: identity`
  - `accept-language: *`
  - `user-agent: claude-cli/2.1.77 (external, cli)`
  - `x-app: cli`
- Body：
```json
{
  "model": "claude-opus-4-6",
  "max_tokens": 1,
  "messages": [{"role": "user", "content": "你是什么模型"}],
  "stream": true
}
```

## 判定逻辑

- 超时 30 秒
- HTTP 状态码非 2xx → 失败，显示 HTTP 状态码和错误内容
- 收到首个 stream chunk → 成功，显示响应时间（ms）
- 使用 `HttpClient` + `HttpCompletionOption.ResponseHeadersRead` + `ReadAsStreamAsync` 实现流式读取

## UI 交互

- 卡片右侧操作按钮区域增加"测试"按钮，位于"启用"和"编辑"之间
- 点击后测试按钮变为 loading 状态（禁用，显示"测试中..."）
- 结果用 MessageBox 显示：
  - 成功：标题"测试成功"，内容"响应时间：{xxx} ms"
  - 失败：标题"测试失败"，内容"HTTP {状态码}: {错误信息}"或超时/连接错误描述

## 项目结构新增

- `Services/ApiTestService.cs` — 封装测试请求逻辑
  - 方法签名：`async Task<ApiTestResult> TestProviderAsync(Provider provider)`
  - 返回 `ApiTestResult` 包含：`bool Success`, `string Message`, `long? ResponseTimeMs`


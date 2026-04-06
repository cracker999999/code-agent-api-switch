## Context

APISwitch 桌面应用已实现供应商的增删改查和一键切换功能。用户在添加供应商后无法验证配置是否正确，需要增加 API 连通性测试能力。

## Goals / Non-Goals

**Goals:**
- 在供应商卡片上提供"测试"按钮，发送流式请求验证 API 端点和 Key 是否有效
- 支持 Codex（Responses API）和 Claude Code（Messages API）两种请求格式
- 以首个 stream chunk 判定成功，显示响应时间

**Non-Goals:**
- 不做完整对话测试，只验证连通性
- 不缓存测试结果
- 不支持自定义测试 prompt 或模型名

## Decisions

### 1. 流式请求判定
- 使用 `HttpClient` + `HttpCompletionOption.ResponseHeadersRead` + `ReadAsStreamAsync` 实现流式读取
- 收到首个 stream chunk 即判定成功，无需等待完整响应
- 备选：等待完整响应（浪费时间和 token）

### 2. 请求格式严格模拟真实客户端
- Codex：POST `{BaseUrl}/responses`，携带 `codex-tui` User-Agent 和 `originator` header
- Claude Code：POST `{BaseUrl}/v1/messages`，携带 `claude-cli` User-Agent、`anthropic-version`、`anthropic-beta` 等 header
- 确保中转站能正确识别和转发请求

### 3. 超时 30 秒
- 网络条件不确定，30 秒足够覆盖大部分场景
- 备选：可配置超时（过度设计，固定值足够）

### 4. 结果展示用 MessageBox
- 简单直接，无需额外 UI 组件
- 成功显示响应时间（ms），失败显示 HTTP 状态码和错误信息

### 5. 新增 ApiTestService 独立服务类
- `Services/ApiTestService.cs`，方法签名 `async Task<ApiTestResult> TestProviderAsync(Provider provider)`
- 返回 `ApiTestResult`：`bool Success`, `string Message`, `long? ResponseTimeMs`
- 与 ConfigWriterService 同级，职责清晰

## Risks / Trade-offs

- [中转站可能拒绝未知 User-Agent] → 严格模拟官方客户端的 header
- [测试消耗 token] → `max_tokens: 1`（Claude Code）/ 最小 input（Codex），消耗极低
- [ApiKey 在请求中明文传输] → 与正常使用工具时一致，HTTPS 保护传输层


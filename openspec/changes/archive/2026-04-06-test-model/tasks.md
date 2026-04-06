## 1. 数据模型

- [x] 1.1 创建 `ApiTestResult` 类（`bool Success`, `string Message`, `long? ResponseTimeMs`），放在 Models/ 或 Services/ 中

## 2. ApiTestService

- [x] 2.1 创建 `Services/ApiTestService.cs`，定义 `async Task<ApiTestResult> TestProviderAsync(Provider provider)` 方法
- [x] 2.2 实现 Codex 测试请求：POST `{BaseUrl}/responses`，携带完整 headers 和 body（model=gpt-5.3-codex, stream=true）
- [x] 2.3 实现 Claude Code 测试请求：POST `{BaseUrl}/v1/messages`，携带完整 headers 和 body（model=claude-opus-4-6, max_tokens=1, stream=true）
- [x] 2.4 实现流式判定逻辑：`HttpCompletionOption.ResponseHeadersRead` + `ReadAsStreamAsync`，收到首个 chunk 判定成功
- [x] 2.5 实现 30 秒超时、HTTP 非 2xx 错误处理、连接异常处理

## 3. 主窗口 UI 修改

- [x] 3.1 在 `MainWindow.xaml` 供应商卡片模板中，"启用"和"编辑"按钮之间增加"测试"按钮
- [x] 3.2 在 `MainWindow.xaml.cs` 中实现测试按钮点击事件：调用 ApiTestService，按钮进入 loading 状态（禁用 + 显示"测试中..."），完成后恢复
- [x] 3.3 实现结果 MessageBox 显示：成功标题"测试成功"内容"响应时间：{xxx} ms"，失败标题"测试失败"内容为错误描述



## Why

用户添加供应商后无法验证 API 配置是否正确（BaseUrl / ApiKey），只有切换激活并实际使用工具时才能发现问题。需要在卡片上提供"测试"按钮，发送轻量级流式请求快速验证连通性。

## What Changes

- 新增 `Services/ApiTestService.cs`，封装 Codex（Responses API）和 Claude Code（Messages API）的流式测试请求
- 供应商卡片操作按钮区域增加"测试"按钮，位于"启用"和"编辑"之间
- 点击测试后按钮进入 loading 状态，结果以 MessageBox 显示

## Capabilities

### New Capabilities
- `api-test`: 供应商 API 连通性测试，封装流式请求发送与结果判定

### Modified Capabilities
- `main-window`: 卡片操作按钮区域新增"测试"按钮及交互逻辑

## Impact

- 新增文件：`Services/ApiTestService.cs`
- 修改文件：`MainWindow.xaml`（卡片模板增加测试按钮）、`MainWindow.xaml.cs`（测试按钮事件处理）
- 使用 `HttpClient` 发送外部 HTTP 请求，依赖网络连通性

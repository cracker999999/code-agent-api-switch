## 1. 模型拉取服务

- [x] 1.1 新增 `ModelDiscoveryService` 与返回模型列表结果结构
- [x] 1.2 实现模型接口 URL 规则：默认 `{BaseUrl}/v1/models`，若 `BaseUrl` 含 `/v1` 则用 `{BaseUrl}/models`
- [x] 1.3 实现请求鉴权：使用当前供应商 `ApiKey`
- [x] 1.4 实现错误处理并返回可展示的错误信息

## 2. 编辑页面 UI 与交互

- [x] 2.1 在 `ProviderDialog.xaml` 增加“获取模型”按钮、搜索框、模型列表区和错误信息区
- [x] 2.2 在 `ProviderDialog.xaml.cs` 实现手动点击获取模型（含 loading 状态）
- [x] 2.3 实现模型列表完整展示（不截断）
- [x] 2.4 实现搜索框本地过滤逻辑
- [x] 2.5 实现失败错误信息在编辑页展示

## 3. 回归与验证

- [x] 3.1 构建验证：`dotnet restore` + `dotnet build`
- [x] 3.2 手工验证：`BaseUrl` 不含 `/v1` 时请求 `{BaseUrl}/v1/models`
- [x] 3.3 手工验证：`BaseUrl` 含 `/v1` 时请求 `{BaseUrl}/models`
- [x] 3.4 手工验证：列表显示全部模型、搜索过滤生效、失败信息可见



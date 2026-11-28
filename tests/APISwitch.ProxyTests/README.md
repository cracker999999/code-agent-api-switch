运行 Gemini 适配回归自检（无需新增测试框架）：

```pwsh
dotnet run --project tests/APISwitch.ProxyTests
```

使用临时数据库和随机本地端口模拟上游，不读取或修改真实供应商配置，不调用外部 API。
覆盖请求与响应转换、并行工具及签名回传、流式错误和用量、数据库迁移、模型分页、接口测试与原 Chat 适配。

## ADDED Requirements

### Requirement: Grok 供应商测试请求
系统 SHALL 向 Grok 供应商发送 Responses API 流式 POST 请求验证连通性，使用供应商配置的 TestModel 作为模型 ID。

#### Scenario: Grok 测试请求使用自定义模型
- **WHEN** 用户测试一个 ToolType=2（Grok）的供应商且 TestModel 不为空
- **THEN** 系统发送 POST 请求，body 中 `model` 字段使用 `provider.TestModel` 的值

#### Scenario: Grok 测试请求使用默认模型
- **WHEN** 用户测试一个 ToolType=2（Grok）的供应商且 TestModel 为空
- **THEN** 系统发送 POST 请求，body 中 `model` 字段使用默认值 `grok-4.5`

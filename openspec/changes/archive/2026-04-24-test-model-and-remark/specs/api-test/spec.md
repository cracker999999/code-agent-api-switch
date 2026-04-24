## MODIFIED Requirements

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

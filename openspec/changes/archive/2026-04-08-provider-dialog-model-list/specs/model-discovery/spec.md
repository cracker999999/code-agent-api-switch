## ADDED Requirements

### Requirement: 模型列表接口拼接
系统 SHALL 根据供应商 `BaseUrl` 规则拼接模型列表接口地址。

#### Scenario: BaseUrl 不包含 /v1
- **WHEN** `BaseUrl` 路径中不包含 `/v1`
- **THEN** 请求地址 MUST 为 `{BaseUrl}/v1/models`

#### Scenario: BaseUrl 包含 /v1
- **WHEN** `BaseUrl` 路径中已包含 `/v1`
- **THEN** 请求地址 MUST 为 `{BaseUrl}/models`

### Requirement: 模型列表请求与鉴权
系统 SHALL 使用当前供应商配置的 `ApiKey` 请求模型列表。

#### Scenario: 发起模型列表请求
- **WHEN** 用户在编辑页面点击“获取模型”
- **THEN** 系统向目标接口发送请求并携带该供应商 `ApiKey` 鉴权

### Requirement: 模型列表结果返回
系统 SHALL 返回并展示接口返回的全部可用模型，不截断。

#### Scenario: 拉取成功
- **WHEN** 模型接口返回成功响应
- **THEN** 系统显示全部可用模型项

### Requirement: 请求失败错误信息
系统 SHALL 在请求失败时返回可读错误信息。

#### Scenario: HTTP 或连接错误
- **WHEN** 请求失败（HTTP 非成功状态、超时或连接异常）
- **THEN** 系统返回错误信息并供 UI 展示

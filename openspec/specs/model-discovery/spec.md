# model-discovery Specification

## Purpose
TBD - created by archiving change provider-dialog-model-list. Update Purpose after archive.
## Requirements
### Requirement: 模型列表接口拼接
系统 SHALL 根据供应商 `BaseUrl` 规则拼接模型列表接口地址。

#### Scenario: BaseUrl 不包含 /v1
- **WHEN** `BaseUrl` 路径中不包含 `/v1`
- **THEN** 请求地址 MUST 为 `{BaseUrl}/v1/models`

#### Scenario: BaseUrl 包含 /v1
- **WHEN** `BaseUrl` 路径中已包含 `/v1`
- **THEN** 请求地址 MUST 为 `{BaseUrl}/models`

### Requirement: 模型列表请求与鉴权
系统 SHALL 使用当前供应商配置的 `ApiKey` 请求模型列表，并按工具类型复用 API 测试请求头。

#### Scenario: 发起模型列表请求
- **WHEN** 用户在编辑页面点击“获取模型”
- **THEN** 系统向目标接口发送请求，并携带该供应商对应 API 测试请求所用的请求头

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

### Requirement: 按模型 ID 查找供应商
系统 SHALL 根据用户输入的模型 ID，以每批最多 10 个供应商并发的方式查询 Codex、Claude Code、Grok 分类下的全部已保存供应商，不受主窗口标签和本地搜索条件影响。

#### Scenario: 分批并发查询
- **WHEN** 用户输入非空模型 ID 并开始查询
- **THEN** 系统去除输入首尾空白，按工具分类及分类内已有排序划分批次，每批最多并发请求 10 个供应商，同一窗口的查询最多有 10 个进行中的请求
- **AND** 当前批次的所有请求完成或失败后才开始下一批，最后一批不足 10 个时只请求剩余供应商
- **AND** 批内每个请求完成后立即返回其结果，不等待其他慢请求
- **AND** 每次请求复用已有模型列表地址拼接规则、鉴权和 30 秒超时

#### Scenario: 匹配模型
- **WHEN** 供应商返回的模型列表包含输入内容的模型 ID（忽略大小写）
- **THEN** 系统立即将该供应商加入匹配结果并展示实际匹配的模型 ID，每个供应商只显示一次

#### Scenario: 模型不匹配
- **WHEN** 所有模型 ID 均不包含输入内容，或供应商返回空模型列表
- **THEN** 系统不将该供应商加入匹配结果

#### Scenario: 单个供应商失败
- **WHEN** 某个供应商的配置无效、请求超时、连接失败、HTTP 非成功、JSON 无法解析或响应缺少 data 数组
- **THEN** 系统记录并显示该供应商的错误，继续查询后续供应商
- **AND** 查询失败不得被当成已确认不提供该模型

#### Scenario: 停止或关闭窗口
- **WHEN** 用户停止查询或关闭查询窗口
- **THEN** 系统取消本批所有进行中的请求并停止请求后续批次，停止时保留已获得的结果

#### Scenario: 空输入
- **WHEN** 用户未输入模型 ID 或仅输入空白
- **THEN** 系统提示输入模型 ID，不发起任何请求

#### Scenario: 只读查询
- **WHEN** 查询完成、失败或被取消
- **THEN** 系统不修改供应商的激活状态、测试状态、测试模型或客户端配置

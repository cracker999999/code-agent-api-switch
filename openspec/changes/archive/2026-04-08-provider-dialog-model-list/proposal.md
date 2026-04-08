## Why

当前用户在编辑供应商时无法直接查看该供应商可用模型，只能离开应用手动请求接口确认。需要在编辑页面内提供即时模型列表与搜索，减少配置试错。

## What Changes

- 在供应商编辑页面增加“获取模型”按钮，手动触发模型拉取
- 模型接口规则：默认 `{BaseUrl}/v1/models`；若 `BaseUrl` 已包含 `/v1` 则使用 `{BaseUrl}/models`
- 使用当前供应商配置的 `ApiKey` 发起请求
- 列表展示返回的全部模型，支持本地搜索过滤
- 请求失败时在编辑页面显示错误信息
- 不将模型列表持久化到 SQLite

## Capabilities

### New Capabilities
- `model-discovery`: 供应商模型列表拉取、解析与过滤展示能力

### Modified Capabilities
- `provider-dialog`: 编辑对话框新增模型获取、搜索与错误展示交互

## Impact

- 受影响代码：`Views/ProviderDialog.xaml`、`Views/ProviderDialog.xaml.cs`、`Services/`（新增模型拉取服务）
- 网络影响：新增 `GET /models` 查询请求
- 数据影响：无数据库结构变化，无持久化新增

## Why

当前“测试”能力只在点击时给出弹窗反馈，卡片上没有持续状态指示，用户切换列表后无法快速判断供应商可用性。需要把测试结果以可视化状态点长期保留在卡片上，降低重复测试成本。

## What Changes

- 在供应商卡片名称前增加测试状态点显示：仅两种状态，绿色（可用）和红色（失败）
- 状态点不显示文字，不显示响应时间，不显示错误摘要
- 测试结果持久化到 SQLite：仅存储最后状态（成功/失败）
- 未测试或未知状态不显示状态点
- 编辑供应商配置（Name/BaseUrl/ApiKey）后重置为未知状态，避免显示过期状态

## Capabilities

### New Capabilities


### Modified Capabilities
- `api-test`: 测试完成后除弹窗外，还需要写入并维护最后测试状态
- `main-window`: 卡片名称前新增状态点展示规则（仅红/绿，无文字）
- `provider-management`: Provider 数据模型与数据库结构新增测试状态字段并参与 CRUD

## Impact

- 受影响代码：`Models/Provider.cs`、`Services/DatabaseService.cs`、`Services/ApiTestService.cs`、`MainWindow.xaml`、`MainWindow.xaml.cs`
- 受影响存储：`Providers` 表新增测试状态字段（带默认值并兼容老库）
- 无新增第三方依赖

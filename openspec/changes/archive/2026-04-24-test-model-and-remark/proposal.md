## Why

测试功能的模型 ID 当前硬编码，不同供应商支持的模型不同，无法灵活测试。同时供应商列表缺少备注字段，多个同类型供应商难以区分。

## What Changes

- Provider 数据模型新增 `TestModel` 和 `Remark` 两个字段
- 供应商编辑对话框新增"测试模型"和"备注"输入框
- 测试服务使用供应商配置的模型 ID 替代硬编码值
- 供应商列表名字旁显示备注（浅色小字）
- 数据库 ALTER TABLE 添加对应列

## Capabilities

### New Capabilities

（无新增能力，均为已有能力的修改）

### Modified Capabilities

- `provider-management`: 新增 TestModel 和 Remark 字段，数据库 schema 变更
- `provider-dialog`: 新增"测试模型"和"备注"输入框
- `api-test`: 使用 provider.TestModel 替代硬编码模型 ID
- `main-window`: 供应商列表项显示备注文字

## Impact

- `Models/Provider.cs` — 加字段
- `Services/DatabaseService.cs` — ALTER TABLE
- `Views/ProviderDialog.xaml` + `.cs` — 加输入框和绑定
- `Services/ApiTestService.cs` — 读取 TestModel
- `MainWindow.xaml` — 列表项模板加备注显示

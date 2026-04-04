# provider-dialog Specification

## Purpose
TBD - created by archiving change apiswitch-desktop-app. Update Purpose after archive.
## Requirements
### Requirement: 供应商对话框字段
对话框 SHALL 包含 Name、BaseUrl、ApiKey 三个输入字段。

#### Scenario: 新增模式
- **WHEN** 用户通过 `+` 按钮打开对话框
- **THEN** 对话框所有字段为空，标题显示"新增供应商"

#### Scenario: 编辑模式
- **WHEN** 用户通过编辑按钮打开对话框
- **THEN** 对话框预填当前供应商的 Name、BaseUrl、ApiKey，标题显示"编辑供应商"

### Requirement: 对话框确认与取消
对话框 SHALL 包含确认和取消按钮。

#### Scenario: 确认提交
- **WHEN** 用户填写完信息并点击确认
- **THEN** 系统保存数据并关闭对话框，刷新供应商列表

#### Scenario: 取消操作
- **WHEN** 用户点击取消
- **THEN** 对话框关闭，不保存任何数据


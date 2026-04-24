## MODIFIED Requirements

### Requirement: 供应商对话框字段
对话框 SHALL 包含 Name、BaseUrl、ApiKey、TestModel、Remark 五个输入字段，并包含模型获取与列表展示区域。

#### Scenario: 新增模式
- **WHEN** 用户通过 `+` 按钮打开对话框
- **THEN** 对话框所有字段为空，标题显示"新增供应商"

#### Scenario: 编辑模式
- **WHEN** 用户通过编辑按钮打开对话框
- **THEN** 对话框预填当前供应商的 Name、BaseUrl、ApiKey、TestModel、Remark

## ADDED Requirements

### Requirement: 测试模型输入框
对话框 SHALL 在 ApiKey 下方显示"测试模型"输入框，用于配置该供应商的测试用模型 ID。

#### Scenario: 测试模型输入框位置
- **WHEN** 对话框渲染完成
- **THEN** "测试模型"输入框位于 ApiKey 输入框下方

#### Scenario: 测试模型可留空
- **WHEN** 用户未填写测试模型
- **THEN** 系统允许提交，TestModel 存储为空

### Requirement: 备注输入框
对话框 SHALL 在名称输入框右侧显示"备注"输入框，两者同一行。

#### Scenario: 备注输入框位置
- **WHEN** 对话框渲染完成
- **THEN** "备注"输入框位于名称输入框右侧，同一行显示

#### Scenario: 备注可留空
- **WHEN** 用户未填写备注
- **THEN** 系统允许提交，Remark 存储为空

# provider-dialog Specification

## Purpose
TBD - created by archiving change apiswitch-desktop-app. Update Purpose after archive.
## Requirements
### Requirement: 供应商对话框字段
对话框 SHALL 包含 Name、BaseUrl、ApiKey、TestModel、Remark 五个输入字段，并包含模型获取与列表展示区域。

#### Scenario: 新增模式
- **WHEN** 用户通过 `+` 按钮打开对话框
- **THEN** 对话框所有字段为空，标题显示"新增供应商"

#### Scenario: 编辑模式
- **WHEN** 用户通过编辑按钮打开对话框
- **THEN** 对话框预填当前供应商的 Name、BaseUrl、ApiKey、TestModel、Remark

### Requirement: 对话框确认与取消
对话框 SHALL 包含确认和取消按钮。

#### Scenario: 确认提交
- **WHEN** 用户填写完信息并点击确认
- **THEN** 系统保存数据并关闭对话框，刷新供应商列表

#### Scenario: 取消操作
- **WHEN** 用户点击取消
- **THEN** 对话框关闭，不保存任何数据

### Requirement: 手动获取模型
对话框 SHALL 仅在用户点击“获取模型”时请求模型列表。

#### Scenario: 点击获取模型
- **WHEN** 用户点击“获取模型”按钮
- **THEN** 系统进入加载状态并发起模型列表请求

### Requirement: 模型搜索过滤
对话框 SHALL 支持对已获取模型列表进行本地搜索过滤。

#### Scenario: 输入搜索关键字
- **WHEN** 用户在搜索框输入关键字
- **THEN** 模型列表实时按关键字过滤显示

### Requirement: 错误信息展示
模型请求失败时对话框 SHALL 显示错误信息。

#### Scenario: 模型请求失败
- **WHEN** 模型接口请求失败
- **THEN** 对话框在模型区域显示错误信息

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

### Requirement: Grok 供应商对话框
供应商对话框 SHALL 在 Grok 新增和编辑场景下使用 Grok 分类上下文。

#### Scenario: 新增 Grok 供应商
- **WHEN** 用户在 Grok 标签页点击新增按钮
- **THEN** 对话框标题显示"新增供应商（Grok）"，提交后供应商 ToolType 为 2

#### Scenario: Grok 默认测试模型占位
- **WHEN** 用户编辑 Grok 供应商且 TestModel 为空
- **THEN** 测试模型输入框占位显示 Grok 全局默认测试模型


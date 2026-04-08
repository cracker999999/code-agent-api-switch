## MODIFIED Requirements

### Requirement: 供应商对话框字段
对话框 SHALL 包含 Name、BaseUrl、ApiKey 三个输入字段，并包含模型获取与列表展示区域。

#### Scenario: 新增模式
- **WHEN** 用户通过 `+` 按钮打开对话框
- **THEN** 对话框所有字段为空，标题显示"新增供应商"

#### Scenario: 编辑模式
- **WHEN** 用户通过编辑按钮打开对话框
- **THEN** 对话框预填当前供应商的 Name、BaseUrl、ApiKey，标题显示"编辑供应商"

#### Scenario: 模型列表区域展示
- **WHEN** 对话框渲染完成
- **THEN** 页面显示“获取模型”按钮、模型列表区域和搜索输入框

## ADDED Requirements

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

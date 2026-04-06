# main-window Specification

## Purpose
TBD - created by archiving change apiswitch-desktop-app. Update Purpose after archive.
## Requirements
### Requirement: 标签页切换
主窗口顶部 SHALL 显示 "Codex" 和 "Claude Code" 两个标签，点击切换显示对应 ToolType 的供应商列表。

#### Scenario: 切换到 Codex 标签
- **WHEN** 用户点击 "Codex" 标签
- **THEN** 列表显示所有 ToolType=0 的供应商卡片

#### Scenario: 切换到 Claude Code 标签
- **WHEN** 用户点击 "Claude Code" 标签
- **THEN** 列表显示所有 ToolType=1 的供应商卡片

### Requirement: 供应商卡片显示
每张供应商卡片 SHALL 显示名称（大字）、BaseUrl（小字灰色），右侧包含启用、测试、编辑、删除四个操作按钮。

#### Scenario: 卡片内容展示
- **WHEN** 供应商列表加载完成
- **THEN** 每张卡片显示供应商名称和 BaseUrl，右侧显示启用、测试、编辑、删除操作按钮

#### Scenario: 测试按钮位置
- **WHEN** 卡片按钮区域渲染
- **THEN** "测试"按钮位于"启用"和"编辑"按钮之间

### Requirement: 激活卡片高亮
当前激活的供应商卡片 SHALL 有蓝色边框/高亮标识，与其他卡片区分。

#### Scenario: 激活项高亮显示
- **WHEN** 某供应商为当前激活项（IsActive=1）
- **THEN** 该卡片显示蓝色边框高亮

### Requirement: 启用按钮触发配置写入
点击启用按钮 SHALL 将该供应商设为激活并立即写入对应配置文件。

#### Scenario: 点击启用
- **WHEN** 用户点击某供应商卡片的启用按钮
- **THEN** 系统将该供应商设为激活，写入配置文件，UI 更新高亮状态

### Requirement: 新增按钮
主窗口右上角 SHALL 显示 `+` 按钮，点击弹出新增供应商对话框。

#### Scenario: 点击新增按钮
- **WHEN** 用户点击右上角 `+` 按钮
- **THEN** 弹出供应商编辑对话框，ToolType 默认为当前标签页对应的类型

### Requirement: 编辑按钮
点击编辑按钮 SHALL 弹出对话框，预填当前供应商信息。

#### Scenario: 点击编辑
- **WHEN** 用户点击某供应商卡片的编辑按钮
- **THEN** 弹出对话框，预填该供应商的 Name、BaseUrl、ApiKey

### Requirement: 删除按钮
点击删除按钮 SHALL 弹出确认对话框，确认后删除。

#### Scenario: 确认删除
- **WHEN** 用户点击删除按钮并在确认对话框中确认
- **THEN** 系统删除该供应商记录并刷新列表

### Requirement: 测试按钮交互
点击测试按钮 SHALL 发送测试请求并以 MessageBox 显示结果。

#### Scenario: 测试按钮 loading 状态
- **WHEN** 用户点击测试按钮
- **THEN** 按钮变为禁用状态并显示"测试中..."

#### Scenario: 测试成功
- **WHEN** 测试请求成功返回
- **THEN** 显示 MessageBox，标题"测试成功"，内容"响应时间：{xxx} ms"，按钮恢复可用

#### Scenario: 测试失败（HTTP 错误）
- **WHEN** 测试请求返回 HTTP 错误
- **THEN** 显示 MessageBox，标题"测试失败"，内容"HTTP {状态码}: {错误信息}"，按钮恢复可用

#### Scenario: 测试失败（超时或连接错误）
- **WHEN** 测试请求超时或连接失败
- **THEN** 显示 MessageBox，标题"测试失败"，内容为超时/连接错误描述，按钮恢复可用


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
每张供应商卡片 SHALL 显示名称（大字）、BaseUrl（小字灰色），右侧包含启用、测试、上移、下移、编辑、删除操作按钮；名称前 SHALL 根据测试状态显示状态点。

#### Scenario: 卡片内容展示
- **WHEN** 供应商列表加载完成
- **THEN** 每张卡片显示供应商名称和 BaseUrl，右侧显示启用、测试、上移、下移、编辑、删除操作按钮

#### Scenario: 测试按钮位置
- **WHEN** 卡片按钮区域渲染
- **THEN** "测试"按钮位于"启用"和"编辑"按钮之间

#### Scenario: 状态点显示位置
- **WHEN** 供应商卡片名称区域渲染
- **THEN** 状态点显示在供应商名称前方

#### Scenario: 状态点显示规则
- **WHEN** `TestStatus=1`
- **THEN** 显示绿色状态点，且不显示任何状态文字

#### Scenario: 状态点显示规则（失败）
- **WHEN** `TestStatus=2`
- **THEN** 显示红色状态点，且不显示任何状态文字

#### Scenario: 状态点显示规则（未知）
- **WHEN** `TestStatus=0` 或空值
- **THEN** 不显示状态点

#### Scenario: 上移按钮边界禁用
- **WHEN** 当前卡片为列表第一项
- **THEN** 上移按钮禁用

#### Scenario: 下移按钮边界禁用
- **WHEN** 当前卡片为列表最后一项
- **THEN** 下移按钮禁用

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
点击测试按钮 SHALL 发送测试请求并以 MessageBox 显示结果，同时更新并持久化供应商最后测试状态。

#### Scenario: 测试按钮 loading 状态
- **WHEN** 用户点击测试按钮
- **THEN** 按钮变为禁用状态并显示"测试中..."

#### Scenario: 测试成功
- **WHEN** 测试请求成功返回
- **THEN** 系统将该供应商 `TestStatus` 更新为 `1` 并持久化，显示 MessageBox，标题"测试成功"，按钮恢复可用

#### Scenario: 测试失败
- **WHEN** 测试请求失败返回（HTTP 错误、超时或连接错误）
- **THEN** 系统将该供应商 `TestStatus` 更新为 `2` 并持久化，显示 MessageBox，标题"测试失败"，按钮恢复可用

### Requirement: 顺序调整按钮交互
点击上移/下移按钮 SHALL 在当前 ToolType 列表内调整供应商顺序并刷新显示。

#### Scenario: 点击上移
- **WHEN** 用户点击某卡片的上移按钮且该卡片不是第一项
- **THEN** 系统将该卡片与前一项交换顺序，列表立即按新顺序刷新

#### Scenario: 点击下移
- **WHEN** 用户点击某卡片的下移按钮且该卡片不是最后一项
- **THEN** 系统将该卡片与后一项交换顺序，列表立即按新顺序刷新

### Requirement: 会话管理按钮
主窗口右上角 SHALL 显示"会话管理"按钮，点击打开 SessionWindow。

#### Scenario: 点击会话管理按钮
- **WHEN** 用户点击右上角"会话管理"按钮
- **THEN** 系统以 `new SessionWindow { Owner = this }.Show()` 打开会话管理窗口


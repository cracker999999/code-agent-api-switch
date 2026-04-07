## MODIFIED Requirements

### Requirement: 供应商卡片显示
每张供应商卡片 SHALL 显示名称（大字）、BaseUrl（小字灰色），右侧包含启用、测试、编辑、删除四个操作按钮；名称前 SHALL 根据测试状态显示状态点。

#### Scenario: 卡片内容展示
- **WHEN** 供应商列表加载完成
- **THEN** 每张卡片显示供应商名称和 BaseUrl，右侧显示启用、测试、编辑、删除操作按钮

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

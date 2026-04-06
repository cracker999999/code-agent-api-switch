## MODIFIED Requirements

### Requirement: 供应商卡片显示
每张供应商卡片 SHALL 显示名称（大字）、BaseUrl（小字灰色），右侧包含启用、测试、编辑、删除四个操作按钮。

#### Scenario: 卡片内容展示
- **WHEN** 供应商列表加载完成
- **THEN** 每张卡片显示供应商名称和 BaseUrl，右侧显示启用、测试、编辑、删除操作按钮

#### Scenario: 测试按钮位置
- **WHEN** 卡片按钮区域渲染
- **THEN** "测试"按钮位于"启用"和"编辑"按钮之间

## ADDED Requirements

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

## MODIFIED Requirements

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

## ADDED Requirements

### Requirement: 顺序调整按钮交互
点击上移/下移按钮 SHALL 在当前 ToolType 列表内调整供应商顺序并刷新显示。

#### Scenario: 点击上移
- **WHEN** 用户点击某卡片的上移按钮且该卡片不是第一项
- **THEN** 系统将该卡片与前一项交换顺序，列表立即按新顺序刷新

#### Scenario: 点击下移
- **WHEN** 用户点击某卡片的下移按钮且该卡片不是最后一项
- **THEN** 系统将该卡片与后一项交换顺序，列表立即按新顺序刷新

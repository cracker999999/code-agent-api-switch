## MODIFIED Requirements

### Requirement: 供应商卡片显示
每张供应商卡片 SHALL 显示名称（大字）、备注（名字右侧，浅色小字）、BaseUrl（小字灰色），右侧包含启用、测试、上移、下移、编辑、删除操作按钮；名称前 SHALL 根据测试状态显示状态点。

#### Scenario: 卡片内容展示
- **WHEN** 供应商列表加载完成
- **THEN** 每张卡片显示供应商名称和 BaseUrl，右侧显示启用、测试、上移、下移、编辑、删除操作按钮

#### Scenario: 备注显示
- **WHEN** 供应商 Remark 不为空
- **THEN** 备注文字显示在名称右侧同一行，颜色为 `#9CA3AF`，字号为 `11`

#### Scenario: 备注为空时不显示
- **WHEN** 供应商 Remark 为空或 null
- **THEN** 名称右侧不显示额外元素

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

### Requirement: 编辑按钮
点击编辑按钮 SHALL 弹出对话框，预填当前供应商信息。

#### Scenario: 点击编辑
- **WHEN** 用户点击某供应商卡片的编辑按钮
- **THEN** 弹出对话框，预填该供应商的 Name、BaseUrl、ApiKey、TestModel、Remark

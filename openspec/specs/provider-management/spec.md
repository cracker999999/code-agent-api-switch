# provider-management Specification

## Purpose
TBD - created by archiving change apiswitch-desktop-app. Update Purpose after archive.
## Requirements
### Requirement: SQLite 数据库初始化
系统 SHALL 在启动时检查 SQLite 数据库文件是否存在，不存在则自动创建并建立 Providers 表。

#### Scenario: 首次启动自动建表
- **WHEN** 应用首次启动且数据库文件不存在
- **THEN** 系统自动创建数据库文件并创建 Providers 表（Id, ToolType, Name, BaseUrl, ApiKey, IsActive, SortOrder, TestStatus）

#### Scenario: 历史数据库自动补列
- **WHEN** 应用启动时检测到 Providers 表缺少 `TestStatus`
- **THEN** 系统 SHALL 自动执行迁移补齐该列，默认值为 `0`

### Requirement: Provider 数据模型
Provider 数据模型 SHALL 包含以下字段：Id（INTEGER PK 自增）、ToolType（INTEGER，0=Codex, 1=ClaudeCode）、Name（TEXT）、BaseUrl（TEXT）、ApiKey（TEXT）、IsActive（INTEGER 0/1）、SortOrder（INTEGER）、TestStatus（INTEGER，0=未知，1=可用，2=失败）。

#### Scenario: Provider 包含测试状态
- **WHEN** 系统从数据库读取供应商记录
- **THEN** 系统 SHALL 同时读取 `TestStatus` 并映射到 Provider 模型

### Requirement: 新增供应商
系统 SHALL 支持新增供应商记录，包含 Name、BaseUrl、ApiKey 字段。

#### Scenario: 成功新增供应商
- **WHEN** 用户提交有效的供应商信息（Name、BaseUrl、ApiKey）
- **THEN** 系统将记录插入数据库，IsActive 默认为 0

### Requirement: 编辑供应商
系统 SHALL 支持修改已有供应商的 Name、BaseUrl、ApiKey。

#### Scenario: 成功编辑供应商
- **WHEN** 用户修改供应商信息并提交
- **THEN** 系统更新数据库中对应记录

#### Scenario: 编辑后重置测试状态
- **WHEN** 用户保存对 Name、BaseUrl 或 ApiKey 的编辑
- **THEN** 系统 SHALL 将该记录 `TestStatus` 重置为 `0`

### Requirement: 删除供应商
系统 SHALL 支持删除供应商记录。删除激活项时不修改配置文件。

#### Scenario: 删除非激活供应商
- **WHEN** 用户删除一个非激活的供应商
- **THEN** 系统从数据库中删除该记录

#### Scenario: 删除激活供应商
- **WHEN** 用户删除当前激活的供应商
- **THEN** 系统从数据库中删除该记录，但不修改配置文件，保持最后写入的值不变

### Requirement: 激活供应商
系统 SHALL 支持将某个供应商设为当前激活项，同一 ToolType 下只能有一个激活项。

#### Scenario: 激活供应商
- **WHEN** 用户点击某供应商的启用按钮
- **THEN** 系统将该供应商 IsActive 设为 1，同 ToolType 下其他供应商 IsActive 设为 0

### Requirement: 按 ToolType 查询供应商列表
系统 SHALL 支持按 ToolType 查询供应商列表，按 SortOrder 排序。

#### Scenario: 查询 Codex 供应商列表
- **WHEN** 用户切换到 Codex 标签
- **THEN** 系统返回所有 ToolType=0 的供应商记录，按 SortOrder 排序

### Requirement: 更新测试状态
系统 SHALL 支持按供应商记录更新最后测试状态。

#### Scenario: 写入可用状态
- **WHEN** 测试结果为成功
- **THEN** 系统 SHALL 将目标供应商 `TestStatus` 更新为 `1`

#### Scenario: 写入失败状态
- **WHEN** 测试结果为失败
- **THEN** 系统 SHALL 将目标供应商 `TestStatus` 更新为 `2`

### Requirement: 供应商顺序重排
系统 SHALL 支持在同一 ToolType 内按记录顺序执行上移/下移重排。

#### Scenario: 上移重排
- **WHEN** 用户对某供应商执行上移操作
- **THEN** 系统 MUST 在同一 ToolType 范围内将该供应商与前一条记录交换 `SortOrder`

#### Scenario: 下移重排
- **WHEN** 用户对某供应商执行下移操作
- **THEN** 系统 MUST 在同一 ToolType 范围内将该供应商与后一条记录交换 `SortOrder`

#### Scenario: 跨 ToolType 隔离
- **WHEN** 用户在 Codex 列表执行顺序调整
- **THEN** 系统 MUST 不影响 Claude Code 列表记录的 `SortOrder`

### Requirement: 顺序调整持久化
顺序调整结果 SHALL 持久化到数据库并在重启后保持一致。

#### Scenario: 重启后顺序保持
- **WHEN** 用户完成顺序调整并重启应用
- **THEN** 系统按调整后的 `SortOrder` 展示供应商列表


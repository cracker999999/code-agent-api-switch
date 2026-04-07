## MODIFIED Requirements

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

### Requirement: 编辑供应商
系统 SHALL 支持修改已有供应商的 Name、BaseUrl、ApiKey。

#### Scenario: 成功编辑供应商
- **WHEN** 用户修改供应商信息并提交
- **THEN** 系统更新数据库中对应记录

#### Scenario: 编辑后重置测试状态
- **WHEN** 用户保存对 Name、BaseUrl 或 ApiKey 的编辑
- **THEN** 系统 SHALL 将该记录 `TestStatus` 重置为 `0`

## ADDED Requirements

### Requirement: 更新测试状态
系统 SHALL 支持按供应商记录更新最后测试状态。

#### Scenario: 写入可用状态
- **WHEN** 测试结果为成功
- **THEN** 系统 SHALL 将目标供应商 `TestStatus` 更新为 `1`

#### Scenario: 写入失败状态
- **WHEN** 测试结果为失败
- **THEN** 系统 SHALL 将目标供应商 `TestStatus` 更新为 `2`

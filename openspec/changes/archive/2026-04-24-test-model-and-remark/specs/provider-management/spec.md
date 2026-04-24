## MODIFIED Requirements

### Requirement: Provider 数据模型
Provider 数据模型 SHALL 包含以下字段：Id（INTEGER PK 自增）、ToolType（INTEGER，0=Codex, 1=ClaudeCode）、Name（TEXT）、BaseUrl（TEXT）、ApiKey（TEXT）、IsActive（INTEGER 0/1）、SortOrder（INTEGER）、TestStatus（INTEGER，0=未知，1=可用，2=失败）、TestModel（TEXT，测试用模型 ID，可为空）、Remark（TEXT，备注，可为空）。

#### Scenario: Provider 包含测试模型和备注
- **WHEN** 系统从数据库读取供应商记录
- **THEN** 系统 SHALL 同时读取 `TestModel` 和 `Remark` 并映射到 Provider 模型

### Requirement: SQLite 数据库初始化
系统 SHALL 在启动时检查 SQLite 数据库文件是否存在，不存在则自动创建并建立 Providers 表。

#### Scenario: 历史数据库自动补列
- **WHEN** 应用启动时检测到 Providers 表缺少 `TestStatus`
- **THEN** 系统 SHALL 自动执行迁移补齐该列，默认值为 `0`

#### Scenario: 历史数据库自动补 TestModel 列
- **WHEN** 应用启动时检测到 Providers 表缺少 `TestModel`
- **THEN** 系统 SHALL 自动执行 `ALTER TABLE Providers ADD COLUMN TestModel TEXT`

#### Scenario: 历史数据库自动补 Remark 列
- **WHEN** 应用启动时检测到 Providers 表缺少 `Remark`
- **THEN** 系统 SHALL 自动执行 `ALTER TABLE Providers ADD COLUMN Remark TEXT`

### Requirement: 新增供应商
系统 SHALL 支持新增供应商记录，包含 Name、BaseUrl、ApiKey、TestModel、Remark 字段。

#### Scenario: 成功新增供应商
- **WHEN** 用户提交有效的供应商信息
- **THEN** 系统将记录插入数据库，IsActive 默认为 0，TestModel 和 Remark 可为空

### Requirement: 编辑供应商
系统 SHALL 支持修改已有供应商的 Name、BaseUrl、ApiKey、TestModel、Remark。

#### Scenario: 成功编辑供应商
- **WHEN** 用户修改供应商信息并提交
- **THEN** 系统更新数据库中对应记录，包括 TestModel 和 Remark 字段

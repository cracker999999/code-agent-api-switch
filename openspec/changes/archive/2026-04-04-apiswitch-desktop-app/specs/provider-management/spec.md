## ADDED Requirements

### Requirement: SQLite 数据库初始化
系统 SHALL 在启动时检查 SQLite 数据库文件是否存在，不存在则自动创建并建立 Providers 表。

#### Scenario: 首次启动自动建表
- **WHEN** 应用首次启动且数据库文件不存在
- **THEN** 系统自动创建数据库文件并创建 Providers 表（Id, ToolType, Name, BaseUrl, ApiKey, IsActive, SortOrder）

### Requirement: Provider 数据模型
Provider 数据模型 SHALL 包含以下字段：Id（INTEGER PK 自增）、ToolType（INTEGER，0=Codex, 1=ClaudeCode）、Name（TEXT）、BaseUrl（TEXT）、ApiKey（TEXT）、IsActive（INTEGER 0/1）、SortOrder（INTEGER）。

#### Scenario: 同一供应商用于两种工具
- **WHEN** 用户希望同一供应商同时用于 Codex 和 Claude Code
- **THEN** 系统 SHALL 要求分别添加两条记录（ToolType 不同）

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

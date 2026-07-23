## MODIFIED Requirements

### Requirement: Provider 数据模型
Provider 数据模型 SHALL 包含以下字段：Id（INTEGER PK 自增）、ToolType（INTEGER，0=Codex, 1=ClaudeCode, 2=Grok）、Name（TEXT）、BaseUrl（TEXT）、ApiKey（TEXT）、IsActive（INTEGER 0/1）、SortOrder（INTEGER）、TestStatus（INTEGER，0=未知，1=可用，2=失败）、TestModel（TEXT，测试用模型 ID，可为空）、Remark（TEXT，备注，可为空）。

#### Scenario: Provider 包含测试模型和备注
- **WHEN** 系统从数据库读取供应商记录
- **THEN** 系统 SHALL 同时读取 `TestModel` 和 `Remark` 并映射到 Provider 模型

## ADDED Requirements

### Requirement: 查询 Grok 供应商列表
系统 SHALL 支持按 ToolType=2 查询 Grok 供应商列表，按 SortOrder 排序。

#### Scenario: 查询 Grok 供应商列表
- **WHEN** 用户切换到 Grok 标签
- **THEN** 系统返回所有 ToolType=2 的供应商记录，按 SortOrder 排序

### Requirement: Grok 顺序隔离
Grok 供应商顺序调整 MUST 只影响 ToolType=2 的记录。

#### Scenario: Grok 顺序调整不影响其他工具
- **WHEN** 用户在 Grok 列表执行顺序调整
- **THEN** 系统 MUST 不影响 Codex 和 Claude Code 列表记录的 `SortOrder`

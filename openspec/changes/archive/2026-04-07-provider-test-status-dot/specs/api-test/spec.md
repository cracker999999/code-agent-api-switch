## ADDED Requirements

### Requirement: 测试结果状态映射
系统 SHALL 将测试结果映射为可持久化状态码，供供应商卡片状态点展示使用。

#### Scenario: 测试成功映射
- **WHEN** `TestProviderAsync` 返回 `Success=true`
- **THEN** 调用方 MUST 将该供应商状态写入 `TestStatus=1`（可用）

#### Scenario: 测试失败映射
- **WHEN** `TestProviderAsync` 返回 `Success=false`
- **THEN** 调用方 MUST 将该供应商状态写入 `TestStatus=2`（失败）

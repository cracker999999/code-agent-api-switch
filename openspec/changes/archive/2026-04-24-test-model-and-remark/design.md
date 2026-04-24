## Context

APISwitch 是一个 WPF 桌面应用，管理 Codex CLI / Claude Code 的 API 供应商配置。当前测试功能的模型 ID 硬编码在 ApiTestService 中（Codex: `gpt-5.3-codex`，Claude: `claude-opus-4-6`），且供应商缺少备注字段。

## Goals / Non-Goals

**Goals:**
- 每个供应商可独立配置测试用模型 ID
- 每个供应商可添加备注，在列表中名字旁显示
- 数据库平滑迁移，兼容已有数据

**Non-Goals:**
- 不做模型 ID 校验或下拉选择
- 不改变测试逻辑本身（仍为流式首 chunk 判定）
- 备注不参与搜索或排序

## Decisions

### 1. TestModel 和 Remark 作为 Provider 字段
- 直接加在 Provider 模型上，每个供应商独立配置
- 备选：全局配置（不够灵活）、单独表（过度设计）

### 2. 数据库迁移用 ALTER TABLE
- 启动时尝试 `ALTER TABLE Providers ADD COLUMN TestModel TEXT` 和 `ALTER TABLE Providers ADD COLUMN Remark TEXT`
- SQLite 的 ALTER TABLE ADD COLUMN 对已有行自动填 NULL，无需数据迁移
- 与现有 TestStatus 迁移方式一致

### 3. TestModel 为空时 fallback 到默认值
- ApiTestService 中 `string.IsNullOrWhiteSpace(provider.TestModel)` 时使用原硬编码值
- 保证已有供应商无需手动填写即可正常测试

### 4. 备注显示样式
- 名字右侧同行，颜色 `#9CA3AF`，字号 `11`
- 备注为空时不显示额外元素

## Risks / Trade-offs

- [TestModel 填错导致测试失败] → 用户自行负责，与 BaseUrl/ApiKey 填错同级风险
- [备注过长撑开卡片] → 不做截断，由用户自行控制长度

## Context

现有实现把工具类型以整数保存在 Provider.ToolType 中，Codex 为 0，Claude Code 为 1。供应商管理、配置写入、测试请求、主窗口、设置页、会话窗口和托盘提示均存在两分类分支。

Grok Build 配置路径按 Codex 的处理方式固定在用户目录下，配置文件为 `~/.grok/config.toml`，会话目录为 `~/.grok/sessions`。

## Goals / Non-Goals

**Goals:**
- 以 `ToolType=2` 新增 Grok 分类，不迁移现有 Codex/Claude Code 数据。
- WPF 与 Avalonia 的用户可见功能保持一致。
- Grok 配置写入固定使用用户目录 `.grok`。
- Grok 会话管理支持扫描、详情、恢复和删除。

**Non-Goals:**
- 不重构现有 Codex/Claude Code 配置格式。
- 不引入新的 TOML 解析依赖。
- 不支持 Grok 远端会话删除；本次只管理本地 `sessions` 目录中的文件。

## Decisions

- 使用 `ToolType=2` 表示 Grok。这样可以复用现有 Providers 表和按 ToolType 隔离的查询、排序、激活逻辑，不需要数据库迁移。
- Grok 路径用与 Codex 一致的方式在调用处通过 `Environment.SpecialFolder.UserProfile` 拼接，不单独引入路径解析器。
- Grok `config.toml` 使用行级 upsert。现有项目没有 TOML 依赖，新增依赖会扩大包体和迁移风险；Grok 这次只写三个顶层字符串字段，用正则定位顶层 key 足够简单。
- Grok 测试请求走 Responses API，默认端点路径为 `/responses`，默认模型为 `grok-4.5`，默认客户端版本号为 `0.2.111`。供应商 TestModel 仍优先于全局默认值，Grok 测试请求的 User-Agent 使用设置中的客户端版本号。
- Grok 会话解析独立为 `GrokSessionParser`。真实 Grok 会话以目录为单位，`chat_history.jsonl` 存消息，`summary.json` / `prompt_context.json` 存元数据；扫描只以 `chat_history.jsonl` 作为会话入口，避免把 `events.jsonl`、`prompt_history.jsonl` 等辅助文件误识别为会话。
- Grok 恢复命令使用 `grok -r <id>`，符合 Grok CLI 公共 flags。

## Risks / Trade-offs

- [Grok 本地会话 JSONL 格式变化] → 解析器按真实目录结构和常见字段宽松提取；无法识别的会话目录跳过，不影响其他会话。
- [Grok 配置中同名 key 位于其他 TOML section] → 只 upsert 文件顶层字段，不改动已有 section 内配置。
- [托盘提示长度限制] → 继续使用现有 63 字符截断策略，新增 Grok 后仍能安全显示。

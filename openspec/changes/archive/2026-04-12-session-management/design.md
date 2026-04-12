## Context

APISwitch 已实现供应商管理和配置切换功能。Codex CLI 和 Claude Code 在使用过程中会在本地目录生成 `.jsonl` 格式的会话文件，用户缺乏直观的方式浏览和管理这些历史对话。

## Goals / Non-Goals

**Goals:**
- 扫描本地 `.jsonl` 文件，提取会话元数据并展示列表
- 支持查看会话的聊天详情（用户/助手/工具消息）
- 支持删除会话（含关联的 sidecar 文件）
- 性能友好：头尾读取策略避免全量解析大文件

**Non-Goals:**
- 不支持编辑或导出会话内容
- 不支持搜索/过滤会话
- 不做会话内容的实时监听或自动刷新

## Decisions

### 1. 头尾读取策略
- 扫描时每文件仅读取前 10 行和后 30 行提取元数据，避免全量读取大文件
- 选中会话时再异步加载全部消息
- 备选：全量读取（大文件性能差）、数据库索引（增加复杂度）

### 2. Codex 解析规格（严格参考 codex.rs）
- `type=session_meta` → 提取 `payload.id`、`payload.cwd`、`payload.timestamp`
- `type=response_item` 且 `payload.type`:
  - `message` → 角色从 `payload.role`，内容从 `payload.content`
  - `function_call` → 角色=assistant，内容=`[Tool: {name}]`
  - `function_call_output` → 角色=tool，内容从 `payload.output`
- 会话 ID 回退：从文件名匹配 UUID 正则
- 标题：从 `cwd` 获取目录基名

### 3. Claude 解析规格（严格参考 claude.rs）
- 头部行：提取 `sessionId`、`cwd`、`timestamp`
- 尾部行（反向）：提取 `last_active_at`、`summary`、`custom-title`
- 标题优先级：custom-title > 目录基名
- 角色重分类：内容项全为 `tool_result` 的用户消息归类为 role=tool
- 跳过 `isMeta=true` 的条目
- 跳过以 `agent-` 开头的文件

### 4. 删除策略
- Codex：删除 `.jsonl` 文件
- Claude：删除 `.jsonl` 文件 + 同名的 sidecar 目录（subagents、tool-results）
- 备选：仅标记删除（增加状态管理复杂度）

### 5. SessionWindow 布局
- 窗口尺寸 ~900x640，CenterOwner
- 顶栏：标题 + Codex/Claude 选项卡
- 左右分栏（Grid 两列）：左侧 ~300px 会话列表，右侧聊天详情
- 会话列表使用 VirtualizingStackPanel 优化大量列表项性能
- 聊天气泡样式：用户蓝色靠右、助手浅灰靠左、工具默认折叠点击展开

### 6. 相对时间显示
- < 1 分钟：刚刚
- < 60 分钟：X 分钟前
- < 24 小时：X 小时前
- < 30 天：X 天前
- 否则：YYYY/MM/DD

## Risks / Trade-offs

- [.jsonl 格式不一致或损坏] → 解析失败时跳过该文件，不影响其他会话
- [目录不存在] → 返回空列表，不报错
- [大型会话文件加载慢] → 选中时通过 Task.Run 异步加载，不阻塞 UI
- [删除 sidecar 目录可能误删] → 仅删除与 .jsonl 同名的目录，不递归删除其他内容

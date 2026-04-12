# 会话管理功能设计文档

## 概述

为 APISwitch 添加一个会话管理窗口，用于查看和管理 Codex 和 Claude Code 的对话会话。该功能将扫描本地目录中的 `.jsonl` 文件，显示会话列表及聊天详情，并支持删除操作。

## 数据源

- **Codex:** `~/.codex/sessions/` -- 遍历扫描 `.jsonl` 文件
- **Claude:** `~/.claude/projects/` -- 递归扫描 `.jsonl` 文件，跳过以 `agent-` 开头的文件

## 数据模型

### SessionMeta

```csharp
public class SessionMeta
{
    public string ProviderId { get; set; }    // "codex" 或 "claude"
    public string SessionId { get; set; }
    public string? Title { get; set; }
    public string? ProjectDir { get; set; }
    public long? CreatedAt { get; set; }      // Unix 毫秒时间戳
    public long? LastActiveAt { get; set; }
    public string SourcePath { get; set; }    // .jsonl 文件的绝对路径
}
```

### SessionMessage

```csharp
public class SessionMessage
{
    public string Role { get; set; }          // "user" / "assistant" / "tool"
    public string Content { get; set; }
    public long? Timestamp { get; set; }      // Unix 毫秒时间戳
}
```

## 服务层: SessionService.cs

### 扫描

- `List<SessionMeta> ScanCodexSessions()` -- 扫描 `~/.codex/sessions/`
- `List<SessionMeta> ScanClaudeSessions()` -- 递归扫描 `~/.claude/projects/`
- 每文件仅读取前 10 行和后 30 行以提取元数据
- 按 `LastActiveAt` 降序排序

### Codex 解析规格 (严格参考 codex.rs)

- `type=session_meta` -> 提取 `payload.id`, `payload.cwd`, `payload.timestamp`
- `type=response_item` 且带有 `payload.type`:
  - `message` -> 角色从 `payload.role` 获取，内容从 `payload.content` 获取
  - `function_call` -> 角色=assistant，内容="[Tool: {name}]"
  - `function_call_output` -> 角色=tool，内容从 `payload.output` 获取
- 会话 ID 回退策略：从文件名匹配 UUID 正则
- 标题：从 `cwd` 获取目录基名

### Claude 解析规格 (严格参考 claude.rs)

- 头部行：提取 `sessionId`, `cwd`, `timestamp`，首条用户消息（跳过 caveats/slash 命令）
- 尾部行 (反向)：提取 `last_active_at`, `summary`, `custom-title`
- 标题优先级：custom-title > 首条用户消息 > 目录基名
- 角色重分类：内容项全为 `tool_result` 的用户消息归类为 role=tool
- 跳过 `isMeta=true` 的条目
- 跳过以 `agent-` 开头的文件

### 删除

- `DeleteSession(string providerId, string sessionId, string sourcePath)`
- Codex: 删除 `.jsonl` 文件
- Claude: 删除 `.jsonl` 文件 + 同名的 sidecar 目录 (subagents, tool-results)

## UI 界面: SessionWindow

### 入口点

- MainWindow 右上角：添加“会话管理”按钮
- 点击 -> `new SessionWindow { Owner = this }.Show()`

### 布局

```
SessionWindow (~900x640, CenterOwner)
顶栏: 标题 + Codex/Claude 选项卡
左右分栏 (Grid 两列)
  左侧 (~300px): 会话列表
    页眉: 会话计数
    带有 VirtualizingStackPanel 的 ListBox
      列表项: 标题 + 相对时间
  右侧 (*): 详情面板
    页眉: 会话标题 + 删除按钮
    带有聊天气泡的 ScrollViewer
      用户: 蓝色背景，白色文字，靠右对齐
      助手: 浅灰色背景，靠左对齐
      工具: 默认折叠，点击展开
```

### 交互

1. 选项卡切换 -> 重新扫描对应的目录
2. 选中列表项 -> 通过 Task.Run 异步加载消息
3. 删除 -> 确认对话框 -> 删除文件 -> 刷新列表
4. 关闭窗口 -> 返回 MainWindow

### 相对时间显示

- < 1 分钟: 刚刚
- < 60 分钟: X 分钟前
- < 24 小时: X 小时前
- < 30 天: X 天前
- 否则: YYYY/MM/DD

## 涉及文件

### 新增文件

- `Models/SessionMeta.cs`
- `Models/SessionMessage.cs`
- `Services/SessionService.cs`
- `Views/SessionWindow.xaml`
- `Views/SessionWindow.xaml.cs`

### 修改文件

- `MainWindow.xaml` -- 添加按钮
- `MainWindow.xaml.cs` -- 添加按钮点击处理

## 性能

- 头尾读取策略：扫描时避免全量读取文件
- 选中时异步加载消息
- 对大型对话列表使用 VirtualizingStackPanel
- 目录不存在时：返回空列表，不报错

## 边缘情况

- 目录不存在 -> 返回空列表
- .jsonl 解析失败 -> 跳过该文件
- 无标题 -> 回退到目录名或会话 ID
- 无消息 -> 跳过

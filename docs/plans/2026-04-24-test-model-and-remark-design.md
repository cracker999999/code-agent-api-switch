# 测试模型可配置 & 供应商备注

## 背景

1. 测试功能的模型 ID 当前硬编码（Codex: `gpt-5.3-codex`，Claude: `claude-opus-4-6`），不同供应商支持的模型不同，需要可配置。
2. 供应商缺少备注字段，不便区分同类型的多个供应商。

## 设计

### 数据模型

Provider 新增两个字段：

| 字段 | 类型 | 说明 |
|------|------|------|
| TestModel | string | 测试用模型 ID，为空时使用当前默认值 |
| Remark | string | 备注，可选 |

### 数据库

DatabaseService 初始化时 ALTER TABLE 添加 `TestModel TEXT` 和 `Remark TEXT` 列（已存在则忽略）。

### 供应商编辑页面（ProviderDialog）

- 新增"测试模型"输入框，位于 ApiKey 下方
- 新增"备注"输入框，位于测试模型下方
- 两个字段均可留空

### 测试服务（ApiTestService）

- `TestCodexAsync`: 使用 `provider.TestModel`，若为空则 fallback 到 `gpt-5.3-codex`
- `TestClaudeAsync`: 使用 `provider.TestModel`，若为空则 fallback 到 `claude-opus-4-6`

### 供应商列表（MainWindow）

备注显示在名字右边同一行，样式：
- 颜色较浅（如 `#9CA3AF`）
- 字号较小（如 `11`）
- 格式：`供应商名 备注内容`

## 涉及文件

- `Models/Provider.cs` — 加字段
- `Services/DatabaseService.cs` — ALTER TABLE
- `Views/ProviderDialog.xaml` + `.cs` — 加输入框和绑定
- `Services/ApiTestService.cs` — 读取 TestModel
- `MainWindow.xaml` — 列表项模板加备注显示

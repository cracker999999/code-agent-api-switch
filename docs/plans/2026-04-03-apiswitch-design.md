# APISwitch 设计文档

## 概述

Windows 10 桌面应用，用于切换 Codex CLI 和 Claude Code 的 API 供应商配置。启动后常驻系统托盘，提供供应商的增删查改和一键切换功能。

## 技术栈

- **框架：** .NET 8 + WPF
- **UI 风格：** 纯 WPF 自定义样式，功能优先
- **架构：** Code-behind（不使用 MVVM 框架）
- **数据库：** SQLite（Microsoft.Data.Sqlite）
- **TOML 写入：** 正则表达式替换（不引入 TOML 库，避免注释丢失）
- **JSON 解析：** System.Text.Json（内置）

## 数据模型

### SQLite 表：Providers

| 字段 | 类型 | 说明 |
|------|------|------|
| Id | INTEGER PK | 自增主键 |
| ToolType | INTEGER | 0=Codex, 1=ClaudeCode |
| Name | TEXT | 供应商名称 |
| BaseUrl | TEXT | API 地址 |
| ApiKey | TEXT | API Key |
| IsActive | INTEGER | 0/1，每个 ToolType 下只有一个为 1 |
| SortOrder | INTEGER | 排序顺序 |

同一供应商若两边都要用，需分别添加两条记录。

## UI 设计

### 主窗口

- **顶部标签栏：** "Codex" | "Claude Code"，点击切换显示对应供应商列表
- **右上角：** `+` 按钮，新增供应商
- **中间：** 供应商卡片列表，每张卡片显示：
  - 名称（大字）
  - BaseUrl（小字灰色）
  - 右侧操作按钮：启用 | 编辑 | 删除
  - 当前激活的卡片有蓝色边框/高亮标识

### 操作行为

- **启用：** 将该供应商设为当前激活，立即写入对应配置文件，其他供应商取消激活
- **编辑：** 弹出对话框，可修改名称、BaseUrl、ApiKey
- **删除：** 确认后删除。若删除的是激活项，不修改配置文件，保持最后写入的值不变
- **新增：** 弹出对话框填写名称、BaseUrl、ApiKey

### 系统托盘

- 启动时显示主窗口，同时在系统托盘创建 NotifyIcon
- 托盘右键菜单：显示主窗口 / 退出
- 关闭窗口 = 最小化到托盘，不退出程序
- 双击托盘图标 = 显示主窗口

## 配置文件写入逻辑

### Codex

**目标文件：**

1. `%USERPROFILE%\.codex\config.toml`（运行时用 `Environment.GetFolderPath` 动态获取）
   - 只修改 `[model_providers.OpenAI]` 段下的 `base_url` 值
   - 使用正则表达式定位并替换该行，不做完整 TOML 解析-序列化，避免丢失注释和特殊格式

2. `%USERPROFILE%\.codex\auth.json`
   - 只修改 `OPENAI_API_KEY` 字段
   - JSON 序列化写回

### Claude Code

**目标文件：**

1. `%USERPROFILE%\.claude\settings.json`
   - 只修改 `env.ANTHROPIC_AUTH_TOKEN` 和 `env.ANTHROPIC_BASE_URL`
   - 如果 `env` 节点不存在则创建
   - 其他字段保持不变

### 安全考虑

- 写入前先备份原文件（`.bak` 后缀），防止写坏
- 文件不存在时：`auth.json` 和 `settings.json` 可自动创建；`config.toml` 不存在则报错提示用户先安装 Codex
- ApiKey 在 SQLite 中明文存储（与原配置文件一致，不额外加密）
- 所有配置路径使用 `Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)` 动态获取，不硬编码用户名

## 项目结构

```
e:\APISwitch\
  APISwitch.sln
  src\APISwitch\
    App.xaml / App.xaml.cs          -- 应用入口，系统托盘初始化
    MainWindow.xaml / .cs           -- 主窗口（标签页 + 供应商列表）
    Views\
      ProviderDialog.xaml / .cs     -- 新增/编辑供应商对话框
    Models\
      Provider.cs                   -- 数据模型
    Services\
      DatabaseService.cs            -- SQLite CRUD
      ConfigWriterService.cs        -- 配置文件读写
    Assets\
      app.ico                       -- 托盘图标
    APISwitch.csproj
```

## 启动流程

1. 初始化 SQLite 数据库（不存在则自动建表）
2. 创建系统托盘 NotifyIcon
3. 显示主窗口
4. 窗口关闭 → 隐藏到托盘
5. 托盘"退出" → Application.Shutdown()

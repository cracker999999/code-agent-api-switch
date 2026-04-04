## Context

用户需要频繁切换 Codex CLI 和 Claude Code 的 API 供应商配置，当前只能手动编辑配置文件。本项目构建一个 Windows 10 桌面应用，提供可视化管理和一键切换。

项目从零开始，无现有代码基础。目标平台为 Windows 10，使用 .NET 8 + WPF。

## Goals / Non-Goals

**Goals:**
- 提供 Codex 和 Claude Code 两种工具的 API 供应商管理（增删改查）
- 一键切换激活供应商，自动写入对应配置文件
- 系统托盘常驻，关闭窗口不退出程序
- 写入配置前自动备份，防止写坏

**Non-Goals:**
- 不做 API 连通性测试或健康检查
- 不加密存储 ApiKey（与原配置文件一致）
- 不支持 MVVM 框架，使用 Code-behind 保持简单
- 不引入第三方 TOML 库，用正则替换避免注释丢失
- 不支持 macOS/Linux

## Decisions

### 1. 框架选择：.NET 8 + WPF
- WPF 是 Windows 桌面应用的成熟方案，原生支持系统托盘
- .NET 8 LTS 提供长期支持
- 备选：WinForms（UI 定制能力弱）、Avalonia（跨平台但增加复杂度）

### 2. 架构：Code-behind 而非 MVVM
- 应用规模小，Code-behind 足够
- 减少抽象层，降低开发复杂度
- 备选：CommunityToolkit.Mvvm（对此规模过度设计）

### 3. 数据存储：SQLite（Microsoft.Data.Sqlite）
- 轻量级，无需额外服务
- 单文件数据库，便于部署
- 备选：JSON 文件存储（并发写入不安全）、LiteDB（额外依赖）

### 4. JSON 解析：System.Text.Json（内置）
- .NET 内置，无需额外依赖
- 用于读写 auth.json 和 settings.json
- 备选：Newtonsoft.Json（额外依赖，此场景无必要）

### 5. TOML 写入：正则替换
- Codex 的 config.toml 可能包含用户注释，完整解析-序列化会丢失注释
- 只需修改 `[model_providers.OpenAI]` 段下的 `base_url`，正则足够
- 备选：Tomlyn 库（会丢失注释和格式）

### 6. UI 风格：纯 WPF 自定义样式，功能优先
- 不引入第三方 UI 框架（如 MaterialDesign、HandyControl）
- 使用 WPF 原生控件 + 自定义 Style/Template 实现界面
- 以功能可用为首要目标，不追求视觉花哨

### 7. 项目结构
```
src/APISwitch/
  App.xaml(.cs)              -- 入口 + 托盘初始化
  MainWindow.xaml(.cs)       -- 主窗口
  Views/ProviderDialog.xaml(.cs) -- 编辑对话框
  Models/Provider.cs         -- 数据模型
  Services/DatabaseService.cs    -- SQLite CRUD
  Services/ConfigWriterService.cs -- 配置写入
  Assets/app.ico             -- 托盘图标
```

### 8. 配置路径
- 使用 `Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)` 动态获取
- Codex: `~/.codex/config.toml` + `~/.codex/auth.json`
- Claude Code: `~/.claude/settings.json`

## Risks / Trade-offs

- [正则替换 TOML 可能匹配错误] → 限定匹配范围在 `[model_providers.OpenAI]` 段内，使用精确的行级匹配
- [配置文件被其他程序同时修改] → 写入前备份 `.bak`，出错可手动恢复
- [config.toml 不存在] → 报错提示用户先安装 Codex，不自动创建
- [SQLite 数据库损坏] → 单用户单进程访问，风险极低
- [ApiKey 明文存储] → 与原配置文件安全级别一致，不额外加密

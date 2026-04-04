## 1. 项目初始化

- [x] 1.1 创建 APISwitch.sln 解决方案和 src/APISwitch WPF 项目（.NET 8，目标框架 net8.0-windows）
- [x] 1.2 添加 NuGet 依赖 Microsoft.Data.Sqlite
- [x] 1.3 创建项目目录结构：Models/、Services/、Views/、Assets/
- [x] 1.4 添加 app.ico 托盘图标资源文件

## 2. 数据模型与数据库

- [x] 2.1 创建 Models/Provider.cs 数据模型（Id, ToolType, Name, BaseUrl, ApiKey, IsActive, SortOrder）
- [x] 2.2 创建 Services/DatabaseService.cs，实现 SQLite 初始化和自动建表
- [x] 2.3 实现 DatabaseService 的 CRUD 方法：GetProviders(toolType)、AddProvider、UpdateProvider、DeleteProvider
- [x] 2.4 实现 ActivateProvider 方法：同 ToolType 下互斥激活

## 3. 配置文件写入

- [x] 3.1 创建 Services/ConfigWriterService.cs 基础结构，实现配置路径动态获取
- [x] 3.2 实现写入前 .bak 备份逻辑
- [x] 3.3 实现 Codex config.toml 写入：正则替换 [model_providers.OpenAI] 段下的 base_url
- [x] 3.4 实现 Codex auth.json 写入：修改 OPENAI_API_KEY 字段，不存在则自动创建
- [x] 3.5 实现 Claude Code settings.json 写入：修改 env.ANTHROPIC_AUTH_TOKEN 和 env.ANTHROPIC_BASE_URL，env 节点不存在则创建
- [x] 3.6 实现 config.toml 不存在时的错误提示逻辑

## 4. 供应商对话框

- [x] 4.1 创建 Views/ProviderDialog.xaml 布局：Name、BaseUrl、ApiKey 输入框 + 确认/取消按钮
- [x] 4.2 实现 ProviderDialog.xaml.cs：支持新增模式（空字段）和编辑模式（预填数据）

## 5. 主窗口

- [x] 5.1 创建 MainWindow.xaml 布局：顶部标签栏（Codex / Claude Code）+ 右上角 + 按钮 + 中间供应商卡片列表
- [x] 5.2 实现供应商卡片样式：名称大字、BaseUrl 小字灰色、右侧启用/编辑/删除按钮、激活项蓝色边框
- [x] 5.3 实现 MainWindow.xaml.cs：标签切换加载对应 ToolType 列表
- [x] 5.4 实现启用按钮逻辑：调用 DatabaseService 激活 + ConfigWriterService 写入 + 刷新 UI
- [x] 5.5 实现编辑按钮逻辑：打开 ProviderDialog 编辑模式，保存后刷新列表
- [x] 5.6 实现删除按钮逻辑：确认对话框 + 删除记录 + 刷新列表
- [x] 5.7 实现新增按钮逻辑：打开 ProviderDialog 新增模式，保存后刷新列表

## 6. 系统托盘

- [x] 6.1 在 App.xaml.cs 中初始化 NotifyIcon，设置托盘图标
- [x] 6.2 实现托盘右键菜单：显示主窗口 / 退出
- [x] 6.3 实现关闭窗口时隐藏到托盘（重写 OnClosing）
- [x] 6.4 实现双击托盘图标显示主窗口
- [x] 6.5 实现退出时清理 NotifyIcon 资源

## 7. 启动流程集成

- [x] 7.1 在 App.xaml.cs 中串联启动流程：初始化数据库 → 创建托盘 → 显示主窗口
- [x] 7.2 整体功能验证：新增/编辑/删除/激活供应商，配置文件写入正确

## 8. 文档

- [x] 8.1 生成 README.md：项目简介、功能说明、技术栈、构建与运行方式



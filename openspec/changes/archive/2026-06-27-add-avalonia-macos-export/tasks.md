## 1. 方案骨架与项目结构

- [x] 1.1 创建 `src/Core/Core.csproj`（`net8.0`）并加入解决方案
- [x] 1.2 创建 `src/Avalonia/Avalonia.csproj` 并加入解决方案
- [x] 1.3 配置 `Avalonia` 引用 `Core`
- [x] 1.4 保留现有 `src/APISwitch`（WPF）项目，确保并行构建路径可用

## 2. 迁移 Core 业务层

- [x] 2.1 将 `Models/*` 迁移到 `Core/Models` 并修正命名空间引用
- [x] 2.2 将 `Services/*` 迁移到 `Core/Services` 并修正命名空间引用
- [x] 2.3 处理服务中的平台路径与文件访问逻辑，确保在 macOS/Windows 均可执行
- [x] 2.4 验证 Core 可独立构建，且不依赖 WPF/WinForms 类型

## 3. 实现 Avalonia 主流程 UI（code-behind）

- [x] 3.1 搭建 Avalonia `MainWindow`（Codex/Claude 切换、供应商卡片、按钮区）
- [x] 3.2 实现供应商增删改、启用、测试、上下移动等事件处理（复用 Core 服务）
- [x] 3.3 搭建并接入 `ProviderDialog`（含模型拉取与过滤）
- [x] 3.4 搭建并接入 `SessionWindow`（会话列表、消息详情、删除）
- [x] 3.5 实现打开配置目录与 BaseUrl 链接打开的跨平台 shell 调用

## 4. 平台差异处理（托盘与窗口生命周期）

- [x] 4.1 为 Windows 保留托盘行为（显示主窗口、退出等）
- [x] 4.2 在 macOS 无托盘实现下提供降级行为（关闭主窗口即退出）
- [x] 4.3 处理单实例与窗口激活策略，避免跨平台 API 不兼容
- [x] 4.4 对照 `system-tray` 变更 spec 做行为回归校验

## 5. 发布与文档

- [x] 5.1 增加 `osx-x64` 发布命令（`--self-contained true`）到 README 或独立发布文档
- [x] 5.2 补充“本机自用、无需签名公证”的运行说明
- [x] 5.3 补充 Windows 构建/运行说明（WPF 或 Avalonia Windows 目标）

## 6. 验证与收尾

- [x] 6.1 执行 `dotnet build APISwitch.sln`，确保新旧项目可同时构建
- [x] 6.2 在 macOS Intel 执行 `osx-x64` 发布并完成启动验证
- [x] 6.3 完成最小 smoke 测试：供应商增删改、启用写配置、会话窗口入口、关闭行为
- [x] 6.4 更新 Readme.md

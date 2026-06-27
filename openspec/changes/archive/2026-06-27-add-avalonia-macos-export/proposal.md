## Why

当前项目基于 `net8.0-windows` + WPF/WinForms，仅能发布 Windows 版本，无法导出并运行 macOS 版本。现在需要在不引入 MVVM 重构的前提下，支持本机 Intel Mac（`osx-x64`）可运行产物，并保留 Windows 可运行能力。

## What Changes

- 新增 `Core`（`net8.0`）类库，承载 `Models` 与 `Services` 等跨平台业务逻辑
- 新增 `Avalonia` 桌面项目（继续使用 XAML + code-behind，不引入 MVVM），覆盖主窗口、供应商对话框、会话管理窗口核心交互
- 增加 `osx-x64` 本机发布流程（自包含产物），明确“本机自用、无需上架签名公证”的交付方式
- 保持 Windows 可运行：继续保留现有 WPF 项目，并允许 Avalonia 项目在 Windows 目标上发布运行
- 调整托盘能力的跨平台约束：Windows 保持托盘行为，macOS 在无托盘实现时提供可接受降级行为

## Capabilities

### New Capabilities
- `cross-platform-app`: 提供基于 Avalonia 的跨平台桌面壳层与发布能力，支持至少 `osx-x64` 与 Windows 运行

### Modified Capabilities
- `system-tray`: 将托盘能力从“统一强依赖”调整为“按平台要求”，确保 macOS 目标可发布可用

## Impact

- 新增项目：`src/Core/`、`src/Avalonia/`
- 调整解决方案：`APISwitch.sln` 引入新项目与依赖关系
- 可能复用/迁移文件：`src/APISwitch/Models/*`、`src/APISwitch/Services/*` 到 Core
- 新增发布脚本或命令文档：`osx-x64` 发布、运行与本机 `.app` 使用说明
- 运行时影响：同一业务逻辑被 Windows 与 macOS UI 复用，降低多端维护分叉

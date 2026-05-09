## Context

当前 APISwitch 是单一 WPF 项目（`net8.0-windows`），并依赖 WinForms 托盘实现。该结构导致 UI 与业务逻辑耦合在同一工程中，且无法发布 macOS。用户目标是输出可在 Intel Mac 本机运行的 `osx-x64` 产物，同时保持 Windows 可运行，并明确不引入 MVVM 改造。

## Goals / Non-Goals

**Goals:**
- 将模型与服务逻辑沉淀到 `Core`，供多 UI 复用
- 新增 `Avalonia` 作为跨平台桌面前端，使用 XAML + code-behind
- 支持 `osx-x64` 自包含发布并可本机启动
- 保留 Windows 可运行路径，避免一次性替换带来的发布风险

**Non-Goals:**
- 不在本次引入 MVVM、DI 容器或大规模重构
- 不处理 Mac App Store 上架、签名、公证流程
- 不对 Provider 数据结构和配置写入格式做功能性变更

## Decisions

### 1. 采用双项目分层：`Core` + `Avalonia`
- 决策：将 `Models/*` 与 `Services/*` 迁移至 `Core`（`net8.0`），Avalonia/WPF 前端均通过引用 Core 使用同一套业务逻辑。
- 原因：最低成本实现跨平台并减少重复实现。
- 备选方案：
  - 继续在 WPF 项目内用 `#if` 做多平台分支：不可行（WPF 本身 Windows 专属）。
  - 全量重写单一 Avalonia 项目并删除 WPF：切换风险高、回退成本高。

### 2. UI 迁移保持 code-behind，不引入 MVVM
- 决策：Avalonia 端沿用当前事件驱动结构（按钮点击、窗口交互、消息框提示）。
- 原因：满足用户限制条件，缩短迁移周期。
- 备选方案：
  - 迁移同时改 MVVM：长期可维护性更高，但短期改造量显著增加，不符合本次目标。

### 3. 托盘与关闭行为按平台分层
- 决策：Windows 保持托盘行为；macOS 允许无托盘实现，关闭主窗口时采用明确降级策略（退出应用）。
- 原因：macOS 托盘/状态栏实现差异较大，本次目标优先保证可发布可运行。
- 备选方案：
  - 强制两端完全一致托盘行为：实现复杂度更高，阻塞 mac 首次交付。

### 4. 发布策略聚焦 `osx-x64` 本机自用
- 决策：提供 `dotnet publish -r osx-x64 --self-contained true` 标准流程，并补充运行说明。
- 原因：匹配 Intel Mac 目标，且不依赖签名公证。
- 备选方案：
  - 同时覆盖 `osx-arm64`：可扩展但非当前必需，增加验证矩阵。

### 5. 渐进迁移与可回退策略
- 决策：保留现有 WPF 项目，新增 Avalonia 路径并行验证；通过独立发布命令切换产物。
- 原因：降低迁移中断风险，允许逐功能回归测试。
- 备选方案：
  - 一次性替换入口工程：变更面过大，回退困难。

## Risks / Trade-offs

- [Avalonia 与 WPF 控件行为差异导致交互回归] → 先迁移主流程（供应商管理/启用/测试/会话入口），用手工 smoke case 逐项回归。
- [系统托盘跨平台能力不一致] → 将托盘要求改为按平台约束，macOS 明确降级行为。
- [发布产物体积增大（self-contained）] → 当前接受体积换取部署简化。
- [并行维护两套 UI 壳层] → 业务逻辑统一沉淀到 Core，降低分叉维护成本。

## Migration Plan

1. 新增 `Core` 并迁移 `Models`、`Services`。
2. 新建 `Avalonia`，先实现主窗口与 ProviderDialog 的等价交互。
3. 迁移 SessionWindow 关键能力（列表、详情、删除、目录打开）。
4. 接入平台差异处理（托盘、窗口关闭、单实例策略）。
5. 更新 `APISwitch.sln` 与 README 发布文档，增加 `osx-x64` 发布命令。
6. 在 Intel Mac 执行发布与本机运行验证，保留 WPF 构建通道用于回退。

## Open Questions

- macOS 首版是否需要状态栏菜单（若需要，将追加托盘实现任务）。
- 现有 WPF 工程是否在 Avalonia 稳定后进入维护模式或下线。

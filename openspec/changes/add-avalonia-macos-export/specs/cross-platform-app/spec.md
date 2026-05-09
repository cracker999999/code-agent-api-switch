## ADDED Requirements

### Requirement: 跨平台工程分层
系统 MUST 提供跨平台分层结构：`Core` 承载可复用业务逻辑，`Avalonia` 承载跨平台桌面 UI，并通过项目引用使用 Core。

#### Scenario: 解决方案包含 Core 与 Avalonia
- **WHEN** 开发者打开并构建解决方案
- **THEN** 可以看到 `Core` 与 `Avalonia` 项目，且 `Avalonia` 引用 `Core`

### Requirement: 业务能力跨 UI 复用
系统 MUST 使 Provider 管理、配置写入、会话数据读取等业务逻辑在 Core 中复用，避免在 Avalonia 中重复实现独立业务分支。

#### Scenario: 激活供应商触发统一配置写入
- **WHEN** 用户在 Avalonia 主窗口点击“启用”某供应商
- **THEN** 系统通过 Core 中的配置写入服务完成文件更新，行为与现有 Windows 版本一致

### Requirement: macOS Intel 发布能力
系统 SHALL 支持 `osx-x64` 自包含发布，以便在 Intel Mac 本机运行。

#### Scenario: 执行 osx-x64 发布
- **WHEN** 开发者执行 `dotnet publish` 并指定 `-r osx-x64 --self-contained true`
- **THEN** 生成可在 macOS Intel 本机启动的发布产物

### Requirement: Windows 可运行兼容
系统 MUST 在引入 Avalonia 后保持 Windows 可运行路径，避免迁移期间仅剩 macOS 可用。

#### Scenario: Windows 目标仍可构建运行
- **WHEN** 开发者执行 Windows 目标构建/发布
- **THEN** 应用可在 Windows 上正常启动并完成核心操作（供应商增删改、启用、配置写入）

### Requirement: UI 架构保持事件驱动
系统 MUST 在本次迁移中继续使用 XAML + code-behind 交互方式，不强制引入 MVVM。

#### Scenario: 交互逻辑位于 code-behind
- **WHEN** 开发者检查 Avalonia 窗口事件处理
- **THEN** 主要交互（按钮点击、窗口切换、对话框确认）在 code-behind 中实现

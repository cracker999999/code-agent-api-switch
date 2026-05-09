# system-tray Specification

## Purpose
TBD - created by archiving change apiswitch-desktop-app. Update Purpose after archive.
## Requirements
### Requirement: 启动时创建托盘图标
系统 SHALL 在启动时创建 NotifyIcon 并显示在系统托盘。

#### Scenario: 应用启动
- **WHEN** 应用启动
- **THEN** 系统托盘显示应用图标

### Requirement: 托盘右键菜单
托盘图标右键 SHALL 显示菜单，包含"显示主窗口"和"退出"两项。

#### Scenario: 点击显示主窗口
- **WHEN** 用户右键托盘图标并点击"显示主窗口"
- **THEN** 主窗口显示并激活到前台

#### Scenario: 点击退出
- **WHEN** 用户右键托盘图标并点击"退出"
- **THEN** 应用调用 Application.Shutdown() 完全退出

### Requirement: 关闭窗口最小化到托盘
关闭主窗口 SHALL 隐藏窗口而非退出程序。

#### Scenario: 点击窗口关闭按钮
- **WHEN** 用户点击主窗口的关闭按钮
- **THEN** 窗口隐藏，应用继续在系统托盘运行

### Requirement: 双击托盘图标恢复窗口
双击托盘图标 SHALL 显示主窗口。

#### Scenario: 双击托盘图标
- **WHEN** 用户双击系统托盘图标
- **THEN** 主窗口显示并激活到前台

### Requirement: macOS Dock 图标随窗口可见性切换
系统 SHALL 在 macOS 下根据窗口可见状态动态切换 Dock 图标显示。

#### Scenario: 至少一个窗口可见
- **WHEN** 应用运行于 macOS 且任一窗口可见
- **THEN** Dock 显示应用图标

#### Scenario: 无任何窗口可见
- **WHEN** 应用运行于 macOS 且所有窗口均不可见
- **THEN** Dock 隐藏应用图标，仅保留菜单栏图标

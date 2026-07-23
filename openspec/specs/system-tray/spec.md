# system-tray Specification

## Purpose
定义 APISwitch 在支持托盘的平台上的托盘行为，并明确 macOS 首版无托盘实现时的窗口降级、退出策略与 Dock 图标可见性要求。

## Requirements
### Requirement: 启动时创建托盘图标
系统 SHALL 在支持托盘的平台（Windows）启动时创建并显示托盘图标；在不提供托盘实现的平台（macOS 首版）应用仍 MUST 可正常使用主窗口功能。

#### Scenario: Windows 启动显示托盘
- **WHEN** 应用在 Windows 启动
- **THEN** 系统托盘显示应用图标

#### Scenario: macOS 启动无托盘降级
- **WHEN** 应用在 macOS 启动且未启用托盘实现
- **THEN** 应用主窗口正常显示并可执行核心功能

### Requirement: 托盘右键菜单
系统 SHALL 在支持托盘的平台提供托盘菜单；在无托盘平台不强制该入口。

#### Scenario: Windows 托盘菜单可用
- **WHEN** 用户在 Windows 右键托盘图标
- **THEN** 菜单至少包含“显示主窗口”和“退出”操作

#### Scenario: macOS 无托盘入口
- **WHEN** 应用运行于 macOS 且未启用托盘
- **THEN** 用户仍可通过窗口内控件完成核心操作并退出应用

### Requirement: 关闭窗口最小化到托盘
系统 SHALL 在支持托盘的平台将主窗口关闭行为映射为“隐藏到托盘”；在无托盘平台关闭主窗口 MUST 触发应用退出。

#### Scenario: Windows 关闭主窗口
- **WHEN** 用户点击主窗口关闭按钮且应用运行于 Windows
- **THEN** 主窗口隐藏，应用继续运行于托盘

#### Scenario: macOS 关闭主窗口
- **WHEN** 用户点击主窗口关闭按钮且应用运行于 macOS
- **THEN** 应用进程退出

### Requirement: 双击托盘图标恢复窗口
系统 SHALL 在支持托盘的平台支持双击托盘图标恢复主窗口。

#### Scenario: Windows 双击托盘图标
- **WHEN** 用户在 Windows 双击托盘图标
- **THEN** 主窗口显示并激活到前台

#### Scenario: macOS 无托盘实现
- **WHEN** 应用运行于 macOS 且无托盘图标
- **THEN** 系统不要求提供双击托盘恢复行为

### Requirement: macOS Dock 图标随窗口可见性切换
系统 SHALL 在 macOS 下根据窗口可见状态动态切换 Dock 图标显示。

#### Scenario: 至少一个窗口可见
- **WHEN** 应用运行于 macOS 且任一窗口可见
- **THEN** Dock 显示应用图标

#### Scenario: 无任何窗口可见
- **WHEN** 应用运行于 macOS 且所有窗口均不可见
- **THEN** Dock 隐藏应用图标，仅保留菜单栏图标

### Requirement: 托盘提示展示 Grok 激活供应商
托盘提示 SHALL 同时展示 Codex、Claude Code 和 Grok 的当前激活供应商名称。

#### Scenario: 刷新托盘提示
- **WHEN** 任一工具分类的供应商列表刷新
- **THEN** 托盘提示包含 Grok 当前激活供应商；若未启用则显示"未启用"

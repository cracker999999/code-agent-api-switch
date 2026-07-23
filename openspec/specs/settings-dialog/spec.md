# settings-dialog Specification

## Purpose
TBD - created by archiving change add-grok-provider. Update Purpose after archive.
## Requirements
### Requirement: 设置页工具分区
设置页 SHALL 展示 Codex、Claude Code、Grok 三个工具分区，每个分区包含默认测试模型、测试端点路径、Prompt 文本和客户端版本号。

#### Scenario: 展示 Grok 设置
- **WHEN** 用户打开设置页
- **THEN** 系统显示 Grok 分区，并显示 Grok 默认测试模型、测试端点路径、Prompt 文本和客户端版本号输入框

#### Scenario: Grok 客户端版本号默认值
- **WHEN** Grok 客户端版本号未配置
- **THEN** 系统使用默认值 `0.2.111`

### Requirement: Grok 配置目录入口
设置页 SHALL 提供 Grok 配置目录打开入口。

#### Scenario: 打开 Grok 配置目录
- **WHEN** 用户点击 Grok 分区的“打开配置目录”
- **THEN** 系统打开 `%USERPROFILE%\.grok`

## ADDED Requirements

### Requirement: Grok 配置写入
系统 SHALL 在激活 Grok 供应商时写入 Grok Build 的 `config.toml`。

#### Scenario: 写入 Grok config.toml
- **WHEN** 用户激活一个 ToolType=2（Grok）的供应商
- **THEN** 系统写入 `models_base_url`、`xai_api_base_url`、`api_key` 三个顶层 TOML 字段

#### Scenario: Grok 配置路径
- **WHEN** 系统需要定位 Grok 配置文件
- **THEN** 系统使用 `%USERPROFILE%\.grok\config.toml`

#### Scenario: Grok config.toml 不存在
- **WHEN** 用户激活 Grok 供应商但 `config.toml` 不存在
- **THEN** 系统自动创建目录和 `config.toml` 并写入 Grok 字段

#### Scenario: Grok config.toml 已存在
- **WHEN** 用户激活 Grok 供应商且 `config.toml` 已存在
- **THEN** 系统先备份为 `.bak`，再仅更新或插入顶层 Grok 字段

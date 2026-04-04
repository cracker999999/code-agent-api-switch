# config-writer Specification

## Purpose
TBD - created by archiving change apiswitch-desktop-app. Update Purpose after archive.
## Requirements
### Requirement: Codex 配置写入
系统 SHALL 在激活 Codex 供应商时写入两个配置文件：config.toml 和 auth.json。

#### Scenario: 写入 config.toml 的 base_url
- **WHEN** 用户激活一个 Codex 供应商
- **THEN** 系统使用正则表达式定位 `%USERPROFILE%\.codex\config.toml` 中 `[model_providers.OpenAI]` 段下的 `base_url` 行并替换其值

#### Scenario: 写入 auth.json 的 OPENAI_API_KEY
- **WHEN** 用户激活一个 Codex 供应商
- **THEN** 系统修改 `%USERPROFILE%\.codex\auth.json` 中的 `OPENAI_API_KEY` 字段并 JSON 序列化写回

### Requirement: Claude Code 配置写入
系统 SHALL 在激活 Claude Code 供应商时写入 settings.json。

#### Scenario: 写入 settings.json
- **WHEN** 用户激活一个 Claude Code 供应商
- **THEN** 系统修改 `%USERPROFILE%\.claude\settings.json` 中的 `env.ANTHROPIC_AUTH_TOKEN` 和 `env.ANTHROPIC_BASE_URL`

#### Scenario: env 节点不存在
- **WHEN** settings.json 中不存在 `env` 节点
- **THEN** 系统 SHALL 自动创建 `env` 节点并写入对应字段

### Requirement: 写入前备份
系统 SHALL 在写入任何配置文件前先备份原文件。

#### Scenario: 备份原文件
- **WHEN** 系统即将写入配置文件
- **THEN** 系统先将原文件复制为 `.bak` 后缀的备份文件

### Requirement: config.toml 不存在时报错
当 config.toml 不存在时系统 SHALL 报错提示用户先安装 Codex，不自动创建。

#### Scenario: config.toml 不存在
- **WHEN** 用户激活 Codex 供应商但 config.toml 不存在
- **THEN** 系统显示错误提示"请先安装 Codex"，不写入任何文件

### Requirement: JSON 配置文件自动创建
auth.json 和 settings.json 不存在时系统 SHALL 自动创建。

#### Scenario: auth.json 不存在
- **WHEN** 用户激活 Codex 供应商但 auth.json 不存在
- **THEN** 系统自动创建 auth.json 并写入 OPENAI_API_KEY

#### Scenario: settings.json 不存在
- **WHEN** 用户激活 Claude Code 供应商但 settings.json 不存在
- **THEN** 系统自动创建 settings.json 并写入 env 节点

### Requirement: 配置路径动态获取
系统 SHALL 使用 `Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)` 动态获取用户目录，不硬编码用户名。

#### Scenario: 动态路径
- **WHEN** 系统需要读写配置文件
- **THEN** 使用运行时获取的用户目录拼接路径


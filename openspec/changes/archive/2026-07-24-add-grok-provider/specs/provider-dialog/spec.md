## ADDED Requirements

### Requirement: Grok 供应商对话框
供应商对话框 SHALL 在 Grok 新增和编辑场景下使用 Grok 分类上下文。

#### Scenario: 新增 Grok 供应商
- **WHEN** 用户在 Grok 标签页点击新增按钮
- **THEN** 对话框标题显示"新增供应商（Grok）"，提交后供应商 ToolType 为 2

#### Scenario: Grok 默认测试模型占位
- **WHEN** 用户编辑 Grok 供应商且 TestModel 为空
- **THEN** 测试模型输入框占位显示 Grok 全局默认测试模型

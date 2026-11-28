# claude-gemini-adapter Specification

## Purpose
让 Claude Code 供应商通过现有本地代理使用 Gemini Native API，直接转换 Anthropic Messages 与 Gemini 格式。

## Requirements

### Requirement: Gemini 请求路由
系统 SHALL 将启用 Gemini Native 适配的 Claude 请求直接转换为 Gemini 请求，复用现有 HTTP 传输与本地代理生命周期。

#### Scenario: 非流式请求
- **WHEN** Claude Code 向本地代理发送 POST /v1/messages，stream 为 false 或缺省
- **THEN** 系统向 models/{model}:generateContent 发送转换后的请求，使用 x-goog-api-key 鉴权
- **AND** BaseUrl 缺少版本路径时补充 /v1beta，已有版本和网关前缀保持不变
- **AND** 模型来自 Claude 请求，允许 models/ 前缀，不使用测试模型替换运行时模型

#### Scenario: 流式请求
- **WHEN** 请求的 stream 为 true
- **THEN** 系统调用 models/{model}:streamGenerateContent?alt=sse
- **AND** 直接将 Gemini SSE 转换为 Anthropic message_start、content_block_start/delta/stop、message_delta 和 message_stop
- **AND** 上游返回完整 JSON 时也能转换成 Anthropic SSE

### Requirement: 消息与工具转换
系统 SHALL 转换 system、消息角色、文本、图片和文档、工具定义、工具选择、生成参数及适用模型的思考参数。

#### Scenario: Gemini 4 思考参数
- **WHEN** 请求使用 gemini-4-neon 或 gemini-4-carbon，并设置 thinking
- **THEN** 系统将启用思考时的 output_config.effort 转换为 generationConfig.thinkingConfig.thinkingLevel，low、medium、high 保留，xhigh 和 max 转为 high
- **AND** 未指定 effort 时，budget_tokens 小于 4000 转为 low，小于 16000 转为 medium，其余转为 high；缺省预算按 24576 处理
- **AND** disabled 转为 low，Gemini 4 不发送 thinkingBudget

#### Scenario: 未设置思考参数
- **WHEN** 请求未设置 thinking 和 output_config.effort
- **THEN** 系统不发送 thinkingConfig，保留模型默认行为

#### Scenario: 保留工具约束
- **WHEN** 工具 input_schema 包含联合类型、引用或 additionalProperties 等 JSON Schema 约束
- **THEN** 系统使用 Gemini parametersJsonSchema 保留约束

#### Scenario: 多轮并行工具调用
- **WHEN** Gemini 返回一个或多个 functionCall
- **THEN** 系统返回唯一的 Claude tool_use ID，并保存原上游 ID、参数和 thoughtSignature
- **AND** 后续 tool_result 按 ID 还原 functionResponse.name 和上游 ID，相邻 user 消息中的工具结果合并发送
- **AND** 合成的 Claude ID 不发送给 Gemini，错误工具结果保留 error 信息

#### Scenario: 签名历史
- **WHEN** 同一供应商和模型的历史工具调用仍在缓存中
- **THEN** 参数未改变时系统原样回传对应的 Gemini 工具 part
- **AND** 缓存仅保留最近 256 次调用及约 8 MiB 内容，重启、淘汰或切换供应商后，无签名的 Gemini 3/4 历史使用官方 skip_thought_signature_validator 占位值

#### Scenario: 历史工具参数变化
- **WHEN** Claude Code 在同名历史工具调用中补充默认值、规范化路径或修改参数
- **THEN** 系统使用当前参数重建调用，不复用旧签名；Gemini 3/4 使用 skip_thought_signature_validator 占位值，其他模型不附加旧签名
- **AND** 保留原上游 ID，使 functionCall 与 functionResponse 的 ID 一致，不发送合成的 Claude ID
- **AND** 原始签名缓存保持不变，未改变的历史仍可原样回放
- **AND** 工具名称改变或 input 不是 JSON 对象时仍明确报错

### Requirement: 响应语义
系统 SHALL 保留文本、工具调用、停止原因和 token 用量，明确报告上游错误。

#### Scenario: 用量统计
- **WHEN** Gemini 返回 usageMetadata，包括只在尾帧返回的情况
- **THEN** input_tokens 为 promptTokenCount 扣除缓存，cache_read_input_tokens 单列缓存
- **AND** output_tokens 包含生成文本和思考用量

#### Scenario: 拒绝与中断
- **WHEN** 上游拒绝请求、返回 HTTP 错误、损坏 SSE 或流提前结束
- **THEN** 系统保留 refusal 或错误信息，HTTP 错误保留状态码
- **AND** 未收到 finishReason 的流不得输出工具调用或 message_stop

#### Scenario: 思考摘要
- **WHEN** Gemini 返回 thought 标记的内部摘要
- **THEN** 系统不将摘要转换为 Anthropic 签名思考块，工具签名单独保存和回传

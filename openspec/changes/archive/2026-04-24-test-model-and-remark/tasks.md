## 1. 数据模型与数据库

- [x] 1.1 Provider.cs 新增 TestModel（string）和 Remark（string）属性
- [x] 1.2 DatabaseService.cs 初始化时 ALTER TABLE 添加 TestModel TEXT 和 Remark TEXT 列
- [x] 1.3 DatabaseService.cs 的 INSERT/UPDATE/SELECT 语句补充 TestModel 和 Remark 字段

## 2. 供应商编辑对话框

- [x] 2.1 ProviderDialog.xaml 在 ApiKey 下方新增"测试模型"输入框
- [x] 2.2 ProviderDialog.xaml 在名称输入框右侧同行新增"备注"输入框
- [x] 2.3 ProviderDialog.xaml.cs 编辑模式预填 TestModel 和 Remark，确认时读取并保存

## 3. 测试服务

- [x] 3.1 ApiTestService.TestCodexAsync 使用 provider.TestModel，为空时 fallback 到 gpt-5.3-codex
- [x] 3.2 ApiTestService.TestClaudeAsync 使用 provider.TestModel，为空时 fallback 到 claude-opus-4-6

## 4. 主窗口列表

- [x] 4.1 MainWindow.xaml 供应商卡片名称右侧添加备注 TextBlock，颜色 #9CA3AF，字号 11，Remark 为空时隐藏

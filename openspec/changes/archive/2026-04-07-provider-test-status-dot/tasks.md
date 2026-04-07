## 1. 数据模型与数据库迁移

- [x] 1.1 在 `Models/Provider.cs` 增加 `TestStatus` 字段（0=未知，1=可用，2=失败）
- [x] 1.2 在 `DatabaseService` 建表 SQL 中加入 `TestStatus INTEGER NOT NULL DEFAULT 0`
- [x] 1.3 在 `DatabaseService.Initialize` 增加历史库补列逻辑（检测并补齐 `TestStatus`）
- [x] 1.4 更新 `DatabaseService` 的查询与新增/编辑映射，确保 `TestStatus` 读写一致

## 2. 测试状态持久化逻辑

- [x] 2.1 在 `DatabaseService` 增加按供应商更新测试状态的方法
- [x] 2.2 在测试按钮成功分支写入 `TestStatus=1` 并刷新列表
- [x] 2.3 在测试按钮失败分支写入 `TestStatus=2` 并刷新列表
- [x] 2.4 在供应商编辑保存后重置 `TestStatus=0`

## 3. 主窗口状态点展示

- [x] 3.1 在 `MainWindow.xaml` 名称区域增加状态点占位，位置在供应商名称前
- [x] 3.2 实现状态点可见性与颜色绑定：`1` 显示绿点，`2` 显示红点，`0` 不显示
- [x] 3.3 确认状态点不显示任何文字，且不影响现有按钮布局

## 4. 验证与回归

- [x] 4.1 构建验证：`dotnet restore` + `dotnet build`
- [x] 4.2 手工验证：测试成功后显示绿点并重启后仍保留
- [x] 4.3 手工验证：测试失败后显示红点并重启后仍保留
- [x] 4.4 手工验证：编辑供应商后状态点消失（`TestStatus=0`）



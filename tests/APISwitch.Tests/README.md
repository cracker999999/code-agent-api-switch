在 Windows 上运行无需额外测试依赖的内存回归自检：

```pwsh
dotnet run --project tests/APISwitch.Tests -c Release
dotnet run --project tests/APISwitch.Tests -c Release -p:SessionUi=Avalonia
```

测量 WPF 默认 Codex 列表的实际打开、关闭内存（不选择会话，不由测试手动触发 GC）：

```pwsh
dotnet run --project tests/APISwitch.Tests -c Release -- --measure-session-list
```

该模式只读扫描本机 Codex 日志，连续三次完成加载后关闭，并验证加载中直接关闭也能回收窗口。

测量 WPF 详情的关闭回收（测试不额外触发 GC）：

```pwsh
dotnet run --project tests/APISwitch.Tests -c Release -- --measure-session-details
```

该模式连续三次加载包含约 40 MB 工具文本的详情，覆盖未进入可视区域的消息，记录工作集、私有内存和托管堆。长弱引用检查确保正文在终结器处理后也已释放。

默认自检检查读取取消和文件释放，并创建不可见窗口，连续三次加载 Markdown、图片、工具消息和计划后切换、关闭，用弱引用确认窗口、虚拟化控件和消息集合可回收。Avalonia 额外检查切换和关闭时位图已释放。

窗口构造时会按正常流程扫描本机 Grok 会话目录，测试立即关闭并取消扫描，不修改会话文件。测试中的强制 GC 仅用于检查对象引用，不用于降低任务管理器显示的工作集。

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace APISwitch.Services;

internal static class SessionTrash
{
    public static void Move(string path)
    {
        path = Path.GetFullPath(path);
        try
        {
            _ = File.GetAttributes(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // 主日志可能已消失，但调用方仍需回收对应的备份和子代理。
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            // 删除由 Task.Run 发起；Windows Shell 的 COM 操作必须在 STA 线程执行。
            var completion = new TaskCompletionSource();
            var thread = new Thread(() =>
            {
                try
                {
                    MoveOnWindows(path);
                    completion.SetResult();
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            completion.Task.GetAwaiter().GetResult();
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/osascript",
                UseShellExecute = false,
                RedirectStandardError = true,
                ArgumentList = { "-l", "JavaScript", "-e", MacTrashScript, "--", path },
            };
            using var process = Process.Start(startInfo)
                ?? throw new IOException("无法启动系统废纸篓操作。");
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new IOException($"无法移入废纸篓：{error.Trim()}");
            }

            return;
        }

        throw new PlatformNotSupportedException("当前系统不支持将会话移入回收站或废纸篓。");
    }

    // 使用系统 API 处理同名文件、外置卷和还原信息；路径通过独立参数传入，不拼接脚本。
    private const string MacTrashScript = """
        ObjC.import("Foundation");
        function run(argv) {
            const error = Ref();
            const url = $.NSURL.fileURLWithPath(argv[0]);
            if (!$.NSFileManager.defaultManager.trashItemAtURLResultingItemURLError(url, Ref(), error)) {
                throw new Error(ObjC.unwrap(error[0].localizedDescription));
            }
        }
        """;

    private static void MoveOnWindows(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows 回收站仅支持 Windows。");
        }

        var operation = (IFileOperation)Activator.CreateInstance(
            Type.GetTypeFromCLSID(new Guid("3ad05575-8857-4850-9277-11b85bdb8e09"))!)!;
        var item = IntPtr.Zero;
        var sink = new RecycleProgressSink();
        try
        {
            // 禁止错误弹窗和永久删除降级；失败立即中断，并保留系统的还原记录。
            const uint silent = 0x0004, noConfirmation = 0x0010, noErrorUi = 0x0400;
            const uint recycleOnDelete = 0x00080000, earlyFailure = 0x00100000, addUndoRecord = 0x20000000;
            operation.SetOperationFlags(silent | noConfirmation | noErrorUi | recycleOnDelete | earlyFailure | addUndoRecord);
            var shellItemId = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref shellItemId, out item);
            operation.DeleteItem(item, sink);
            operation.PerformOperations();
            Marshal.ThrowExceptionForHR(sink.Result);
            operation.GetAnyOperationsAborted(out var aborted);
            if (aborted)
            {
                throw new IOException("移入回收站的操作已取消。");
            }
        }
        catch (COMException ex) when (sink.CannotRecycle)
        {
            throw new IOException($"无法将“{path}”移入回收站，已取消删除。", ex);
        }
        finally
        {
            if (item != IntPtr.Zero)
            {
                Marshal.Release(item);
            }

            Marshal.FinalReleaseComObject(operation);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid interfaceId, out IntPtr item);

    [ComImport, Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        void Advise(IntPtr sink, out uint cookie);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        void SetProgressDialog(IntPtr dialog);
        void SetProperties(IntPtr properties);
        void SetOwnerWindow(IntPtr window);
        void ApplyPropertiesToItem(IntPtr item);
        void ApplyPropertiesToItems(IntPtr items);
        void RenameItem(IntPtr item, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr sink);
        void RenameItems(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] string name);
        void MoveItem(IntPtr item, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr sink);
        void MoveItems(IntPtr items, IntPtr destination);
        void CopyItem(IntPtr item, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr sink);
        void CopyItems(IntPtr items, IntPtr destination);
        void DeleteItem(IntPtr item, IFileOperationProgressSink sink);
        void DeleteItems(IntPtr items);
        void NewItem(IntPtr destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name,
            [MarshalAs(UnmanagedType.LPWStr)] string template, IntPtr sink);
        void PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }

    [ComVisible(true), Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int result);
        [PreserveSig] int PreRenameItem(uint flags, IntPtr item, IntPtr name);
        [PreserveSig] int PostRenameItem(uint flags, IntPtr item, IntPtr name, int result, IntPtr newItem);
        [PreserveSig] int PreMoveItem(uint flags, IntPtr item, IntPtr destination, IntPtr name);
        [PreserveSig] int PostMoveItem(uint flags, IntPtr item, IntPtr destination, IntPtr name, int result, IntPtr newItem);
        [PreserveSig] int PreCopyItem(uint flags, IntPtr item, IntPtr destination, IntPtr name);
        [PreserveSig] int PostCopyItem(uint flags, IntPtr item, IntPtr destination, IntPtr name, int result, IntPtr newItem);
        [PreserveSig] int PreDeleteItem(uint flags, IntPtr item);
        [PreserveSig] int PostDeleteItem(uint flags, IntPtr item, int result, IntPtr newItem);
        [PreserveSig] int PreNewItem(uint flags, IntPtr destination, IntPtr name);
        [PreserveSig] int PostNewItem(uint flags, IntPtr destination, IntPtr name, IntPtr template, uint attributes,
            int result, IntPtr newItem);
        [PreserveSig] int UpdateProgress(uint total, uint completed);
        [PreserveSig] int ResetTimer();
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class RecycleProgressSink : IFileOperationProgressSink
    {
        public bool CannotRecycle { get; private set; }
        public int Result { get; private set; }

        public int PreDeleteItem(uint flags, IntPtr item)
        {
            // TSF_DELETE_RECYCLE_IF_POSSIBLE 缺失表示系统将永久删除，必须在执行前中止。
            if ((flags & 0x80) == 0)
            {
                CannotRecycle = true;
                return unchecked((int)0x80004004); // E_ABORT
            }

            return 0;
        }

        public int PostDeleteItem(uint flags, IntPtr item, int result, IntPtr newItem)
        {
            Result = result;
            return 0;
        }

        public int StartOperations() => 0;
        public int FinishOperations(int result) => 0;
        public int PreRenameItem(uint flags, IntPtr item, IntPtr name) => 0;
        public int PostRenameItem(uint flags, IntPtr item, IntPtr name, int result, IntPtr newItem) => 0;
        public int PreMoveItem(uint flags, IntPtr item, IntPtr destination, IntPtr name) => 0;
        public int PostMoveItem(uint flags, IntPtr item, IntPtr destination, IntPtr name, int result, IntPtr newItem) => 0;
        public int PreCopyItem(uint flags, IntPtr item, IntPtr destination, IntPtr name) => 0;
        public int PostCopyItem(uint flags, IntPtr item, IntPtr destination, IntPtr name, int result, IntPtr newItem) => 0;
        public int PreNewItem(uint flags, IntPtr destination, IntPtr name) => 0;
        public int PostNewItem(uint flags, IntPtr destination, IntPtr name, IntPtr template, uint attributes,
            int result, IntPtr newItem) => 0;
        public int UpdateProgress(uint total, uint completed) => 0;
        public int ResetTimer() => 0;
        public int PauseTimer() => 0;
        public int ResumeTimer() => 0;
    }
}

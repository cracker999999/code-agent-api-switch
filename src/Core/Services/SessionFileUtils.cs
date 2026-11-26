using System.IO;
using System.Text;

namespace APISwitch.Services;

public static class SessionFileUtils
{
    private const int TailReadWindowBytes = 16_384;
    private const FileShare SessionReadShare = FileShare.ReadWrite | FileShare.Delete;

    public static (List<string> HeadLines, List<string> TailLines) ReadHeadAndTailLines(
        string filePath,
        int headLineCount,
        int tailLineCount)
    {
        using var file = OpenReadShared(filePath);
        var fileLength = file.Length;

        if (fileLength < TailReadWindowBytes)
        {
            var allLines = new List<string>();
            using (var smallReader = new StreamReader(file))
            {
                allLines.AddRange(ReadLines(smallReader));
            }

            var head = allLines
                .Take(headLineCount)
                .ToList();
            var tailSkip = Math.Max(0, allLines.Count - tailLineCount);
            var tail = allLines
                .Skip(tailSkip)
                .ToList();
            return (head, tail);
        }

        var headLines = new List<string>(headLineCount);
        using (var headStream = OpenReadShared(filePath))
        using (var headReader = new StreamReader(headStream))
        {
            headLines.AddRange(ReadLines(headReader).Take(headLineCount));
        }

        var tailReadWindowBytes = (long)TailReadWindowBytes;
        List<string> tailCandidateLines;
        while (true)
        {
            var seekPosition = Math.Max(0, fileLength - tailReadWindowBytes);
            tailCandidateLines = new List<string>();
            using (var tailStream = OpenReadShared(filePath))
            {
                tailStream.Seek(seekPosition, SeekOrigin.Begin);
                using var tailReader = new StreamReader(tailStream);
                tailCandidateLines.AddRange(ReadLines(tailReader));
            }

            if (seekPosition > 0 && tailCandidateLines.Count > 0)
            {
                tailCandidateLines.RemoveAt(0);
            }

            if (seekPosition == 0 || tailCandidateLines.Count >= tailLineCount)
            {
                break;
            }

            // JSONL 单行可能包含大体积图片数据，逐步扩窗以确保真正取得末尾指定行数。
            tailReadWindowBytes = Math.Min(fileLength, tailReadWindowBytes * 2);
        }

        var tailStart = Math.Max(0, tailCandidateLines.Count - tailLineCount);
        var tailLines = tailCandidateLines
            .Skip(tailStart)
            .ToList();

        return (headLines, tailLines);
    }

    public static List<string> ReadAllLinesShared(string filePath)
    {
        return EnumerateLinesShared(filePath).ToList();
    }

    /// <summary>
    /// 流式逐行读取 - 会话日志单行可达上百 KB，全量读入会把大量字符串推进大对象堆。
    /// </summary>
    public static IEnumerable<string> EnumerateLinesShared(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = OpenReadShared(filePath);
        using var reader = new StreamReader(stream);
        foreach (var line in ReadLines(reader, cancellationToken))
        {
            yield return line;
        }
    }

    private static IEnumerable<string> ReadLines(StreamReader reader, CancellationToken cancellationToken = default)
    {
        // ReadLine 对长记录使用共享字符池；固定小块配合 StringBuilder，避免关闭后仍缓存数十 MB。
        var buffer = new char[4096];
        StringBuilder? remainder = null;
        var skipLineFeed = false;
        while (true)
        {
            // 在读下一条大记录前响应窗口关闭，避免继续分配日志和图片字符串。
            cancellationToken.ThrowIfCancellationRequested();
            var length = reader.Read(buffer, 0, buffer.Length);
            if (length == 0)
            {
                break;
            }

            // CRLF 可能跨越缓冲区边界；独立 CR、LF 也保留 StreamReader.ReadLine 的语义。
            var start = skipLineFeed && buffer[0] == '\n' ? 1 : 0;
            skipLineFeed = false;
            for (var index = start; index < length; index++)
            {
                if (buffer[index] is not ('\r' or '\n'))
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var line = remainder is null
                    ? new string(buffer, start, index - start)
                    : remainder.Append(buffer, start, index - start).ToString();
                remainder = null;
                if (buffer[index] == '\r')
                {
                    if (index + 1 < length && buffer[index + 1] == '\n')
                    {
                        index++;
                    }
                    else if (index == length - 1)
                    {
                        skipLineFeed = true;
                    }
                }

                start = index + 1;
                yield return line;
            }

            if (start < length)
            {
                (remainder ??= new StringBuilder()).Append(buffer, start, length - start);
            }
        }

        if (remainder is not null)
        {
            yield return remainder.ToString();
        }
    }

    private static FileStream OpenReadShared(string filePath)
    {
        return new FileStream(filePath, FileMode.Open, FileAccess.Read, SessionReadShare);
    }
}

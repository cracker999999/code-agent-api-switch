using System.IO;

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
                while (smallReader.ReadLine() is { } line)
                {
                    allLines.Add(line);
                }
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
            while (headLines.Count < headLineCount && headReader.ReadLine() is { } line)
            {
                headLines.Add(line);
            }
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
                while (tailReader.ReadLine() is { } line)
                {
                    tailCandidateLines.Add(line);
                }
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
    public static IEnumerable<string> EnumerateLinesShared(string filePath)
    {
        using var stream = OpenReadShared(filePath);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    private static FileStream OpenReadShared(string filePath)
    {
        return new FileStream(filePath, FileMode.Open, FileAccess.Read, SessionReadShare);
    }
}

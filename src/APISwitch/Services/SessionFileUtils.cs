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

        var seekPosition = Math.Max(0, fileLength - TailReadWindowBytes);
        var tailCandidateLines = new List<string>();
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

        var tailStart = Math.Max(0, tailCandidateLines.Count - tailLineCount);
        var tailLines = tailCandidateLines
            .Skip(tailStart)
            .ToList();

        return (headLines, tailLines);
    }

    public static List<string> ReadAllLinesShared(string filePath)
    {
        var lines = new List<string>();
        using var stream = OpenReadShared(filePath);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }

    private static FileStream OpenReadShared(string filePath)
    {
        return new FileStream(filePath, FileMode.Open, FileAccess.Read, SessionReadShare);
    }
}

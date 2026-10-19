namespace APISwitch.Services;

// 会话图片统一以 data URL 承载。显示与另存为共用解码结果,另存为直接写原始字节,避免重新编码导致失真或格式变化。
public static class ImageDataUrlDecoder
{
    // 非 base64 的 data URL 或远程 http 地址返回 false,调用方直接跳过该图片。
    public static bool TryDecode(string? imageDataUrl, out byte[] bytes, out string fileExtension)
    {
        bytes = Array.Empty<byte>();
        fileExtension = string.Empty;

        if (string.IsNullOrWhiteSpace(imageDataUrl))
        {
            return false;
        }

        var commaIndex = imageDataUrl.IndexOf(',');
        if (commaIndex <= 0 || commaIndex >= imageDataUrl.Length - 1)
        {
            return false;
        }

        var prefix = imageDataUrl[..commaIndex];
        if (!prefix.Contains(";base64", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            bytes = Convert.FromBase64String(imageDataUrl[(commaIndex + 1)..]);
        }
        catch (FormatException)
        {
            return false;
        }

        fileExtension = GetFileExtension(prefix);
        return true;
    }

    // prefix 形如 "data:image/png;base64",取 "/" 与 ";" 之间的子类型作为扩展名。
    private static string GetFileExtension(string prefix)
    {
        var slashIndex = prefix.IndexOf('/');
        var semicolonIndex = prefix.IndexOf(';');
        var subtype = slashIndex >= 0 && semicolonIndex > slashIndex
            ? prefix[(slashIndex + 1)..semicolonIndex].Trim().ToLowerInvariant()
            : string.Empty;

        return subtype switch
        {
            "jpeg" or "pjpeg" => "jpg",
            // 子类型可能带 "+" "-" 等字符(如 svg+xml、x-icon),不适合直接当扩展名,退回 png。
            _ when subtype.Length == 0 || !subtype.All(char.IsAsciiLetterOrDigit) => "png",
            _ => subtype
        };
    }
}

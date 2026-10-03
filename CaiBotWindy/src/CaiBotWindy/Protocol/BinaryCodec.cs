using System.IO.Compression;
using System.Text;

namespace CaiBotWindy.Protocol;

/// <summary>
/// 二进制内容的两层编码，与 Bot / 适配插件约定的流程一致：
/// <code>
/// bytes -> Base64 字符串 -> gzip(该字符串的 UTF-8 字节) -> 再 Base64
/// </code>
/// 用于 <c>map_image</c> / <c>world_file</c> / <c>map_file</c> 的 <c>payload.base64</c> 字段。
/// </summary>
public static class BinaryCodec
{
    public static string Encode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        string inner = Convert.ToBase64String(data);
        byte[] raw = Encoding.UTF8.GetBytes(inner);

        using MemoryStream buffer = new();
        using (GZipStream gzip = new(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(raw, 0, raw.Length);
        }

        return Convert.ToBase64String(buffer.ToArray());
    }

    public static string EncodeFile(string path)
    {
        return Encode(File.ReadAllBytes(path));
    }

    public static byte[] Decode(string encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return [];
        }

        byte[] compressed = Convert.FromBase64String(encoded);
        using MemoryStream input = new(compressed, writable: false);
        using GZipStream gzip = new(input, CompressionMode.Decompress);
        using MemoryStream output = new();
        gzip.CopyTo(output);

        string inner = Encoding.UTF8.GetString(output.ToArray());
        return Convert.FromBase64String(inner);
    }
}

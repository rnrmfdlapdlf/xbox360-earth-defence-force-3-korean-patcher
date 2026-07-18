using System.Security.Cryptography;
using System.Text;

namespace Edf3Koreanizer.Patching;

public static class PatchIdentity
{
    // The package stores only this keyed digest. No game path is serialized.
    private static readonly byte[] PathKey = FromHexString(
        "6ED3756E45DAF873C7D29C8E5D0C72E36A4D3A8F92AE381E9D09B771F243D2C8");

    private static readonly byte[] NodeKey = FromHexString(
        "8C9084BF4EDB36D31C7924F5138A1BE5463E60D4E340D2A48D2662BBE9348E98");

    public static string FromRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Relative path is required.", nameof(relativePath));
        }

        var normalized = relativePath
            .Replace('\\', '/')
            .TrimStart('/')
            .ToUpperInvariant();

        using var hmac = new HMACSHA256(PathKey);
        return HashUtility.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(normalized)));
    }

    public static string FromNodePath(string nodePath)
    {
        if (string.IsNullOrWhiteSpace(nodePath))
        {
            throw new ArgumentException("Node path is required.", nameof(nodePath));
        }

        using var hmac = new HMACSHA256(NodeKey);
        return HashUtility.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(nodePath)));
    }

    public static string FromSourceText(string source)
    {
        using var sha256 = SHA256.Create();
        return HashUtility.ToHexString(sha256.ComputeHash(Encoding.UTF8.GetBytes(source)));
    }

    private static byte[] FromHexString(string value)
    {
        if ((value.Length & 1) != 0)
        {
            throw new FormatException("Hexadecimal text must contain an even number of characters.");
        }

        var result = new byte[value.Length / 2];
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = Convert.ToByte(value.Substring(index * 2, 2), 16);
        }

        return result;
    }
}

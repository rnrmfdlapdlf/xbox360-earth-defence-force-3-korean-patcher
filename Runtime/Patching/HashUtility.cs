using System.Security.Cryptography;

namespace Edf3Koreanizer.Patching;

public static class HashUtility
{
    public static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                FileOptions.SequentialScan);
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(stream);
            cancellationToken.ThrowIfCancellationRequested();
            return ToHexString(hash);
        }, cancellationToken);
    }

    internal static string ToHexString(byte[] bytes)
    {
        var result = new char[bytes.Length * 2];
        const string alphabet = "0123456789ABCDEF";
        for (var index = 0; index < bytes.Length; index++)
        {
            result[index * 2] = alphabet[bytes[index] >> 4];
            result[index * 2 + 1] = alphabet[bytes[index] & 0x0F];
        }

        return new string(result);
    }
}

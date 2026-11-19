using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Rolehound.Api.Tests.Infrastructure;

/// <summary>RFC 6238 codes (SHA-1, 30 seconds, 6 digits), as an authenticator app computes them.</summary>
internal static class Totp
{
    public static string Code(string base32Key, DateTimeOffset at)
    {
        var key = Base32Decode(base32Key.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant());
        var counter = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, at.ToUnixTimeSeconds() / 30);

#pragma warning disable CA5350 // RFC 6238 default; this is the test side of the authenticator.
        var hash = HMACSHA1.HashData(key, counter);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var binary = (BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset)) & 0x7FFFFFFF) % 1_000_000;
        return binary.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in input.TrimEnd('='))
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        return [.. output];
    }
}

using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace APINineHalo.Services;

/// <summary>RFC 6238 / Google Authenticator: HMAC-SHA1, 6 dígitos, pasos de 30 segundos.</summary>
public static class TotpAlgorithm
{
    public static string GenerateCode(byte[] secret, long step, int digits = 6)
    {
        if (step < 0 || digits is not (6 or 8)) throw new ArgumentOutOfRangeException();
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        byte[] hash = HMACSHA1.HashData(secret, counter);
        int offset = hash[^1] & 15;
        int value = BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset, 4)) & 0x7fffffff;
        return (value % (digits == 6 ? 1_000_000 : 100_000_000)).ToString(new string('0', digits), CultureInfo.InvariantCulture);
    }

    public static long? Match(byte[] secret, string? code, DateTimeOffset now, long? lastStep)
    {
        if (code is null || code.Length != 6 || !code.All(c => c is >= '0' and <= '9')) return null;
        long current = now.ToUnixTimeSeconds() / 30;
        byte[] candidate = Encoding.ASCII.GetBytes(code);
        foreach (long step in new[] { current + 1, current, current - 1 })
            if (step >= 0 && (!lastStep.HasValue || step > lastStep) &&
                CryptographicOperations.FixedTimeEquals(candidate, Encoding.ASCII.GetBytes(GenerateCode(secret, step))))
                return step;
        return null;
    }

    public static string Base32(byte[] bytes)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var result = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (byte value in bytes)
        {
            buffer = (buffer << 8) | value; bits += 8;
            while (bits >= 5) { bits -= 5; result.Append(alphabet[(buffer >> bits) & 31]); }
        }
        if (bits > 0) result.Append(alphabet[(buffer << (5 - bits)) & 31]);
        return result.ToString();
    }
}

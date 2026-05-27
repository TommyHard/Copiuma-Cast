using System.Security.Cryptography;
using System.Text;

namespace Cast.API.Common;

/// <summary>
/// Детерминированный GUID из строкового ключа (SHA-1 -> 16 байт). Нужен для
/// идемпотентности начислений: один и тот же логический ключ
/// всегда даёт один и тот же GUID,
/// поэтому повторная проводка отсекается по уникальному ключу журнала
/// </summary>
public static class DeterministicGuid
{
    public static Guid Create(string key)
    {
        Span<byte> hash = stackalloc byte[20];
        SHA1.HashData(Encoding.UTF8.GetBytes(key), hash);
        return new Guid(hash[..16]);
    }
}
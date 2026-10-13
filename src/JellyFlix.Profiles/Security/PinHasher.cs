using System;
using System.Security.Cryptography;
using System.Text;

namespace JellyFlix.Profiles.Security;

/// <summary>
/// Hashes and verifies profile PINs.
/// </summary>
public static class PinHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;

    /// <summary>
    /// Checks that a PIN is exactly four digits.
    /// </summary>
    /// <param name="pin">The PIN.</param>
    /// <returns>Whether the PIN is well formed.</returns>
    public static bool IsValidPin(string? pin)
    {
        if (pin is null || pin.Length != 4)
        {
            return false;
        }

        foreach (var c in pin)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Hashes a PIN with a random salt.
    /// </summary>
    /// <param name="pin">The PIN.</param>
    /// <returns>The encoded salt and hash.</returns>
    public static string Hash(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Derive(pin, salt);
        return Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Verifies a PIN against a stored hash.
    /// </summary>
    /// <param name="pin">The PIN to check.</param>
    /// <param name="encoded">The stored hash from <see cref="Hash"/>.</param>
    /// <returns>Whether the PIN matches.</returns>
    public static bool Verify(string? pin, string encoded)
    {
        if (pin is null)
        {
            return false;
        }

        var parts = encoded.Split(':');
        if (parts.Length != 2)
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[0]);
        var expected = Convert.FromBase64String(parts[1]);
        return CryptographicOperations.FixedTimeEquals(Derive(pin, salt), expected);
    }

    private static byte[] Derive(string pin, byte[] salt)
        => Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin), salt, Iterations, HashAlgorithmName.SHA256, HashSize);
}

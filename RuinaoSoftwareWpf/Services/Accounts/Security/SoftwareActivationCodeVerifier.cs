namespace RuinaoSoftwareWpf;

using System.Security.Cryptography;

internal static class SoftwareActivationCodeVerifier
{
    private const int Iterations = 100_000;
    private const int HashSize = 32;
    private const string ExpectedSalt = "WhMQTv6NfWyfNDgZrlgFJg==";
    private const string ExpectedHash = "6fyK5Zltzkg7SsuPWBTTSmQCb0EenDdxJYBF9OtZQPM=";

    public static bool Verify(string activationCode)
    {
        var normalized = Normalize(activationCode);
        if (normalized.Length == 0)
        {
            return false;
        }

        var salt = Convert.FromBase64String(ExpectedSalt);
        var expectedHash = Convert.FromBase64String(ExpectedHash);
        var actualHash = Rfc2898DeriveBytes.Pbkdf2(
            normalized,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    internal static string Normalize(string activationCode)
    {
        if (string.IsNullOrWhiteSpace(activationCode))
        {
            return string.Empty;
        }

        return string.Concat(activationCode
            .Where(character => !char.IsWhiteSpace(character) && character != '-'))
            .ToUpperInvariant();
    }
}

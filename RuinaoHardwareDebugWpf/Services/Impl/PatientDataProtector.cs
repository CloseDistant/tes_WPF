namespace RuinaoHardwareDebugWpf;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;

public sealed class PatientDataProtector
{
    private const string CurrentPrefix = "v2:";
    private const string LegacyPrefix = "v1:";
    private const int KeySize = 32;
    private readonly ILoggingService logger;
    private byte[]? key;

    public PatientDataProtector(ILoggingService logger)
    {
        this.logger = logger;
    }

    public void Initialize(bool currentCiphertextExists)
    {
        if (key is not null)
        {
            return;
        }

        var keyPath = AppDatabasePathProvider.PatientKeyPath;
        if (!File.Exists(keyPath))
        {
            if (currentCiphertextExists)
            {
                throw new InvalidOperationException("患者数据密钥缺失，请恢复 data/security/patient.key 后重试。");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
            var generatedKey = RandomNumberGenerator.GetBytes(KeySize);
            WriteKeyFile(keyPath, generatedKey);
            key = generatedKey;
            logger.Info("已生成患者数据自动密钥文件。请将数据库与密钥文件成套备份。");
            return;
        }

        key = ReadKeyFile(keyPath);
    }

    public string? Protect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var activeKey = key ?? throw new InvalidOperationException("患者数据密钥尚未初始化。");
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plainBytes = Encoding.UTF8.GetBytes(value.Trim());
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(activeKey, tag.Length);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        var payload = new byte[nonce.Length + tag.Length + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipherBytes, 0, payload, nonce.Length + tag.Length, cipherBytes.Length);
        return CurrentPrefix + Convert.ToBase64String(payload);
    }

    public string? Unprotect(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!value.StartsWith(CurrentPrefix, StringComparison.Ordinal)
            && !value.StartsWith(LegacyPrefix, StringComparison.Ordinal))
        {
            return value;
        }

        try
        {
            return value.StartsWith(CurrentPrefix, StringComparison.Ordinal)
                ? UnprotectCurrent(value)
                : UnprotectLegacy(value);
        }
        catch (Exception exception)
        {
            logger.Error($"患者敏感字段解密失败，字段={fieldName}。日志未记录字段原文。", exception);
            return null;
        }
    }

    public static bool IsCurrentCiphertext(string? value) => value?.StartsWith(CurrentPrefix, StringComparison.Ordinal) == true;

    public static bool IsLegacyCiphertext(string? value) => value?.StartsWith(LegacyPrefix, StringComparison.Ordinal) == true;

    private string UnprotectCurrent(string value)
    {
        var activeKey = key ?? throw new InvalidOperationException("患者数据密钥尚未初始化。");
        var payload = Convert.FromBase64String(value[CurrentPrefix.Length..]);
        if (payload.Length < 29)
        {
            throw new CryptographicException("患者密文字节长度无效。");
        }

        var nonce = payload[..12];
        var tag = payload[12..28];
        var cipherBytes = payload[28..];
        var plainBytes = new byte[cipherBytes.Length];
        using var aes = new AesGcm(activeKey, tag.Length);
        aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private static string UnprotectLegacy(string value)
    {
        var payload = Convert.FromBase64String(value[LegacyPrefix.Length..]);
        if (payload.Length <= 16)
        {
            throw new CryptographicException("旧版患者密文字节长度无效。");
        }

        using var aes = Aes.Create();
        var material = $"Ruinao.Patient.v1|{Environment.MachineName}|{Environment.UserName}";
        aes.Key = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        aes.IV = payload[..16];
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(payload, 16, payload.Length - 16);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private static void WriteKeyFile(string path, byte[] generatedKey)
    {
        var document = new PatientKeyDocument(
            1,
            Convert.ToBase64String(generatedKey),
            Convert.ToHexString(SHA256.HashData(generatedKey)));
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document), Encoding.UTF8);
        File.Move(temporaryPath, path, true);
    }

    private static byte[] ReadKeyFile(string path)
    {
        var document = JsonSerializer.Deserialize<PatientKeyDocument>(File.ReadAllText(path, Encoding.UTF8))
            ?? throw new InvalidOperationException("患者数据密钥文件格式无效。");
        if (document.Version != 1)
        {
            throw new InvalidOperationException($"不支持的患者数据密钥版本：{document.Version}");
        }

        var loadedKey = Convert.FromBase64String(document.Key);
        var checksum = Convert.ToHexString(SHA256.HashData(loadedKey));
        if (loadedKey.Length != KeySize || !string.Equals(checksum, document.Checksum, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("患者数据密钥文件校验失败，请恢复正确的密钥备份。");
        }

        return loadedKey;
    }

    private sealed record PatientKeyDocument(int Version, string Key, string Checksum);
}

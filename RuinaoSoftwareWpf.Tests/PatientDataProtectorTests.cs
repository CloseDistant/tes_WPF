namespace RuinaoSoftwareWpf.Tests;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

public sealed class PatientDataProtectorTests
{
    [Fact]
    public void ValidateKeyFile_AcceptsUtf8Bom()
    {
        var content = CreateValidKeyDocument(includeBom: true);

        PatientDataProtector.ValidateKeyFile(content);
    }

    [Fact]
    public void ValidateKeyFile_AcceptsUtf8WithoutBom()
    {
        var content = CreateValidKeyDocument(includeBom: false);

        PatientDataProtector.ValidateKeyFile(content);
    }

    [Fact]
    public void ValidateKeyFile_MapsInvalidJsonToChineseMessage()
    {
        var exception = Assert.Throws<InvalidDataException>(
            () => PatientDataProtector.ValidateKeyFile([0xEF, 0x00]));

        Assert.Equal("患者数据密钥备份格式无效", exception.Message);
    }

    private static byte[] CreateValidKeyDocument(bool includeBom)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var json = JsonSerializer.Serialize(new
        {
            Version = 1,
            Key = Convert.ToBase64String(key),
            Checksum = Convert.ToHexString(SHA256.HashData(key))
        });
        var content = Encoding.UTF8.GetBytes(json);
        return includeBom
            ? [.. Encoding.UTF8.GetPreamble(), .. content]
            : content;
    }
}

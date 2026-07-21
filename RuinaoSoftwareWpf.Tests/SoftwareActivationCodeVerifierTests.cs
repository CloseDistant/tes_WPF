namespace RuinaoSoftwareWpf.Tests;

using Xunit;

public sealed class SoftwareActivationCodeVerifierTests
{
    [Theory]
    [InlineData(" ab-c 123 ", "ABC123")]
    [InlineData("Sample-Code", "SAMPLECODE")]
    [InlineData("", "")]
    public void Normalize_RemovesSeparatorsAndIgnoresCase(string input, string expected)
    {
        Assert.Equal(expected, SoftwareActivationCodeVerifier.Normalize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("INVALID-CODE")]
    [InlineData("SAMPLE-0000")]
    public void Verify_RejectsInvalidCode(string activationCode)
    {
        Assert.False(SoftwareActivationCodeVerifier.Verify(activationCode));
    }
}

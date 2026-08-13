namespace RuinaoSoftwareWpf.Tests;

using Xunit;

public sealed class AccountCredentialPolicyTests
{
    [Theory]
    [InlineData("A")]
    [InlineData("Admin")]
    [InlineData("Doctor01234567890123456789012345")]
    public void LoginNamePolicy_AcceptsAsciiLettersAndDigitsWithinLimit(string loginName)
    {
        Assert.True(AccountLoginNamePolicy.IsValid(loginName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Doctor012345678901234567890123456")]
    [InlineData("Doctor_01")]
    [InlineData("Doctor 01")]
    [InlineData("医生01")]
    [InlineData("Doctor-01")]
    public void LoginNamePolicy_RejectsOutOfRangeOrUnsupportedCharacters(string? loginName)
    {
        Assert.False(AccountLoginNamePolicy.IsValid(loginName));
    }

    [Fact]
    public void LoginNamePolicy_ValidateReportsSharedRequirement()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => AccountLoginNamePolicy.Validate("Doctor_01"));

        Assert.Equal(AccountLoginNamePolicy.RequirementText, exception.Message);
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("Abc123!@#")]
    [InlineData("12345678901234567890")]
    public void PasswordPolicy_LoginInputAcceptsExistingPasswordsWithinLimit(string password)
    {
        Assert.True(AccountPasswordPolicy.IsValidLoginInput(password));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123456789012345678901")]
    [InlineData("Password 1")]
    [InlineData("Password\t1")]
    public void PasswordPolicy_LoginInputRejectsEmptyOverlongOrWhitespace(string? password)
    {
        Assert.False(AccountPasswordPolicy.IsValidLoginInput(password));
    }

    [Fact]
    public void PasswordPolicy_LoginInputDoesNotApplyNewPasswordMinimumLength()
    {
        Assert.True(AccountPasswordPolicy.IsValidLoginInput("123456"));
        Assert.Throws<InvalidOperationException>(() => AccountPasswordPolicy.Validate("123456", "123456"));
    }

    [Fact]
    public void PasswordPolicy_NewPasswordAllowsTwentyCharactersAndRejectsTwentyOne()
    {
        const string validPassword = "Abcdefghij123456789!";
        const string overlongPassword = "Abcdefghij1234567890!";

        AccountPasswordPolicy.Validate(validPassword, validPassword);
        Assert.Throws<InvalidOperationException>(
            () => AccountPasswordPolicy.Validate(overlongPassword, overlongPassword));
    }
}

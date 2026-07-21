namespace RuinaoSoftwareWpf.Tests;

using Xunit;

public sealed class SessionSecurityPolicyTests
{
    [Theory]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(30)]
    public void IsValidIdleTimeout_AcceptsConfiguredRange(int minutes)
    {
        Assert.True(SessionSecurityPolicy.IsValidIdleTimeout(minutes));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(31)]
    public void IsValidIdleTimeout_RejectsValuesOutsideConfiguredRange(int minutes)
    {
        Assert.False(SessionSecurityPolicy.IsValidIdleTimeout(minutes));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("4")]
    [InlineData("31")]
    [InlineData("invalid")]
    public void ParseIdleTimeoutOrDefault_UsesDefaultForInvalidStoredValues(string? value)
    {
        Assert.Equal(
            ISessionSecurityService.DefaultIdleTimeoutMinutes,
            SessionSecurityPolicy.ParseIdleTimeoutOrDefault(value));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ShouldSuppressAutoLock_ReturnsTrueDuringProtectedWorkflow(
        bool hasRunningModule,
        bool hasActiveAssessment)
    {
        Assert.True(SessionSecurityPolicy.ShouldSuppressAutoLock(
            hasRunningModule,
            hasActiveAssessment));
    }

    [Fact]
    public void ShouldSuppressAutoLock_ReturnsFalseWhenNoProtectedWorkflowIsActive()
    {
        Assert.False(SessionSecurityPolicy.ShouldSuppressAutoLock(false, false));
    }

    [Fact]
    public void HasIdleTimeoutElapsed_LocksAtBoundaryButNotBeforeBoundary()
    {
        Assert.False(SessionSecurityPolicy.HasIdleTimeoutElapsed(
            TimeSpan.FromMinutes(15) - TimeSpan.FromMilliseconds(1),
            15));
        Assert.True(SessionSecurityPolicy.HasIdleTimeoutElapsed(TimeSpan.FromMinutes(15), 15));
    }
}

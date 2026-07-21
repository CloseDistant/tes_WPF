namespace RuinaoSoftwareWpf.Tests;

using Xunit;

public sealed class AuthorizationPolicyTests
{
    [Theory]
    [InlineData(AccountRoles.Admin, AppPermission.DeletePrescription)]
    [InlineData(AccountRoles.Admin, AppPermission.ManageStartupSettings)]
    [InlineData(AccountRoles.Admin, AppPermission.ManagePatients)]
    [InlineData(AccountRoles.Doctor, AppPermission.ManagePatients)]
    public void HasPermission_AllowsConfiguredRole(int roleId, AppPermission permission)
    {
        Assert.True(AuthorizationPolicy.HasPermission(roleId, permission));
    }

    [Theory]
    [InlineData(AccountRoles.Doctor, AppPermission.DeletePrescription)]
    [InlineData(AccountRoles.Doctor, AppPermission.ManageStartupSettings)]
    [InlineData(AccountRoles.Technician, AppPermission.DeletePrescription)]
    [InlineData(AccountRoles.Technician, AppPermission.ManageStartupSettings)]
    [InlineData(AccountRoles.Technician, AppPermission.ManagePatients)]
    public void HasPermission_DeniesUnconfiguredRole(int roleId, AppPermission permission)
    {
        Assert.False(AuthorizationPolicy.HasPermission(roleId, permission));
    }
}

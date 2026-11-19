using CampusFlow.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using Xunit;

namespace CampusFlow.AccountRecovery;

[Collection(CampusFlowTestConsts.CollectionDefinitionName)]
public sealed class RecoveryBackendRegistrationTests : CampusFlowEntityFrameworkCoreTestBase
{
    [Theory]
    [InlineData("MicrosoftWriteback", typeof(MicrosoftWritebackAccountRecoveryDirectory))]
    [InlineData("CloudOnly", typeof(MicrosoftAccountRecoveryDirectory))]
    public void Actual_host_resolves_the_explicitly_selected_recovery_backend(string backend, Type expected)
    {
        var configuration = GetRequiredService<IConfiguration>();
        configuration["PasswordRecovery:Backend"] = backend;
        using var scope = GetRequiredService<IServiceScopeFactory>().CreateScope();
        Assert.Equal(expected, scope.ServiceProvider.GetRequiredService<IAccountRecoveryDirectory>().GetType());
    }
}

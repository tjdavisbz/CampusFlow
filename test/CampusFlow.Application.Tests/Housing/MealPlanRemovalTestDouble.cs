using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CampusFlow.Housing;
using NSubstitute;
namespace CampusFlow.Application.Tests.Housing;
internal static class MealPlanRemovalTestDouble
{
    public static IMealPlanRemovalCoordinator Create()
    {
        var result=Substitute.For<IMealPlanRemovalCoordinator>();
        result.PendingAsync(Arg.Any<int>(),Arg.Any<int>()).Returns(_=>new List<MealPlanRemovalTarget>());
        result.LockAsync(Arg.Any<int>(),Arg.Any<int>()).Returns(_=>Task.FromResult<IAsyncDisposable>(new Lease()));
        return result;
    }
    private sealed class Lease:IAsyncDisposable { public ValueTask DisposeAsync()=>ValueTask.CompletedTask; }
}

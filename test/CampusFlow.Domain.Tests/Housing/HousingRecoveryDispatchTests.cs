using System;
using System.Threading.Tasks;
using CampusFlow.Housing;
using Xunit;
namespace CampusFlow.Domain.Tests.Housing;
public class HousingRecoveryDispatchTests
{
    [Fact] public void Recent_assignment_progress_keeps_a_long_background_run_protected()
    {
        var now=DateTime.UtcNow;var draft=new HousingAssignmentDraft(Guid.NewGuid(),Guid.NewGuid(),2,"[]",now);
        var dispatch=new HousingRecoveryDispatch{Id=Guid.NewGuid(),StartedAt=now.AddHours(-2)};
        new HousingApplyProgress{StartedAt=now.AddHours(-2),UpdatedAt=now.AddMinutes(-1)}.Save(draft);
        Assert.True(dispatch.IsActive(draft,now));Assert.False(dispatch.IsActive(draft,now.AddMinutes(31)));
        dispatch.Status="Failed";Assert.False(dispatch.IsActive(draft,now));
    }
    [Fact] public async Task Only_the_scoped_worker_identity_can_bypass_its_own_dispatch_guard()
    {
        Assert.Null(HousingRecoveryDispatch.CurrentExecutionId);var id=Guid.NewGuid();
        using(HousingRecoveryDispatch.EnterExecution(id)){await Task.Yield();Assert.Equal(id,HousingRecoveryDispatch.CurrentExecutionId);}
        Assert.Null(HousingRecoveryDispatch.CurrentExecutionId);
    }
}

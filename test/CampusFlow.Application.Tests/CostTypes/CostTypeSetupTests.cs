using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.CostTypes;
using NSubstitute;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Xunit;
namespace CampusFlow.Application.Tests.CostTypes;
public class CostTypeSetupTests
{
    private readonly IRepository<CostTypeConfiguration,Guid> _configs=Substitute.For<IRepository<CostTypeConfiguration,Guid>>();
    private readonly ICostTypeElements _elements=Substitute.For<ICostTypeElements>();
    private readonly CostTypeAppService _service;
    private CostTypeConfiguration? _saved;
    public CostTypeSetupTests()
    {
        _configs.FindAsync(Arg.Any<Expression<Func<CostTypeConfiguration,bool>>>(),Arg.Any<bool>(),Arg.Any<CancellationToken>()).Returns(_=>_saved);
        _configs.InsertAsync(Arg.Any<CostTypeConfiguration>(),Arg.Any<bool>(),Arg.Any<CancellationToken>()).Returns(c=>{_saved=c.Arg<CostTypeConfiguration>();return _saved;});
        _elements.GetChoicesAsync().Returns(new List<CostTypeChoice>{new(4,"DE","Distance Education"),new(71,"GRAD1","Graduate"),new(93,"SOM2200","School of Ministry")});
        var lazy=Substitute.For<IAbpLazyServiceProvider>();var tenant=Substitute.For<ICurrentTenant>();tenant.Id.Returns(Guid.NewGuid());
        lazy.LazyGetRequiredService<ICurrentTenant>().Returns(tenant);var ids=Substitute.For<IGuidGenerator>();ids.Create().Returns(_=>Guid.NewGuid());lazy.LazyGetRequiredService<IGuidGenerator>().Returns(ids);
        _service=new(_configs,Substitute.For<IRepository<CostTypeRun,Guid>>(),_elements,Substitute.For<IUnitOfWorkManager>()){LazyServiceProvider=lazy};
    }
    [Fact] public async Task Baseline_can_be_saved_when_an_old_cost_type_is_unavailable()
    {
        var baseline=await _service.GetAsync();Assert.Equal("",baseline.Revision);Assert.Equal(239,baseline.Rules.Count);
        var de=Assert.Single(baseline.Rules,x=>x.Campus=="Main"&&x.Attendance=="Distance Education");Assert.Equal(4,de.CostTypeId);
        Assert.Contains(baseline.Rules,x=>x.CostType=="LAWTON"&&x.CostTypeId is null&&x.Enabled);
        baseline.ChangeReason="Review legacy mappings";var saved=await _service.SaveAsync(baseline);
        Assert.NotEmpty(saved.Revision);Assert.NotNull(_saved);Assert.Contains(saved.Rules,x=>x.CostType=="LAWTON"&&x.CostTypeId is null);
    }
    [Fact] public async Task A_selected_invalid_ID_is_rejected_instead_of_silently_accepted()
    {
        var input=new CostTypeSetupDto{ChangeReason="test",Rules=[new(){Priority=1,Attendance="Distance Education",CostType="DE",CostTypeId=999}]};
        await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.SaveAsync(input));Assert.Null(_saved);
    }
    [Fact] public async Task Labels_are_canonicalized_without_discarding_the_selected_ID()
    {
        var input=new CostTypeSetupDto{ChangeReason="test",Rules=[new(){Priority=1,Attendance="Distance Education",CostType=" de ",CostTypeId=4}]};
        var saved=await _service.SaveAsync(input);Assert.Equal("DE",saved.Rules[0].CostType);Assert.Equal(4,saved.Rules[0].CostTypeId);
    }
    [Fact] public async Task Stale_revision_cannot_overwrite_another_saved_configuration()
    {
        var setup=await _service.GetAsync();setup.ChangeReason="first";await _service.SaveAsync(setup);
        await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.SaveAsync(setup));
    }
    [Fact] public async Task Preview_requires_a_saved_configuration()
    {await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.PreviewAsync());await _elements.DidNotReceiveWithAnyArgs().PreviewAsync(default!,default);}
    [Fact] public async Task Unmapped_rules_remain_explicit_and_do_not_become_candidates()
    {
        var setup=await _service.GetAsync();setup.ChangeReason="review";await _service.SaveAsync(setup);
        _elements.PreviewAsync(Arg.Any<List<CostTypeRule>>(),8313).Returns(new List<CostTypePreviewRow>{new(8313,"test","Test",1,269,"Spring 2027","MAIN","Distance Education",null,26,"AC",147,null,"LAWTON","Invalid lookup")});
        var preview=await _service.PreviewAsync(8313);Assert.Null(preview.RunId);Assert.Equal("Invalid lookup",Assert.Single(preview.Rows).Action);
        await _elements.DidNotReceiveWithAnyArgs().FingerprintAsync(default!);
    }
}

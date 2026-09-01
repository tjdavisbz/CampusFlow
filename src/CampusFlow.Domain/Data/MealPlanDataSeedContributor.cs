using System;
using System.Text.Json;
using System.Threading.Tasks;
using CampusFlow.Housing;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;

namespace CampusFlow.Data;

public class MealPlanDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IRepository<MealPlanConfiguration, Guid> _configurations;
    private readonly IGuidGenerator _guidGenerator;

    public MealPlanDataSeedContributor(IRepository<MealPlanConfiguration, Guid> configurations,
        IGuidGenerator guidGenerator)
    {
        _configurations = configurations;
        _guidGenerator = guidGenerator;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        if (!context.TenantId.HasValue || await _configurations.AnyAsync(x => x.TenantId == context.TenantId)) return;
        var residential = JsonSerializer.Serialize(new[] { HousingChoice.OnCampus, HousingChoice.SeniorHousing });
        var commuter = JsonSerializer.Serialize(new[] { HousingChoice.Commuter });
        var allAttendanceTypes = "[]";
        var plans = new[]
        {
            new Seed("12 Meal Plan", "12 meals each week plus $325 in Lion Bucks.", residential, 2679.19m, 10, false),
            new Seed("15 Meal Plan", "15 meals each week plus $350 in Lion Bucks.", residential, 2787.44m, 20, false),
            new Seed("120 Meal Plan", "120 meals for the semester plus $375 in Lion Bucks.", residential, 2435.63m, 30, false),
            new Seed("Unlimited Meal Plan", "Unlimited dining access plus $225 in Lion Bucks.", residential, 3085.13m, 40, false),
            new Seed("None", "I do not want to purchase a commuter meal plan.", commuter, 0m, 50, true),
            new Seed("20 Meal Plan", "20 meals plus $75 in Lion Bucks.", commuter, 340.99m, 60, false),
            new Seed("40 Meal Plan", "40 meals plus $100 in Lion Bucks.", commuter, 557.49m, 70, false),
            new Seed("60 Meal Plan", "60 meals plus $100 in Lion Bucks.", commuter, 773.99m, 80, false)
        };
        foreach (var plan in plans)
        {
            await _configurations.InsertAsync(new MealPlanConfiguration(_guidGenerator.Create(), context.TenantId,
                null, plan.Name, plan.Name, plan.Description, plan.Choices, allAttendanceTypes,
                plan.Price, plan.Sort, plan.None));
        }
    }

    private sealed record Seed(string Name, string Description, string Choices, decimal Price, int Sort, bool None);
}

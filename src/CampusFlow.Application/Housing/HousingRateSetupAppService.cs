using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CampusFlow.Permissions;
using CampusFlow.StudentInformationSystems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace CampusFlow.Housing;

[Authorize(CampusFlowPermissions.Admin.HousingAssignments)]
public class HousingRateSetupAppService : CampusFlowAppService, IHousingRateSetupAppService
{
    private readonly IRepository<HousingRateSchedule, Guid> _schedules;
    private readonly IElementsHousingReader _elements;
    private readonly IConfiguration _configuration;

    public HousingRateSetupAppService(IRepository<HousingRateSchedule, Guid> schedules,
        IElementsHousingReader elements, IConfiguration configuration)
    {
        _schedules = schedules;
        _elements = elements;
        _configuration = configuration;
    }

    private Guid Tenant() => CurrentTenant.Id ?? throw new UserFriendlyException("Select a university before managing housing rates.");

    private async Task<HousingRateSchedule?> FindAsync(int periodId)
    {
        var tenantId = Tenant();
        return await _schedules.FindAsync(x => x.TenantId == tenantId && x.PeriodId == periodId);
    }

    public async Task<HousingRateScheduleDto> GetAsync(int periodId)
    {
        var period = (await _elements.GetPeriodsAsync()).SingleOrDefault(x => x.Id == periodId)
            ?? throw new UserFriendlyException("Choose an available housing period.");
        var pricing = await _elements.GetPricingAsync(periodId);
        var saved = await FindAsync(periodId);
        if (saved is not null && saved.TermCalendarId != pricing.TermCalendarId)
            throw new UserFriendlyException("The saved housing rates belong to a different term. Contact an administrator.");
        List<HousingRateAmount> rates;
        try
        {
            rates = saved is null
                ? HousingChargeCalculator.SuggestRates(pricing).ToList()
                : JsonSerializer.Deserialize<List<HousingRateAmount>>(saved.RatesJson) ?? [];
        }
        catch (ArgumentException e) { throw new UserFriendlyException(e.Message); }
        return new HousingRateScheduleDto
        {
            PeriodId = periodId,
            TermCalendarId = pricing.TermCalendarId,
            PeriodName = period.Name,
            IsSaved = saved is not null,
            Revision = saved?.ConcurrencyStamp ?? "",
            Rates = rates.Select(x => new HousingRateAmountDto { Building = x.Building,
                RateChoice = x.RateChoice, BaseAmount = x.BaseAmount,
                SingleSurchargeAmount = x.SingleSurchargeAmount }).ToList()
        };
    }

    public async Task<HousingRateScheduleDto> SaveAsync(HousingRateScheduleDto input)
    {
        if (!((_configuration.GetValue<bool>("Housing:EnableNonProductionWrites") &&
               string.Equals(_configuration["App:EnvironmentLabel"], "NON-PRODUCTION", StringComparison.OrdinalIgnoreCase)) ||
              (_configuration.GetValue<bool>("Housing:EnableProductionWrites") &&
               string.IsNullOrWhiteSpace(_configuration["App:EnvironmentLabel"]))))
            throw new UserFriendlyException("Housing rate editing is not enabled for this portal.");
        if (input.Rates is null) throw new UserFriendlyException("Enter housing rates before saving.");
        var current = await GetAsync(input.PeriodId);
        if (input.TermCalendarId != current.TermCalendarId || input.Revision != current.Revision)
            throw new UserFriendlyException("The housing period or rates changed. Reload the setup page before saving.");
        var definitions = HousingChargeCalculator.GetRateDefinitions(current.TermCalendarId);
        if (input.Rates.Count != definitions.Count)
            throw new UserFriendlyException("Every reviewed building and rate choice needs an amount.");
        var keys = new HashSet<(string, string)>();
        foreach (var rate in input.Rates)
        {
            if (!definitions.Any(x => x.Building == rate.Building && x.RateChoice == rate.RateChoice) ||
                !keys.Add((rate.Building, rate.RateChoice)))
                throw new UserFriendlyException("The rate list contains an unknown or duplicate building/rate choice.");
            if (!ValidAmount(rate.BaseAmount) || !ValidAmount(rate.SingleSurchargeAmount))
                throw new UserFriendlyException("Each base charge and intentional-single surcharge must be positive, at most $100,000, and have no more than two decimal places.");
        }
        var json = JsonSerializer.Serialize(input.Rates.Select(x =>
            new HousingRateAmount(x.Building, x.RateChoice, x.BaseAmount, x.SingleSurchargeAmount)));
        var saved = await FindAsync(input.PeriodId);
        if (saved is null)
            await _schedules.InsertAsync(new HousingRateSchedule(GuidGenerator.Create(), Tenant(),
                input.PeriodId, input.TermCalendarId, json), autoSave: true);
        else
        {
            saved.Update(json);
            await _schedules.UpdateAsync(saved, autoSave: true);
        }
        return await GetAsync(input.PeriodId);
    }

    private static bool ValidAmount(decimal amount) =>
        amount > 0m && amount <= 100000m && decimal.Round(amount, 2) == amount;
}

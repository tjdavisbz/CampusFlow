using System.Collections.Generic;

namespace CampusFlow.Housing;

public class HousingRateScheduleDto
{
    public int PeriodId { get; set; }
    public int TermCalendarId { get; set; }
    public string PeriodName { get; set; } = "";
    public bool IsSaved { get; set; }
    public string Revision { get; set; } = "";
    public List<HousingRateAmountDto> Rates { get; set; } = [];
}

public class HousingRateAmountDto
{
    public string Building { get; set; } = "";
    public string RateChoice { get; set; } = "";
    public decimal BaseAmount { get; set; }
    public decimal SingleSurchargeAmount { get; set; }
}

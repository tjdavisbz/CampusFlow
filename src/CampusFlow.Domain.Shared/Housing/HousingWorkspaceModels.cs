using System;
using System.Collections.Generic;

namespace CampusFlow.Housing;

public class HousingPeriodOption
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class HousingWorkspaceRoom
{
    public int Id { get; set; }
    public string Building { get; set; } = "";
    public string Name { get; set; } = "";
    public int Capacity { get; set; }
    public List<HousingWorkspaceOccupant> Occupants { get; set; } = [];
}

public class HousingWorkspaceOccupant
{
    public int StudentUid { get; set; }
    public string Name { get; set; } = "";
    public bool IntentionalSingle { get; set; }
    public string Rate { get; set; } = "Unreviewed";
    public string Notes { get; set; } = "";
}

public class HousingWorkspaceState
{
    public Guid Id { get; set; }
    public string Revision { get; set; } = "";
    public int PeriodId { get; set; }
    public DateTime RefreshedAt { get; set; }
    public List<HousingWorkspaceRoom> Rooms { get; set; } = [];
}

public class HousingWorkspaceChange
{
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
}

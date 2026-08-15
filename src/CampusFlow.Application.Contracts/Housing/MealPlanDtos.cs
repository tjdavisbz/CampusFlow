using System;
using System.Collections.Generic;

namespace CampusFlow.Housing;

public sealed class MealPlanOptionDto
{
    public int? ExternalMealPlanId { get; set; }
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
    public decimal? Price { get; set; }
    public bool IsNoPlanOption { get; set; }
}

public sealed class MealPlanSelectionDto
{
    public string StudentName { get; set; } = null!;
    public string StudentId { get; set; } = null!;
    public string AttendanceType { get; set; } = null!;
    public string TermName { get; set; } = null!;
    public DateTime? TermStartDate { get; set; }
    public DateTime? TermEndDate { get; set; }
    public HousingChoice? SelectedHousingChoice { get; set; }
    public int? SelectedMealPlanId { get; set; }
    public string? SelectedMealPlanName { get; set; }
    public bool? SyncedToStudentInformationSystem { get; set; }
    public Dictionary<HousingChoice, List<MealPlanOptionDto>> Options { get; set; } = [];
}

public sealed class SaveMealPlanSelectionInput
{
    public HousingChoice HousingChoice { get; set; }
    public int? ExternalMealPlanId { get; set; }
}

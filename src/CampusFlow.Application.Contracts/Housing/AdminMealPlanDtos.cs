using System;
using System.Collections.Generic;

namespace CampusFlow.Housing;

public class AdminMealPlanStudentDto
{
    public string ExternalStudentId { get; set; } = string.Empty;
    public string StudentId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class AdminMealPlanOptionDto
{
    public int MealPlanId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}

public class AdminMealPlanContextDto
{
    public AdminMealPlanStudentDto Student { get; set; } = new();
    public string AttendanceType { get; set; } = string.Empty;
    public int? SelectedTermCalendarId { get; set; }
    public List<AdminMealPlanTermDto> Terms { get; set; } = [];
    public List<AdminHousingStatusDto> HousingStatuses { get; set; } = [];
    public int? CurrentHousingStatusId { get; set; }
    public List<AdminMealPlanOptionDto> Options { get; set; } = [];
}

public class AdminMealPlanTermDto
{
    public int TermCalendarId { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class AdminHousingStatusDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class AssignStudentMealPlanInput
{
    public string ExternalStudentId { get; set; } = string.Empty;
    public int TermCalendarId { get; set; }
    public int HousingStatusId { get; set; }
    public int MealPlanId { get; set; }
}

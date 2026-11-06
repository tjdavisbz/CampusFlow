namespace CampusFlow.AdvisorPortal;
public interface ICurrentSchedulerView
{
    bool IsActive { get; }
    string? Email { get; }
}

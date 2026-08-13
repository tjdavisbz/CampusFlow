using System.Threading.Tasks;

namespace CampusFlow.Data;

public interface ICampusFlowDbSchemaMigrator
{
    Task MigrateAsync();
}

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Auditing;
namespace CampusFlow.Bibliu;

[Route("api/integrations/bibliu/exports")]
[Authorize(CampusFlowPermissions.Admin.BibliuExport)]
[DisableAuditing]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BibliuExportController(BibliuExportService service) : AbpController
{
    [HttpGet("{kind}")]
    public async Task<JsonResult> GetAsync(string kind, CancellationToken cancellationToken) =>
        new(await service.ExportAsync(kind, cancellationToken),
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { DictionaryKeyPolicy = null });
}

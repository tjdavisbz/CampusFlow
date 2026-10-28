using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.StudentInformationSystems;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace CampusFlow.FinancialAid;

public class ReconcileAwardCodesAppServiceTests
{
    [Fact]
    public async Task DisabledEnvironmentNeverReachesAwardWrite()
    {
        var fake = new FakeMaintenance { CanWrite = false };
        var service = Create(fake);

        await Should.ThrowAsync<UserFriendlyException>(() => service.ApplyAsync("Fall 2026", "41"));

        fake.Updates.ShouldBe(0);
        fake.Reads.ShouldBe(0);
    }

    [Fact]
    public async Task ChangedPreviewNeverReachesAwardWrite()
    {
        var fake = new FakeMaintenance { CanWrite = true };
        var service = Create(fake);

        await Should.ThrowAsync<UserFriendlyException>(() => service.ApplyAsync("Fall 2026", "42"));

        fake.Updates.ShouldBe(0);
        fake.Reads.ShouldBe(1);
    }

    private static ReconcileAwardCodesAppService Create(FakeMaintenance fake) =>
        new([fake], NullLogger<ReconcileAwardCodesAppService>.Instance);

    private sealed class FakeMaintenance : IStudentInformationSystemAwardCodeMaintenance
    {
        public StudentInformationSystemProvider Provider => StudentInformationSystemProvider.ThesisElements;
        public bool CanWrite { get; set; }
        public int Reads { get; private set; }
        public int Updates { get; private set; }

        public Task<IReadOnlyList<string>> GetTermsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(["Fall 2026"]);

        public Task<IReadOnlyList<AwardCodeMismatch>> GetMismatchesAsync(string term, int take,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult<IReadOnlyList<AwardCodeMismatch>>(
                [new(41, 7, "S7", "Student", term, "Award", 1, 2, false, true)]);
        }

        public Task UpdateAsync(AwardCodeMismatch expected, CancellationToken cancellationToken = default)
        {
            Updates++;
            return Task.CompletedTask;
        }
    }
}

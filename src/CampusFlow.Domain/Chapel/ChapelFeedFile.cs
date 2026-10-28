using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.Chapel;

/// <summary>
/// Durable, immutable copy of a CBORD file. A file is retained even when its scans
/// require review, so a failed or repeated download never loses the vendor evidence.
/// </summary>
public class ChapelFeedFile : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public string FileName { get; private set; } = "";
    public string Sha256 { get; private set; } = "";
    public byte[] Content { get; private set; } = [];
    public int ScanCount { get; private set; }
    public DateTime ReceivedAtUtc { get; private set; }

    protected ChapelFeedFile() { }

    public ChapelFeedFile(Guid id, Guid? tenantId, string fileName, string sha256,
        byte[] content, int scanCount, DateTime receivedAtUtc) : base(id)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 160)
            throw new ArgumentException("A file name is required.", nameof(fileName));
        if (sha256.Length != 64) throw new ArgumentException("A SHA-256 digest is required.", nameof(sha256));
        if (content.Length == 0 || scanCount <= 0)
            throw new ArgumentException("The feed must contain scans.", nameof(content));
        TenantId = tenantId;
        FileName = fileName;
        Sha256 = sha256.ToUpperInvariant();
        Content = (byte[])content.Clone();
        ScanCount = scanCount;
        ReceivedAtUtc = DateTime.SpecifyKind(receivedAtUtc, DateTimeKind.Utc);
    }
}

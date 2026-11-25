using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rolehound.Api.Features.Accounts;

namespace Rolehound.Api.Features.Collection;

public enum CollectionRequestKind
{
    /// <summary>Every enabled source, like a scheduled collection.</summary>
    Full,

    /// <summary>One source, read right after it was added.</summary>
    Target,
}

public enum CollectionRequestStatus
{
    Queued,
    Running,
    Done,
    Failed,
    Interrupted,
}

/// <summary>
/// A collection someone asked for. It runs in the background, one at a time;
/// when it ends, the person who asked hears about it (bell and Telegram).
/// </summary>
public sealed class CollectionRequest
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public CollectionRequestKind Kind { get; init; }

    public Guid? TargetId { get; init; }

    public Guid? RequestedBy { get; init; }

    public DateTimeOffset RequestedAt { get; init; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public CollectionRequestStatus Status { get; set; }

    public int Targets { get; set; }

    public int Added { get; set; }

    public int Reopened { get; set; }

    public int Closed { get; set; }

    public int Failed { get; set; }

    /// <summary>For a one-source read: open jobs from it that match the person who asked.</summary>
    public int? ForYou { get; set; }

    public string? Error { get; set; }

    /// <summary>When the person saw the result; the notification goes away then.</summary>
    public DateTimeOffset? SeenAt { get; set; }
}

/// <summary>A page a source opened and found not to be a job in Brazil, so it is not opened again.</summary>
public sealed class SkippedPage
{
    public Guid TargetId { get; init; }

    public required string PageId { get; init; }

    public DateTimeOffset SkippedAt { get; init; }
}

/// <summary>One collection at a time, whoever starts it: the scheduler, a person, or a new source.</summary>
public sealed class CollectionLock
{
    public SemaphoreSlim Gate { get; } = new(1, 1);
}

/// <summary>Requests waiting for the background worker.</summary>
public sealed class CollectionBacklog
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

    public ChannelReader<Guid> Reader => _channel.Reader;

    public void Enqueue(Guid requestId) => _channel.Writer.TryWrite(requestId);
}

internal sealed class CollectionRequestConfiguration : IEntityTypeConfiguration<CollectionRequest>
{
    public void Configure(EntityTypeBuilder<CollectionRequest> builder)
    {
        builder.ToTable("collection_requests");
        builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.Error).HasMaxLength(500);
        builder.HasIndex(r => new { r.RequestedBy, r.FinishedAt });
        builder.HasIndex(r => r.Status);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.RequestedBy).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SourceTarget>().WithMany().HasForeignKey(r => r.TargetId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class SkippedPageConfiguration : IEntityTypeConfiguration<SkippedPage>
{
    public void Configure(EntityTypeBuilder<SkippedPage> builder)
    {
        builder.ToTable("skipped_pages");
        builder.HasKey(p => new { p.TargetId, p.PageId });
        builder.Property(p => p.PageId).HasMaxLength(200);
        builder.HasIndex(p => p.SkippedAt);
        builder.HasOne<SourceTarget>().WithMany().HasForeignKey(p => p.TargetId).OnDelete(DeleteBehavior.Cascade);
    }
}

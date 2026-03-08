namespace DemoStudio.Infrastructure.Persistence.Configurations;

using DemoStudio.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class DemoRunConfiguration : IEntityTypeConfiguration<DemoRun>
{
    public void Configure(EntityTypeBuilder<DemoRun> builder)
    {
        builder.ToTable("DemoRuns");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.RequestedBy)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.OutputVideoPath)
            .HasMaxLength(2048);

        builder.Property(x => x.OutputDirectory)
            .HasMaxLength(2048);

        builder.Property(x => x.RawVideoPath)
            .HasMaxLength(2048);

        builder.Property(x => x.RedactedVideoPath)
            .HasMaxLength(2048);

        builder.Property(x => x.LogPath)
            .HasMaxLength(2048);

        builder.Property(x => x.FailureReason)
            .HasMaxLength(2000);
        builder.Property(x => x.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.QueuedAtUtc);

        builder.HasOne<DemoProject>()
            .WithMany()
            .HasForeignKey(x => x.DemoProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<DemoFlow>()
            .WithMany()
            .HasForeignKey(x => x.DemoFlowId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

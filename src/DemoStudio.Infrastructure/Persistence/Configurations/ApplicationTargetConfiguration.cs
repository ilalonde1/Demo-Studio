namespace DemoStudio.Infrastructure.Persistence.Configurations;

using DemoStudio.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class ApplicationTargetConfiguration : IEntityTypeConfiguration<ApplicationTarget>
{
    public void Configure(EntityTypeBuilder<ApplicationTarget> builder)
    {
        builder.ToTable("ApplicationTargets");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.TargetReference)
            .HasMaxLength(1024)
            .IsRequired();

        builder.Property(x => x.ApplicationType)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(x => x.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        builder.HasIndex(x => new { x.DemoProjectId, x.Name }).IsUnique();

        builder.HasOne<DemoProject>()
            .WithMany(x => x.Targets)
            .HasForeignKey(x => x.DemoProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

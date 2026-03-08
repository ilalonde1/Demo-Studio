namespace DemoStudio.Infrastructure.Persistence.Configurations;

using DemoStudio.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class DemoFlowConfiguration : IEntityTypeConfiguration<DemoFlow>
{
    public void Configure(EntityTypeBuilder<DemoFlow> builder)
    {
        builder.ToTable("DemoFlows");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Version)
            .IsRequired();

        builder.Property(x => x.IsDeterministic)
            .IsRequired();
        builder.Property(x => x.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        builder.HasIndex(x => new { x.DemoProjectId, x.Name, x.Version }).IsUnique();

        builder.HasOne<DemoProject>()
            .WithMany(x => x.Flows)
            .HasForeignKey(x => x.DemoProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

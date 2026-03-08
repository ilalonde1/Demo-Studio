namespace DemoStudio.Infrastructure.Persistence.Configurations;

using DemoStudio.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class DemoProjectConfiguration : IEntityTypeConfiguration<DemoProject>
{
    public void Configure(EntityTypeBuilder<DemoProject> builder)
    {
        builder.ToTable("DemoProjects");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasMaxLength(2000);

        builder.Property(x => x.IsActive)
            .IsRequired();

        builder.Property(x => x.CreatedUtc)
            .IsRequired();

        builder.Property(x => x.UpdatedUtc);
        builder.Property(x => x.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        builder.OwnsOne(x => x.Code, code =>
        {
            code.Property(p => p.Value)
                .HasColumnName("Code")
                .HasMaxLength(32)
                .IsRequired();
            code.HasIndex(p => p.Value).IsUnique();
        });

        builder.Navigation(x => x.Targets).AutoInclude(false);
        builder.Navigation(x => x.Flows).AutoInclude(false);
        builder.Navigation(x => x.RedactionRules).AutoInclude(false);
    }
}

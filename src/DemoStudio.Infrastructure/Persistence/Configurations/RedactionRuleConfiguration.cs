namespace DemoStudio.Infrastructure.Persistence.Configurations;

using DemoStudio.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class RedactionRuleConfiguration : IEntityTypeConfiguration<RedactionRule>
{
    public void Configure(EntityTypeBuilder<RedactionRule> builder)
    {
        builder.ToTable("RedactionRules");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.MatchExpression)
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(x => x.ReplacementText)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.IsEnabled)
            .IsRequired();
        builder.Property(x => x.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        builder.HasIndex(x => new { x.DemoProjectId, x.Name }).IsUnique();

        builder.HasOne<DemoProject>()
            .WithMany(x => x.RedactionRules)
            .HasForeignKey(x => x.DemoProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

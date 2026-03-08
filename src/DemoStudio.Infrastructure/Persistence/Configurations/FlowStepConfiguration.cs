namespace DemoStudio.Infrastructure.Persistence.Configurations;

using DemoStudio.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class FlowStepConfiguration : IEntityTypeConfiguration<FlowStep>
{
    public void Configure(EntityTypeBuilder<FlowStep> builder)
    {
        builder.ToTable("FlowSteps");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Sequence)
            .IsRequired();

        builder.Property(x => x.StepType)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.ActionKey)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.PayloadJson)
            .HasColumnType("nvarchar(max)");

        builder.Property(x => x.TimeoutSeconds)
            .IsRequired();
        builder.Property(x => x.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        builder.HasIndex(x => new { x.DemoFlowId, x.Sequence }).IsUnique();

        builder.HasOne<DemoFlow>()
            .WithMany(x => x.Steps)
            .HasForeignKey(x => x.DemoFlowId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RetailCentral.Api.Data.Entities.Orchestration;

namespace RetailCentral.Api.Data.Configurations.Orchestration
{
    public class OrchestrationRunStepConfiguration : IEntityTypeConfiguration<OrchestrationRunStep>
    {
        public void Configure(EntityTypeBuilder<OrchestrationRunStep> builder)
        {
            builder.ToTable("OrchestrationRunSteps");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Status)
                .HasConversion<int>()
                .IsRequired();

            builder.Property(x => x.AttemptCount)
                .IsRequired();

            builder.Property(x => x.ResultJson)
                .HasColumnType("nvarchar(max)");

            builder.Property(x => x.ErrorMessage)
                .HasMaxLength(4000);

            builder.Property(x => x.LogsJson)
                .HasColumnType("nvarchar(max)");

            builder.HasIndex(x => x.RunId);

            builder.HasIndex(x => new { x.RunId, x.StepOrder })
                .IsUnique();

            builder.HasIndex(x => x.CommandId);

            builder.HasOne(x => x.Run)
                .WithMany(x => x.Steps)
                .HasForeignKey(x => x.RunId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(x => x.TemplateStep)
                .WithMany()
                .HasForeignKey(x => x.TemplateStepId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
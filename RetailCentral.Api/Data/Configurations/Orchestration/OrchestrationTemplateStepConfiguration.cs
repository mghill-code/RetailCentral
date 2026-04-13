using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RetailCentral.Api.Data.Entities.Orchestration;

namespace RetailCentral.Api.Data.Configurations.Orchestration
{
    public class OrchestrationTemplateStepConfiguration : IEntityTypeConfiguration<OrchestrationTemplateStep>
    {
        public void Configure(EntityTypeBuilder<OrchestrationTemplateStep> builder)
        {
            builder.ToTable("OrchestrationTemplateSteps");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(200);

            builder.Property(x => x.StepType)
                .HasConversion<int>()
                .IsRequired();

            builder.Property(x => x.CommandType)
                .HasMaxLength(100);

            builder.Property(x => x.ParametersJson)
                .HasColumnType("nvarchar(max)");

            builder.Property(x => x.SuccessCriteriaJson)
                .HasColumnType("nvarchar(max)");

            builder.Property(x => x.TimeoutSeconds)
                .IsRequired();

            builder.Property(x => x.MaxRetries)
                .IsRequired();

            builder.Property(x => x.OnFailureAction)
                .HasConversion<int>()
                .IsRequired();

            builder.Property(x => x.ContinueOnFailure)
                .IsRequired();

            builder.HasIndex(x => new { x.TemplateId, x.StepOrder })
                .IsUnique();

            builder.HasIndex(x => x.StepType);

            builder.HasOne(x => x.Template)
                .WithMany(x => x.Steps)
                .HasForeignKey(x => x.TemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
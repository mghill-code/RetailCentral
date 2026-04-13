using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RetailCentral.Api.Data.Entities.Orchestration;

namespace RetailCentral.Api.Data.Configurations.Orchestration
{
    public class OrchestrationRunConfiguration : IEntityTypeConfiguration<OrchestrationRun>
    {
        public void Configure(EntityTypeBuilder<OrchestrationRun> builder)
        {
            builder.ToTable("OrchestrationRuns");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Status)
                .HasConversion<int>()
                .IsRequired();

            builder.Property(x => x.TriggerSource)
                .HasConversion<int>()
                .IsRequired();

            builder.Property(x => x.CorrelationId)
                .IsRequired()
                .HasMaxLength(64);

            builder.Property(x => x.RequestedBy)
                .HasMaxLength(200);

            builder.Property(x => x.ParametersJson)
                .HasColumnType("nvarchar(max)");

            builder.Property(x => x.StartedUtc)
                .IsRequired();

            builder.Property(x => x.CompletedUtc);

            builder.HasIndex(x => x.CorrelationId)
                .IsUnique();

            builder.HasIndex(x => x.Status);
            builder.HasIndex(x => x.AgentId);
            builder.HasIndex(x => x.DeviceId);
            builder.HasIndex(x => x.StoreId);
            builder.HasIndex(x => x.RegisterId);

            builder.HasOne(x => x.Template)
                .WithMany(x => x.Runs)
                .HasForeignKey(x => x.TemplateId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasMany(x => x.Steps)
                .WithOne(x => x.Run)
                .HasForeignKey(x => x.RunId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
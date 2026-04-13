using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RetailCentral.Api.Data.Entities.Orchestration;

namespace RetailCentral.Api.Data.Configurations.Orchestration
{
    public class OrchestrationTemplateConfiguration : IEntityTypeConfiguration<OrchestrationTemplate>
    {
        public void Configure(EntityTypeBuilder<OrchestrationTemplate> builder)
        {
            builder.ToTable("OrchestrationTemplates");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(200);

            builder.Property(x => x.Description)
                .HasMaxLength(2000);

            builder.Property(x => x.DeviceType)
                .HasMaxLength(100);

            builder.Property(x => x.Environment)
                .HasMaxLength(100);

            builder.Property(x => x.TriggerType)
                .HasConversion<int>()
                .IsRequired();

            builder.Property(x => x.CreatedUtc)
                .IsRequired();

            builder.Property(x => x.UpdatedUtc)
                .IsRequired();

            builder.HasIndex(x => new { x.Name, x.Version })
                .IsUnique();

            builder.HasIndex(x => x.IsActive);

            builder.HasMany(x => x.Steps)
                .WithOne(x => x.Template)
                .HasForeignKey(x => x.TemplateId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(x => x.ProvisioningProfiles)
                .WithOne(x => x.Template)
                .HasForeignKey(x => x.TemplateId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasMany(x => x.Runs)
                .WithOne(x => x.Template)
                .HasForeignKey(x => x.TemplateId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
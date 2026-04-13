using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RetailCentral.Api.Data.Entities.Orchestration;

namespace RetailCentral.Api.Data.Configurations.Orchestration
{
    public class ProvisioningProfileConfiguration : IEntityTypeConfiguration<ProvisioningProfile>
    {
        public void Configure(EntityTypeBuilder<ProvisioningProfile> builder)
        {
            builder.ToTable("ProvisioningProfiles");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(200);

            builder.Property(x => x.DeviceType)
                .HasMaxLength(100);

            builder.Property(x => x.StoreGroup)
                .HasMaxLength(100);

            builder.Property(x => x.Environment)
                .HasMaxLength(100);

            builder.Property(x => x.ParametersJson)
                .HasColumnType("nvarchar(max)");

            builder.Property(x => x.CreatedUtc)
                .IsRequired();

            builder.Property(x => x.UpdatedUtc)
                .IsRequired();

            builder.HasIndex(x => x.IsActive);
            builder.HasIndex(x => x.IsDefault);
            builder.HasIndex(x => x.DeviceType);
            builder.HasIndex(x => x.Environment);

            builder.HasOne(x => x.Template)
                .WithMany(x => x.ProvisioningProfiles)
                .HasForeignKey(x => x.TemplateId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
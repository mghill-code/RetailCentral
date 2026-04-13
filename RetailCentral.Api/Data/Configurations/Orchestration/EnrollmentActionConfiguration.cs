using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RetailCentral.Api.Data.Entities.Orchestration;

namespace RetailCentral.Api.Data.Configurations.Orchestration
{
    public class EnrollmentActionConfiguration : IEntityTypeConfiguration<EnrollmentAction>
    {
        public void Configure(EntityTypeBuilder<EnrollmentAction> builder)
        {
            builder.ToTable("EnrollmentActions");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Status)
                .HasConversion<int>()
                .IsRequired();

            builder.Property(x => x.CreatedUtc)
                .IsRequired();

            builder.Property(x => x.CompletedUtc);

            builder.HasIndex(x => x.DeviceId);
            builder.HasIndex(x => x.AgentId);
            builder.HasIndex(x => x.Status);
            builder.HasIndex(x => x.InitialRunId);

            builder.HasOne(x => x.AssignedProfile)
                .WithMany()
                .HasForeignKey(x => x.AssignedProfileId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(x => x.InitialRun)
                .WithMany()
                .HasForeignKey(x => x.InitialRunId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
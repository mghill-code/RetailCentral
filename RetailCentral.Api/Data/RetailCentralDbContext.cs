using Microsoft.EntityFrameworkCore;
using RetailCentral.Api.Models;

namespace RetailCentral.Api.Data
{
    public class RetailCentralDbContext : DbContext
    {
        public RetailCentralDbContext(DbContextOptions<RetailCentralDbContext> options)
            : base(options) { }

        public DbSet<Device> Devices => Set<Device>();
        public DbSet<Heartbeat> Heartbeats => Set<Heartbeat>();
        public DbSet<Command> Commands => Set<Command>();
        public DbSet<CommandResult> CommandResults => Set<CommandResult>();
        public DbSet<DeviceGroup> DeviceGroups => Set<DeviceGroup>();
        public DbSet<DeviceGroupMember> DeviceGroupMembers => Set<DeviceGroupMember>();
        public DbSet<RegisterInventory> RegisterInventories => Set<RegisterInventory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Device>()
                .HasKey(d => d.DeviceId);

            modelBuilder.Entity<Device>()
                .HasIndex(d => new { d.StoreNumber, d.Hostname });

            modelBuilder.Entity<Heartbeat>()
                .HasKey(h => h.HeartbeatId);

            modelBuilder.Entity<Heartbeat>()
                .HasOne(h => h.Device)
                .WithMany()
                .HasForeignKey(h => h.DeviceId);

            modelBuilder.Entity<CommandResult>()
                .HasKey(r => r.CommandResultId);

            modelBuilder.Entity<CommandResult>()
                .HasIndex(r => r.CommandId)
                .IsUnique();

            modelBuilder.Entity<DeviceGroup>()
                .HasKey(g => g.DeviceGroupId);

            modelBuilder.Entity<DeviceGroup>()
                .HasIndex(g => g.GroupName)
                .IsUnique();

            modelBuilder.Entity<DeviceGroupMember>()
                .HasKey(m => m.DeviceGroupMemberId);

            modelBuilder.Entity<DeviceGroupMember>()
                .HasIndex(m => new { m.DeviceGroupId, m.DeviceId })
                .IsUnique();

            modelBuilder.Entity<DeviceGroupMember>()
                .HasOne(m => m.DeviceGroup)
                .WithMany()
                .HasForeignKey(m => m.DeviceGroupId);

            modelBuilder.Entity<DeviceGroupMember>()
                .HasOne(m => m.Device)
                .WithMany()
                .HasForeignKey(m => m.DeviceId);

            modelBuilder.Entity<RegisterInventory>(entity =>
            {
                entity.HasKey(x => x.RegisterInventoryId);

                entity.HasIndex(x => x.DeviceId)
                    .IsUnique();

                entity.HasOne<Device>()
                    .WithMany()
                    .HasForeignKey(x => x.DeviceId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.Property(x => x.ComputerName).HasMaxLength(200);
                entity.Property(x => x.Store).HasMaxLength(50);
                entity.Property(x => x.RegisterNumber).HasMaxLength(50);

                entity.Property(x => x.IPAddress).HasMaxLength(100);
                entity.Property(x => x.MACAddress).HasMaxLength(100);

                entity.Property(x => x.Manufacturer).HasMaxLength(100);
                entity.Property(x => x.Model).HasMaxLength(100);
                entity.Property(x => x.SerialNumber).HasMaxLength(100);

                entity.Property(x => x.Memory).HasMaxLength(100);
                entity.Property(x => x.HardDriveSize).HasMaxLength(100);
                entity.Property(x => x.HardDriveFreeSpace).HasMaxLength(100);

                entity.Property(x => x.StoreName).HasMaxLength(200);
                entity.Property(x => x.StoreAddress).HasMaxLength(200);
                entity.Property(x => x.StoreCity).HasMaxLength(100);
                entity.Property(x => x.StoreState).HasMaxLength(50);
                entity.Property(x => x.StoreZipCode).HasMaxLength(30);

                entity.Property(x => x.ReleaseLevel).HasMaxLength(100);
                entity.Property(x => x.ReleaseApplied).HasMaxLength(100);

                entity.Property(x => x.Domain).HasMaxLength(100);
                entity.Property(x => x.OSVersion).HasMaxLength(200);
                entity.Property(x => x.CPUArch).HasMaxLength(50);

                entity.Property(x => x.VerifoneModel).HasMaxLength(100);
                entity.Property(x => x.VerifoneIP).HasMaxLength(100);

                entity.Property(x => x.ScannerName).HasMaxLength(200);
                entity.Property(x => x.ScannerSerialNumber).HasMaxLength(100);
            });
        }
    }
}
using Microsoft.EntityFrameworkCore;
using RetailCentral.Api.Data.Entities;
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
        public DbSet<Package> Packages => Set<Package>();
        public DbSet<Deployment> Deployments => Set<Deployment>();
        public DbSet<DeploymentDevice> DeploymentDevices => Set<DeploymentDevice>();
        public DbSet<InstalledSoftware> InstalledSoftwares => Set<InstalledSoftware>();
        public DbSet<InstalledWindowsUpdate> InstalledWindowsUpdates => Set<InstalledWindowsUpdate>();
        public DbSet<ProcessStatusInventory> ProcessStatusInventories => Set<ProcessStatusInventory>();
        public DbSet<UserActivityInventory> UserActivityInventories => Set<UserActivityInventory>();
        public DbSet<UserActivityHistory> UserActivityHistories => Set<UserActivityHistory>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

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

            modelBuilder.Entity<Heartbeat>()
                .HasIndex(h => new { h.DeviceId, h.TimestampUtc });

            modelBuilder.Entity<Command>(entity =>
            {
                entity.HasKey(c => c.CommandId);

                entity.HasIndex(c => c.Status);
                entity.HasIndex(c => c.DeviceId);
                entity.HasIndex(c => c.StoreNumber);
                entity.HasIndex(c => c.ExpiresUtc);
                entity.HasIndex(c => c.LockedUtc);

                entity.Property(c => c.Scope)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(c => c.Type)
                    .HasMaxLength(100)
                    .IsRequired();

                entity.Property(c => c.StoreNumber)
                    .HasMaxLength(50);

                entity.Property(c => c.GroupName)
                    .HasMaxLength(200);

                entity.Property(c => c.Status)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(c => c.LastError)
                    .HasMaxLength(2000);

                entity.Property(c => c.IssuedBy)
                    .HasMaxLength(200);
            });

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

            modelBuilder.Entity<Package>(entity =>
            {
                entity.ToTable("Packages");
                entity.HasKey(x => x.Id);

                entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
                entity.Property(x => x.Version).HasMaxLength(50);
                entity.Property(x => x.Description).HasMaxLength(1000);
                entity.Property(x => x.FileName).HasMaxLength(260).IsRequired();
                entity.Property(x => x.StoragePath).HasMaxLength(1000).IsRequired();
                entity.Property(x => x.Sha256).HasMaxLength(128).IsRequired();
                entity.Property(x => x.ExecutionCommand).HasMaxLength(500);
                entity.Property(x => x.ExecutionArguments).HasMaxLength(2000);
                entity.Property(x => x.WorkingDirectory).HasMaxLength(500);
                entity.Property(x => x.CreatedBy).HasMaxLength(100);
            });

            modelBuilder.Entity<InstalledSoftware>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.HasIndex(x => x.DeviceId);

                entity.Property(x => x.Name).HasMaxLength(500);
                entity.Property(x => x.Version).HasMaxLength(100);
                entity.Property(x => x.Publisher).HasMaxLength(200);
                entity.Property(x => x.InstallDate).HasMaxLength(50);
            });

            modelBuilder.Entity<InstalledWindowsUpdate>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.HasIndex(x => x.DeviceId);

                entity.Property(x => x.HotFixId).HasMaxLength(100);
                entity.Property(x => x.Description).HasMaxLength(500);
                entity.Property(x => x.InstalledOn).HasMaxLength(50);
            });

            modelBuilder.Entity<Deployment>(entity =>
            {
                entity.ToTable("Deployments");
                entity.HasKey(x => x.Id);

                entity.Property(x => x.TargetValue).HasMaxLength(200).IsRequired();
                entity.Property(x => x.Notes).HasMaxLength(1000);
                entity.Property(x => x.CreatedBy).HasMaxLength(100);

                entity.HasOne(x => x.Package)
                    .WithMany(x => x.Deployments)
                    .HasForeignKey(x => x.PackageId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => x.PackageId);
                entity.HasIndex(x => x.Status);
            });

            modelBuilder.Entity<ProcessStatusInventory>(entity =>
            {
                entity.HasKey(x => x.ProcessStatusInventoryId);

                entity.HasIndex(x => x.DeviceId)
                    .IsUnique();

                entity.HasOne<Device>()
                    .WithMany()
                    .HasForeignKey(x => x.DeviceId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.Property(x => x.PosProcessName).HasMaxLength(200);
                entity.Property(x => x.RetailShellProcessName).HasMaxLength(200);
                entity.Property(x => x.AgentProcessName).HasMaxLength(200);

                // CPU percentages
                entity.Property(x => x.PosCpuPercent).HasPrecision(5, 2);
                entity.Property(x => x.RetailShellCpuPercent).HasPrecision(5, 2);
                entity.Property(x => x.AgentCpuPercent).HasPrecision(5, 2);

                // Working set memory in MB
                entity.Property(x => x.PosWorkingSetMb).HasPrecision(10, 2);
                entity.Property(x => x.RetailShellWorkingSetMb).HasPrecision(10, 2);
                entity.Property(x => x.AgentWorkingSetMb).HasPrecision(10, 2);
            });

            modelBuilder.Entity<UserActivityInventory>(entity =>
            {
                entity.HasKey(x => x.UserActivityInventoryId);

                entity.HasIndex(x => x.DeviceId)
                    .IsUnique();

                entity.HasOne<Device>()
                    .WithMany()
                    .HasForeignKey(x => x.DeviceId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.Property(x => x.SessionState).HasMaxLength(50);
                entity.Property(x => x.ConsoleUserName).HasMaxLength(200);
            });

            modelBuilder.Entity<UserActivityHistory>(entity =>
            {
                entity.HasKey(x => x.UserActivityHistoryId);

                entity.HasIndex(x => new { x.DeviceId, x.CapturedUtc });

                entity.HasOne<Device>()
                    .WithMany()
                    .HasForeignKey(x => x.DeviceId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.Property(x => x.SessionState).HasMaxLength(50);
            });

            modelBuilder.Entity<DeploymentDevice>(entity =>
            {
                entity.ToTable("DeploymentDevices");
                entity.HasKey(x => x.Id);

                entity.Property(x => x.StoreNumber).HasMaxLength(50);
                entity.Property(x => x.Hostname).HasMaxLength(200);
                entity.Property(x => x.ResultMessage).HasMaxLength(2000);
                entity.Property(x => x.FilePath).HasMaxLength(500);

                entity.HasOne(x => x.Deployment)
                    .WithMany(x => x.DeploymentDevices)
                    .HasForeignKey(x => x.DeploymentId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(x => x.DeploymentId);
                entity.HasIndex(x => x.DeviceId);
                entity.HasIndex(x => x.Status);
            });
        }
    }
}

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
        }
    }
}
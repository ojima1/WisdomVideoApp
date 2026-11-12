using Microsoft.EntityFrameworkCore;
using WisdomVideoApp.Models;

namespace WisdomVideoApp.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<VideoJob> VideoJobs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<VideoJob>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.JobId)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(e => e.Script)
                    .IsRequired();

                entity.Property(e => e.VoiceName)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.Status)
                    .IsRequired()
                    .HasMaxLength(20);

                entity.Property(e => e.VideoUrl)
                    .HasMaxLength(500);

                // Index for faster lookups by JobId
                entity.HasIndex(e => e.JobId)
                    .IsUnique();
            });
        }
    }
}
using Microsoft.EntityFrameworkCore;

namespace Vidcron.DataModel
{
    public class DownloadsDbContext : DbContext
    {
        public DbSet<DownloadRecord> DownloadRecords { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseSqlite("Data Source=downloads.db");
    }
}
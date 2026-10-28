using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SCTools.Server.Data;

/// <summary>
/// The database for the EF Core tools (dotnet ef migrations add ...): they make a migration from the tables of TrackerDb without starting the server. The
/// file is never written by them.
/// </summary>
public sealed class TrackerDbFactory : IDesignTimeDbContextFactory<TrackerDb>
{
    public TrackerDb CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<TrackerDb>().UseSqlite("Data Source=design.db").Options);
}

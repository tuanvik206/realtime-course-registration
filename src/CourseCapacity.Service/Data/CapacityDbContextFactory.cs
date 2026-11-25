using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CourseCapacity.Service.Data;

public sealed class CapacityDbContextFactory : IDesignTimeDbContextFactory<CapacityDbContext>
{
    public CapacityDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__CapacityDb")
            ?? throw new InvalidOperationException("Set ConnectionStrings__CapacityDb for EF design-time commands.");

        var options = new DbContextOptionsBuilder<CapacityDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new CapacityDbContext(options);
    }
}

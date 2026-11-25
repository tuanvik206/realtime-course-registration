using Microsoft.EntityFrameworkCore;

namespace IntegrationTests;

public class UnitTest1
{
    [Fact]
    public void CapacityContextUsesPostgreSqlProvider()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<CourseCapacity.Service.Data.CapacityDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only;Username=test;Password=test")
            .Options;
        using var context = new CourseCapacity.Service.Data.CapacityDbContext(options);
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
    }
}

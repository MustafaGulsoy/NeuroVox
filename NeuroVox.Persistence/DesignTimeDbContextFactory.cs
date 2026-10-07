using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using NeuroVox.Persistence.Contexts;

namespace NeuroVox.Persistence
{
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NeuroVoxDbContext>
    {
        public NeuroVoxDbContext CreateDbContext(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            var optionsBuilder = new DbContextOptionsBuilder<NeuroVoxDbContext>();
            optionsBuilder.UseNpgsql(configuration["NeuroVox:ConnectionString"]);

            return new NeuroVoxDbContext(optionsBuilder.Options, configuration);
        }
    }
}

using Microsoft.EntityFrameworkCore;

namespace MultiTenantAPI.Models
{
    public class TenantDbContext : DbContext
    {
        public TenantDbContext(DbContextOptions<TenantDbContext> options) : base(options)
        {
        }
        public DbSet<Tenant> Tenants { get; set; }
    }
}

using Microsoft.EntityFrameworkCore;
using MultiTenantAPI.Services;

namespace MultiTenantAPI.Models
{
    public class AppDbContext : DbContext
    {
        private readonly ICurrentTenantService _currentTenantService;
        public string CurrentTenantId { get; set; }
        public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenantService currentTenantService) : base(options)
        {
            _currentTenantService = currentTenantService;
            CurrentTenantId = _currentTenantService.TenantId;
        }
        public DbSet<Tenant> Tenants { get; set; }
        public DbSet<Product> Products { get; set; }

        // On app startup
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Global query filter to ensure tenant isolation
            modelBuilder.Entity<Product>().HasQueryFilter(p => p.TenantId == CurrentTenantId);
        }

        // Everytime trigger when we save anything in database
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries<IMustHaveTenant>().ToList())
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                    case EntityState.Modified:
                        entry.Entity.TenantId = CurrentTenantId;
                        break;
                }
            }
            var result = base.SaveChanges();
            return result;
        }
    }
}

using Microsoft.EntityFrameworkCore;
using MultiTenantAPI.Models;

namespace MultiTenantAPI.Services
{
    public class CurrentTenantService : ICurrentTenantService
    {
        private readonly TenantDbContext _context;
        public CurrentTenantService(TenantDbContext context)
        {
            _context = context;
        }

        public string? TenantId { get; set; }

        public async Task<bool> SetTenant(string tenant)
        {
            var tenantExists = await _context.Tenants.Where(t => t.Id == tenant).FirstOrDefaultAsync();
            if (tenantExists != null)
            {
                TenantId = tenantExists.Id;
                return true;
            }
            throw new Exception("Tenant Invalid!");
        }
    }
}

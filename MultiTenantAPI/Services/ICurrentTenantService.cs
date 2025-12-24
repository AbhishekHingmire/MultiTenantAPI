namespace MultiTenantAPI.Services
{
    public interface ICurrentTenantService
    {
        string? TenantId { get; set; }
        public Task<bool> SetTenant(string tenant); // method to set above value
    }
}

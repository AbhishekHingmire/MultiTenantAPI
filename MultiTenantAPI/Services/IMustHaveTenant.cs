namespace MultiTenantAPI.Services
{
    public interface IMustHaveTenant
    {
        public string TenantId { get; set; }
    }
}

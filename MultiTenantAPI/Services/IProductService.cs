using MultiTenantAPI.Models;
using MultiTenantAPI.Services.DTOs;

namespace MultiTenantAPI.Services
{
    public interface IProductService
    {
        IEnumerable<Product> GetAllProducts();
        Product CreateProduct(CreateProductServiceRequest request);
        bool DeleteProduct(int id);
    }
}

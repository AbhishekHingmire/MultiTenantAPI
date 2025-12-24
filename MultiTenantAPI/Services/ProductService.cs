using MultiTenantAPI.Models;
using MultiTenantAPI.Services.DTOs;

namespace MultiTenantAPI.Services
{
    public class ProductService : IProductService
    {
        public readonly AppDbContext _context;
        public ProductService(AppDbContext context) 
        {
            _context = context;
        }
        public Product CreateProduct(CreateProductServiceRequest request)
        {
            var product = new Product
            {
                Name = request.Name,
                Description = request.Description
            };
            _context.Products.Add(product);
            _context.SaveChanges();
            return product;
        }

        public bool DeleteProduct(int id)
        {
            var product = _context.Products.Find(id);
            if (product == null)
            {
                return false;
            }
            _context.Products.Remove(product);
            _context.SaveChanges();
            return true;
        }

        public IEnumerable<Product> GetAllProducts()
        {
            var products = _context.Products.ToList();
            return products;
        }
    }
}

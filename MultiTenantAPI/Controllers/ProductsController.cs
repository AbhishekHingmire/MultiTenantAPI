using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MultiTenantAPI.Services;
using MultiTenantAPI.Services.DTOs;

namespace MultiTenantAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProductsController : ControllerBase
    {
        public readonly IProductService _productService;
        public ProductsController(IProductService productService)
        {
            _productService = productService;
        }

        [HttpGet]
        public IActionResult Get()
        {
            var products = _productService.GetAllProducts();
            return Ok(products);
        }

        [HttpPost]
        public IActionResult Post([FromBody] CreateProductServiceRequest request)
        {
            var product = _productService.CreateProduct(request);
            return Ok(product);
        }

        [HttpDelete("{id}")]
        public IActionResult Delete(int id)
        {
            var result = _productService.DeleteProduct(id);
            if (!result)
            {
                return NotFound();
            }
            return Ok(result);
        }
    }
}
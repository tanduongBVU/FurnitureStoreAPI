using FurnitureStoreAPI.Data;
using Microsoft.AspNetCore.Mvc;

namespace FurnitureStoreAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProductsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ProductsController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Get()
        {
            return Ok(_context.Products.ToList());
        }
    }
}
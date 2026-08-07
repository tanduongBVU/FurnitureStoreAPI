using FurnitureStoreAPI.Data;
using FurnitureStoreAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

        // GET: api/products
        // Công khai — dùng cho Client (cửa hàng). CHỈ trả về sản phẩm đang bán (IsActive = true).
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var products = await _context.Products
                .Where(p => p.IsActive)
                .ToListAsync();
            return Ok(products);
        }

        // GET: api/products/all
        // Chỉ Admin & Nhân viên — dùng cho trang quản trị, xem được CẢ sản phẩm đã ẩn.
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpGet("all")]
        public async Task<IActionResult> GetAll()
        {
            var products = await _context.Products.ToListAsync();
            return Ok(products);
        }

        // GET: api/products/5  — dùng chung cho cả Client (chi tiết sản phẩm) và Admin (trang sửa)
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null) return NotFound();
            return Ok(product);
        }

        // POST: api/products  — Admin & Nhân viên đều được thêm sản phẩm
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpPost]
        public async Task<IActionResult> Create(Product product)
        {
            product.CreatedAt = DateTime.Now;
            product.IsActive = true;
            _context.Products.Add(product);
            await _context.SaveChangesAsync();
            return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
        }

        // PUT: api/products/5  — Admin & Nhân viên đều được sửa sản phẩm
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, Product product)
        {
            if (id != product.Id) return BadRequest();

            var existing = await _context.Products.FindAsync(id);
            if (existing == null) return NotFound();

            existing.Name = product.Name;
            existing.Category = product.Category;
            existing.Price = product.Price;
            existing.Description = product.Description;
            existing.Image = product.Image;
            existing.Stock = product.Stock;
            existing.IsBestSeller = product.IsBestSeller;
            // Không đổi IsActive ở đây — việc ẩn/hiện dùng riêng 2 endpoint bên dưới

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // DELETE: api/products/5
        // "Xoá" ở đây thực chất là ẨN sản phẩm (soft-delete) — set IsActive = false.
        // Không xoá dữ liệu thật để không phá vỡ các đơn hàng cũ đã tham chiếu tới
        // sản phẩm này (ràng buộc khoá ngoại OrderItems -> Products là Restrict).
        // CHỈ Admin được ẩn sản phẩm.
        [Authorize(Roles = "Admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null) return NotFound();

            product.IsActive = false;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // PATCH: api/products/5/restore
        // Hiện lại sản phẩm đã bị ẩn trước đó. CHỈ Admin được thao tác.
        [Authorize(Roles = "Admin")]
        [HttpPatch("{id}/restore")]
        public async Task<IActionResult> Restore(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null) return NotFound();

            product.IsActive = true;
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}
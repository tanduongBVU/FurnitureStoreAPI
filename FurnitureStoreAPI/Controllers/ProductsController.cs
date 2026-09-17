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

        private async Task<List<object>> AttachRatingsAsync(List<Product> products)
        {
            if (products.Count == 0) return new List<object>();

            var productIds = products.Select(p => p.Id).ToList();

            var ratingStats = await _context.Reviews
                .Where(r => productIds.Contains(r.ProductId) && r.IsApproved)
                .GroupBy(r => r.ProductId)
                .Select(g => new { ProductId = g.Key, Avg = g.Average(r => r.Rating), Count = g.Count() })
                .ToDictionaryAsync(x => x.ProductId, x => x);

            var variants = await _context.ProductVariants
                .Where(v => productIds.Contains(v.ProductId))
                .ToListAsync();

            return products.Select(p =>
            {
                ratingStats.TryGetValue(p.Id, out var stat);

                // Lấy .Name TRỰC TIẾP từ đối tượng ProductVariant (đã load vào bộ nhớ ở
                // trên) — property Name là [NotMapped]/tính toán, KHÔNG dùng lại được
                // trong biểu thức LINQ dịch sang SQL, nhưng ở đây variants đã là List
                // trong bộ nhớ (đã ToListAsync xong) nên gọi thẳng .Name bình thường.
                var pVariants = variants
                    .Where(v => v.ProductId == p.Id)
                    .Select(v => new { v.Id, v.Material, v.Color, v.Name, v.Price, v.Stock })
                    .ToList();

                return (object)new
                {
                    p.Id,
                    p.Name,
                    p.Category,
                    p.Price,
                    p.Description,
                    p.Image,
                    p.Stock,
                    p.IsBestSeller,
                    p.IsActive,
                    p.CreatedAt,
                    p.DiscountPercent,
                    p.Material,
                    p.Color,
                    AverageRating = stat != null ? Math.Round(stat.Avg, 1) : 0,
                    ReviewCount = stat?.Count ?? 0,
                    Variants = pVariants,
                    HasVariants = pVariants.Count > 0,
                    DisplayPrice = pVariants.Count > 0 ? pVariants.Min(v => v.Price) : p.Price,
                };
            }).ToList();
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var products = await _context.Products
                .Where(p => p.IsActive)
                .ToListAsync();
            return Ok(await AttachRatingsAsync(products));
        }

        [HttpGet("sale")]
        public async Task<IActionResult> GetOnSale()
        {
            var products = await _context.Products
                .Where(p => p.IsActive && p.DiscountPercent > 0)
                .ToListAsync();
            return Ok(await AttachRatingsAsync(products));
        }

        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string q)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
                return Ok(new List<object>());

            var keyword = q.Trim().ToLower();
            var products = await _context.Products
                .Where(p => p.IsActive && p.Name.ToLower().Contains(keyword))
                .OrderBy(p => p.Name)
                .Take(5)
                .ToListAsync();

            return Ok(await AttachRatingsAsync(products));
        }

        [HttpGet("{id}/related")]
        public async Task<IActionResult> GetRelated(int id)
        {
            const int RELATED_COUNT = 4;

            var product = await _context.Products.FindAsync(id);
            if (product == null) return NotFound();

            var sameCategory = await _context.Products
                .Where(p => p.IsActive && p.Id != id && p.Category == product.Category)
                .OrderByDescending(p => p.IsBestSeller)
                .ThenByDescending(p => p.CreatedAt)
                .Take(RELATED_COUNT)
                .ToListAsync();

            if (sameCategory.Count < RELATED_COUNT)
            {
                var excludeIds = sameCategory.Select(p => p.Id).ToList();
                excludeIds.Add(id);

                var filler = await _context.Products
                    .Where(p => p.IsActive && !excludeIds.Contains(p.Id))
                    .OrderByDescending(p => p.IsBestSeller)
                    .ThenByDescending(p => p.CreatedAt)
                    .Take(RELATED_COUNT - sameCategory.Count)
                    .ToListAsync();

                sameCategory.AddRange(filler);
            }

            return Ok(await AttachRatingsAsync(sameCategory));
        }

        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpGet("all")]
        public async Task<IActionResult> GetAll()
        {
            var products = await _context.Products.ToListAsync();
            return Ok(products);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var product = await _context.Products
                .Include(p => p.Variants)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (product == null) return NotFound();
            return Ok(product);
        }

        // POST: api/products
        // MỚI: nếu có biến thể, kiểm tra tổng Stock các biến thể KHÔNG được vượt quá
        // Stock tổng của sản phẩm gốc — Stock tổng coi như "sức chứa tối đa", các biến
        // thể cộng lại không được tràn ra ngoài con số đó.
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpPost]
        public async Task<IActionResult> Create(Product product)
        {
            if (product.Variants.Count > 0)
            {
                var totalVariantStock = product.Variants.Sum(v => v.Stock);
                if (totalVariantStock > product.Stock)
                    return BadRequest(new
                    {
                        message = $"Tổng tồn kho các biến thể ({totalVariantStock}) không được vượt quá tồn kho tổng ({product.Stock})."
                    });
            }

            product.CreatedAt = DateTime.Now;
            product.IsActive = true;
            foreach (var v in product.Variants) v.Id = 0;

            _context.Products.Add(product);
            await _context.SaveChangesAsync();
            return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
        }

        // PUT: api/products/5
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, Product product)
        {
            if (id != product.Id) return BadRequest();

            var existing = await _context.Products.FindAsync(id);
            if (existing == null) return NotFound();

            if (product.Variants.Count > 0)
            {
                var totalVariantStock = product.Variants.Sum(v => v.Stock);
                if (totalVariantStock > product.Stock)
                    return BadRequest(new
                    {
                        message = $"Tổng tồn kho các biến thể ({totalVariantStock}) không được vượt quá tồn kho tổng ({product.Stock})."
                    });
            }

            existing.Name = product.Name;
            existing.Category = product.Category;
            existing.Price = product.Price;
            existing.Description = product.Description;
            existing.Image = product.Image;
            existing.Stock = product.Stock;
            existing.IsBestSeller = product.IsBestSeller;
            existing.DiscountPercent = product.DiscountPercent;
            existing.Material = product.Material;
            existing.Color = product.Color;

            var oldVariants = _context.ProductVariants.Where(v => v.ProductId == id);
            _context.ProductVariants.RemoveRange(oldVariants);
            foreach (var v in product.Variants)
            {
                _context.ProductVariants.Add(new ProductVariant
                {
                    ProductId = id,
                    Material = v.Material,
                    Color = v.Color,
                    Price = v.Price,
                    Stock = v.Stock,
                });
            }

            await _context.SaveChangesAsync();
            return NoContent();
        }

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
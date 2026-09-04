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

        // Gắn thêm AverageRating (làm tròn 1 chữ số thập phân) + ReviewCount vào từng sản phẩm,
        // CHỈ tính trên review đã duyệt (IsApproved = true) — đúng những gì khách thấy công khai.
        // Dùng LEFT JOIN thủ công (GroupJoin) để sản phẩm chưa có review nào vẫn trả về
        // AverageRating = 0, ReviewCount = 0 thay vì bị loại khỏi kết quả.
        // Trả về DTO ẩn danh gộp toàn bộ field gốc của Product + 2 field mới, để KHÔNG phải
        // sửa Product.cs (model) hay chạy migration DB nào cả.
        private async Task<List<object>> AttachRatingsAsync(List<Product> products)
        {
            if (products.Count == 0) return new List<object>();

            var productIds = products.Select(p => p.Id).ToList();

            var ratingStats = await _context.Reviews
                .Where(r => productIds.Contains(r.ProductId) && r.IsApproved)
                .GroupBy(r => r.ProductId)
                .Select(g => new { ProductId = g.Key, Avg = g.Average(r => r.Rating), Count = g.Count() })
                .ToDictionaryAsync(x => x.ProductId, x => x);

            return products.Select(p =>
            {
                ratingStats.TryGetValue(p.Id, out var stat);
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
                };
            }).ToList();
        }

        // GET: api/products
        // Công khai — dùng cho Client (cửa hàng). CHỈ trả về sản phẩm đang bán (IsActive = true).
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var products = await _context.Products
                .Where(p => p.IsActive)
                .ToListAsync();
            return Ok(await AttachRatingsAsync(products));
        }

        // GET: api/products/sale
        // Công khai — danh sách sản phẩm đang giảm giá (DiscountPercent > 0), dùng cho trang Khuyến Mãi.
        [HttpGet("sale")]
        public async Task<IActionResult> GetOnSale()
        {
            var products = await _context.Products
                .Where(p => p.IsActive && p.DiscountPercent > 0)
                .ToListAsync();
            return Ok(await AttachRatingsAsync(products));
        }

        // GET: api/products/search?q=...
        // Công khai — dùng cho autocomplete ở ô search Navbar. Tìm theo tên (không phân biệt hoa/thường),
        // CHỈ sản phẩm đang bán (IsActive), giới hạn 5 kết quả để dropdown gợi ý luôn gọn.
        // Query rỗng/quá ngắn (<2 ký tự) trả về mảng rỗng ngay, không cần chạm DB — tránh gợi ý
        // vô nghĩa khi khách vừa gõ 1 ký tự đầu tiên.
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

        // GET: api/products/5/related
        // Công khai — gợi ý sản phẩm liên quan cho trang chi tiết sản phẩm.
        // Ưu tiên CÙNG DANH MỤC với sản phẩm đang xem, xếp bán chạy lên trước rồi tới mới nhất.
        // Nếu cùng danh mục không đủ RELATED_COUNT sản phẩm (VD: danh mục quá ít hàng), lấy bù
        // thêm sản phẩm bất kỳ (khác danh mục) để luôn đủ số lượng, tránh phần gợi ý trông trống trải.
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

        // GET: api/products/all
        // Chỉ Admin & Nhân viên — dùng cho trang quản trị, xem được CẢ sản phẩm đã ẩn.
        // KHÔNG gắn rating ở đây — Admin không cần hiện sao trong bảng quản lý sản phẩm.
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpGet("all")]
        public async Task<IActionResult> GetAll()
        {
            var products = await _context.Products.ToListAsync();
            return Ok(products);
        }

        // GET: api/products/5  — dùng chung cho cả Client (chi tiết sản phẩm) và Admin (trang sửa)
        // Giữ nguyên trả về Product gốc — trang ProductDetail.jsx đã tự gọi review riêng để
        // tính rating hiển thị ở đó, không cần đụng vào endpoint này.

        //
        // LƯU Ý THỨ TỰ ROUTE: định nghĩa này ({id}) PHẢI nằm SAU "search", "sale", "all" và
        // "{id}/related" ở trên — nếu đặt lên trước, ASP.NET sẽ hiểu nhầm "search"/"sale"/"all"
        // là một giá trị id (kiểu route "/products/search" khớp nhầm với "/products/{id}").

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
            existing.DiscountPercent = product.DiscountPercent;

            existing.Material = product.Material;
            existing.Color = product.Color;


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
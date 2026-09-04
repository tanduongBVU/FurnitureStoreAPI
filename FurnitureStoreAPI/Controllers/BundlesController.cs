using FurnitureStoreAPI.Data;
using FurnitureStoreAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FurnitureStoreAPI.Controllers
{
    public class BundleItemInputDto
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; } = 1;
    }

    public class BundleInputDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Image { get; set; }
        public int DiscountPercent { get; set; } = 0;
        public List<BundleItemInputDto> Items { get; set; } = new();
    }

    [ApiController]
    [Route("api/[controller]")]
    public class BundlesController : ControllerBase
    {
        private readonly AppDbContext _context;
        public BundlesController(AppDbContext context)
        {
            _context = context;
        }

        // Gộp thông tin sản phẩm thật (tên/ảnh/giá HIỆN TẠI) vào từng dòng combo, và tự
        // tính sẵn Subtotal (tổng giá gốc từng món cộng lại) + FinalPrice (sau khi áp thêm
        // DiscountPercent của combo) — để Client không phải tự tính lại, tránh sai lệch
        // nếu công thức tính giá đổi sau này (chỉ cần sửa đúng 1 chỗ ở đây).
        private async Task<List<object>> AttachDetailsAsync(List<Bundle> bundles)
        {
            if (bundles.Count == 0) return new List<object>();

            var bundleIds = bundles.Select(b => b.Id).ToList();
            var items = await _context.BundleItems
                .Where(bi => bundleIds.Contains(bi.BundleId))
                .Include(bi => bi.Product)
                .ToListAsync();

            return bundles.Select(b =>
            {
                var bundleItems = items.Where(i => i.BundleId == b.Id).ToList();

                var itemDtos = bundleItems.Select(i =>
                {
                    var p = i.Product;
                    // Dùng giá BÁN THẬT của từng sản phẩm (đã trừ khuyến mãi riêng của nó,
                    // nếu có) làm gốc, rồi combo mới áp thêm % giảm của riêng combo lên trên.
                    var unitPrice = p != null ? p.Price * (1 - p.DiscountPercent / 100m) : 0;
                    return new
                    {
                        i.ProductId,
                        ProductName = p?.Name ?? "(Sản phẩm đã bị xoá)",
                        ProductImage = p?.Image,
                        ProductPrice = unitPrice,
                        i.Quantity,
                        LineTotal = unitPrice * i.Quantity,
                    };
                }).ToList();

                var subtotal = itemDtos.Sum(i => i.LineTotal);
                var finalPrice = Math.Round(subtotal * (1 - b.DiscountPercent / 100m), 0);

                return (object)new
                {
                    b.Id,
                    b.Name,
                    b.Description,
                    b.Image,
                    b.DiscountPercent,
                    b.IsActive,
                    b.CreatedAt,
                    Subtotal = subtotal,
                    FinalPrice = finalPrice,
                    Items = itemDtos,
                };
            }).ToList();
        }

        // GET: api/bundles — công khai, dùng cho Client, chỉ combo đang bật
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var bundles = await _context.Bundles
                .Where(b => b.IsActive)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();
            return Ok(await AttachDetailsAsync(bundles));
        }

        // GET: api/bundles/all — Admin & Nhân viên, xem cả combo đã ẩn
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpGet("all")]
        public async Task<IActionResult> GetAll()
        {
            var bundles = await _context.Bundles
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();
            return Ok(await AttachDetailsAsync(bundles));
        }

        // GET: api/bundles/5 — dùng chung Client (chi tiết) và Admin (trang sửa)
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var bundle = await _context.Bundles.FindAsync(id);
            if (bundle == null) return NotFound();
            var result = await AttachDetailsAsync(new List<Bundle> { bundle });
            return Ok(result[0]);
        }

        // POST: api/bundles — Admin & Nhân viên
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpPost]
        public async Task<IActionResult> Create(BundleInputDto dto)
        {
            if (dto.Items == null || dto.Items.Count < 2)
                return BadRequest(new { message = "Combo cần ít nhất 2 sản phẩm." });

            var bundle = new Bundle
            {
                Name = dto.Name,
                Description = dto.Description,
                Image = dto.Image,
                DiscountPercent = dto.DiscountPercent,
                IsActive = true,
                CreatedAt = DateTime.Now,
            };
            _context.Bundles.Add(bundle);
            await _context.SaveChangesAsync(); // lưu trước để có bundle.Id cho BundleItem

            foreach (var item in dto.Items)
            {
                _context.BundleItems.Add(new BundleItem
                {
                    BundleId = bundle.Id,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity < 1 ? 1 : item.Quantity,
                });
            }
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = bundle.Id }, new { bundle.Id });
        }

        // PUT: api/bundles/5 — cập nhật thông tin + THAY TOÀN BỘ danh sách sản phẩm (xoá hết
        // BundleItem cũ, tạo lại mới theo danh sách gửi lên). Đơn giản hơn nhiều so với dò
        // từng dòng thêm/bớt/sửa riêng lẻ, và 1 combo thường không có nhiều sản phẩm nên
        // không tốn kém.
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, BundleInputDto dto)
        {
            var bundle = await _context.Bundles.FindAsync(id);
            if (bundle == null) return NotFound();

            if (dto.Items == null || dto.Items.Count < 2)
                return BadRequest(new { message = "Combo cần ít nhất 2 sản phẩm." });

            bundle.Name = dto.Name;
            bundle.Description = dto.Description;
            bundle.Image = dto.Image;
            bundle.DiscountPercent = dto.DiscountPercent;

            var oldItems = _context.BundleItems.Where(bi => bi.BundleId == id);
            _context.BundleItems.RemoveRange(oldItems);

            foreach (var item in dto.Items)
            {
                _context.BundleItems.Add(new BundleItem
                {
                    BundleId = id,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity < 1 ? 1 : item.Quantity,
                });
            }

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // DELETE: api/bundles/5 — soft-delete, giống pattern Product. CHỈ Admin.
        [Authorize(Roles = "Admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var bundle = await _context.Bundles.FindAsync(id);
            if (bundle == null) return NotFound();
            bundle.IsActive = false;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // PATCH: api/bundles/5/restore — CHỈ Admin.
        [Authorize(Roles = "Admin")]
        [HttpPatch("{id}/restore")]
        public async Task<IActionResult> Restore(int id)
        {
            var bundle = await _context.Bundles.FindAsync(id);
            if (bundle == null) return NotFound();
            bundle.IsActive = true;
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}
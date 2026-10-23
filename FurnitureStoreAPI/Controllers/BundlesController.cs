using FurnitureStoreAPI.Data;
using FurnitureStoreAPI.Models;
using FurnitureStoreAPI.Services;
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
        // tính sẵn Subtotal (tổng giá bán từng món cộng lại) + FinalPrice (giá combo sau khi
        // áp thêm DiscountPercent) — để Client không phải tự tính lại.
        //
        // GIÁ: dùng PricingHelper (dùng chung với OrdersController). FinalPrice = tổng của
        // ComboUnitPrice x số lượng từng dòng, nên ĐÚNG BẰNG số tiền đơn hàng sẽ tính.
        //
        // TỒN KHO: mỗi dòng có thêm ProductStock + InStock (đủ số lượng combo cần không), và cả combo có
        // IsAvailable (mọi sản phẩm đều đủ hàng và còn đang bán), OutOfStockItems (tên các sản phẩm đang
        // thiếu hàng) và MaxQuantity (mua tối đa được bao nhiêu combo với tồn kho hiện tại). Client dùng
        // để báo "hết hàng" và chặn thêm vào giỏ.
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
                    var unitPrice = p != null ? PricingHelper.SaleUnitPrice(p) : 0m;
                    var comboUnitPrice = p != null ? PricingHelper.ComboUnitPrice(p, b.DiscountPercent) : 0m;
                    var stock = p?.Stock ?? 0;
                    var inStock = p != null && p.IsActive && stock >= i.Quantity;
                    return new
                    {
                        i.ProductId,
                        ProductName = p?.Name ?? "(Sản phẩm đã bị xoá)",
                        ProductImage = p?.Image,
                        ProductPrice = unitPrice,
                        ComboUnitPrice = comboUnitPrice,
                        i.Quantity,
                        LineTotal = unitPrice * i.Quantity,
                        ComboLineTotal = comboUnitPrice * i.Quantity,
                        ProductStock = stock,
                        InStock = inStock,
                    };
                }).ToList();

                var subtotal = itemDtos.Sum(i => i.LineTotal);
                var finalPrice = itemDtos.Sum(i => i.ComboLineTotal);

                var outOfStockNames = itemDtos.Where(i => !i.InStock).Select(i => i.ProductName).ToList();
                var isAvailable = itemDtos.Count > 0 && outOfStockNames.Count == 0;
                var maxQuantity = itemDtos.Count == 0
                    ? 0
                    : itemDtos.Min(i => i.Quantity > 0 ? i.ProductStock / i.Quantity : 0);

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
                    IsAvailable = isAvailable,
                    OutOfStockItems = outOfStockNames,
                    MaxQuantity = maxQuantity,
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

        // DELETE: api/bundles/5 — soft-delete, giống pattern Product. Admin & Nhân viên đều được ẩn.
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var bundle = await _context.Bundles.FindAsync(id);
            if (bundle == null) return NotFound();
            bundle.IsActive = false;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // PATCH: api/bundles/5/restore — Admin & Nhân viên.
        [Authorize(Roles = "Admin,Nhân viên")]
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
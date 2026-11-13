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
        // Biến thể được chọn — bắt buộc nếu sản phẩm có biến thể, bỏ trống (null) nếu không có.
        public int? VariantId { get; set; }
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

            // Biến thể của mọi sản phẩm xuất hiện trong các combo (để biết sản phẩm nào có biến thể và lấy giá/tồn kho đúng biến thể)
            var productIds = items.Select(i => i.ProductId).Distinct().ToList();
            var allVariants = await _context.ProductVariants
                .Where(v => productIds.Contains(v.ProductId))
                .ToListAsync();

            return bundles.Select(b =>
            {
                var bundleItems = items.Where(i => i.BundleId == b.Id).ToList();

                var itemDtos = bundleItems.Select(i =>
                {
                    var p = i.Product;
                    var productVariants = allVariants.Where(v => v.ProductId == i.ProductId).ToList();
                    var hasVariants = productVariants.Count > 0;
                    var variant = i.VariantId.HasValue ? productVariants.FirstOrDefault(v => v.Id == i.VariantId.Value) : null;

                    // Combo cũ (tạo trước khi có chọn biến thể) hoặc biến thể đã bị xoá: không bán được cho tới khi Admin chọn lại
                    var variantProblem = p != null && hasVariants && variant == null;

                    string name;
                    if (p == null) name = "(Sản phẩm đã bị xoá)";
                    else if (variant != null) name = $"{p.Name} - {variant.Name}";
                    else if (variantProblem) name = $"{p.Name} (chưa chọn biến thể)";
                    else name = p.Name;

                    var unitPrice = p == null ? 0m
                        : variant != null ? PricingHelper.VariantSaleUnitPrice(p, variant)
                        : PricingHelper.SaleUnitPrice(p);
                    var comboUnitPrice = p == null ? 0m
                        : variant != null ? PricingHelper.ComboUnitPrice(p, variant, b.DiscountPercent)
                        : PricingHelper.ComboUnitPrice(p, b.DiscountPercent);

                    // Tồn kho: theo biến thể nếu có (và không vượt kho tổng của sản phẩm)
                    var stock = p == null ? 0 : variant != null ? Math.Min(variant.Stock, p.Stock) : p.Stock;
                    var inStock = p != null && p.IsActive && !variantProblem && stock >= i.Quantity;
                    return new
                    {
                        i.ProductId,
                        VariantId = variant?.Id,
                        VariantName = variant?.Name,
                        ProductName = name,
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

        private bool IsStaff() => User.IsInRole("Admin") || User.IsInRole("Nhân viên");

        // Kiểm tra dữ liệu combo ở Backend. Trả câu báo lỗi, hoặc null nếu hợp lệ. Gọi SAU khi đã Trim tên.
        private async Task<string?> ValidateBundleAsync(BundleInputDto dto)
        {
            if (dto.Name.Length == 0 || dto.Name.Length > 200)
                return "Tên combo không được để trống và tối đa 200 ký tự.";
            if (dto.DiscountPercent < 0 || dto.DiscountPercent > 100)
                return "Giảm giá của combo phải từ 0 đến 100%.";
            if (dto.Items == null || dto.Items.Count < 2)
                return "Combo cần ít nhất 2 sản phẩm.";

            // Mỗi dòng là 1 cặp (sản phẩm, biến thể) duy nhất — cùng sản phẩm được xuất hiện nhiều lần nếu khác biến thể.
            var pairs = dto.Items.Select(i => (i.ProductId, i.VariantId)).ToList();
            if (pairs.Distinct().Count() != pairs.Count)
                return "Mỗi sản phẩm (cùng biến thể) chỉ được xuất hiện 1 lần trong combo (muốn nhiều cái thì tăng số lượng).";

            var ids = dto.Items.Select(i => i.ProductId).Distinct().ToList();
            var products = await _context.Products.Where(p => ids.Contains(p.Id)).ToListAsync();
            if (products.Count != ids.Count)
                return "Combo có sản phẩm không tồn tại.";

            var variants = await _context.ProductVariants.Where(v => ids.Contains(v.ProductId)).ToListAsync();
            foreach (var item in dto.Items)
            {
                var product = products.First(p => p.Id == item.ProductId);
                var pv = variants.Where(v => v.ProductId == item.ProductId).ToList();
                if (pv.Count > 0)
                {
                    // Sản phẩm CÓ biến thể → bắt buộc chọn rõ 1 biến thể của chính sản phẩm đó
                    if (!item.VariantId.HasValue)
                        return $"Sản phẩm '{product.Name}' có biến thể — vui lòng chọn rõ biến thể cho combo.";
                    if (!pv.Any(v => v.Id == item.VariantId.Value))
                        return $"Biến thể đã chọn không thuộc sản phẩm '{product.Name}'.";
                }
                else if (item.VariantId.HasValue)
                {
                    return $"Sản phẩm '{product.Name}' không có biến thể.";
                }
            }
            return null;
        }

        // GET: api/bundles/5 — dùng chung Client (chi tiết) và Admin (trang sửa)
        // Combo đang ẩn: chỉ Admin & Nhân viên xem được.
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var bundle = await _context.Bundles.FindAsync(id);
            if (bundle == null || (!bundle.IsActive && !IsStaff())) return NotFound();
            var result = await AttachDetailsAsync(new List<Bundle> { bundle });
            return Ok(result[0]);
        }

        // POST: api/bundles — Admin & Nhân viên
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpPost]
        public async Task<IActionResult> Create(BundleInputDto dto)
        {
            dto.Name = (dto.Name ?? "").Trim();
            var error = await ValidateBundleAsync(dto);
            if (error != null) return BadRequest(new { message = error });

            // Chặn trùng tên (không phân biệt hoa thường), tính cả combo đang ẩn.
            var lowerName = dto.Name.ToLower();
            if (await _context.Bundles.AnyAsync(b => b.Name.ToLower() == lowerName))
                return BadRequest(new { message = "Combo này đã tồn tại. Nếu nó đang bị ẩn, hãy khôi phục thay vì tạo mới." });

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
                    VariantId = item.VariantId,
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

            dto.Name = (dto.Name ?? "").Trim();
            var error = await ValidateBundleAsync(dto);
            if (error != null) return BadRequest(new { message = error });

            var lowerName = dto.Name.ToLower();
            if (await _context.Bundles.AnyAsync(b => b.Id != id && b.Name.ToLower() == lowerName))
                return BadRequest(new { message = "Đã có combo khác trùng tên này." });

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
                    VariantId = item.VariantId,
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
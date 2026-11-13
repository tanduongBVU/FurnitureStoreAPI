using FurnitureStoreAPI.Data;
using FurnitureStoreAPI.Models;
using FurnitureStoreAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FurnitureStoreAPI.Controllers
{
    // ── DTO đặt hàng ──────────────────────────────────────────────────────────────
    // Trước đây Create nhận thẳng entity Order nên Client gửi gì (kể cả Price, Total) server
    // tin nấy. Giờ Client chỉ gửi "khách muốn mua GÌ, bao nhiêu" — giá do server tự tính.
    public class CreateOrderItemDto
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
        // Giá Client đang hiển thị. CHỈ dùng để đối chiếu cho dòng có biến thể (xem
        // ResolveVariantPrice), mọi dòng khác server bỏ qua và tự tính từ database.
        public decimal Price { get; set; }
        public int? VariantId { get; set; }
        public string? VariantName { get; set; }
    }

    public class CreateOrderBundleDto
    {
        public int BundleId { get; set; }
        public int Quantity { get; set; } = 1;
    }

    // Body của POST /api/Orders/{id}/confirm-transfer
    public class ConfirmTransferDto
    {
        public string? Code { get; set; }
    }

    public class CreateOrderDto
    {
        public int? UserId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string? PaymentMethod { get; set; }
        public string? CouponCode { get; set; }
        public List<CreateOrderItemDto> OrderItems { get; set; } = new();
        public List<CreateOrderBundleDto> Bundles { get; set; } = new();
    }

    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IEmailService _emailService;
        private readonly TokenService _tokenService;
        public OrdersController(AppDbContext context, IEmailService emailService, TokenService tokenService)
        {
            _context = context;
            _emailService = emailService;
            _tokenService = tokenService;
        }

        private async Task<string?> GetUserEmailAsync(int? userId)
        {
            if (!userId.HasValue) return null;
            var user = await _context.Users.FindAsync(userId.Value);
            return user?.Email;
        }

        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var orders = await _context.Orders
                .Include(o => o.OrderItems)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();
            return Ok(orders);
        }

        [Authorize]
        [HttpGet("mine")]
        public async Task<IActionResult> GetMyOrders()
        {
            var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(idClaim, out var userId))
                return Unauthorized();
            var orders = await _context.Orders
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.Product)
                .Where(o => o.UserId == userId)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();
            return Ok(orders);
        }

        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var order = await _context.Orders
                .Include(o => o.OrderItems)
                .FirstOrDefaultAsync(o => o.Id == id);
            if (order == null) return NotFound();
            return Ok(order);
        }

        // POST: api/orders  — công khai, khách hàng đặt hàng từ FE Client không cần đăng nhập.
        //
        // GIÁ DO SERVER TÍNH: Client chỉ gửi mã sản phẩm / mã combo + số lượng. Giá lấy từ
        // database (giá hiện tại + giảm giá sản phẩm; combo thì thêm % giảm của combo). Nhờ vậy
        // combo luôn được giảm đúng, và khách không thể tự sửa giá đơn hàng.
        //
        // COMBO được tách thành từng dòng OrderItem (mỗi sản phẩm 1 dòng, giá đã giảm, tên có
        // tiền tố "[Combo ...]") — nên trừ kho, hoàn kho khi huỷ đơn, hiển thị lịch sử đơn hàng
        // đều dùng lại nguyên logic cũ, không cần đổi bảng/migration.
        //
        // Kiểm tra ĐỦ HÀNG và TRỪ TỒN KHO ngay khi đơn được tạo (trạng thái "Chờ xác nhận").
        // Quy tắc trừ kho:
        //   - Sản phẩm KHÔNG biến thể: trừ thẳng Product.Stock.
        //   - Sản phẩm CÓ biến thể: trừ ProductVariant.Stock CỦA ĐÚNG biến thể đã chọn,
        //     ĐỒNG THỜI trừ luôn Product.Stock (tồn kho tổng).
        //   - Sản phẩm trong combo: trừ Product.Stock (số lượng x số combo).
        // Nếu bất kỳ dòng nào không đủ hàng, HUỶ TOÀN BỘ giao dịch (không lưu đơn, không
        // trừ kho dòng nào cả).
        [HttpPost]
        public async Task<IActionResult> Create(CreateOrderDto dto)
        {
            if (dto.OrderItems.Count == 0 && dto.Bundles.Count == 0)
                return BadRequest(new { message = "Giỏ hàng trống." });

            var order = new Order
            {
                UserId = dto.UserId,
                CustomerName = dto.CustomerName,
                Phone = dto.Phone,
                Address = dto.Address,
                CreatedAt = DateTime.Now,
                Status = "Chờ xác nhận",
                // Chỉ chấp nhận đúng 2 giá trị hợp lệ — thiếu/khác coi như COD.
                PaymentMethod = dto.PaymentMethod == "QR" ? "QR" : "COD",
                // LUÔN bắt đầu "Chưa thanh toán" kể cả QR — Admin tự xác nhận thủ công sau.
                PaymentStatus = "Chưa thanh toán",
            };

            if (order.UserId.HasValue)
            {
                var userExists = await _context.Users.AnyAsync(u => u.Id == order.UserId.Value);
                if (!userExists)
                    return BadRequest(new { message = "Tài khoản không hợp lệ." });
            }

            // ── Sản phẩm lẻ ──
            foreach (var line in dto.OrderItems)
            {
                if (line.Quantity < 1)
                    return BadRequest(new { message = "Số lượng sản phẩm không hợp lệ." });

                var product = await _context.Products.FindAsync(line.ProductId);
                if (product == null || !product.IsActive)
                    return BadRequest(new { message = $"Sản phẩm #{line.ProductId} không tồn tại hoặc đã ngừng bán." });

                if (line.VariantId.HasValue)
                {
                    var variant = await _context.ProductVariants.FindAsync(line.VariantId.Value);
                    if (variant == null || variant.ProductId != product.Id)
                        return BadRequest(new { message = $"Biến thể của sản phẩm '{product.Name}' không hợp lệ." });

                    if (variant.Stock < line.Quantity)
                        return BadRequest(new
                        {
                            message = $"'{product.Name} - {variant.Name}' không đủ hàng (còn {variant.Stock}, cần {line.Quantity})."
                        });

                    variant.Stock -= line.Quantity;
                    product.Stock -= line.Quantity;

                    order.OrderItems.Add(new OrderItem
                    {
                        ProductId = product.Id,
                        ProductName = $"{product.Name} - {variant.Name}",
                        ProductImage = product.Image,
                        Quantity = line.Quantity,
                        // Giá do SERVER tự tính: giá biến thể trong database trừ giảm giá của sản phẩm (giá Client gửi lên bị bỏ qua).
                        Price = PricingHelper.VariantSaleUnitPrice(product, variant),
                        VariantId = variant.Id,
                        VariantName = variant.Name,
                    });
                }
                else
                {
                    if (product.Stock < line.Quantity)
                        return BadRequest(new
                        {
                            message = $"'{product.Name}' không đủ hàng (còn {product.Stock}, cần {line.Quantity})."
                        });

                    product.Stock -= line.Quantity;

                    order.OrderItems.Add(new OrderItem
                    {
                        ProductId = product.Id,
                        ProductName = product.Name,
                        ProductImage = product.Image,
                        Quantity = line.Quantity,
                        Price = PricingHelper.SaleUnitPrice(product),
                    });
                }
            }

            // Tổng tiền giảm nhờ combo — lưu vào Order.BundleDiscount để hiện chi tiết cho khách.
            decimal bundleDiscount = 0m;

            // ── Combo ──
            foreach (var bl in dto.Bundles)
            {
                if (bl.Quantity < 1)
                    return BadRequest(new { message = "Số lượng combo không hợp lệ." });

                var bundle = await _context.Bundles.FindAsync(bl.BundleId);
                if (bundle == null || !bundle.IsActive)
                    return BadRequest(new { message = $"Combo #{bl.BundleId} không tồn tại hoặc đã ngừng bán." });

                var bundleItems = await _context.BundleItems
                    .Where(bi => bi.BundleId == bundle.Id)
                    .ToListAsync();
                if (bundleItems.Count == 0)
                    return BadRequest(new { message = $"Combo '{bundle.Name}' hiện không có sản phẩm nào." });

                // Nhãn ghi vào tên từng dòng đơn hàng. Không thêm chữ "Combo" nếu tên combo đã bắt đầu
                // bằng "Combo"; có giảm giá thì ghi luôn % để khách thấy trong lịch sử đơn hàng.
                var bundleLabel = bundle.Name.StartsWith("Combo", StringComparison.OrdinalIgnoreCase)
                    ? bundle.Name
                    : $"Combo {bundle.Name}";
                if (bundle.DiscountPercent > 0)
                    bundleLabel += $", giảm {bundle.DiscountPercent}%";

                foreach (var bi in bundleItems)
                {
                    var product = await _context.Products.FindAsync(bi.ProductId);
                    if (product == null || !product.IsActive)
                        return BadRequest(new { message = $"Combo '{bundle.Name}' có sản phẩm đã ngừng bán, tạm thời không đặt được." });

                    var need = bi.Quantity * bl.Quantity;

                    // Sản phẩm CÓ biến thể thì combo phải trỏ tới 1 biến thể cụ thể: tính giá, trừ kho và ghi đơn theo biến thể đó.
                    ProductVariant? variant = null;
                    var productHasVariants = await _context.ProductVariants.AnyAsync(v => v.ProductId == product.Id);
                    if (productHasVariants)
                    {
                        if (!bi.VariantId.HasValue)
                            return BadRequest(new { message = $"Combo '{bundle.Name}': '{product.Name}' chưa được chọn biến thể, tạm thời không đặt được. Vui lòng liên hệ cửa hàng." });
                        variant = await _context.ProductVariants.FindAsync(bi.VariantId.Value);
                        if (variant == null || variant.ProductId != product.Id)
                            return BadRequest(new { message = $"Combo '{bundle.Name}': biến thể của '{product.Name}' không còn tồn tại, tạm thời không đặt được." });
                        if (variant.Stock < need)
                            return BadRequest(new
                            {
                                message = $"Combo '{bundle.Name}': '{product.Name} - {variant.Name}' không đủ hàng (còn {variant.Stock}, cần {need})."
                            });
                    }

                    if (product.Stock < need)
                        return BadRequest(new
                        {
                            message = $"Combo '{bundle.Name}': '{product.Name}' không đủ hàng (còn {product.Stock}, cần {need})."
                        });

                    product.Stock -= need;
                    if (variant != null) variant.Stock -= need;

                    var comboUnit = variant != null
                        ? PricingHelper.ComboUnitPrice(product, variant, bundle.DiscountPercent)
                        : PricingHelper.ComboUnitPrice(product, bundle.DiscountPercent);
                    var saleUnit = variant != null
                        ? PricingHelper.VariantSaleUnitPrice(product, variant)
                        : PricingHelper.SaleUnitPrice(product);
                    bundleDiscount += (saleUnit - comboUnit) * need;

                    order.OrderItems.Add(new OrderItem
                    {
                        ProductId = product.Id,
                        ProductName = variant != null
                            ? $"[{bundleLabel}] {product.Name} - {variant.Name}"
                            : $"[{bundleLabel}] {product.Name}",
                        ProductImage = product.Image,
                        Quantity = need,
                        Price = comboUnit,
                        VariantId = variant?.Id,
                        VariantName = variant?.Name,
                    });
                }
            }

            var subtotal = order.OrderItems.Sum(i => i.Price * i.Quantity);
            decimal total = subtotal;
            string? couponNote = null;

            // Transaction bao từ lúc giành lượt mã giảm giá đến lúc lưu đơn: dòng Coupon bị khoá cho tới khi
            // Commit, nên 2 đơn đặt cùng lúc với cùng 1 mã sẽ phải xếp hàng, không thể cùng lọt qua.
            // Rời hàm bằng lỗi/exception thì transaction tự Rollback (không mất lượt mã oan).
            await using var tx = await _context.Database.BeginTransactionAsync();

            if (!string.IsNullOrWhiteSpace(dto.CouponCode))
            {
                var code = dto.CouponCode.Trim().ToUpper();
                var matching = await _context.Coupons.Where(c => c.Code == code).ToListAsync();
                var (valid, reason, coupon) = CouponsController.CheckCoupon(matching, subtotal);

                if (valid && coupon != null)
                {
                    // GIÀNH 1 LƯỢT bằng 1 câu UPDATE nguyên tử: chỉ tăng UsedCount nếu còn lượt (ngay trong
                    // database, không đọc-rồi-ghi). Trả về 0 dòng = vừa có người khác lấy hết lượt cuối.
                    var claimed = await _context.Coupons
                        .Where(c => c.Id == coupon.Id && (c.MaxUsage == null || c.UsedCount < c.MaxUsage))
                        .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedCount, c => c.UsedCount + 1));

                    if (claimed == 0)
                    {
                        valid = false;
                        reason = "Mã giảm giá đã hết lượt sử dụng.";
                    }
                    // Kiểm tra "mỗi khách 1 lần" SAU khi đã giữ khoá dòng mã: đơn của chính khách này gửi
                    // song song (bấm đúp, 2 tab) đã Commit xong nên chắc chắn được nhìn thấy ở đây.
                    else if (await CouponsController.HasCustomerUsedAsync(_context, coupon.Code, order.UserId, order.Phone))
                    {
                        await _context.Coupons
                            .Where(c => c.Id == coupon.Id && c.UsedCount > 0)
                            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedCount, c => c.UsedCount - 1));
                        valid = false;
                        reason = CouponsController.AlreadyUsedMessage;
                    }
                }

                if (valid && coupon != null)
                {
                    var discount = Math.Round(subtotal * coupon.DiscountPercent / 100, 0);
                    total = subtotal - discount;
                    order.CouponCode = coupon.Code;
                    order.CouponDiscount = discount;
                    // UsedCount đã được tăng ở câu UPDATE nguyên tử phía trên — KHÔNG tăng lần nữa ở đây.
                }
                else
                {
                    order.CouponCode = null;
                    couponNote = reason;
                }
            }

            order.BundleDiscount = bundleDiscount;
            order.Total = total;
            _context.Orders.Add(order);
            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            try
            {
                var email = await GetUserEmailAsync(order.UserId);
                if (email != null)
                {
                    await _emailService.SendAsync(
                        email,
                        $"Xác nhận đơn hàng #{order.Id} — LuxWood",
                        EmailTemplates.OrderConfirmation(order));
                }
            }
            catch
            {
            }

            // confirmCode: mã để KHÁCH vừa đặt đơn này xác nhận "Tôi đã chuyển khoản" (đơn QR).
            // Luôn trả về dạng { order, couponNote, confirmCode } — Client đọc order qua res.data.order.
            var confirmCode = _tokenService.CreateOrderConfirmCode(order.Id);
            return CreatedAtAction(nameof(GetById), new { id = order.Id }, new { order, couponNote, confirmCode });
        }

        private static readonly string[] OrderStatuses =
            { "Chờ xác nhận", "Đang xử lý", "Đang giao", "Hoàn thành", "Huỷ" };

        // Mở lại đơn ĐÃ HUỶ (huỷ → trạng thái khác): đơn lại giữ hàng nên phải TRỪ KHO LẠI, ngược với lúc huỷ.
        // Kiểm tra đủ hàng cho TẤT CẢ dòng trước, rồi mới trừ — thiếu hàng ở dòng nào thì không trừ dòng nào
        // và trả thông báo lỗi (null = đã giữ hàng thành công). Cộng dồn theo sản phẩm / biến thể phòng khi
        // 1 sản phẩm xuất hiện ở nhiều dòng (VD: vừa mua lẻ vừa nằm trong combo).
        private async Task<string?> ReserveStockForReopenAsync(Order order)
        {
            var needByProduct = order.OrderItems
                .GroupBy(i => i.ProductId)
                .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
            var needByVariant = order.OrderItems
                .Where(i => i.VariantId.HasValue)
                .GroupBy(i => i.VariantId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

            var products = new Dictionary<int, Product>();
            foreach (var (productId, need) in needByProduct)
            {
                var product = await _context.Products.FindAsync(productId);
                if (product == null)
                    return "Đơn có sản phẩm đã bị xoá khỏi hệ thống nên không thể mở lại.";
                if (product.Stock < need)
                    return $"'{product.Name}' không đủ hàng để mở lại đơn (còn {product.Stock}, cần {need}).";
                products[productId] = product;
            }

            // Biến thể không còn tồn tại (đơn cũ tạo trước khi sửa sản phẩm giữ nguyên ID biến thể) thì BỎ QUA
            // biến thể đó, chỉ trừ kho tổng của sản phẩm — giống cách lúc huỷ đơn vẫn hoàn kho sản phẩm khi
            // không tìm thấy biến thể. Nhờ vậy đơn cũ vẫn mở lại được.
            var variants = new Dictionary<int, ProductVariant>();
            foreach (var (variantId, need) in needByVariant)
            {
                var variant = await _context.ProductVariants.FindAsync(variantId);
                if (variant == null) continue;
                if (variant.Stock < need)
                    return $"Biến thể '{variant.Name}' không đủ hàng để mở lại đơn (còn {variant.Stock}, cần {need}).";
                variants[variantId] = variant;
            }

            foreach (var (productId, need) in needByProduct) products[productId].Stock -= need;
            foreach (var (variantId, need) in needByVariant)
                if (variants.TryGetValue(variantId, out var v)) v.Stock -= need;
            return null;
        }

        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpPatch("{id}/status")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] string status)
        {
            if (!OrderStatuses.Contains(status))
                return BadRequest(new { message = "Trạng thái đơn hàng không hợp lệ." });

            var order = await _context.Orders
                .Include(o => o.OrderItems)
                .FirstOrDefaultAsync(o => o.Id == id);
            if (order == null) return NotFound();

            if (order.Status == "Huỷ" && status != "Huỷ")
            {
                var reopenError = await ReserveStockForReopenAsync(order);
                if (reopenError != null)
                    return BadRequest(new { message = reopenError });

                // Mở lại đơn thì tính lại 1 lượt dùng mã giảm giá (đã được trả lại lúc huỷ).
                if (!string.IsNullOrEmpty(order.CouponCode))
                {
                    var coupon = await _context.Coupons.FirstOrDefaultAsync(c => c.Code == order.CouponCode);
                    if (coupon != null) coupon.UsedCount += 1;
                }
            }

            // Nếu đơn bị chuyển sang "Huỷ", HOÀN LẠI tồn kho đã trừ lúc tạo đơn — tránh
            // tồn kho bị "mất" oan khi khách/Admin huỷ đơn. Chỉ hoàn 1 lần — nếu đơn ĐÃ
            // huỷ từ trước rồi lại bấm huỷ lần nữa thì không hoàn thêm lần 2.
            if (status == "Huỷ" && order.Status != "Huỷ")
            {
                // Trả lại 1 lượt dùng mã giảm giá để mã giới hạn lượt không bị "mất" oan vì đơn đã huỷ.
                if (!string.IsNullOrEmpty(order.CouponCode))
                {
                    var coupon = await _context.Coupons.FirstOrDefaultAsync(c => c.Code == order.CouponCode);
                    if (coupon != null && coupon.UsedCount > 0) coupon.UsedCount -= 1;
                }

                foreach (var item in order.OrderItems)
                {
                    var product = await _context.Products.FindAsync(item.ProductId);
                    if (product == null) continue;

                    if (item.VariantId.HasValue)
                    {
                        var variant = await _context.ProductVariants.FindAsync(item.VariantId.Value);
                        if (variant != null) variant.Stock += item.Quantity;
                        product.Stock += item.Quantity;
                    }
                    else
                    {
                        product.Stock += item.Quantity;
                    }
                }
            }

            order.Status = status;
            await _context.SaveChangesAsync();

            try
            {
                var email = await GetUserEmailAsync(order.UserId);
                if (email != null)
                {
                    await _emailService.SendAsync(
                        email,
                        $"Đơn hàng #{order.Id} — {status}",
                        EmailTemplates.OrderStatusUpdate(order, status));
                }
            }
            catch
            {
            }

            return NoContent();
        }

        // POST: api/orders/5/confirm-transfer — công khai nhưng PHẢI kèm mã xác nhận (xem TokenService).
        // Khách bấm "Tôi đã chuyển khoản" ở bước QR → đơn QR được ghi nhận "Đã thanh toán" ngay, Admin
        // không cần bấm xác nhận nữa. Trạng thái đơn (Chờ xác nhận/Đang xử lý/Đang giao...) KHÔNG đổi:
        // tiền về chưa có nghĩa là đã giao xong.
        // LƯU Ý: đây là khách TỰ báo đã chuyển — hệ thống không tự kiểm tra được ngân hàng (không tích hợp
        // cổng thanh toán/webhook), chủ shop nên vẫn đối chiếu sao kê trước khi giao hàng.
        // Trả mã 400 (không phải 401) khi sai mã, để Client không tưởng là hết phiên đăng nhập.
        [HttpPost("{id}/confirm-transfer")]
        public async Task<IActionResult> ConfirmTransfer(int id, ConfirmTransferDto dto)
        {
            if (!_tokenService.VerifyOrderConfirmCode(id, dto.Code ?? ""))
                return BadRequest(new { message = "Mã xác nhận không hợp lệ." });

            var order = await _context.Orders.FindAsync(id);
            if (order == null) return NotFound();

            if (order.PaymentMethod != "QR")
                return BadRequest(new { message = "Đơn hàng này không thanh toán bằng chuyển khoản QR." });
            if (order.Status == "Huỷ")
                return BadRequest(new { message = "Đơn hàng đã bị huỷ." });

            if (order.PaymentStatus != "Đã thanh toán")
            {
                order.PaymentStatus = "Đã thanh toán";
                await _context.SaveChangesAsync();
            }
            return NoContent();
        }

        // Admin/Nhân viên xác nhận ĐÃ NHẬN TIỀN — dùng cho đơn COD (thu tiền tận tay khi giao hàng).
        // (Đơn QR do khách tự xác nhận ở bước thanh toán, xem ConfirmTransfer.)
        // Khi xác nhận "Đã thanh toán" mà đơn chưa bị huỷ/chưa hoàn thành → TỰ CHUYỂN trạng thái đơn
        // sang "Hoàn thành" và gửi email báo khách. Đặt lại về "Chưa thanh toán" thì không đổi trạng thái đơn.
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpPatch("{id}/payment-status")]
        public async Task<IActionResult> UpdatePaymentStatus(int id, [FromBody] string paymentStatus)
        {
            if (paymentStatus != "Đã thanh toán" && paymentStatus != "Chưa thanh toán")
                return BadRequest(new { message = "Trạng thái thanh toán không hợp lệ." });

            var order = await _context.Orders.FindAsync(id);
            if (order == null) return NotFound();

            order.PaymentStatus = paymentStatus;

            var completed = false;
            if (paymentStatus == "Đã thanh toán" && order.Status != "Huỷ" && order.Status != "Hoàn thành")
            {
                order.Status = "Hoàn thành";
                completed = true;
            }

            await _context.SaveChangesAsync();

            if (completed)
            {
                try
                {
                    var email = await GetUserEmailAsync(order.UserId);
                    if (email != null)
                    {
                        await _emailService.SendAsync(
                            email,
                            $"Đơn hàng #{order.Id} — Hoàn thành",
                            EmailTemplates.OrderStatusUpdate(order, "Hoàn thành"));
                    }
                }
                catch
                {
                }
            }

            return NoContent();
        }

        // Theo ma trận phân quyền vòng 2: Nhân viên được toàn quyền với Đơn hàng.
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var order = await _context.Orders
                .Include(o => o.OrderItems)
                .FirstOrDefaultAsync(o => o.Id == id);
            if (order == null) return NotFound();
            _context.Orders.Remove(order);
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}
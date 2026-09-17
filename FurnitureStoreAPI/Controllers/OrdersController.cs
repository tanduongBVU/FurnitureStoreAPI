using FurnitureStoreAPI.Data;
using FurnitureStoreAPI.Models;
using FurnitureStoreAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FurnitureStoreAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IEmailService _emailService;
        public OrdersController(AppDbContext context, IEmailService emailService)
        {
            _context = context;
            _emailService = emailService;
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
        // MỚI: kiểm tra ĐỦ HÀNG và TRỪ TỒN KHO ngay khi đơn được tạo (trạng thái "Chờ xác
        // nhận") — trước đây tồn kho hoàn toàn không tự động thay đổi khi có đơn, Admin
        // phải tự tay sửa. Quy tắc trừ kho:
        //   - Sản phẩm KHÔNG biến thể: trừ thẳng Product.Stock.
        //   - Sản phẩm CÓ biến thể: trừ ProductVariant.Stock CỦA ĐÚNG biến thể đã chọn,
        //     ĐỒNG THỜI trừ luôn Product.Stock (tồn kho tổng) — vì tồn kho tổng được coi
        //     là bao gồm/đại diện tổng các biến thể, phải giữ đồng bộ.
        // Nếu bất kỳ dòng nào không đủ hàng, HUỶ TOÀN BỘ giao dịch (không lưu đơn, không
        // trừ kho dòng nào cả) — tránh tình trạng đơn tạo được 1 phần, tồn kho sai lệch.
        [HttpPost]
        public async Task<IActionResult> Create(Order order)
        {
            order.CreatedAt = DateTime.Now;
            order.Status = "Chờ xác nhận";

            if (order.UserId.HasValue)
            {
                var userExists = await _context.Users.AnyAsync(u => u.Id == order.UserId.Value);
                if (!userExists)
                    return BadRequest(new { message = "Tài khoản không hợp lệ." });
            }

            foreach (var item in order.OrderItems)
            {
                var product = await _context.Products.FindAsync(item.ProductId);
                if (product == null)
                    return BadRequest(new { message = $"Sản phẩm #{item.ProductId} không tồn tại." });

                if (item.VariantId.HasValue)
                {
                    var variant = await _context.ProductVariants.FindAsync(item.VariantId.Value);
                    if (variant == null || variant.ProductId != product.Id)
                        return BadRequest(new { message = $"Biến thể của sản phẩm '{product.Name}' không hợp lệ." });

                    if (variant.Stock < item.Quantity)
                        return BadRequest(new
                        {
                            message = $"'{product.Name} - {item.VariantName}' không đủ hàng (còn {variant.Stock}, cần {item.Quantity})."
                        });

                    variant.Stock -= item.Quantity;
                    product.Stock -= item.Quantity;
                }
                else
                {
                    if (product.Stock < item.Quantity)
                        return BadRequest(new
                        {
                            message = $"'{product.Name}' không đủ hàng (còn {product.Stock}, cần {item.Quantity})."
                        });

                    product.Stock -= item.Quantity;
                }
            }

            var subtotal = order.OrderItems.Sum(i => i.Price * i.Quantity);
            decimal total = subtotal;
            string? couponNote = null;

            if (!string.IsNullOrWhiteSpace(order.CouponCode))
            {
                var code = order.CouponCode.Trim().ToUpper();
                var matching = await _context.Coupons.Where(c => c.Code == code).ToListAsync();
                var (valid, reason, coupon) = CouponsController.CheckCoupon(matching, subtotal);

                if (valid && coupon != null)
                {
                    var discount = Math.Round(subtotal * coupon.DiscountPercent / 100, 0);
                    total = subtotal - discount;
                    order.CouponCode = coupon.Code;

                    coupon.UsedCount += 1;
                }
                else
                {
                    order.CouponCode = null;
                    couponNote = reason;
                }
            }

            order.Total = total;
            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

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

            if (couponNote != null)
                return Ok(new { order, couponNote });

            return CreatedAtAction(nameof(GetById), new { id = order.Id }, order);
        }

        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpPatch("{id}/status")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] string status)
        {
            var order = await _context.Orders
                .Include(o => o.OrderItems)
                .FirstOrDefaultAsync(o => o.Id == id);
            if (order == null) return NotFound();

            // MỚI: nếu đơn bị chuyển sang "Huỷ", HOÀN LẠI tồn kho đã trừ lúc tạo đơn —
            // tránh tồn kho bị "mất" oan khi khách/Admin huỷ đơn. Chỉ hoàn 1 lần — nếu
            // đơn ĐÃ huỷ từ trước rồi lại bấm huỷ lần nữa thì không hoàn thêm lần 2.
            if (status == "Huỷ" && order.Status != "Huỷ")
            {
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

        [Authorize(Roles = "Admin")]
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
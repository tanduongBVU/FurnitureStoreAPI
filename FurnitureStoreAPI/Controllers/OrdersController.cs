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

        // Chỉ gửi email cho khách ĐÃ ĐĂNG NHẬP — Order chưa có field Email riêng, nên lấy
        // trực tiếp từ bảng Users. UserId giờ là int? (đúng bản chất: null = khách vãng
        // lai), nên chỉ cần kiểm tra HasValue là đủ, không cần so sánh với 0 như trước nữa.
        private async Task<string?> GetUserEmailAsync(int? userId)
        {
            if (!userId.HasValue) return null;
            var user = await _context.Users.FindAsync(userId.Value);
            return user?.Email;
        }

        // GET: api/orders  — chỉ Admin & Nhân viên xem được danh sách đơn (trang quản trị)
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

        // GET: api/orders/mine — khách hàng đã đăng nhập xem đơn hàng CỦA CHÍNH MÌNH
        // Lấy UserId từ claim NameIdentifier trong token (TokenService đã gắn sẵn khi login/register).
        // Đặt route "mine" trước "{id}" để tránh ASP.NET hiểu nhầm "mine" là 1 tham số id.
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

        // GET: api/orders/5  — chỉ Admin & Nhân viên
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
        // UserId = null hợp lệ cho khách vãng lai (xem Order.cs). Nếu order.CouponCode có
        // giá trị: server TỰ kiểm tra lại mã và TỰ tính giảm giá, KHÔNG tin bất kỳ số tiền
        // đã giảm nào gửi từ Client — tránh khách sửa request để giả mạo giảm giá. Nếu mã
        // không còn hợp lệ tại thời điểm đặt hàng, đơn hàng vẫn được tạo nhưng KHÔNG áp dụng
        // giảm giá, và trả về ghi chú để FE báo cho khách biết.
        [HttpPost]
        public async Task<IActionResult> Create(Order order)
        {
            order.CreatedAt = DateTime.Now;
            order.Status = "Chờ xác nhận";

            // Nếu UserId được gửi lên nhưng không trỏ tới User thật nào (VD: 0, hoặc ID đã
            // bị xoá) — chặn sớm ở đây thay vì để FK constraint ném lỗi 500 khó hiểu về sau.
            if (order.UserId.HasValue)
            {
                var userExists = await _context.Users.AnyAsync(u => u.Id == order.UserId.Value);
                if (!userExists)
                    return BadRequest(new { message = "Tài khoản không hợp lệ." });
            }

            // Tổng tiền gốc luôn tính lại từ OrderItems thật trong DB, không tin Price gửi
            // từ Client, để tránh khách sửa giá sản phẩm qua request giả mạo.
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
                    // Mã không còn hợp lệ ngay lúc chốt đơn — vẫn tạo đơn nhưng bỏ mã,
                    // không chặn khách mất luôn cả đơn hàng chỉ vì mã bị lỗi.
                    order.CouponCode = null;
                    couponNote = reason;
                }
            }

            order.Total = total;
            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            // Gửi email xác nhận — CHỈ khi khách đã đăng nhập (xem GetUserEmailAsync).
            // Bọc try/catch riêng để chắc chắn lỗi gửi email không ảnh hưởng response.
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
                // Không để lỗi gửi email ảnh hưởng tới việc đơn hàng đã tạo thành công.
            }

            if (couponNote != null)
                return Ok(new { order, couponNote });

            return CreatedAtAction(nameof(GetById), new { id = order.Id }, order);
        }

        // PATCH: api/orders/5/status — Admin & Nhân viên đều được cập nhật trạng thái
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpPatch("{id}/status")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] string status)
        {
            var order = await _context.Orders
                .Include(o => o.OrderItems)
                .FirstOrDefaultAsync(o => o.Id == id);
            if (order == null) return NotFound();

            order.Status = status;
            await _context.SaveChangesAsync();

            // Gửi email thông báo đổi trạng thái — CHỈ khi khách đã đăng nhập, giống Create().
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
                // Không để lỗi gửi email ảnh hưởng tới việc đổi trạng thái đã lưu thành công.
            }

            return NoContent();
        }

        // DELETE: api/orders/5  — CHỈ Admin được xoá đơn hàng
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
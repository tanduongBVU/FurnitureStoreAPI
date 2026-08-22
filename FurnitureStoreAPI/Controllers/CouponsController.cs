using FurnitureStoreAPI.Data;
using FurnitureStoreAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FurnitureStoreAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CouponsController : ControllerBase
    {
        private readonly AppDbContext _context;
        public CouponsController(AppDbContext context) { _context = context; }

        // Kiểm tra 1 mã có hợp lệ để áp dụng cho đơn hàng giá trị orderValue hay không.
        // Dùng chung cho cả Validate (Checkout gọi trước) và OrdersController (chốt đơn thật) —
        // để 2 nơi luôn đồng nhất điều kiện, tránh trường hợp validate báo hợp lệ nhưng lúc
        // đặt hàng thật lại bị từ chối vì lệch logic.
        public static (bool valid, string reason, Coupon? coupon) CheckCoupon(
            List<Coupon> allMatching, decimal orderValue)
        {
            var coupon = allMatching.FirstOrDefault();
            if (coupon == null) return (false, "Mã giảm giá không tồn tại.", null);
            if (!coupon.IsActive) return (false, "Mã giảm giá này đã ngừng áp dụng.", null);
            if (coupon.ExpiryDate.HasValue && coupon.ExpiryDate.Value < DateTime.Now)
                return (false, "Mã giảm giá đã hết hạn.", null);
            if (coupon.MaxUsage.HasValue && coupon.UsedCount >= coupon.MaxUsage.Value)
                return (false, "Mã giảm giá đã hết lượt sử dụng.", null);
            if (coupon.MinOrderValue.HasValue && orderValue < coupon.MinOrderValue.Value)
                return (false, $"Đơn hàng cần tối thiểu {coupon.MinOrderValue.Value:N0}₫ để áp dụng mã này.", null);
            return (true, "", coupon);
        }

        // GET: api/coupons — Admin & Nhân viên xem danh sách quản lý
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var coupons = await _context.Coupons
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();
            return Ok(coupons);
        }

        // POST: api/coupons/validate — công khai, Checkout gọi để kiểm tra mã TRƯỚC khi đặt hàng
        // (chỉ để hiện UI, không tính là đã "dùng" mã — UsedCount chỉ tăng khi đơn hàng thật
        // được tạo thành công ở OrdersController, tránh mã bị đếm hao vì khách gõ thử rồi bỏ).
        [HttpPost("validate")]
        public async Task<IActionResult> Validate([FromBody] ValidateCouponDto dto)
        {
            var code = (dto.Code ?? "").Trim().ToUpper();
            var matching = await _context.Coupons.Where(c => c.Code == code).ToListAsync();
            var (valid, reason, coupon) = CheckCoupon(matching, dto.OrderValue);

            if (!valid) return BadRequest(new { message = reason });

            return Ok(new
            {
                code = coupon!.Code,
                discountPercent = coupon.DiscountPercent,
                discountAmount = Math.Round(dto.OrderValue * coupon.DiscountPercent / 100, 0),
            });
        }

        // POST: api/coupons — CHỈ Admin được tạo mã mới
        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> Create(Coupon coupon)
        {
            var code = coupon.Code.Trim().ToUpper();
            if (string.IsNullOrWhiteSpace(code))
                return BadRequest(new { message = "Mã giảm giá không được để trống." });
            if (coupon.DiscountPercent < 1 || coupon.DiscountPercent > 100)
                return BadRequest(new { message = "Phần trăm giảm giá phải từ 1 đến 100." });

            var exists = await _context.Coupons.AnyAsync(c => c.Code == code);
            if (exists) return BadRequest(new { message = "Mã giảm giá này đã tồn tại." });

            coupon.Code = code;
            coupon.UsedCount = 0;
            coupon.CreatedAt = DateTime.Now;
            _context.Coupons.Add(coupon);
            await _context.SaveChangesAsync();
            return Ok(coupon);
        }

        // PUT: api/coupons/5 — CHỈ Admin được sửa mã
        [Authorize(Roles = "Admin")]
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, Coupon coupon)
        {
            var existing = await _context.Coupons.FindAsync(id);
            if (existing == null) return NotFound();

            var code = coupon.Code.Trim().ToUpper();
            var duplicated = await _context.Coupons.AnyAsync(c => c.Code == code && c.Id != id);
            if (duplicated) return BadRequest(new { message = "Mã giảm giá này đã tồn tại." });

            existing.Code = code;
            existing.DiscountPercent = coupon.DiscountPercent;
            existing.MaxUsage = coupon.MaxUsage;
            existing.MinOrderValue = coupon.MinOrderValue;
            existing.ExpiryDate = coupon.ExpiryDate;
            existing.IsActive = coupon.IsActive;
            // KHÔNG cho sửa UsedCount từ FE — chỉ hệ thống tự tăng khi có đơn hàng thật dùng mã.

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // DELETE: api/coupons/5 — CHỈ Admin
        [Authorize(Roles = "Admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var coupon = await _context.Coupons.FindAsync(id);
            if (coupon == null) return NotFound();
            _context.Coupons.Remove(coupon);
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }

    public class ValidateCouponDto
    {
        public string Code { get; set; } = string.Empty;
        public decimal OrderValue { get; set; }
    }
}
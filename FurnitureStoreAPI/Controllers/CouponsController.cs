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

        // Mỗi khách chỉ được dùng 1 mã đúng 1 lần. Đơn đã Huỷ không tính (huỷ thì được dùng lại).
        //  - Khách ĐÃ ĐĂNG NHẬP: nhận diện theo TÀI KHOẢN (UserId). Nhiều tài khoản dùng chung 1 số điện thoại
        //    vẫn độc lập với nhau, không chặn nhầm nhau.
        //  - Khách KHÔNG đăng nhập: nhận diện theo SỐ ĐIỆN THOẠI (so với mọi đơn đã dùng mã).
        // Không cần cột/migration mới: tra thẳng từ Orders.CouponCode.
        public static async Task<bool> HasCustomerUsedAsync(AppDbContext ctx, string code, int? userId, string? phone)
        {
            var q = ctx.Orders.Where(o => o.CouponCode == code && o.Status != "Huỷ");

            if (userId.HasValue)
                return await q.AnyAsync(o => o.UserId == userId.Value);

            var digits = NormalizePhone(phone);
            if (digits.Length == 0) return false;

            var phones = await q.Select(o => o.Phone).ToListAsync();
            return phones.Any(p => NormalizePhone(p) == digits);
        }

        public const string AlreadyUsedMessage = "Bạn đã sử dụng mã giảm giá này rồi. Mỗi khách chỉ được dùng 1 lần.";

        // Chuẩn hoá SĐT để "0912 345 678", "0912.345.678", "+84912345678", "84912345678" đều coi là cùng 1 số.
        private static string NormalizePhone(string? phone)
        {
            var d = new string((phone ?? "").Where(char.IsDigit).ToArray());
            if (d.StartsWith("84") && d.Length >= 11) d = "0" + d.Substring(2);
            return d;
        }

        // GET: api/coupons — Admin & Nhân viên xem danh sách quản lý mã giảm giá.
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

            if (await HasCustomerUsedAsync(_context, coupon!.Code, dto.UserId, dto.Phone))
                return BadRequest(new { message = AlreadyUsedMessage });

            return Ok(new
            {
                code = coupon!.Code,
                discountPercent = coupon.DiscountPercent,
                discountAmount = Math.Round(dto.OrderValue * coupon.DiscountPercent / 100, 0),
            });
        }

        // POST: api/coupons — Admin & Nhân viên được tạo mã mới
        [Authorize(Roles = "Admin,Nhân viên")]
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

        // PUT: api/coupons/5 — Admin & Nhân viên được sửa mã
        [Authorize(Roles = "Admin,Nhân viên")]
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

        // DELETE: api/coupons/5 — Admin & Nhân viên
        [Authorize(Roles = "Admin,Nhân viên")]
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
        public int? UserId { get; set; }
        public string? Phone { get; set; }
    }
}
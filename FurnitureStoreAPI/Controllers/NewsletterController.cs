using FurnitureStoreAPI.Data;
using FurnitureStoreAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace FurnitureStoreAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class NewsletterController : ControllerBase
    {
        private readonly AppDbContext _context;
        public NewsletterController(AppDbContext context) { _context = context; }

        // Regex kiểm tra định dạng email cơ bản — chỉ chặn rác rõ ràng (VD: "abc", "123"),
        // không cần validate quá chặt vì mục đích chính là newsletter, không phải xác thực tài khoản.
        private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

        // GET: api/newsletter/all — chỉ Admin & Nhân viên xem được danh sách đã đăng ký
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpGet("all")]
        public async Task<IActionResult> GetAll()
        {
            var subscribers = await _context.NewsletterSubscribers
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
            return Ok(subscribers);
        }

        // POST: api/newsletter — công khai, khách đăng ký nhận tin từ FE Client không cần đăng nhập.
        // Tự kiểm tra định dạng + trùng email ở server, không tin dữ liệu client gửi lên.
        [HttpPost]
        public async Task<IActionResult> Subscribe([FromBody] SubscribeDto dto)
        {
            var email = (dto.Email ?? "").Trim().ToLower();

            if (string.IsNullOrWhiteSpace(email) || !EmailRegex.IsMatch(email))
                return BadRequest(new { message = "Email không hợp lệ." });

            var exists = await _context.NewsletterSubscribers.AnyAsync(s => s.Email == email);
            if (exists)
                return BadRequest(new { message = "Email này đã đăng ký nhận tin rồi." });

            var subscriber = new NewsletterSubscriber
            {
                Email = email,
                CreatedAt = DateTime.Now,
            };
            _context.NewsletterSubscribers.Add(subscriber);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Đăng ký nhận tin thành công!" });
        }

        // DELETE: api/newsletter/5 — CHỈ Admin được xoá (bỏ theo dõi 1 email khỏi danh sách)
        [Authorize(Roles = "Admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var subscriber = await _context.NewsletterSubscribers.FindAsync(id);
            if (subscriber == null) return NotFound();
            _context.NewsletterSubscribers.Remove(subscriber);
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }

    public class SubscribeDto
    {
        public string Email { get; set; } = string.Empty;
    }
}
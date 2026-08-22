using FurnitureStoreAPI.Data;
using FurnitureStoreAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FurnitureStoreAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // Toàn bộ controller này chỉ dành cho Admin quản lý tài khoản nội bộ
    // (Admin / Nhân viên). Khách hàng tự đăng ký phải dùng POST /api/Auth/register.
    [Authorize(Roles = "Admin")]
    public class UsersController : ControllerBase
    {
        private readonly AppDbContext _context;
        public UsersController(AppDbContext context) { _context = context; }

        // Lấy Id của chính Admin đang đăng nhập (từ token) — dùng để chặn tự khoá/tự xoá chính mình.
        private int? GetCurrentUserId()
        {
            var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(idClaim, out var id) ? id : null;
        }

        // GET: api/users
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var users = await _context.Users
                .Select(u => new {
                    u.Id,
                    u.Name,
                    u.Email,
                    u.Phone,
                    u.Role,
                    u.Status,
                    u.CreatedAt,
                    Orders = _context.Orders.Count(o => o.UserId == u.Id)
                })
                .ToListAsync();
            return Ok(users);
        }

        // GET: api/users/5
        // LƯU Ý: chỉ trả về các field cần cho form Sửa — KHÔNG trả PasswordHash
        // (dù đã hash, vẫn không nên đưa hash mật khẩu ra ngoài client nếu không cần thiết).
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var user = await _context.Users
                .Where(u => u.Id == id)
                .Select(u => new {
                    u.Id,
                    u.Name,
                    u.Email,
                    u.Phone,
                    u.Role,
                    u.Status,
                    u.CreatedAt,
                })
                .FirstOrDefaultAsync();
            if (user == null) return NotFound();
            return Ok(user);
        }

        // POST: api/users
        // Admin dùng để tạo tài khoản Admin/Nhân viên khác trong nội bộ.
        [HttpPost]
        public async Task<IActionResult> Create(User user)
        {
            if (await _context.Users.AnyAsync(u => u.Email == user.Email))
                return BadRequest(new { message = "Email đã tồn tại!" });

            // PasswordHash ở đây thực chất đang chứa mật khẩu THÔ do form gửi lên
            // (tên field giữ nguyên để khớp property của model User) — validate độ dài
            // trước khi hash, tránh tạo tài khoản với mật khẩu quá yếu/rỗng.
            if (string.IsNullOrWhiteSpace(user.PasswordHash) || user.PasswordHash.Length < 6)
                return BadRequest(new { message = "Mật khẩu phải có ít nhất 6 ký tự!" });

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(user.PasswordHash);
            user.CreatedAt = DateTime.Now;
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            return CreatedAtAction(nameof(GetById), new { id = user.Id }, user);
        }

        // PUT: api/users/5
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, User user)
        {
            if (id != user.Id) return BadRequest();
            var existing = await _context.Users.FindAsync(id);
            if (existing == null) return NotFound();
            existing.Name = user.Name;
            existing.Phone = user.Phone;
            existing.Role = user.Role;
            existing.Status = user.Status;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // PATCH: api/users/5/status  — khoá/mở khoá nhanh
        // Chặn: (1) tự khoá chính tài khoản đang đăng nhập, (2) khoá nốt Admin đang hoạt động cuối cùng.
        [HttpPatch("{id}/status")]
        public async Task<IActionResult> ToggleStatus(int id, [FromBody] string status)
        {
            if (status != "Hoạt động" && status != "Bị khoá")
                return BadRequest(new { message = "Trạng thái không hợp lệ!" });

            var currentUserId = GetCurrentUserId();
            if (status == "Bị khoá" && currentUserId == id)
                return BadRequest(new { message = "Không thể tự khoá chính tài khoản đang đăng nhập!" });

            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            if (status == "Bị khoá" && user.Role == "Admin")
            {
                var activeAdminCount = await _context.Users
                    .CountAsync(u => u.Role == "Admin" && u.Status == "Hoạt động");
                if (activeAdminCount <= 1)
                    return BadRequest(new { message = "Không thể khoá Admin đang hoạt động cuối cùng của hệ thống!" });
            }

            user.Status = status;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // PATCH: api/users/5/reset-password
        // Admin đặt lại mật khẩu mới cho 1 user khác (VD: khi họ quên mật khẩu).
        // KHÔNG dùng chung với PUT Update vì đây là hành động nhạy cảm, tách riêng cho rõ log/quyền.
        [HttpPatch("{id}/reset-password")]
        public async Task<IActionResult> ResetPassword(int id, [FromBody] string newPassword)
        {
            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
                return BadRequest(new { message = "Mật khẩu mới phải có ít nhất 6 ký tự!" });

            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // DELETE: api/users/5
        // Chặn: (1) tự xoá chính tài khoản đang đăng nhập, (2) xoá nốt Admin cuối cùng.
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == id)
                return BadRequest(new { message = "Không thể tự xoá chính tài khoản đang đăng nhập!" });

            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            if (user.Role == "Admin")
            {
                var adminCount = await _context.Users.CountAsync(u => u.Role == "Admin");
                if (adminCount <= 1)
                    return BadRequest(new { message = "Không thể xoá Admin cuối cùng của hệ thống!" });
            }

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}
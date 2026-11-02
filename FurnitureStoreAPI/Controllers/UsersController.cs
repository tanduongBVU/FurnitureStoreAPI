using FurnitureStoreAPI.Data;
using FurnitureStoreAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace FurnitureStoreAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // Toàn bộ controller này chỉ dành cho Admin quản lý tài khoản.
    //
    // QUY TẮC VỚI KHÁCH HÀNG:
    //   - Khách hàng TỰ ĐĂNG KÝ qua website (POST /api/Auth/register) và tự quản lý thông tin
    //     cá nhân của mình ở trang Tài khoản.
    //   - Admin KHÔNG được sửa thông tin khách hàng và KHÔNG được đặt lại mật khẩu của khách
    //     (nếu được, Admin có thể tự đăng nhập bằng tài khoản khách mà khách không hề biết).
    //     Admin chỉ được XEM và KHOÁ / MỞ KHOÁ. Khách tự đặt lại mật khẩu của mình.
    //   - Admin chỉ tạo/sửa tài khoản NỘI BỘ (Admin / Nhân viên).
    [Authorize(Roles = "Admin")]
    public class UsersController : ControllerBase
    {
        private const string ROLE_CUSTOMER = "Khách hàng";

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
        // LƯU Ý: chỉ trả về các field cần cho trang Xem/Sửa — KHÔNG trả PasswordHash
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
        // Admin dùng để tạo tài khoản NỘI BỘ (Admin / Nhân viên). Khách hàng phải tự đăng ký
        // trên website nên KHÔNG được tạo ở đây.
        [HttpPost]
        public async Task<IActionResult> Create(User user)
        {
            if (user.Role != "Admin" && user.Role != "Nhân viên")
                return BadRequest(new { message = "Chỉ được tạo tài khoản Nhân viên hoặc Admin. Khách hàng tự đăng ký trên website!" });

            if (string.IsNullOrWhiteSpace(user.Name) || user.Name.Trim().Length > 100)
                return BadRequest(new { message = "Họ tên không được để trống và tối đa 100 ký tự!" });
            try { _ = new System.Net.Mail.MailAddress((user.Email ?? "").Trim()); }
            catch { return BadRequest(new { message = "Email không hợp lệ!" }); }
            user.Name = user.Name.Trim();
            user.Email = user.Email.Trim();

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

        // PUT: api/users/5 — sửa thông tin tài khoản NỘI BỘ (Admin / Nhân viên).
        // Tài khoản KHÁCH HÀNG bị chặn hoàn toàn (403): thông tin khách do chính khách quản lý.
        // Phải chặn ở đây chứ không chỉ ẩn nút ở giao diện, vì vẫn có thể gọi thẳng API.
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, User user)
        {
            if (id != user.Id) return BadRequest();
            var existing = await _context.Users.FindAsync(id);
            if (existing == null) return NotFound();

            if (existing.Role == ROLE_CUSTOMER)
                return StatusCode(403, new
                {
                    message = "Admin không được sửa thông tin của khách hàng. Chỉ có thể xem, khoá/mở khoá hoặc đặt lại mật khẩu."
                });

            // ── Kiểm tra dữ liệu (không chỉ tin form phía giao diện) ──
            var name = (user.Name ?? "").Trim();
            if (name.Length == 0 || name.Length > 100)
                return BadRequest(new { message = "Họ tên không được để trống và tối đa 100 ký tự!" });

            var phone = (user.Phone ?? "").Trim();
            if (phone.Length > 0 && !Regex.IsMatch(phone, @"^[0-9+\s\-]{9,15}$"))
                return BadRequest(new { message = "Số điện thoại không hợp lệ!" });

            if (user.Role != "Admin" && user.Role != "Nhân viên")
                return BadRequest(new { message = "Vai trò chỉ có thể là Admin hoặc Nhân viên!" });
            if (user.Status != "Hoạt động" && user.Status != "Bị khoá")
                return BadRequest(new { message = "Trạng thái không hợp lệ!" });

            // ── Chặn các thao tác tự phá hệ thống — giống ToggleStatus / Delete, vì Update cũng đổi được
            // Role và Status nên không được để lọt qua đây ──
            var currentUserId = GetCurrentUserId();
            if (currentUserId == id)
            {
                if (user.Role != existing.Role)
                    return BadRequest(new { message = "Không thể tự đổi vai trò của chính tài khoản đang đăng nhập!" });
                if (user.Status == "Bị khoá")
                    return BadRequest(new { message = "Không thể tự khoá chính tài khoản đang đăng nhập!" });
            }

            // Admin không bị khoá được (xem ToggleStatus) — Update cũng đổi được Status nên chặn ở đây luôn.
            if (existing.Role == "Admin" && existing.Status != "Bị khoá" && user.Status == "Bị khoá")
                return BadRequest(new { message = "Không thể khoá tài khoản Admin!" });

            var losesAdmin = existing.Role == "Admin" && existing.Status == "Hoạt động"
                             && (user.Role != "Admin" || user.Status == "Bị khoá");
            if (losesAdmin)
            {
                var otherActiveAdmins = await _context.Users
                    .CountAsync(u => u.Role == "Admin" && u.Status == "Hoạt động" && u.Id != id);
                if (otherActiveAdmins == 0)
                    return BadRequest(new { message = "Không thể hạ quyền hoặc khoá Admin đang hoạt động cuối cùng của hệ thống!" });
            }

            existing.Name = name;
            existing.Phone = phone;
            existing.Role = user.Role;
            existing.Status = user.Status;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // PATCH: api/users/5/status  — khoá/mở khoá nhanh (áp dụng cả với khách hàng)
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

            // Tài khoản Admin KHÔNG bị khoá được (kể cả bởi Admin khác). Chỉ khoá được Nhân viên và Khách hàng.
            if (status == "Bị khoá" && user.Role == "Admin")
                return BadRequest(new { message = "Không thể khoá tài khoản Admin!" });

            user.Status = status;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // PATCH: api/users/5/reset-password — CHỈ áp dụng cho tài khoản NỘI BỘ (Admin / Nhân viên).
        // Tài khoản KHÁCH HÀNG bị chặn (403): Admin không được tự đặt mật khẩu của khách.
        // Admin đặt lại mật khẩu mới cho 1 tài khoản nội bộ khác.
        // KHÔNG dùng chung với PUT Update vì đây là hành động nhạy cảm, tách riêng cho rõ log/quyền.
        [HttpPatch("{id}/reset-password")]
        public async Task<IActionResult> ResetPassword(int id, [FromBody] string newPassword)
        {
            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
                return BadRequest(new { message = "Mật khẩu mới phải có ít nhất 6 ký tự!" });

            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            if (user.Role == ROLE_CUSTOMER)
                return StatusCode(403, new
                {
                    message = "Admin không được đặt lại mật khẩu của khách hàng. Khách hàng tự đặt lại mật khẩu của mình."
                });

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
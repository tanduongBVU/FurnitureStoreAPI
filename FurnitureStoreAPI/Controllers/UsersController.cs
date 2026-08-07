using FurnitureStoreAPI.Data;
using FurnitureStoreAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var user = await _context.Users.FindAsync(id);
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
        [HttpPatch("{id}/status")]
        public async Task<IActionResult> ToggleStatus(int id, [FromBody] string status)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();
            user.Status = status;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // DELETE: api/users/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();
            _context.Users.Remove(user);
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}
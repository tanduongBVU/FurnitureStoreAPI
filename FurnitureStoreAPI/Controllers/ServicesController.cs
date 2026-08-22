using FurnitureStoreAPI.Data;
using FurnitureStoreAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FurnitureStoreAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ServicesController : ControllerBase
    {
        private readonly AppDbContext _context;
        public ServicesController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/services?type=thi-cong
        // Công khai — dùng cho Client. CHỈ trả dịch vụ đang hiện (IsActive), lọc theo Type
        // nếu có truyền query, ngược lại trả tất cả. Sắp DisplayOrder trước, CreatedAt sau
        // để dịch vụ chưa đặt thứ tự thủ công vẫn có thứ tự ổn định (mới nhất trước).
        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] string? type)
        {
            var query = _context.Services.Where(s => s.IsActive);
            if (!string.IsNullOrWhiteSpace(type))
                query = query.Where(s => s.Type == type);

            var services = await query
                .OrderBy(s => s.DisplayOrder)
                .ThenByDescending(s => s.CreatedAt)
                .ToListAsync();
            return Ok(services);
        }

        // GET: api/services/all — Admin & Nhân viên, xem được cả dịch vụ đã ẩn
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpGet("all")]
        public async Task<IActionResult> GetAll()
        {
            var services = await _context.Services
                .OrderBy(s => s.DisplayOrder)
                .ThenByDescending(s => s.CreatedAt)
                .ToListAsync();
            return Ok(services);
        }

        // GET: api/services/5 — dùng chung cho Client (trang chi tiết) và Admin (trang sửa)
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var service = await _context.Services.FindAsync(id);
            if (service == null) return NotFound();
            return Ok(service);
        }

        // POST: api/services — Admin & Nhân viên đều được thêm dịch vụ
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpPost]
        public async Task<IActionResult> Create(Service service)
        {
            service.CreatedAt = DateTime.Now;
            service.IsActive = true;
            _context.Services.Add(service);
            await _context.SaveChangesAsync();
            return CreatedAtAction(nameof(GetById), new { id = service.Id }, service);
        }

        // PUT: api/services/5 — Admin & Nhân viên đều được sửa dịch vụ
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, Service service)
        {
            if (id != service.Id) return BadRequest();

            var existing = await _context.Services.FindAsync(id);
            if (existing == null) return NotFound();

            existing.Type = service.Type;
            existing.Title = service.Title;
            existing.Image = service.Image;
            existing.ShortDescription = service.ShortDescription;
            existing.Content = service.Content;
            existing.DisplayOrder = service.DisplayOrder;
            // Không đổi IsActive ở đây — việc ẩn/hiện dùng riêng endpoint Delete/Restore

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // DELETE: api/services/5 — soft-delete (giống Product), CHỈ Admin
        [Authorize(Roles = "Admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var service = await _context.Services.FindAsync(id);
            if (service == null) return NotFound();

            service.IsActive = false;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // PATCH: api/services/5/restore — CHỈ Admin
        [Authorize(Roles = "Admin")]
        [HttpPatch("{id}/restore")]
        public async Task<IActionResult> Restore(int id)
        {
            var service = await _context.Services.FindAsync(id);
            if (service == null) return NotFound();

            service.IsActive = true;
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}
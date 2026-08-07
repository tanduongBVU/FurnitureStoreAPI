using FurnitureStoreAPI.Data;
using FurnitureStoreAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FurnitureStoreAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ContactsController : ControllerBase
    {
        private readonly AppDbContext _context;
        public ContactsController(AppDbContext context) { _context = context; }

        // GET: api/contacts  — chỉ Admin & Nhân viên xem được danh sách liên hệ
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var contacts = await _context.Contacts
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();
            return Ok(contacts);
        }

        // GET: api/contacts/5  — chỉ Admin & Nhân viên
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var contact = await _context.Contacts.FindAsync(id);
            if (contact == null) return NotFound();
            return Ok(contact);
        }

        // POST: api/contacts  — công khai, khách hàng gửi liên hệ từ FE Client không cần đăng nhập
        [HttpPost]
        public async Task<IActionResult> Create(Contact contact)
        {
            contact.Status = "Mới";
            contact.CreatedAt = DateTime.Now;
            _context.Contacts.Add(contact);
            await _context.SaveChangesAsync();
            return CreatedAtAction(nameof(GetById), new { id = contact.Id }, contact);
        }

        // PATCH: api/contacts/5 — Admin & Nhân viên đều được cập nhật trạng thái + ghi chú
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpPatch("{id}")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateContactDto dto)
        {
            var contact = await _context.Contacts.FindAsync(id);
            if (contact == null) return NotFound();
            contact.Status = dto.Status;
            contact.Note = dto.Note;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // DELETE: api/contacts/5  — CHỈ Admin được xoá liên hệ
        [Authorize(Roles = "Admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var contact = await _context.Contacts.FindAsync(id);
            if (contact == null) return NotFound();
            _context.Contacts.Remove(contact);
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }

    public class UpdateContactDto
    {
        public string Status { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
    }
}
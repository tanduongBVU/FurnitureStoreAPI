using FurnitureStoreAPI.Data;
using FurnitureStoreAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FurnitureStoreAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProjectsController : ControllerBase
    {
        private readonly AppDbContext _context;
        public ProjectsController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/projects
        // Công khai — dùng cho Client (trang Dự án kiến trúc, dạng portfolio).
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var projects = await _context.Projects
                .Where(p => p.IsActive)
                .OrderBy(p => p.DisplayOrder)
                .ThenByDescending(p => p.CreatedAt)
                .ToListAsync();
            return Ok(projects);
        }

        // GET: api/projects/all — Admin & Nhân viên, xem được cả dự án đã ẩn
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpGet("all")]
        public async Task<IActionResult> GetAll()
        {
            var projects = await _context.Projects
                .OrderBy(p => p.DisplayOrder)
                .ThenByDescending(p => p.CreatedAt)
                .ToListAsync();
            return Ok(projects);
        }

        // GET: api/projects/5 — dùng chung cho Client (trang chi tiết dự án) và Admin (sửa)
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var project = await _context.Projects.FindAsync(id);
            if (project == null) return NotFound();
            return Ok(project);
        }

        // POST: api/projects — Admin & Nhân viên đều được thêm dự án
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpPost]
        public async Task<IActionResult> Create(Project project)
        {
            project.CreatedAt = DateTime.Now;
            project.IsActive = true;
            _context.Projects.Add(project);
            await _context.SaveChangesAsync();
            return CreatedAtAction(nameof(GetById), new { id = project.Id }, project);
        }

        // PUT: api/projects/5 — Admin & Nhân viên đều được sửa dự án
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, Project project)
        {
            if (id != project.Id) return BadRequest();

            var existing = await _context.Projects.FindAsync(id);
            if (existing == null) return NotFound();

            existing.Title = project.Title;
            existing.CoverImage = project.CoverImage;
            existing.Images = project.Images;
            existing.Location = project.Location;
            existing.Category = project.Category;
            existing.Year = project.Year;
            existing.ShortDescription = project.ShortDescription;
            existing.Content = project.Content;
            existing.DisplayOrder = project.DisplayOrder;

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // DELETE: api/projects/5 — soft-delete, CHỈ Admin
        [Authorize(Roles = "Admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var project = await _context.Projects.FindAsync(id);
            if (project == null) return NotFound();

            project.IsActive = false;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // PATCH: api/projects/5/restore — CHỈ Admin
        [Authorize(Roles = "Admin")]
        [HttpPatch("{id}/restore")]
        public async Task<IActionResult> Restore(int id)
        {
            var project = await _context.Projects.FindAsync(id);
            if (project == null) return NotFound();

            project.IsActive = true;
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}
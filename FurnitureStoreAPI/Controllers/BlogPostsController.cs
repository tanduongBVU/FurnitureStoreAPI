using FurnitureStoreAPI.Data;
using FurnitureStoreAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FurnitureStoreAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BlogPostsController : ControllerBase
    {
        private readonly AppDbContext _context;
        public BlogPostsController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/blogposts
        // Công khai — dùng cho Client (trang Cẩm nang). CHỈ trả bài đã đăng (IsPublished = true).
        // Sắp xếp mới nhất lên trước.
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var posts = await _context.BlogPosts
                .Where(p => p.IsPublished)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
            return Ok(posts);
        }

        // GET: api/blogposts/all
        // Chỉ Admin & Nhân viên — dùng cho trang quản trị, xem được cả bài nháp/đã ẩn.
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpGet("all")]
        public async Task<IActionResult> GetAll()
        {
            var posts = await _context.BlogPosts
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
            return Ok(posts);
        }

        // GET: api/blogposts/5 — dùng chung cho cả Client (đọc bài) và Admin (trang sửa)
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var post = await _context.BlogPosts.FindAsync(id);
            if (post == null) return NotFound();
            return Ok(post);
        }

        // POST: api/blogposts — Admin & Nhân viên đều được viết bài
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpPost]
        public async Task<IActionResult> Create(BlogPost post)
        {
            post.CreatedAt = DateTime.Now;
            _context.BlogPosts.Add(post);
            await _context.SaveChangesAsync();
            return CreatedAtAction(nameof(GetById), new { id = post.Id }, post);
        }

        // PUT: api/blogposts/5 — Admin & Nhân viên đều được sửa bài
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, BlogPost post)
        {
            if (id != post.Id) return BadRequest();

            var existing = await _context.BlogPosts.FindAsync(id);
            if (existing == null) return NotFound();

            existing.Title = post.Title;
            existing.Category = post.Category;
            existing.Thumbnail = post.Thumbnail;
            existing.Excerpt = post.Excerpt;
            existing.Content = post.Content;
            existing.Author = post.Author;
            existing.IsPublished = post.IsPublished;

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // DELETE: api/blogposts/5
        // Xoá hẳn khỏi DB — khác với Products, không có đơn hàng nào tham chiếu tới bài viết
        // nên không cần soft-delete. CHỈ Admin được xoá.
        [Authorize(Roles = "Admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var post = await _context.BlogPosts.FindAsync(id);
            if (post == null) return NotFound();

            _context.BlogPosts.Remove(post);
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}
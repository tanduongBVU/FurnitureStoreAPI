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
    public class ReviewsController : ControllerBase
    {
        private readonly AppDbContext _context;
        public ReviewsController(AppDbContext context) { _context = context; }

        private int? GetCurrentUserId()
        {
            var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(idClaim, out var id) ? id : null;
        }

        // Kiểm tra khách đã có đơn hàng "Hoàn thành" chứa sản phẩm này chưa —
        // dùng chung cho cả CanReview và Create, KHÔNG tin dữ liệu gửi từ client.
        private async Task<bool> HasPurchasedAsync(int userId, int productId)
        {
            return await _context.Orders
                .Where(o => o.UserId == userId && o.Status == "Hoàn thành")
                .SelectMany(o => o.OrderItems)
                .AnyAsync(oi => oi.ProductId == productId);
        }

        // GET: api/reviews/product/5
        // Công khai — chỉ trả review đã được Admin/Nhân viên duyệt (IsApproved = true).
        [HttpGet("product/{productId}")]
        public async Task<IActionResult> GetByProduct(int productId)
        {
            var reviews = await _context.Reviews
                .Where(r => r.ProductId == productId && r.IsApproved)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
            return Ok(reviews);
        }

        // GET: api/reviews/can-review/5
        // Yêu cầu đăng nhập — FE gọi trước khi hiện form đánh giá, để biết có cho phép không
        // và hiện đúng lý do nếu không được (chưa mua / đã review rồi).
        [Authorize]
        [HttpGet("can-review/{productId}")]
        public async Task<IActionResult> CanReview(int productId)
        {
            var userId = GetCurrentUserId();
            if (userId == null) return Unauthorized();

            var alreadyReviewed = await _context.Reviews
                .AnyAsync(r => r.UserId == userId && r.ProductId == productId);
            if (alreadyReviewed)
                return Ok(new { canReview = false, reason = "Bạn đã đánh giá sản phẩm này rồi." });

            var hasPurchased = await HasPurchasedAsync(userId.Value, productId);
            if (!hasPurchased)
                return Ok(new { canReview = false, reason = "Bạn cần mua và nhận hàng thành công trước khi đánh giá sản phẩm này." });

            return Ok(new { canReview = true, reason = "" });
        }

        // POST: api/reviews — khách hàng đã đăng nhập gửi đánh giá.
        // Server tự kiểm tra lại điều kiện (đã mua + chưa review), không tin dữ liệu từ client.
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateReviewDto dto)
        {
            var userId = GetCurrentUserId();
            if (userId == null) return Unauthorized();

            if (dto.Rating < 1 || dto.Rating > 5)
                return BadRequest(new { message = "Số sao đánh giá phải từ 1 đến 5!" });

            var user = await _context.Users.FindAsync(userId);
            if (user == null) return Unauthorized();

            var alreadyReviewed = await _context.Reviews
                .AnyAsync(r => r.UserId == userId && r.ProductId == dto.ProductId);
            if (alreadyReviewed)
                return BadRequest(new { message = "Bạn đã đánh giá sản phẩm này rồi." });

            var hasPurchased = await HasPurchasedAsync(userId.Value, dto.ProductId);
            if (!hasPurchased)
                return BadRequest(new { message = "Bạn cần mua và nhận hàng thành công trước khi đánh giá sản phẩm này." });

            var review = new Review
            {
                ProductId = dto.ProductId,
                UserId = userId.Value,
                UserName = user.Name,
                Rating = dto.Rating,
                Comment = (dto.Comment ?? "").Trim(),
                IsApproved = false,
                CreatedAt = DateTime.Now,
            };
            _context.Reviews.Add(review);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Đã gửi đánh giá! Đánh giá của bạn sẽ hiện công khai sau khi được duyệt." });
        }

        // GET: api/reviews/all — Admin & Nhân viên, xem TẤT CẢ review (kể cả chưa duyệt) để kiểm duyệt.
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpGet("all")]
        public async Task<IActionResult> GetAll()
        {
            var reviews = await _context.Reviews
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            // Gắn tên sản phẩm cho từng review để hiện trong bảng Admin — không có navigation
            // property nên tự query danh sách Product 1 lần rồi map, tránh N+1 query trong vòng lặp.
            var productIds = reviews.Select(r => r.ProductId).Distinct().ToList();
            var productNames = await _context.Products
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name);

            var result = reviews.Select(r => new
            {
                r.Id,
                r.ProductId,
                ProductName = productNames.TryGetValue(r.ProductId, out var name) ? name : "(Sản phẩm đã bị xoá)",
                r.UserName,
                r.Rating,
                r.Comment,
                r.IsApproved,
                r.CreatedAt,
            });
            return Ok(result);
        }

        // PATCH: api/reviews/5/approve — Admin & Nhân viên duyệt review cho hiện công khai
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpPatch("{id}/approve")]
        public async Task<IActionResult> Approve(int id)
        {
            var review = await _context.Reviews.FindAsync(id);
            if (review == null) return NotFound();
            review.IsApproved = true;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // DELETE: api/reviews/5 — CHỈ Admin, dùng để từ chối review chờ duyệt hoặc gỡ review đã duyệt
        [Authorize(Roles = "Admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var review = await _context.Reviews.FindAsync(id);
            if (review == null) return NotFound();
            _context.Reviews.Remove(review);
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }

    public class CreateReviewDto
    {
        public int ProductId { get; set; }
        public int Rating { get; set; }
        public string? Comment { get; set; }
    }
}
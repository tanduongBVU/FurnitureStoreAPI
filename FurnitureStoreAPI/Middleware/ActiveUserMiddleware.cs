using FurnitureStoreAPI.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FurnitureStoreAPI.Middleware
{
    // Token đăng nhập (JWT) tự nó còn hạn đến khi hết thời gian, nên nếu chỉ dựa vào token thì:
    //   - tài khoản vừa bị KHOÁ hoặc bị XOÁ vẫn dùng được cho đến khi token hết hạn;
    //   - tài khoản vừa bị ĐỔI VAI TRÒ vẫn giữ quyền cũ (VD: Nhân viên vừa bị hạ quyền vẫn làm được việc cũ).
    // Middleware này kiểm tra lại trạng thái và vai trò TRONG DATABASE cho mỗi request đã đăng nhập;
    // không khớp thì trả 401 (giao diện sẽ xoá phiên và đưa người dùng về trang đăng nhập).
    // Chạy sau UseAuthentication và trước UseAuthorization.
    public class ActiveUserMiddleware
    {
        private readonly RequestDelegate _next;
        public ActiveUserMiddleware(RequestDelegate next) { _next = next; }

        public async Task InvokeAsync(HttpContext context, AppDbContext db)
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var idClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
                var roleClaim = context.User.FindFirstValue(ClaimTypes.Role);

                if (int.TryParse(idClaim, out var userId))
                {
                    var current = await db.Users
                        .AsNoTracking()
                        .Where(u => u.Id == userId)
                        .Select(u => new { u.Role, u.Status })
                        .FirstOrDefaultAsync();

                    string? message = null;
                    if (current == null)
                        message = "Tài khoản không còn tồn tại.";
                    else if (current.Status == "Bị khoá")
                        message = "Tài khoản đã bị khoá.";
                    else if (!string.IsNullOrEmpty(roleClaim) && current.Role != roleClaim)
                        message = "Quyền của tài khoản đã thay đổi, vui lòng đăng nhập lại.";

                    if (message != null)
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(new { message });
                        return;
                    }
                }
            }

            await _next(context);
        }
    }
}
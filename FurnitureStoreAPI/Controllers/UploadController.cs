using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FurnitureStoreAPI.Controllers
{
    // [Authorize] — chỉ Admin/Nhân viên đã đăng nhập mới upload được, tránh việc
    // ai đó ngoài internet spam file lên server làm đầy ổ đĩa. Nếu các controller
    // khác trong project dùng role cụ thể (VD [Authorize(Roles = "Admin")]) thì
    // đổi lại dòng bên dưới cho khớp cách các Controller khác (ProductsController,
    // OrdersController...) đang làm.
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class UploadController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;

        public UploadController(IWebHostEnvironment env)
        {
            _env = env;
        }

        private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
        private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10MB — đủ thoải mái cho ảnh điện thoại chụp thường (thường 5-10MB)

        // POST /api/Upload — nhận 1 file ảnh (multipart/form-data, field name "file"),
        // trả về { url: "https://.../uploads/xxxx.jpg" } để Admin dán thẳng vào các ô
        // "URL ảnh" đang có sẵn khắp SiteSettings/Products/Bundles/... — không cần sửa
        // gì thêm ở những nơi đó, vì chúng vốn chỉ cần 1 chuỗi URL.
        [HttpPost]
        [RequestSizeLimit(MaxFileSizeBytes)]
        public async Task<IActionResult> UploadImage(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "Không có file nào được gửi lên." });

            if (file.Length > MaxFileSizeBytes)
                return BadRequest(new { message = "Ảnh vượt quá 10MB, vui lòng chọn ảnh nhẹ hơn." });

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(ext))
                return BadRequest(new { message = "Định dạng file không được hỗ trợ. Chỉ nhận: jpg, jpeg, png, webp, gif." });

            // wwwroot có thể chưa tồn tại trên máy dev mới — tự tạo cả wwwroot lẫn
            // wwwroot/uploads nếu thiếu, tránh lỗi "Could not find a part of the path".
            var webRoot = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var uploadsFolder = Path.Combine(webRoot, "uploads");
            Directory.CreateDirectory(uploadsFolder);

            // Đặt tên file random (GUID) — tránh trùng tên khi 2 người cùng up file
            // tên giống nhau, và tránh ký tự lạ/dấu tiếng Việt trong tên file gốc gây
            // lỗi URL ở vài trình duyệt/server.
            var fileName = $"{Guid.NewGuid()}{ext}";
            var filePath = Path.Combine(uploadsFolder, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Trả về URL TUYỆT ĐỐI (kèm domain Backend) — bắt buộc, vì Admin (port 5174)
            // và Client (port 5173) là 2 app khác domain/port với Backend (port 7025).
            // Nếu trả về URL tương đối ("/uploads/xxx.jpg"), trình duyệt sẽ tự ghép vào
            // domain của Admin/Client (sai hẳn), không phải domain Backend nơi file thật
            // sự nằm ở đó.
            var url = $"{Request.Scheme}://{Request.Host}/uploads/{fileName}";
            return Ok(new { url });
        }
    }
}
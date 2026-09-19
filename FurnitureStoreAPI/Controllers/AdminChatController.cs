using FurnitureStoreAPI.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;

namespace FurnitureStoreAPI.Controllers
{
    public class AdminChatRequestDto
    {
        public string Message { get; set; } = string.Empty;
    }

    // Trợ lý AI RIÊNG cho Admin — khác hẳn ChatController (dành cho khách hàng ở Client).
    // Controller này tự truy vấn DB thật mỗi lần Admin hỏi, đóng gói thành 1 bản tóm tắt số
    // liệu TOÀN DIỆN (đơn hàng, sản phẩm, combo, mã giảm giá, dịch vụ, dự án, blog, đánh giá,
    // người dùng, liên hệ), rồi mới gửi kèm câu hỏi cho Gemini — nhờ vậy AI trả lời DỰA TRÊN
    // SỐ LIỆU THẬT thay vì bịa, và luôn cập nhật (không lưu cache).
    // [Authorize] — chỉ tài khoản đã đăng nhập mới hỏi được, vì nội dung có thể chứa số liệu
    // kinh doanh nhạy cảm. Đổi thành [Authorize(Roles = "Admin")] nếu cần giới hạn chặt hơn.
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class AdminChatController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly AppDbContext _db;

        public AdminChatController(IHttpClientFactory httpClientFactory, IConfiguration config, AppDbContext db)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
            _db = db;
        }

        [HttpPost]
        public async Task<IActionResult> Send([FromBody] AdminChatRequestDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Message))
                return BadRequest(new { message = "Tin nhắn không được để trống." });

            var apiKey = _config["Gemini:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
                return StatusCode(500, new { message = "Chưa cấu hình Gemini API Key ở Backend (appsettings.json)." });

            var model = _config["Gemini:Model"];
            if (string.IsNullOrWhiteSpace(model)) model = "gemini-flash-lite-latest";

            var dataSnapshot = await BuildDataSnapshotAsync();

            var systemPrompt =
$@"Bạn là trợ lý nội bộ dành cho ADMIN của cửa hàng nội thất LuxWood (KHÔNG PHẢI trợ lý
khách hàng). Người hỏi là chủ shop hoặc nhân viên quản trị, có toàn quyền xem số liệu kinh
doanh nội bộ. Nói chuyện tự nhiên như một trợ lý thực thụ đang báo cáo cho sếp — chuyên
nghiệp, đi thẳng vào số liệu, nhưng không cần quá trang trọng hay lặp khuôn mẫu câu. Luôn
trả lời bằng tiếng Việt.

Bạn được cung cấp bản TÓM TẮT số liệu thật của hệ thống ngay bên dưới, lấy trực tiếp từ
database tại thời điểm câu hỏi này được gửi lên — LUÔN dựa vào đây để trả lời, KHÔNG bịa
thêm số liệu ngoài phần dữ liệu được cung cấp. Nếu câu hỏi cần thông tin không có trong bản
tóm tắt (VD: chi tiết 1 đơn hàng cụ thể không nằm trong danh sách bên dưới), hãy nói rõ là
không có đủ dữ liệu trong bản tóm tắt này, và gợi ý Admin vào đúng trang quản lý tương ứng
để xem chi tiết đầy đủ.

Bạn CÓ THỂ trả lời cả những câu hỏi chung không liên quan tới số liệu (VD: gợi ý cách viết
mô tả sản phẩm, soạn tin nhắn cho khách, giải thích 1 khái niệm...) — không cần né tránh.

GIỚI HẠN QUAN TRỌNG: Bạn CHỈ có thể trả lời bằng CHỮ trong khung chat này — bạn KHÔNG có khả
năng tạo hay xuất ra bất kỳ FILE nào (Excel, PDF, Word, ảnh...), KHÔNG thể tải file lên đâu
cả. Nếu Admin yêu cầu “xuất file”, “tải về”, “làm file Excel”, “in ra PDF”... hãy trả lời
THẬT LÒNG rằng bạn không tạo file được, và hướng dẫn Admin dùng đúng tính năng xuất Excel/PDF
đã có sẵn ở trang Đơn hàng (nút xuất file, có bộ lọc theo ngày và theo bộ lọc đang hiển thị).
TUYỆT ĐỐI KHÔNG giả vờ là đã tạo/xuất file thành công.

QUAN TRỌNG VỀ ĐỊNH DẠNG: Khung chat hiện tại chỉ hiển thị CHỮ THƯỜNG, KHÔNG render được
Markdown. TUYỆT ĐỐI KHÔNG dùng ký hiệu **chữ đậm**, *in nghiêng*, # tiêu đề, hay dấu - đầu
dòng kiểu Markdown — nếu dùng, người đọc sẽ thấy nguyên các ký tự thừa đó trong câu. Muốn
liệt kê, dùng số thứ tự kiểu “1. ”, “2. ” hoặc xuống dòng bình thường.

{dataSnapshot}";

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

            var payload = new
            {
                systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
                contents = new[]
                {
                    new { role = "user", parts = new[] { new { text = dto.Message } } }
                },
            };

            var client = _httpClientFactory.CreateClient();

            HttpResponseMessage response;
            try
            {
                response = await client.PostAsync(
                    url,
                    new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
            }
            catch
            {
                return StatusCode(502, new { message = "Không kết nối được tới dịch vụ AI, vui lòng thử lại sau." });
            }

            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return StatusCode(502, new { message = "AI trả về lỗi.", detail = body });
            }

            string? reply;
            try
            {
                using var doc = JsonDocument.Parse(body);
                reply = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();
            }
            catch
            {
                reply = null;
            }

            if (string.IsNullOrWhiteSpace(reply))
                return StatusCode(502, new { message = "AI không trả về nội dung hợp lệ." });

            return Ok(new { reply = reply.Trim() });
        }

        // Truy vấn DB thật và dựng thành 1 khối text tóm tắt TOÀN DIỆN — bao phủ mọi mảng dữ
        // liệu chính trong hệ thống, để Admin hỏi gì AI cũng có cơ sở trả lời thay vì phải tự
        // vào từng trang tra cứu. Phần Đơn hàng/Sản phẩm dùng ĐÚNG logic tính toán với
        // Dashboard.jsx (đơn Hoàn thành mới tính doanh thu, so sánh tháng này/tháng trước...)
        // để số liệu AI trả lời KHỚP với những gì Admin đang thấy trên Dashboard.
        private async Task<string> BuildDataSnapshotAsync()
        {
            var now = DateTime.Now;
            var startOfThisMonth = new DateTime(now.Year, now.Month, 1);
            var startOfLastMonth = startOfThisMonth.AddMonths(-1);

            var orders = await _db.Orders.Include(o => o.OrderItems).ToListAsync();
            var products = await _db.Products.Include(p => p.Variants).ToListAsync();
            var contacts = await _db.Contacts.ToListAsync();
            var bundles = await _db.Bundles.Include(b => b.Items).ToListAsync();
            var coupons = await _db.Coupons.ToListAsync();
            var services = await _db.Services.ToListAsync();
            var projects = await _db.Projects.ToListAsync();
            var blogPosts = await _db.BlogPosts.ToListAsync();
            var reviews = await _db.Reviews.ToListAsync();
            var users = await _db.Users.ToListAsync();

            var completedOrders = orders.Where(o => o.Status == "Hoàn thành").ToList();
            var totalRevenue = completedOrders.Sum(o => o.Total);
            var revenueThisMonth = completedOrders.Where(o => o.CreatedAt >= startOfThisMonth).Sum(o => o.Total);
            var revenueLastMonth = completedOrders.Where(o => o.CreatedAt >= startOfLastMonth && o.CreatedAt < startOfThisMonth).Sum(o => o.Total);

            var ordersThisMonth = orders.Count(o => o.CreatedAt >= startOfThisMonth);
            var ordersLastMonth = orders.Count(o => o.CreatedAt >= startOfLastMonth && o.CreatedAt < startOfThisMonth);
            var cancelledThisMonth = orders.Count(o => o.Status == "Huỷ" && o.CreatedAt >= startOfThisMonth);

            var pendingOrders = orders
                .Where(o => o.Status == "Chờ xác nhận")
                .OrderByDescending(o => o.CreatedAt)
                .Take(10)
                .ToList();

            var activeProducts = products.Where(p => p.IsActive).ToList();
            var outOfStock = activeProducts.Where(p => p.Stock == 0).Take(10).ToList();
            var lowStock = activeProducts.Where(p => p.Stock > 0 && p.Stock <= 3).OrderBy(p => p.Stock).Take(10).ToList();
            var bestSellersFlagged = activeProducts.Where(p => p.IsBestSeller).Take(10).ToList();

            var soldQtyByProduct = new Dictionary<int, int>();
            foreach (var o in completedOrders)
                foreach (var item in o.OrderItems)
                    soldQtyByProduct[item.ProductId] = soldQtyByProduct.GetValueOrDefault(item.ProductId) + item.Quantity;
            var neverSoldCount = activeProducts.Count(p => !soldQtyByProduct.ContainsKey(p.Id));

            var newContacts = contacts
                .Where(c => c.Status == "Mới")
                .OrderByDescending(c => c.CreatedAt)
                .Take(5)
                .ToList();

            var activeBundles = bundles.Where(b => b.IsActive).ToList();
            var priceById = products.ToDictionary(p => p.Id, p => p.Price * (1 - p.DiscountPercent / 100m));

            var activeCoupons = coupons.Where(c =>
                c.IsActive &&
                (c.ExpiryDate == null || c.ExpiryDate >= now) &&
                (c.MaxUsage == null || c.UsedCount < c.MaxUsage)
            ).ToList();
            var expiredOrUsedUpCoupons = coupons.Where(c => c.IsActive && !activeCoupons.Contains(c)).ToList();

            var activeServices = services.Where(s => s.IsActive).ToList();
            var activeProjects = projects.Where(p => p.IsActive).ToList();
            var publishedPosts = blogPosts.Where(b => b.IsPublished).ToList();
            var draftPosts = blogPosts.Where(b => !b.IsPublished).ToList();
            var pendingReviews = reviews.Where(r => !r.IsApproved).ToList();

            var usersByRole = users.GroupBy(u => u.Role).ToDictionary(g => g.Key, g => g.Count());
            var newUsersThisMonth = users.Count(u => u.CreatedAt >= startOfThisMonth);

            string TruncateMsg(string s, int max) => s.Length > max ? s.Substring(0, max) + "..." : s;

            var sb = new StringBuilder();
            sb.AppendLine($"=== DỮ LIỆU KINH DOANH LUXWOOD (cập nhật lúc {now:dd/MM/yyyy HH:mm}) ===");
            sb.AppendLine();

            sb.AppendLine("TỔNG QUAN DOANH THU & ĐƠN HÀNG:");
            sb.AppendLine($"- Tổng doanh thu từ trước đến nay (chỉ tính đơn Hoàn thành): {totalRevenue:N0} đ");
            sb.AppendLine($"- Doanh thu tháng này: {revenueThisMonth:N0} đ (tháng trước: {revenueLastMonth:N0} đ)");
            sb.AppendLine($"- Số đơn phát sinh tháng này: {ordersThisMonth} (tháng trước: {ordersLastMonth})");
            sb.AppendLine($"- Số đơn bị huỷ tháng này: {cancelledThisMonth}");
            sb.AppendLine();

            sb.AppendLine($"ĐƠN HÀNG ĐANG CHỜ XÁC NHẬN ({orders.Count(o => o.Status == "Chờ xác nhận")} đơn, liệt kê tối đa 10 đơn mới nhất):");
            if (pendingOrders.Count == 0)
                sb.AppendLine("- Không có đơn nào đang chờ xác nhận.");
            else
                foreach (var o in pendingOrders)
                    sb.AppendLine($"- #{o.Id} — {o.CustomerName} — {o.Total:N0} đ — {o.CreatedAt:dd/MM HH:mm}");
            sb.AppendLine();

            sb.AppendLine("SẢN PHẨM:");
            sb.AppendLine($"- Tổng số sản phẩm đang bán (IsActive): {activeProducts.Count}");
            sb.AppendLine($"- Sản phẩm HẾT HÀNG ({outOfStock.Count} trong danh sách, tối đa 10): " +
                (outOfStock.Count == 0 ? "không có." : string.Join(", ", outOfStock.Select(p => p.Name))));
            sb.AppendLine($"- Sản phẩm SẮP HẾT HÀNG (≤3, {lowStock.Count} trong danh sách, tối đa 10): " +
                (lowStock.Count == 0 ? "không có." : string.Join(", ", lowStock.Select(p => $"{p.Name} (còn {p.Stock})"))));
            sb.AppendLine($"- Sản phẩm đánh dấu BÁN CHẠY thủ công ({bestSellersFlagged.Count} trong danh sách, tối đa 10): " +
                (bestSellersFlagged.Count == 0 ? "không có." : string.Join(", ", bestSellersFlagged.Select(p => p.Name))));
            sb.AppendLine($"- Số sản phẩm ĐANG BÁN nhưng CHƯA TỪNG có lượt bán nào: {neverSoldCount}");
            sb.AppendLine();

            sb.AppendLine($"COMBO ({activeBundles.Count} đang bán):");
            if (activeBundles.Count == 0)
            {
                sb.AppendLine("- Không có combo nào đang bán.");
            }
            else
            {
                foreach (var b in activeBundles)
                {
                    var subtotal = b.Items.Sum(i => i.Quantity * priceById.GetValueOrDefault(i.ProductId, 0m));
                    var finalPrice = subtotal * (1 - b.DiscountPercent / 100m);
                    sb.AppendLine($"- {b.Name} — giảm {b.DiscountPercent}%, {b.Items.Count} sản phẩm, giá sau giảm ~{finalPrice:N0} đ");
                }
            }
            sb.AppendLine();

            sb.AppendLine($"MÃ GIẢM GIÁ (còn hiệu lực: {activeCoupons.Count}, đã hết hạn/hết lượt: {expiredOrUsedUpCoupons.Count}):");
            if (activeCoupons.Count == 0)
            {
                sb.AppendLine("- Không có mã nào còn hiệu lực.");
            }
            else
            {
                foreach (var c in activeCoupons)
                {
                    var usageText = c.MaxUsage == null ? $"đã dùng {c.UsedCount} lần (không giới hạn)" : $"đã dùng {c.UsedCount}/{c.MaxUsage} lần";
                    var expiryText = c.ExpiryDate == null ? "không hết hạn" : $"hết hạn {c.ExpiryDate:dd/MM/yyyy}";
                    sb.AppendLine($"- {c.Code} — giảm {c.DiscountPercent}% — {usageText} — {expiryText}");
                }
            }
            sb.AppendLine();

            sb.AppendLine($"DỊCH VỤ ({activeServices.Count} đang hiển thị, tổng {services.Count} kể cả đã ẩn):");
            sb.AppendLine($"- Thi công: {activeServices.Count(s => s.Type == "thi-cong")} dịch vụ");
            sb.AppendLine($"- Thiết kế: {activeServices.Count(s => s.Type == "thiet-ke")} dịch vụ");
            sb.AppendLine();

            sb.AppendLine($"DỰ ÁN: {activeProjects.Count} dự án đang hiển thị (tổng {projects.Count} kể cả đã ẩn)");
            sb.AppendLine();

            sb.AppendLine($"CẨM NANG (BLOG): {publishedPosts.Count} bài đã đăng, {draftPosts.Count} bài nháp/đã ẩn");
            sb.AppendLine();

            sb.AppendLine($"ĐÁNH GIÁ SẢN PHẨM: {pendingReviews.Count} đánh giá đang CHỜ DUYỆT (chưa hiện công khai), tổng {reviews.Count} đánh giá");
            sb.AppendLine();

            sb.AppendLine($"NGƯỜI DÙNG: {users.Count} tài khoản (mới đăng ký tháng này: {newUsersThisMonth})");
            if (usersByRole.Count > 0)
                sb.AppendLine("- Theo vai trò: " + string.Join(", ", usersByRole.Select(kv => $"{kv.Key}: {kv.Value}")));
            sb.AppendLine();

            sb.AppendLine($"LIÊN HỆ CHƯA XỬ LÝ ({contacts.Count(c => c.Status == "Mới")} liên hệ, liệt kê tối đa 5 mới nhất):");
            if (newContacts.Count == 0)
            {
                sb.AppendLine("- Không có liên hệ mới nào.");
            }
            else
            {
                foreach (var c in newContacts)
                    sb.AppendLine($"- {c.Name} ({c.Phone}) — \"{TruncateMsg(c.Message, 60)}\" — {c.CreatedAt:dd/MM HH:mm}");
            }

            return sb.ToString();
        }
    }
}
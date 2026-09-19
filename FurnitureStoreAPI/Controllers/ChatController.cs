using FurnitureStoreAPI.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;

namespace FurnitureStoreAPI.Controllers
{
    public class ChatRequestDto
    {
        public string Message { get; set; } = string.Empty;
    }

    [ApiController]
    [Route("api/[controller]")]
    public class ChatController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly AppDbContext _db;

        public ChatController(IHttpClientFactory httpClientFactory, IConfiguration config, AppDbContext db)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
            _db = db;
        }

        [HttpPost]
        public async Task<IActionResult> Send([FromBody] ChatRequestDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Message))
                return BadRequest(new { message = "Tin nhắn không được để trống." });

            var apiKey = _config["Gemini:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
                return StatusCode(500, new { message = "Chưa cấu hình Gemini API Key ở Backend (appsettings.json)." });

            var model = _config["Gemini:Model"];
            if (string.IsNullOrWhiteSpace(model)) model = "gemini-flash-lite-latest";

            var dataSnapshot = await BuildPublicDataSnapshotAsync();

            // System prompt mô tả LuxWood cho AI. Giờ AI ĐƯỢC cung cấp dữ liệu THẬT (sản phẩm,
            // combo, dịch vụ, dự án, blog — toàn bộ đều là nội dung CÔNG KHAI, an toàn để đưa cho
            // 1 chatbot public không cần đăng nhập). KHÔNG đưa Orders/Contacts/Users vào đây —
            // những bảng đó chỉ dành cho AdminChatController (có [Authorize] bảo vệ).
            var systemPrompt =
$@"Bạn là trợ lý tư vấn của LuxWood, một cửa hàng nội thất trực tuyến tại Việt Nam. Nói
chuyện tự nhiên như một nhân viên tư vấn thực thụ — thân thiện, chuyên nghiệp, nhưng không
máy móc hay lặp lại khuôn mẫu câu. Có thể xưng “mình” hoặc “bên mình”. Trả lời ngắn gọn khi
câu hỏi đơn giản, giải thích kỹ hơn khi cần, luôn bằng tiếng Việt.

Bạn có thể trả lời MỌI câu hỏi khách đặt ra, kể cả câu hỏi không liên quan tới nội thất
(kiến thức chung, thời tiết, trò chuyện thông thường...) — không cần từ chối hay né tránh.

Bạn được cung cấp dữ liệu THẬT về sản phẩm, combo, dịch vụ, dự án và bài viết Cẩm nang của
LuxWood ngay bên dưới, lấy trực tiếp từ hệ thống tại thời điểm câu hỏi này được gửi lên —
LUÔN dùng đúng thông tin này để trả lời (giá, tồn kho, mô tả...), KHÔNG bịa thêm. Nếu khách
hỏi về sản phẩm/dịch vụ không có trong danh sách bên dưới, hãy nói thật là hiện chưa có mặt
hàng đó, đừng đoán mò.

Khi câu hỏi liên quan tới chính sách chung, dùng đúng thông tin sau:
- Đổi trả: trong vòng 7 ngày kể từ khi nhận hàng, sản phẩm còn nguyên vẹn chưa qua sử dụng.
- Giao hàng: 3-7 ngày làm việc tuỳ khu vực, nội thành 24-48h với hàng có sẵn.
- Thanh toán: hỗ trợ COD (thanh toán khi nhận hàng) và chuyển khoản ngân hàng.
- Giờ hỗ trợ khách hàng: 8:00 - 21:00 tất cả các ngày trong tuần.

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

        // Dựng bản tóm tắt dữ liệu CÔNG KHAI (sản phẩm, combo, dịch vụ, dự án, blog) — chỉ
        // những bảng an toàn để hiển thị cho bất kỳ ai, vì Controller này KHÔNG có [Authorize]
        // (ChatWidget bên Client gọi được mà không cần đăng nhập).
        private async Task<string> BuildPublicDataSnapshotAsync()
        {
            var now = DateTime.Now;

            var products = await _db.Products.Include(p => p.Variants).Where(p => p.IsActive).ToListAsync();
            var bundles = await _db.Bundles.Include(b => b.Items).Where(b => b.IsActive).ToListAsync();
            var services = await _db.Services.Where(s => s.IsActive).OrderBy(s => s.DisplayOrder).ToListAsync();
            var projects = await _db.Projects.Where(p => p.IsActive).OrderBy(p => p.DisplayOrder).ToListAsync();
            var blogPosts = await _db.BlogPosts
                .Where(b => b.IsPublished)
                .OrderByDescending(b => b.CreatedAt)
                .Take(15)
                .ToListAsync();

            var priceById = products.ToDictionary(p => p.Id, p => p.Price * (1 - p.DiscountPercent / 100m));

            string StockText(int stock) => stock > 0 ? $"còn {stock}" : "hết hàng";

            var sb = new StringBuilder();
            sb.AppendLine($"=== DỮ LIỆU CÔNG KHAI LUXWOOD (cập nhật lúc {now:dd/MM/yyyy HH:mm}) ===");
            sb.AppendLine();

            sb.AppendLine($"SẢN PHẨM ĐANG BÁN ({products.Count} sản phẩm):");
            if (products.Count == 0)
            {
                sb.AppendLine("- Hiện chưa có sản phẩm nào.");
            }
            else
            {
                foreach (var p in products)
                {
                    var priceAfter = p.Price * (1 - p.DiscountPercent / 100m);
                    var priceText = p.DiscountPercent > 0
                        ? $"{priceAfter:N0}đ (giá gốc {p.Price:N0}đ, giảm {p.DiscountPercent}%)"
                        : $"{p.Price:N0}đ";
                    sb.Append($"- {p.Name} | Danh mục: {p.Category} | Giá: {priceText} | Tồn kho: {StockText(p.Stock)}");
                    if (!string.IsNullOrWhiteSpace(p.Material)) sb.Append($" | Chất liệu: {p.Material}");
                    if (!string.IsNullOrWhiteSpace(p.Color)) sb.Append($" | Màu: {p.Color}");
                    if (p.Variants.Count > 0)
                    {
                        var variantText = string.Join("; ", p.Variants.Select(v => $"{v.Name}: {v.Price:N0}đ ({StockText(v.Stock)})"));
                        sb.Append($" | Biến thể: {variantText}");
                    }
                    sb.AppendLine();
                }
            }
            sb.AppendLine();

            sb.AppendLine($"COMBO ĐANG BÁN ({bundles.Count}):");
            if (bundles.Count == 0)
            {
                sb.AppendLine("- Hiện chưa có combo nào.");
            }
            else
            {
                foreach (var b in bundles)
                {
                    var subtotal = b.Items.Sum(i => i.Quantity * priceById.GetValueOrDefault(i.ProductId, 0m));
                    var finalPrice = subtotal * (1 - b.DiscountPercent / 100m);
                    var itemNames = string.Join(", ", b.Items.Select(i =>
                    {
                        var pName = products.FirstOrDefault(p => p.Id == i.ProductId)?.Name ?? $"SP #{i.ProductId}";
                        return $"{pName} x{i.Quantity}";
                    }));
                    sb.AppendLine($"- {b.Name} — giảm thêm {b.DiscountPercent}% khi mua trọn bộ — gồm: {itemNames} — giá sau giảm khoảng {finalPrice:N0}đ");
                }
            }
            sb.AppendLine();

            sb.AppendLine("DỊCH VỤ ĐANG CUNG CẤP:");
            var thiCong = services.Where(s => s.Type == "thi-cong").ToList();
            var thietKe = services.Where(s => s.Type == "thiet-ke").ToList();
            sb.AppendLine($"- Thi công ({thiCong.Count}): " +
                (thiCong.Count == 0 ? "chưa có." : string.Join("; ", thiCong.Select(s => $"{s.Title} — {s.ShortDescription}"))));
            sb.AppendLine($"- Thiết kế ({thietKe.Count}): " +
                (thietKe.Count == 0 ? "chưa có." : string.Join("; ", thietKe.Select(s => $"{s.Title} — {s.ShortDescription}"))));
            sb.AppendLine();

            sb.AppendLine($"DỰ ÁN ĐÃ THỰC HIỆN ({projects.Count}):");
            if (projects.Count == 0)
            {
                sb.AppendLine("- Chưa có dự án nào.");
            }
            else
            {
                foreach (var p in projects)
                    sb.AppendLine($"- {p.Title} — {p.Category}, {p.Location}, năm {p.Year}: {p.ShortDescription}");
            }
            sb.AppendLine();

            sb.AppendLine($"BÀI VIẾT CẨM NANG ĐÃ ĐĂNG (liệt kê {blogPosts.Count} bài mới nhất):");
            if (blogPosts.Count == 0)
            {
                sb.AppendLine("- Chưa có bài viết nào.");
            }
            else
            {
                foreach (var b in blogPosts)
                    sb.AppendLine($"- [{b.Category}] {b.Title}: {b.Excerpt}");
            }

            return sb.ToString();
        }
    }
}
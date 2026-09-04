using Microsoft.AspNetCore.Mvc;
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

        public ChatController(IHttpClientFactory httpClientFactory, IConfiguration config)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
        }

        // System prompt mô tả LuxWood cho AI — SỬA LẠI nội dung này cho đúng chính sách
        // thật của shop trước khi dùng thật (đây là nội dung mẫu).
        // Cố tình dặn AI KHÔNG tự bịa thông tin sản phẩm/giá cụ thể — vì AI không có quyền
        // truy cập dữ liệu Products thật, chỉ nên trả lời câu hỏi chung (FAQ, tư vấn phong
        // cách...), còn tra sản phẩm cụ thể vẫn để nhánh tìm kiếm DB thật xử lý (đã có sẵn).
        private const string SystemPrompt = @"Bạn là trợ lý ảo của LuxWood, một cửa hàng nội thất trực tuyến tại Việt Nam.
Trả lời ngắn gọn (2-4 câu, có thể dài hơn nếu câu hỏi cần giải thích), thân thiện, bằng tiếng Việt.

Bạn có thể trả lời MỌI câu hỏi khách đặt ra, kể cả câu hỏi không liên quan tới nội thất
(kiến thức chung, thời tiết, trò chuyện thông thường...) — không cần từ chối hay né tránh
các câu hỏi ngoài phạm vi cửa hàng.

Khi câu hỏi liên quan tới LuxWood, ưu tiên dùng đúng thông tin chính sách sau:
- Đổi trả: trong vòng 7 ngày kể từ khi nhận hàng, sản phẩm còn nguyên vẹn chưa qua sử dụng.
- Giao hàng: 3-7 ngày làm việc tuỳ khu vực, nội thành 24-48h với hàng có sẵn.
- Thanh toán: hỗ trợ COD (thanh toán khi nhận hàng) và chuyển khoản ngân hàng.
- Giờ hỗ trợ khách hàng: 8:00 - 21:00 tất cả các ngày trong tuần.

QUAN TRỌNG: Bạn KHÔNG có quyền truy cập dữ liệu sản phẩm/giá/tồn kho thật của cửa hàng.
Nếu khách hỏi về MỘT sản phẩm cụ thể (giá, còn hàng không, mẫu mã...), đừng bịa thông tin —
hãy trả lời rằng bạn sẽ không đoán mò, và gợi ý khách dùng nút 'Tìm sản phẩm' hoặc ghé trang
Sản phẩm để xem thông tin chính xác nhất.";

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

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

            var payload = new
            {
                systemInstruction = new { parts = new[] { new { text = SystemPrompt } } },
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
                // Trả nguyên văn lỗi từ Gemini để dễ debug lúc dev (VD: key sai, hết quota free tier...)
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
    }
}
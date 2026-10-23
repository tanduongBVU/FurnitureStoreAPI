using FurnitureStoreAPI.Data;
using FurnitureStoreAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;

namespace FurnitureStoreAPI.Controllers
{
    // 1 lượt trò chuyện trước đó — Client gửi vài lượt gần nhất để AI hiểu ngữ cảnh
    // (VD khách hỏi "còn màu khác không?" ngay sau khi hỏi về 1 sản phẩm).
    public class ChatTurnDto
    {
        public string Role { get; set; } = "user"; // "user" hoặc "bot"
        public string Text { get; set; } = string.Empty;
    }

    public class ChatRequestDto
    {
        public string Message { get; set; } = string.Empty;
        public List<ChatTurnDto>? History { get; set; }
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

        // POST: api/chat — nhận tin nhắn của khách, trả về { reply, products }.
        //   - reply: câu trả lời bằng chữ thường (khung chat không hiển thị Markdown).
        //   - products: các sản phẩm AI THẤY ĐÁNG gợi ý để khách bấm xem — CHỈ có khi khách đang tìm/hỏi
        //     về sản phẩm; chào hỏi, trò chuyện, câu hỏi chung, chính sách... thì danh sách này rỗng.
        // Trước đây Client tự tìm sản phẩm theo từ khoá cho MỌI tin nhắn nên gõ "hi" hay "2" cũng ra sản
        // phẩm (khớp nhầm "hiện đại", "2m"...). Giờ AI quyết định, dựa trên dữ liệu thật bên dưới.
        [HttpPost]
        public async Task<IActionResult> Send([FromBody] ChatRequestDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Message))
                return BadRequest(new { message = "Tin nhắn không được để trống." });
            if (dto.Message.Length > 1000)
                return BadRequest(new { message = "Tin nhắn quá dài, bạn vui lòng nhập ngắn gọn hơn nhé." });

            var apiKey = _config["Gemini:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
                return StatusCode(500, new { message = "Chưa cấu hình Gemini API Key ở Backend (appsettings.json)." });

            // Chat cần chọn sản phẩm ĐÚNG LOẠI nên có thể dùng model mạnh hơn các tính năng phụ (gợi ý giỏ hàng...):
            // đặt "Gemini:ChatModel" trong appsettings.json (VD "gemini-flash-latest"). Không đặt thì dùng Gemini:Model.
            var model = _config["Gemini:ChatModel"];
            if (string.IsNullOrWhiteSpace(model)) model = _config["Gemini:Model"];
            if (string.IsNullOrWhiteSpace(model)) model = "gemini-flash-lite-latest";

            var (dataSnapshot, activeProducts) = await BuildPublicDataSnapshotAsync();

            // System prompt mô tả LuxWood cho AI. AI ĐƯỢC cung cấp dữ liệu THẬT (sản phẩm,
            // combo, dịch vụ, blog — toàn bộ đều là nội dung CÔNG KHAI, an toàn để đưa cho
            // 1 chatbot public không cần đăng nhập). KHÔNG đưa Orders/Contacts/Users vào đây —
            // những bảng đó chỉ dành cho AdminChatController (có [Authorize] bảo vệ).
            var systemPrompt =
$@"Bạn là trợ lý tư vấn của LuxWood, một cửa hàng nội thất trực tuyến tại Việt Nam. Nói
chuyện tự nhiên như một nhân viên tư vấn thực thụ — thân thiện, chuyên nghiệp, nhưng không
máy móc hay lặp lại khuôn mẫu câu. Có thể xưng “mình” hoặc “bên mình”. Trả lời ngắn gọn khi
câu hỏi đơn giản, giải thích kỹ hơn khi cần, luôn bằng tiếng Việt.

Bạn có thể trả lời MỌI câu hỏi khách đặt ra, kể cả câu hỏi không liên quan tới nội thất
(kiến thức chung, thời tiết, trò chuyện thông thường...) — không cần từ chối hay né tránh.
Khi tin nhắn quá ngắn hoặc chưa rõ ý (ví dụ chỉ một con số hay một chữ), đừng đoán mò —
hãy hỏi lại khách muốn nói gì.

Bạn được cung cấp dữ liệu THẬT về sản phẩm, combo, dịch vụ và bài viết Cẩm nang của
LuxWood ngay bên dưới, lấy trực tiếp từ hệ thống tại thời điểm câu hỏi này được gửi lên —
LUÔN dùng đúng thông tin này để trả lời (giá, tồn kho, mô tả...), KHÔNG bịa thêm. Nếu khách
hỏi về sản phẩm/dịch vụ không có trong danh sách bên dưới, hãy nói thật là hiện chưa có mặt
hàng đó, đừng đoán mò.

Khi câu hỏi liên quan tới chính sách chung, dùng đúng thông tin sau:
- Đổi trả: trong vòng 7 ngày kể từ khi nhận hàng, sản phẩm còn nguyên vẹn chưa qua sử dụng.
- Giao hàng: 3-7 ngày làm việc tuỳ khu vực, nội thành 24-48h với hàng có sẵn.
- Thanh toán: hỗ trợ COD (thanh toán khi nhận hàng) và chuyển khoản ngân hàng.
- Giờ hỗ trợ khách hàng: 8:00 - 21:00 tất cả các ngày trong tuần.

QUAN TRỌNG VỀ ĐỊNH DẠNG CÂU TRẢ LỜI: Khung chat chỉ hiển thị CHỮ THƯỜNG, KHÔNG render được
Markdown. TUYỆT ĐỐI KHÔNG dùng ký hiệu **chữ đậm**, *in nghiêng*, # tiêu đề, hay dấu - đầu
dòng kiểu Markdown. Muốn liệt kê, dùng số thứ tự kiểu “1. ”, “2. ” hoặc xuống dòng bình thường.

CHỌN SẢN PHẨM ĐÚNG LOẠI (rất quan trọng): khi khách nói rõ loại sản phẩm, CHỈ chọn sản phẩm thuộc ĐÚNG loại
đó, KHÔNG chọn loại khác dù tên nghe gần giống. Đúng và ít còn hơn nhiều mà lẫn loại khác.
- Dựa vào TÊN và MÔ TẢ để xác định sản phẩm thật sự là loại gì (không chỉ thấy chữ trùng là chọn).
- ""Ghế sofa"" / ""sofa"" thường là sofa để ngồi: sofa văn phòng, sofa nỉ, sofa da, sofa góc, sofa chữ L... KHÁC với
  ""sofa giường"" (sofa kéo ra thành giường, tên thường có chữ ""sofa giường"" hoặc ""sofa bed""). Chỉ chọn sofa giường
  khi khách nhắc tới giường, sofa giường, sofa bed, kéo ra nằm, ngủ.
- Tương tự với mọi loại khác: ghế khác bàn, bàn ăn khác bàn trang điểm khác bàn làm việc, giường khác sofa giường, tủ
  khác kệ... Chỉ chọn loại khác khi khách hỏi cả loại đó hoặc nói chung chung (""đồ phòng khách"").
- Nếu không có sản phẩm nào đúng loại khách cần, nói thật là hiện chưa có và để productIds rỗng; có thể gợi ý loại gần
  giống bằng chữ trong ""reply"" nhưng KHÔNG đưa vào productIds.

ĐỊNH DẠNG ĐẦU RA (bắt buộc): chỉ trả về JSON thuần, đúng dạng, viết theo ĐÚNG THỨ TỰ các khoá dưới đây:
{{""wanted"":""loại sản phẩm khách cần"",""productIds"":[12,5],""reply"":""câu trả lời cho khách""}}
không kèm chữ nào khác bên ngoài JSON.
- ""wanted"": 1 cụm ngắn nêu CHÍNH XÁC loại sản phẩm khách đang cần kèm điều kiện (VD ""ghế sofa ngồi, không phải sofa
  giường"", ""bàn làm việc dưới 5 triệu""). Để chuỗi rỗng """" nếu khách không tìm/hỏi xem sản phẩm. Viết khoá này TRƯỚC để
  tự kiểm tra lại khi chọn.
- ""productIds"": ID các sản phẩm đúng loại ""wanted"" để khách bấm xem (tối đa 4, lấy từ số sau chữ ID trong danh sách
  bên dưới). CHỈ điền khi khách đang tìm, muốn xem hoặc nhờ gợi ý sản phẩm. Với chào hỏi, trò chuyện, câu hỏi chung, hỏi
  chính sách, hỏi số liệu, tin nhắn chưa rõ ý... thì để mảng rỗng []. Không gợi ý sản phẩm đã hết hàng, trừ khi khách hỏi
  đích danh sản phẩm đó.
- ""reply"": câu trả lời bằng chữ thường theo quy tắc trên. Nếu có productIds thì chỉ nhắc tới đúng những sản phẩm đó
  (không nhắc sản phẩm không nằm trong productIds).

{dataSnapshot}";

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

            // Lịch sử trò chuyện: chỉ lấy vài lượt gần nhất (giới hạn độ dài để không tốn token/bị lợi dụng).
            // Gemini yêu cầu lượt đầu là "user" và các lượt xen kẽ → bỏ lượt "model" ở đầu, gộp các lượt liên tiếp cùng vai trò.
            var turns = new List<(string Role, string Text)>();
            foreach (var h in (dto.History ?? new List<ChatTurnDto>()).TakeLast(8))
            {
                var t = (h.Text ?? "").Trim();
                if (t.Length == 0) continue;
                if (t.Length > 500) t = t.Substring(0, 500);
                turns.Add((h.Role == "bot" ? "model" : "user", t));
            }
            turns.Add(("user", dto.Message.Trim()));
            while (turns.Count > 0 && turns[0].Role == "model") turns.RemoveAt(0);

            var merged = new List<(string Role, string Text)>();
            foreach (var t in turns)
            {
                if (merged.Count > 0 && merged[^1].Role == t.Role)
                    merged[^1] = (t.Role, merged[^1].Text + "\n" + t.Text);
                else
                    merged.Add(t);
            }
            var contents = merged
                .Select(t => new { role = t.Role, parts = new[] { new { text = t.Text } } })
                .ToArray();

            var payload = new
            {
                systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
                contents,
                // temperature thấp: chọn sản phẩm ổn định, ít "sáng tạo" lẫn sang loại khác
                generationConfig = new { responseMimeType = "application/json", temperature = 0.2 },
            };

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(25);

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
                // Chỉ ghi log ở server, KHÔNG trả nguyên nội dung lỗi của Gemini ra cho người dùng công khai.
                Console.WriteLine($"[Chat] Gemini trả lỗi HTTP {(int)response.StatusCode}: {body}");
                return StatusCode(502, new { message = "AI trả về lỗi, vui lòng thử lại sau." });
            }

            string? text;
            try
            {
                using var doc = JsonDocument.Parse(body);
                text = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();
            }
            catch
            {
                text = null;
            }

            if (string.IsNullOrWhiteSpace(text))
                return StatusCode(502, new { message = "AI không trả về nội dung hợp lệ." });

            // Tách { reply, productIds } từ JSON AI trả về; nếu AI lỡ trả chữ thường thì dùng luôn làm câu trả lời.
            string reply = text.Trim();
            var productIds = new List<int>();
            try
            {
                var start = text.IndexOf('{');
                var end = text.LastIndexOf('}');
                var json = (start >= 0 && end > start) ? text.Substring(start, end - start + 1) : text;

                using var inner = JsonDocument.Parse(json);
                if (inner.RootElement.ValueKind == JsonValueKind.Object)
                {
                    reply = inner.RootElement.TryGetProperty("reply", out var r) && r.ValueKind == JsonValueKind.String
                        ? (r.GetString() ?? "")
                        : "";

                    if (inner.RootElement.TryGetProperty("productIds", out var ids) && ids.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in ids.EnumerateArray())
                            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var id))
                                productIds.Add(id);
                    }
                }
            }
            catch
            {
                // giữ nguyên reply = text thô, không có sản phẩm
            }

            reply = reply.Replace("**", "").Trim();

            // Chỉ trả những sản phẩm CÓ THẬT và đang bán (loại ID AI bịa), giữ đúng thứ tự AI chọn, tối đa 4.
            var byId = activeProducts.ToDictionary(p => p.Id);
            var products = productIds
                .Distinct()
                .Where(byId.ContainsKey)
                .Select(id => byId[id])
                .Take(4)
                .Select(p => new { p.Id, p.Name, p.Image, p.Price, p.DiscountPercent })
                .ToList();

            if (reply.Length == 0)
            {
                if (products.Count == 0)
                    return StatusCode(502, new { message = "AI không trả về nội dung hợp lệ." });
                reply = "Bạn xem thử các sản phẩm này nhé:";
            }

            return Ok(new { reply, products });
        }

        // Dựng bản tóm tắt dữ liệu CÔNG KHAI (sản phẩm, combo, dịch vụ, blog) — chỉ
        // những bảng an toàn để hiển thị cho bất kỳ ai, vì Controller này KHÔNG có [Authorize]
        // (ChatWidget bên Client gọi được mà không cần đăng nhập).
        // Trả thêm danh sách sản phẩm đang bán để Send() đối chiếu ID AI chọn.
        private async Task<(string Snapshot, List<Product> Products)> BuildPublicDataSnapshotAsync()
        {
            var now = DateTime.Now;

            var products = await _db.Products.Include(p => p.Variants).Where(p => p.IsActive).ToListAsync();
            var bundles = await _db.Bundles.Include(b => b.Items).Where(b => b.IsActive).ToListAsync();
            var services = await _db.Services.Where(s => s.IsActive).OrderBy(s => s.DisplayOrder).ToListAsync();
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
                    sb.Append($"- ID {p.Id} | {p.Name} | Danh mục: {p.Category} | Giá: {priceText} | Tồn kho: {StockText(p.Stock)}");
                    // Mô tả ngắn (bỏ xuống dòng, cắt 140 ký tự) giúp AI hiểu sản phẩm thật sự là loại gì
                    var shortDesc = (p.Description ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
                    if (shortDesc.Length > 140) shortDesc = shortDesc.Substring(0, 140) + "…";
                    if (shortDesc.Length > 0) sb.Append($" | Mô tả: {shortDesc}");
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

            return (sb.ToString(), products);
        }
    }
}
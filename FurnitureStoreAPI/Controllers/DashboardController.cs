using FurnitureStoreAPI.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;

namespace FurnitureStoreAPI.Controllers
{
    // Endpoint riêng phục vụ trang Dashboard (Admin): (1) nhận xét nhanh bằng AI,
    // (2) gợi ý hành động bằng AI. KHÔNG dùng AI để TÍNH số liệu — số liệu tính bằng code C#
    // từ DB, AI chỉ đọc số đã tính sẵn rồi viết nhận xét/gợi ý, để không có rủi ro AI bịa số.
    // [Authorize] giống AdminChatController — chỉ tài khoản đã đăng nhập mới gọi được.
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class DashboardController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly AppDbContext _db;

        public DashboardController(IHttpClientFactory httpClientFactory, IConfiguration config, AppDbContext db)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
            _db = db;
        }

        // GET: api/dashboard/insights?days=14
        [HttpGet("insights")]
        public async Task<IActionResult> GetInsights([FromQuery] int days = 14)
        {
            if (days != 7 && days != 14 && days != 30) days = 14;

            var apiKey = _config["Gemini:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
                return StatusCode(500, new { message = "Chưa cấu hình Gemini API Key ở Backend (appsettings.json)." });

            var model = _config["Gemini:Model"];
            if (string.IsNullOrWhiteSpace(model)) model = "gemini-flash-lite-latest";

            var dataSnapshot = await BuildInsightSnapshotAsync(days);

            var systemPrompt =
$@"Bạn là trợ lý phân tích kinh doanh nội bộ của cửa hàng nội thất LuxWood, viết nhận xét
NGẮN GỌN cho chủ shop/Admin đọc ngay trên trang Dashboard. Đây KHÔNG phải hội thoại — chỉ
viết 1 đoạn nhận xét liền mạch, 3 đến 5 câu, bằng tiếng Việt, giọng điệu như một trợ lý đang
báo cáo nhanh cho sếp: thẳng vào trọng tâm, không chào hỏi, không mở đầu kiểu ""Dưới đây
là..."", không kết thúc kiểu ""Hy vọng giúp ích"".

Bạn được cung cấp số liệu THẬT của hệ thống ngay bên dưới, lấy trực tiếp từ database tại
thời điểm này — LUÔN dựa vào đúng các con số đó, TUYỆT ĐỐI KHÔNG bịa thêm số liệu nào ngoài
phần được cung cấp.

Hãy ưu tiên nêu, theo thứ tự quan trọng giảm dần, nếu dữ liệu cho thấy:
1. Điểm BẤT THƯỜNG đáng chú ý nhất trong số liệu (VD: 1 danh mục hoặc 1 ngày có doanh thu
   cao/thấp bất thường so với phần còn lại — nếu chênh lệch lớn tới mức khó tin, hãy gợi ý
   kiểm tra lại xem có phải do 1 đơn hàng nhập sai giá hoặc dữ liệu thử nghiệm không, thay
   vì khẳng định chắc chắn đó là thật).
2. Xu hướng chính đáng mừng hoặc đáng lo (doanh thu/đơn hàng tăng hay giảm rõ rệt so với
   tháng trước, tỷ lệ huỷ đơn cao bất thường).
3. Một việc CỤ THỂ Admin nên để ý hoặc xử lý sớm (đơn chờ xác nhận tồn đọng, sản phẩm sắp
   hết hàng, liên hệ chưa trả lời...).
Nếu số liệu quá ít hoặc chưa có gì nổi bật để nhận xét, hãy nói thẳng là chưa đủ dữ liệu để
đưa ra nhận xét có ý nghĩa, đừng cố bịa ra nhận xét gượng ép.

QUAN TRỌNG VỀ ĐỊNH DẠNG: Khung hiển thị chỉ render CHỮ THƯỜNG, KHÔNG hỗ trợ Markdown. TUYỆT
ĐỐI KHÔNG dùng **chữ đậm**, *in nghiêng*, # tiêu đề, hay gạch đầu dòng kiểu Markdown — viết
thành câu văn xuôi bình thường, không xuống dòng ngắt đoạn.

{dataSnapshot}";

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

            var payload = new
            {
                systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
                contents = new[]
                {
                    new { role = "user", parts = new[] { new { text = "Viết nhận xét ngắn gọn về tình hình kinh doanh hiện tại." } } }
                },
            };

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);

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

            string? summary;
            try
            {
                using var doc = JsonDocument.Parse(body);
                summary = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();
            }
            catch
            {
                summary = null;
            }

            if (string.IsNullOrWhiteSpace(summary))
                return StatusCode(502, new { message = "AI không trả về nội dung hợp lệ." });

            return Ok(new { summary = summary.Trim(), generatedAt = DateTime.Now });
        }

        // GET: api/dashboard/recommendations
        // Gợi ý HÀNH ĐỘNG cụ thể cho Admin (tối đa 5). Dùng CHUNG BuildInsightSnapshotAsync(14)
        // với mục nhận xét để số liệu AI đọc luôn khớp với số Admin đang thấy.
        // Gemini trả mảng JSON [{ title, detail, priority }]; JSON hỏng thì trả danh sách rỗng
        // (KHÔNG báo lỗi 500) để Dashboard hiện "chưa có gợi ý" thay vì vỡ trang.
        [HttpGet("recommendations")]
        public async Task<IActionResult> GetRecommendations()
        {
            var apiKey = _config["Gemini:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
                return StatusCode(500, new { message = "Chưa cấu hình Gemini API Key ở Backend (appsettings.json)." });

            var model = _config["Gemini:Model"];
            if (string.IsNullOrWhiteSpace(model)) model = "gemini-flash-lite-latest";

            var dataSnapshot = await BuildInsightSnapshotAsync(14);

            var systemPrompt =
$@"Bạn là trợ lý vận hành nội bộ của cửa hàng nội thất LuxWood. Dựa vào số liệu THẬT bên dưới,
hãy đề xuất tối đa 5 HÀNH ĐỘNG CỤ THỂ mà Admin nên làm ngay (VD: xác nhận các đơn đang chờ,
nhập thêm hàng sắp hết, giảm giá hoặc gộp combo cho sản phẩm bán chậm/chưa từng bán, trả lời
liên hệ chưa xử lý, kiểm tra đơn có số liệu bất thường).

QUY TẮC:
- CHỈ dựa vào số liệu được cung cấp, TUYỆT ĐỐI KHÔNG bịa thêm số liệu hay tên sản phẩm.
- Chỉ đề xuất việc mà số liệu thực sự cho thấy cần làm. Không có gì đáng làm thì trả mảng rỗng [].
- Sắp xếp theo mức ưu tiên giảm dần.
- Viết tiếng Việt, ngắn gọn, KHÔNG dùng Markdown.

ĐỊNH DẠNG TRẢ VỀ: ĐÚNG 1 mảng JSON, mỗi phần tử có dạng:
{{ ""title"": ""việc cần làm, dưới 12 từ"", ""detail"": ""1-2 câu giải thích, nêu số liệu liên quan"", ""priority"": ""cao"" | ""trung bình"" | ""thấp"" }}

{dataSnapshot}";

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

            var payload = new
            {
                systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
                contents = new[]
                {
                    new { role = "user", parts = new[] { new { text = "Đưa ra danh sách hành động Admin nên làm." } } }
                },
                generationConfig = new { responseMimeType = "application/json" },
            };

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);

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
                return StatusCode(502, new { message = "AI trả về lỗi.", detail = body });

            var recommendations = new List<object>();
            try
            {
                using var doc = JsonDocument.Parse(body);
                var text = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString() ?? "";

                // Phòng AI kèm chữ thừa: cắt từ "[" đầu tiên đến "]" cuối cùng.
                var start = text.IndexOf('[');
                var end = text.LastIndexOf(']');
                if (start >= 0 && end > start)
                {
                    using var arr = JsonDocument.Parse(text.Substring(start, end - start + 1));
                    foreach (var el in arr.RootElement.EnumerateArray())
                    {
                        if (recommendations.Count >= 5) break;
                        if (el.ValueKind != JsonValueKind.Object) continue;

                        var title = el.TryGetProperty("title", out var t) ? t.GetString() : null;
                        var detail = el.TryGetProperty("detail", out var d) ? d.GetString() : null;
                        var priorityRaw = el.TryGetProperty("priority", out var p) ? p.GetString() : null;
                        if (string.IsNullOrWhiteSpace(title)) continue;

                        recommendations.Add(new
                        {
                            title = title.Trim(),
                            detail = detail?.Trim() ?? "",
                            priority = NormalizePriority(priorityRaw)
                        });
                    }
                }
            }
            catch
            {
                recommendations.Clear();
            }

            return Ok(new { recommendations, generatedAt = DateTime.Now });
        }

        // Dữ liệu 1 ngày bất thường — dùng nội bộ cho endpoint anomalies.
        private sealed record AnomalyDay(
            DateTime Day, decimal Total, decimal Ratio, int OrderCount,
            int TopOrderId, decimal TopOrderTotal, decimal TopOrderShare,
            string TopOrderItems, string Explanation);

        // GET: api/dashboard/anomalies?days=14
        // Tìm ngày có doanh thu VỌT CAO bất thường trên biểu đồ. Việc PHÁT HIỆN do code C# làm
        // (ngày nào > 3 lần mức trung vị của các ngày có doanh thu), AI KHÔNG tự đoán ngày nào
        // bất thường — AI chỉ đọc dữ liệu thật của ngày đó rồi viết giải thích 1-2 câu.
        // Gemini lỗi/trả JSON hỏng thì dùng câu giải thích dựng sẵn bằng code (không bao giờ 500).
        [HttpGet("anomalies")]
        public async Task<IActionResult> GetAnomalies([FromQuery] int days = 14)
        {
            if (days != 7 && days != 14 && days != 30) days = 14;

            var now = DateTime.Now;
            var from = now.Date.AddDays(-(days - 1));

            // Cùng công thức biểu đồ Dashboard.jsx: mọi đơn trừ đơn Huỷ, nhóm theo ngày.
            var orders = await _db.Orders
                .Include(o => o.OrderItems)
                .Where(o => o.Status != "Huỷ" && o.CreatedAt >= from)
                .ToListAsync();
            var productNameById = (await _db.Products.ToListAsync()).ToDictionary(p => p.Id, p => p.Name);

            var dayTotals = new List<(DateTime day, decimal total)>();
            for (int i = days - 1; i >= 0; i--)
            {
                var day = now.Date.AddDays(-i);
                var next = day.AddDays(1);
                var total = orders.Where(o => o.CreatedAt >= day && o.CreatedAt < next).Sum(o => o.Total);
                dayTotals.Add((day, total));
            }

            // Cần ít nhất 3 ngày có doanh thu mới đủ dữ liệu để so sánh.
            var nonZero = dayTotals.Where(d => d.total > 0).Select(d => d.total).OrderBy(x => x).ToList();
            if (nonZero.Count < 3)
                return Ok(new { anomalies = new List<object>(), aiGenerated = false, generatedAt = now });

            var mid = nonZero.Count / 2;
            var median = nonZero.Count % 2 == 1 ? nonZero[mid] : (nonZero[mid - 1] + nonZero[mid]) / 2;
            var threshold = median * 3;

            var details = new List<AnomalyDay>();
            var spikes = dayTotals
                .Where(d => d.total > threshold)
                .OrderByDescending(d => d.total)
                .Take(3)
                .OrderBy(d => d.day)
                .ToList();

            foreach (var (day, total) in spikes)
            {
                var next = day.AddDays(1);
                var dayOrders = orders.Where(o => o.CreatedAt >= day && o.CreatedAt < next).ToList();
                var top = dayOrders.OrderByDescending(o => o.Total).First();
                var share = total > 0 ? top.Total / total * 100 : 0;
                var items = string.Join(", ", top.OrderItems.Take(3).Select(i =>
                    $"{productNameById.GetValueOrDefault(i.ProductId, $"SP #{i.ProductId}")} x{i.Quantity}"));

                var d0 = new AnomalyDay(day, total, total / median, dayOrders.Count,
                    top.Id, top.Total, share, items, "");
                details.Add(d0 with { Explanation = BuildFallbackExplanation(d0, median) });
            }

            var aiGenerated = false;
            if (details.Count > 0)
            {
                try
                {
                    var apiKey = _config["Gemini:ApiKey"];
                    var model = _config["Gemini:Model"];
                    if (string.IsNullOrWhiteSpace(model)) model = "gemini-flash-lite-latest";

                    if (!string.IsNullOrWhiteSpace(apiKey))
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine($"Mức trung vị doanh thu ngày (các ngày có đơn): {median:N0} đ");
                        foreach (var a in details)
                            sb.AppendLine($"- {a.Day:dd/MM}: tổng {a.Total:N0} đ (gấp {a.Ratio:0.#} lần trung vị), {a.OrderCount} đơn; đơn lớn nhất #{a.TopOrderId} = {a.TopOrderTotal:N0} đ chiếm {a.TopOrderShare:0}% doanh thu ngày, gồm: {a.TopOrderItems}");

                        var systemPrompt =
$@"Bạn là trợ lý phân tích kinh doanh của cửa hàng nội thất LuxWood. Dưới đây là các ngày có
doanh thu VỌT CAO bất thường (đã được hệ thống phát hiện sẵn bằng code). Với MỖI ngày, hãy viết
1 đến 2 câu tiếng Việt giải thích vì sao doanh thu cao, chỉ dựa vào số liệu được cung cấp:
nếu 1 đơn chiếm phần lớn doanh thu ngày, hãy nói rõ và gợi ý Admin kiểm tra lại đơn đó (có
thể nhập sai giá hoặc là đơn thử nghiệm) — KHÔNG khẳng định chắc chắn. Nếu doanh thu đến từ
nhiều đơn, hãy nói là do nhiều đơn cùng lúc. TUYỆT ĐỐI KHÔNG bịa số liệu hay tên sản phẩm
ngoài phần được cung cấp. KHÔNG dùng Markdown.

ĐỊNH DẠNG TRẢ VỀ: ĐÚNG 1 mảng JSON, mỗi phần tử:
{{ ""date"": ""dd/MM"" (giữ đúng như số liệu bên dưới), ""explanation"": ""1-2 câu"" }}

{sb}";

                        var payload = new
                        {
                            systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
                            contents = new[]
                            {
                                new { role = "user", parts = new[] { new { text = "Giải thích các ngày doanh thu bất thường." } } }
                            },
                            generationConfig = new { responseMimeType = "application/json" },
                        };

                        var client = _httpClientFactory.CreateClient();
                        client.Timeout = TimeSpan.FromSeconds(15);
                        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
                        var response = await client.PostAsync(
                            url,
                            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));

                        if (response.IsSuccessStatusCode)
                        {
                            var body = await response.Content.ReadAsStringAsync();
                            using var doc = JsonDocument.Parse(body);
                            var text = doc.RootElement
                                .GetProperty("candidates")[0]
                                .GetProperty("content")
                                .GetProperty("parts")[0]
                                .GetProperty("text")
                                .GetString() ?? "";

                            var start = text.IndexOf('[');
                            var end = text.LastIndexOf(']');
                            if (start >= 0 && end > start)
                            {
                                using var arr = JsonDocument.Parse(text.Substring(start, end - start + 1));
                                foreach (var el in arr.RootElement.EnumerateArray())
                                {
                                    if (el.ValueKind != JsonValueKind.Object) continue;
                                    var dateStr = el.TryGetProperty("date", out var dp) ? dp.GetString() : null;
                                    var expl = el.TryGetProperty("explanation", out var ep) ? ep.GetString() : null;
                                    if (string.IsNullOrWhiteSpace(dateStr) || string.IsNullOrWhiteSpace(expl)) continue;

                                    var idx = details.FindIndex(a => a.Day.ToString("dd/MM") == dateStr.Trim());
                                    if (idx >= 0)
                                    {
                                        details[idx] = details[idx] with { Explanation = expl.Trim() };
                                        aiGenerated = true;
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Gemini lỗi/JSON hỏng: giữ nguyên câu giải thích dựng sẵn bằng code.
                }
            }

            return Ok(new
            {
                anomalies = details.Select(a => new
                {
                    date = a.Day.ToString("yyyy-MM-dd"),
                    total = a.Total,
                    ratio = Math.Round(a.Ratio, 1),
                    orderCount = a.OrderCount,
                    topOrderId = a.TopOrderId,
                    topOrderTotal = a.TopOrderTotal,
                    topOrderSharePct = Math.Round(a.TopOrderShare, 0),
                    explanation = a.Explanation
                }),
                aiGenerated,
                generatedAt = now
            });
        }

        // Câu giải thích dự phòng dựng bằng code (không cần AI).
        private static string BuildFallbackExplanation(AnomalyDay a, decimal median)
        {
            var text = $"Doanh thu {a.Total:N0} đ, gấp khoảng {a.Ratio:0.#} lần mức trung vị ngày ({median:N0} đ). ";
            if (a.OrderCount == 1 || a.TopOrderShare >= 50)
                text += $"Đơn #{a.TopOrderId} ({a.TopOrderTotal:N0} đ) chiếm khoảng {a.TopOrderShare:0}% doanh thu ngày này — nên kiểm tra lại đơn này có đúng giá, không phải đơn thử nghiệm.";
            else
                text += $"Ngày này có {a.OrderCount} đơn, doanh thu tăng do nhiều đơn cùng lúc chứ không chỉ một đơn lớn.";
            return text;
        }

        // Ép priority về 1 trong 3 giá trị hợp lệ, mặc định "trung bình" nếu AI trả sai.
        private static string NormalizePriority(string? raw)
        {
            var s = (raw ?? "").Trim().ToLowerInvariant();
            if (s == "cao") return "cao";
            if (s == "thấp" || s == "thap") return "thấp";
            return "trung bình";
        }

        // Dựng bản tóm tắt số liệu — dùng ĐÚNG công thức với Dashboard.jsx (đơn "Hoàn thành"
        // mới tính doanh thu, so sánh tháng này/tháng trước theo lịch, N ngày gần nhất...).
        private async Task<string> BuildInsightSnapshotAsync(int days)
        {
            var now = DateTime.Now;
            var startOfThisMonth = new DateTime(now.Year, now.Month, 1);
            var startOfLastMonth = startOfThisMonth.AddMonths(-1);

            var orders = await _db.Orders.Include(o => o.OrderItems).ToListAsync();
            var products = await _db.Products.ToListAsync();
            var contacts = await _db.Contacts.ToListAsync();

            var completedOrders = orders.Where(o => o.Status == "Hoàn thành").ToList();
            var totalRevenue = completedOrders.Sum(o => o.Total);
            var revenueThisMonth = completedOrders.Where(o => o.CreatedAt >= startOfThisMonth).Sum(o => o.Total);
            var revenueLastMonth = completedOrders.Where(o => o.CreatedAt >= startOfLastMonth && o.CreatedAt < startOfThisMonth).Sum(o => o.Total);

            var ordersThisMonth = orders.Count(o => o.CreatedAt >= startOfThisMonth);
            var ordersLastMonth = orders.Count(o => o.CreatedAt >= startOfLastMonth && o.CreatedAt < startOfThisMonth);

            var cancelledThisMonth = orders.Count(o => o.Status == "Huỷ" && o.CreatedAt >= startOfThisMonth);
            var cancellationRateThisMonth = ordersThisMonth > 0 ? (decimal)cancelledThisMonth / ordersThisMonth * 100 : 0;

            var completedThisMonthList = completedOrders.Where(o => o.CreatedAt >= startOfThisMonth).ToList();
            var aovThisMonth = completedThisMonthList.Count > 0 ? revenueThisMonth / completedThisMonthList.Count : 0;

            var pendingOrdersCount = orders.Count(o => o.Status == "Chờ xác nhận");

            var dayBuckets = new List<(DateTime day, decimal total)>();
            for (int i = days - 1; i >= 0; i--)
            {
                var day = now.Date.AddDays(-i);
                var next = day.AddDays(1);
                var total = orders
                    .Where(o => o.Status != "Huỷ" && o.CreatedAt >= day && o.CreatedAt < next)
                    .Sum(o => o.Total);
                dayBuckets.Add((day, total));
            }

            var categoryById = products.ToDictionary(p => p.Id, p => string.IsNullOrEmpty(p.Category) ? "Khác" : p.Category);
            var revenueByCategory = new Dictionary<string, decimal>();
            foreach (var o in completedOrders)
                foreach (var item in o.OrderItems)
                {
                    var cat = categoryById.GetValueOrDefault(item.ProductId, "Khác");
                    revenueByCategory[cat] = revenueByCategory.GetValueOrDefault(cat) + item.Price * item.Quantity;
                }

            var soldQtyByProduct = new Dictionary<int, int>();
            foreach (var o in completedOrders)
                foreach (var item in o.OrderItems)
                    soldQtyByProduct[item.ProductId] = soldQtyByProduct.GetValueOrDefault(item.ProductId) + item.Quantity;

            var productNameById = products.ToDictionary(p => p.Id, p => p.Name);
            var topSelling = soldQtyByProduct
                .OrderByDescending(kv => kv.Value)
                .Take(3)
                .Select(kv => $"{productNameById.GetValueOrDefault(kv.Key, $"SP #{kv.Key}")} ({kv.Value} đã bán)")
                .ToList();
            var worstSelling = soldQtyByProduct
                .OrderBy(kv => kv.Value)
                .Take(3)
                .Select(kv => $"{productNameById.GetValueOrDefault(kv.Key, $"SP #{kv.Key}")} ({kv.Value} đã bán)")
                .ToList();

            var activeProducts = products.Where(p => p.IsActive).ToList();
            var outOfStockCount = activeProducts.Count(p => p.Stock == 0);
            var lowStockCount = activeProducts.Count(p => p.Stock > 0 && p.Stock <= 3);
            var neverSoldCount = activeProducts.Count(p => !soldQtyByProduct.ContainsKey(p.Id));
            var newContactsCount = contacts.Count(c => c.Status == "Mới");

            var sb = new StringBuilder();
            sb.AppendLine($"=== SỐ LIỆU DASHBOARD LUXWOOD (cập nhật lúc {now:dd/MM/yyyy HH:mm}) ===");
            sb.AppendLine();
            sb.AppendLine("TỔNG QUAN:");
            sb.AppendLine($"- Tổng doanh thu từ trước đến nay (chỉ đơn Hoàn thành): {totalRevenue:N0} đ");
            sb.AppendLine($"- Doanh thu tháng này: {revenueThisMonth:N0} đ — Tháng trước: {revenueLastMonth:N0} đ");
            sb.AppendLine($"- Số đơn phát sinh tháng này: {ordersThisMonth} — Tháng trước: {ordersLastMonth}");
            sb.AppendLine($"- Giá trị đơn trung bình tháng này: {aovThisMonth:N0} đ");
            sb.AppendLine($"- Tỷ lệ đơn huỷ tháng này: {cancellationRateThisMonth:0.0}% ({cancelledThisMonth}/{ordersThisMonth} đơn)");
            sb.AppendLine($"- Số đơn đang CHỜ XÁC NHẬN (chưa xử lý): {pendingOrdersCount}");
            sb.AppendLine();

            sb.AppendLine($"DOANH THU THEO TỪNG NGÀY ({days} ngày gần nhất, mọi đơn trừ đơn Huỷ):");
            foreach (var (day, total) in dayBuckets)
                sb.AppendLine($"- {day:dd/MM}: {total:N0} đ");
            sb.AppendLine();

            sb.AppendLine("DOANH THU THEO DANH MỤC (chỉ đơn Hoàn thành):");
            if (revenueByCategory.Count == 0)
            {
                sb.AppendLine("- Chưa có dữ liệu.");
            }
            else
            {
                foreach (var kv in revenueByCategory.OrderByDescending(kv => kv.Value))
                    sb.AppendLine($"- {kv.Key}: {kv.Value:N0} đ");
            }
            sb.AppendLine();

            sb.AppendLine("SẢN PHẨM BÁN CHẠY NHẤT (theo số lượng thực bán): " +
                (topSelling.Count == 0 ? "chưa có dữ liệu." : string.Join(", ", topSelling)));
            sb.AppendLine("SẢN PHẨM BÁN CHẬM NHẤT (trong số đã có ít nhất 1 lượt bán): " +
                (worstSelling.Count == 0 ? "chưa có dữ liệu." : string.Join(", ", worstSelling)));
            sb.AppendLine();

            sb.AppendLine("TÌNH TRẠNG KHO & LIÊN HỆ:");
            sb.AppendLine($"- Sản phẩm đang bán: {activeProducts.Count}, trong đó hết hàng: {outOfStockCount}, sắp hết hàng (≤3): {lowStockCount}");
            sb.AppendLine($"- Sản phẩm đang bán nhưng CHƯA TỪNG có lượt bán nào: {neverSoldCount}");
            sb.AppendLine($"- Liên hệ MỚI chưa xử lý: {newContactsCount}");

            return sb.ToString();
        }
    }
}
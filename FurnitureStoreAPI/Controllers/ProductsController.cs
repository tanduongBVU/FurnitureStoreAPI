using FurnitureStoreAPI.Data;
using FurnitureStoreAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace FurnitureStoreAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProductsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;

        // ── TĂNG TỐC "Có thể bạn cũng thích" ──────────────────────────────────────────
        // Gọi Gemini mất vài giây nên: (1) NHỚ kết quả AI trong 15 phút theo (người dùng + giỏ
        // hàng) — lần sau trả ngay; (2) nếu Gemini lỗi/chậm thì NGHỈ gọi AI 2 phút, trong lúc đó
        // dùng gợi ý theo danh mục (tức thì) thay vì bắt khách chờ timeout lần nữa.
        // Chỉ lưu (id, lý do): tồn kho/giá vẫn đọc mới từ database ở mỗi lần.
        private static readonly ConcurrentDictionary<string, (DateTime Expires, List<(int Id, string Reason)> Picks)> RecoCache = new();
        private static DateTime _aiDownUntil = DateTime.MinValue;
        // Ghi chú chẩn đoán (ASCII) về lần gọi AI gần nhất — trả kèm trong header X-Reco-Debug.
        private static string _aiNote = "not called yet";
        public ProductsController(AppDbContext context, IHttpClientFactory httpClientFactory, IConfiguration config)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _config = config;
        }

        // reasons (tuỳ chọn): lý do gợi ý theo từng sản phẩm — chỉ endpoint recommendations truyền
        // vào, các endpoint khác để null (trường "reason" trả về null, Client không dùng tới).
        private async Task<List<object>> AttachRatingsAsync(List<Product> products, Dictionary<int, string>? reasons = null)
        {
            if (products.Count == 0) return new List<object>();

            var productIds = products.Select(p => p.Id).ToList();

            var ratingStats = await _context.Reviews
                .Where(r => productIds.Contains(r.ProductId))
                .GroupBy(r => r.ProductId)
                .Select(g => new { ProductId = g.Key, Avg = g.Average(r => r.Rating), Count = g.Count() })
                .ToDictionaryAsync(x => x.ProductId, x => x);

            var variants = await _context.ProductVariants
                .Where(v => productIds.Contains(v.ProductId))
                .ToListAsync();

            return products.Select(p =>
            {
                ratingStats.TryGetValue(p.Id, out var stat);

                string? reason = null;
                reasons?.TryGetValue(p.Id, out reason);

                // Lấy .Name TRỰC TIẾP từ đối tượng ProductVariant (đã load vào bộ nhớ ở
                // trên) — property Name là [NotMapped]/tính toán, KHÔNG dùng lại được
                // trong biểu thức LINQ dịch sang SQL, nhưng ở đây variants đã là List
                // trong bộ nhớ (đã ToListAsync xong) nên gọi thẳng .Name bình thường.
                var pVariants = variants
                    .Where(v => v.ProductId == p.Id)
                    .Select(v => new { v.Id, v.Material, v.Color, v.Name, v.Price, v.Stock })
                    .ToList();

                return (object)new
                {
                    p.Id,
                    p.Name,
                    p.Category,
                    p.Price,
                    p.Description,
                    p.Image,
                    p.Images,
                    p.Stock,
                    p.IsBestSeller,
                    p.IsActive,
                    p.CreatedAt,
                    p.DiscountPercent,
                    p.Material,
                    p.Color,
                    AverageRating = stat != null ? Math.Round(stat.Avg, 1) : 0,
                    ReviewCount = stat?.Count ?? 0,
                    Variants = pVariants,
                    HasVariants = pVariants.Count > 0,
                    DisplayPrice = pVariants.Count > 0 ? pVariants.Min(v => v.Price) : p.Price,
                    Reason = reason,
                };
            }).ToList();
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var products = await _context.Products
                .Where(p => p.IsActive)
                .ToListAsync();
            return Ok(await AttachRatingsAsync(products));
        }

        [HttpGet("sale")]
        public async Task<IActionResult> GetOnSale()
        {
            var products = await _context.Products
                .Where(p => p.IsActive && p.DiscountPercent > 0)
                .ToListAsync();
            return Ok(await AttachRatingsAsync(products));
        }

        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string q)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
                return Ok(new List<object>());

            var keyword = q.Trim().ToLower();
            var products = await _context.Products
                .Where(p => p.IsActive && p.Name.ToLower().Contains(keyword))
                .OrderBy(p => p.Name)
                .Take(5)
                .ToListAsync();

            return Ok(await AttachRatingsAsync(products));
        }

        [HttpGet("{id}/related")]
        public async Task<IActionResult> GetRelated(int id)
        {
            const int RELATED_COUNT = 4;

            var product = await _context.Products.FindAsync(id);
            if (product == null) return NotFound();

            var sameCategory = await _context.Products
                .Where(p => p.IsActive && p.Id != id && p.Category == product.Category)
                .OrderByDescending(p => p.IsBestSeller)
                .ThenByDescending(p => p.CreatedAt)
                .Take(RELATED_COUNT)
                .ToListAsync();

            if (sameCategory.Count < RELATED_COUNT)
            {
                var excludeIds = sameCategory.Select(p => p.Id).ToList();
                excludeIds.Add(id);

                var filler = await _context.Products
                    .Where(p => p.IsActive && !excludeIds.Contains(p.Id))
                    .OrderByDescending(p => p.IsBestSeller)
                    .ThenByDescending(p => p.CreatedAt)
                    .Take(RELATED_COUNT - sameCategory.Count)
                    .ToListAsync();

                sameCategory.AddRange(filler);
            }

            return Ok(await AttachRatingsAsync(sameCategory));
        }

        // GET: api/products/recommendations?cartIds=1,2,3
        // Gợi ý "Có thể bạn cũng thích" ở trang Giỏ hàng — CHỈ gợi ý sản phẩm LIÊN QUAN tới
        // những gì khách đã mua và đang để trong giỏ:
        //   - Tín hiệu = sản phẩm ĐÃ MUA (đơn không bị huỷ) + sản phẩm đang trong giỏ.
        //   - userId lấy từ TOKEN đăng nhập, KHÔNG tin tham số Client gửi (tránh xem lịch sử
        //     mua của người khác). Chưa đăng nhập thì chỉ có tín hiệu từ giỏ hàng.
        //   - Không có tín hiệu nào (khách vãng lai + giỏ trống) → trả MẢNG RỖNG, Client ẩn mục này.
        //   - Loại trừ: đã có trong giỏ, hết hàng, đang ẩn. Sản phẩm đã mua vẫn có thể được gợi ý.
        //   - Gemini chọn tối đa 8 sản phẩm liên quan, mỗi sản phẩm kèm 1 câu lý do ("reason").
        //     AI lỗi → dự phòng theo danh mục. KHÔNG lấp đầy bằng sản phẩm bán chạy chung chung:
        //     không có gì liên quan thì trả ít hơn hoặc rỗng.
        [HttpGet("recommendations")]
        public async Task<IActionResult> GetRecommendations([FromQuery] string? cartIds)
        {
            const int COUNT = 8;

            var cartProductIds = ParseIds(cartIds);

            int? userId = null;
            if (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
                userId = uid;

            var purchasedIds = new List<int>();
            if (userId.HasValue)
            {
                purchasedIds = await _context.OrderItems
                    .Where(oi => oi.Order != null && oi.Order.UserId == userId.Value && oi.Order.Status != "Huỷ")
                    .Select(oi => oi.ProductId)
                    .Distinct()
                    .ToListAsync();
            }

            // Không có tín hiệu nào → không có gì để "liên quan" → trả rỗng.
            if (purchasedIds.Count == 0 && cartProductIds.Count == 0)
            {
                Response.Headers["X-Reco-Debug"] = "no-signal (not logged in or no purchase history, and cart empty)";
                return Ok(new List<object>());
            }

            // Sản phẩm làm tín hiệu: lấy cả khi nó đã bị ẩn (khách vẫn từng mua nó).
            var signalIds = purchasedIds.Concat(cartProductIds).Distinct().ToList();
            var signalProducts = await _context.Products.Where(p => signalIds.Contains(p.Id)).ToListAsync();
            var purchasedProducts = signalProducts.Where(p => purchasedIds.Contains(p.Id)).ToList();
            var cartProducts = signalProducts.Where(p => cartProductIds.Contains(p.Id)).ToList();

            // Chỉ loại sản phẩm đang có trong giỏ hàng.
            // Không loại sản phẩm đã mua: khách vẫn có thể thấy lại sản phẩm đó trong gợi ý.
            var excluded = new HashSet<int>(cartProductIds);

            var candidates = await _context.Products
                .Where(p => p.IsActive && p.Stock > 0 && !excluded.Contains(p.Id))
                .ToListAsync();
            if (candidates.Count == 0)
            {
                Response.Headers["X-Reco-Debug"] = "no-candidates (every other product is hidden, out of stock or in cart)";
                return Ok(new List<object>());
            }

            // Bao gồm đầy đủ ID sản phẩm đã mua trong cache key để lịch sử mua khác nhau
            // không dùng nhầm kết quả gợi ý của nhau.
            var cacheKey = $"v2|{userId?.ToString() ?? "guest"}|{string.Join(",", purchasedIds.OrderBy(x => x))}|{string.Join(",", cartProductIds.OrderBy(x => x))}";
            List<(Product Product, string Reason)>? picks = null;
            var source = "";

            // 1) Có kết quả AI đã nhớ → dùng luôn (lọc lại theo ứng viên hiện tại để loại món vừa hết hàng/vào giỏ)
            if (RecoCache.TryGetValue(cacheKey, out var cached) && cached.Expires > DateTime.UtcNow)
            {
                var byId = candidates.ToDictionary(p => p.Id);
                picks = cached.Picks
                    .Where(x => byId.ContainsKey(x.Id))
                    .Select(x => (byId[x.Id], x.Reason))
                    .ToList();
                source = "cache";
            }

            // 2) Chưa có → gọi AI (trừ khi AI đang "nghỉ" vì vừa lỗi)
            if (picks == null && DateTime.UtcNow >= _aiDownUntil)
            {
                var ai = await TryGetAiRecommendationsAsync(candidates, purchasedProducts, cartProducts, COUNT);
                if (ai != null)
                {
                    // Chỉ nhớ khi AI CÓ gợi ý — kết quả rỗng không được nhớ, tránh "kẹt" rỗng 15 phút
                    if (ai.Count > 0)
                    {
                        if (RecoCache.Count > 500) RecoCache.Clear(); // tránh phình bộ nhớ
                        RecoCache[cacheKey] = (DateTime.UtcNow.AddMinutes(15), ai.Select(x => (x.Product.Id, x.Reason)).ToList());
                    }
                    picks = ai;
                    source = "ai";
                }
                else
                {
                    _aiDownUntil = DateTime.UtcNow.AddMinutes(2);
                }
            }

            // 3) AI không dùng được → dự phòng theo danh mục (tức thì)
            if (picks == null)
            {
                picks = BuildRuleBasedRecommendations(candidates, purchasedProducts, cartProducts, COUNT);
                source = DateTime.UtcNow < _aiDownUntil ? "rule-based (AI resting 2 min)" : "rule-based";
            }

            // Header chẩn đoán — xem trong DevTools > Network > recommendations > Headers
            Response.Headers["X-Reco-Debug"] =
                $"source={source}; candidates={candidates.Count}; picks={picks.Count}; signals(purchased={purchasedProducts.Count},cart={cartProducts.Count}); {_aiNote}";

            var reasons = picks.ToDictionary(x => x.Product.Id, x => x.Reason);
            return Ok(await AttachRatingsAsync(picks.Select(x => x.Product).ToList(), reasons));
        }

        private static List<int> ParseIds(string? raw) =>
            (raw ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s.Trim(), out var n) ? n : (int?)null)
                .Where(n => n.HasValue)
                .Select(n => n!.Value)
                .Distinct()
                .ToList();

        // Gọi Gemini thật để chọn sản phẩm LIÊN QUAN trong danh sách ứng viên.
        //   - Trả về null  = AI không dùng được (thiếu key, lỗi mạng, JSON hỏng...) → nơi gọi
        //     chuyển sang phương án dự phòng.
        //   - Trả về danh sách rỗng = AI chạy tốt và kết luận KHÔNG có sản phẩm nào liên quan →
        //     tôn trọng kết quả đó, không lấp đầy bằng thứ khác.
        private async Task<List<(Product Product, string Reason)>?> TryGetAiRecommendationsAsync(
            List<Product> candidates, List<Product> purchasedProducts, List<Product> cartProducts, int count)
        {
            var apiKey = _config["Gemini:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey)) { _aiNote = "AI: missing Gemini:ApiKey"; return null; }

            var model = _config["Gemini:Model"];
            if (string.IsNullOrWhiteSpace(model)) model = "gemini-flash-lite-latest";

            var catalogLines = candidates.Select(p =>
            {
                var priceAfter = p.Price * (1 - p.DiscountPercent / 100m);
                return $"ID {p.Id} | {p.Name} | Danh mục: {p.Category} | Chất liệu: {p.Material ?? "—"} | Màu: {p.Color ?? "—"} | Giá: {priceAfter:N0}đ";
            });

            var purchasedText = purchasedProducts.Count > 0
                ? string.Join(", ", purchasedProducts.Select(p => $"{p.Name} ({p.Category})"))
                : "chưa có";

            var cartText = cartProducts.Count > 0
                ? string.Join(", ", cartProducts.Select(p => $"{p.Name} ({p.Category})"))
                : "giỏ hàng đang trống";

            var systemPrompt =
$@"Bạn là hệ thống gợi ý sản phẩm cho cửa hàng nội thất LuxWood, hiển thị ở trang giỏ hàng.
Nhiệm vụ: dựa trên những sản phẩm khách ĐÃ MUA và đang để trong GIỎ HÀNG, chọn tối đa {count} sản phẩm LIÊN QUAN TRỰC TIẾP từ danh sách ứng viên bên dưới.

""Liên quan"" nghĩa là: dùng cùng phòng/không gian, cùng bộ hoặc phong cách/chất liệu/màu, hoặc thường được mua kèm (ví dụ: đã mua sofa thì gợi ý bàn trà, kệ TV; đã mua giường thì gợi ý tủ đầu giường).

QUY TẮC BẮT BUỘC:
- CHỈ chọn ID có trong danh sách ứng viên, không bịa ID.
- ĐƯỢC PHÉP gợi ý lại sản phẩm khách đã mua trước đây nếu sản phẩm đó phù hợp; không loại sản phẩm chỉ vì khách đã mua.
- KHÔNG chọn sản phẩm chỉ vì bán chạy hay đang giảm giá. Nếu không có sản phẩm nào thực sự liên quan, trả về mảng rỗng [].
- Chọn ít hơn {count} cũng được: ưu tiên đúng hơn đủ số lượng.
- Với mỗi sản phẩm, viết 1 câu lý do ngắn bằng tiếng Việt (tối đa 90 ký tự) nói rõ nó liên quan tới sản phẩm nào của khách. Không bịa giá hay khuyến mãi.
- CHỈ trả về JSON thuần, đúng dạng: [{{""id"":12,""reason"":""Hợp với sofa bạn đã chọn""}}]. Không kèm chữ nào khác, không dùng markdown.

DANH SÁCH ỨNG VIÊN:
{string.Join("\n", catalogLines)}

KHÁCH ĐÃ MUA: {purchasedText}
ĐANG TRONG GIỎ HÀNG: {cartText}";

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
            var payload = new
            {
                systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
                contents = new[]
                {
                    new { role = "user", parts = new[] { new { text = "Hãy chọn các sản phẩm liên quan nhất và trả về đúng định dạng JSON đã yêu cầu." } } }
                },
                generationConfig = new { responseMimeType = "application/json" },
            };

            var client = _httpClientFactory.CreateClient();
            // Gợi ý chỉ là phần BỔ TRỢ của trang giỏ hàng — timeout ngắn để Gemini chậm cũng
            // không bắt khách chờ lâu (đã có phương án dự phòng + cơ chế nghỉ 2 phút khi lỗi).
            client.Timeout = TimeSpan.FromSeconds(6);

            string body;
            try
            {
                var response = await client.PostAsync(
                    url,
                    new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
                if (!response.IsSuccessStatusCode) { _aiNote = $"AI: Gemini returned HTTP {(int)response.StatusCode}"; return null; }
                body = await response.Content.ReadAsStringAsync();
            }
            catch
            {
                _aiNote = "AI: network error or timeout";
                return null;
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
                return null;
            }

            if (string.IsNullOrWhiteSpace(text)) { _aiNote = "AI: empty response text"; return null; }

            var byId = candidates.ToDictionary(p => p.Id);
            var picked = new List<(Product Product, string Reason)>();

            try
            {
                // AI đôi khi vẫn kèm chữ thừa dù đã dặn kỹ — cắt lấy đúng đoạn từ "[" tới "]".
                var start = text.IndexOf('[');
                var end = text.LastIndexOf(']');
                var jsonPart = (start >= 0 && end > start) ? text.Substring(start, end - start + 1) : text;

                using var arr = JsonDocument.Parse(jsonPart);
                if (arr.RootElement.ValueKind != JsonValueKind.Array) return null;

                foreach (var el in arr.RootElement.EnumerateArray())
                {
                    if (picked.Count >= count) break;

                    var id = 0;
                    var reason = "";
                    if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
                    {
                        id = n;
                    }
                    else if (el.ValueKind == JsonValueKind.Object
                             && el.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var n2))
                    {
                        id = n2;
                        if (el.TryGetProperty("reason", out var rEl) && rEl.ValueKind == JsonValueKind.String)
                            reason = rEl.GetString() ?? "";
                    }
                    else continue;

                    // ID phải thuộc danh sách ứng viên (đã loại sản phẩm trong giỏ/hết hàng/đang ẩn).
                    if (!byId.TryGetValue(id, out var product)) continue;
                    if (picked.Any(x => x.Product.Id == id)) continue;

                    reason = reason.Trim();
                    if (reason.Length > 140) reason = reason.Substring(0, 140).TrimEnd() + "…";
                    if (reason.Length == 0) reason = $"Cùng nhóm {product.Category} với sản phẩm của bạn";

                    picked.Add((product, reason));
                }
            }
            catch
            {
                _aiNote = "AI: could not parse JSON";
                return null;
            }

            _aiNote = $"AI: ok ({picked.Count} picks)";
            return picked;
        }

        // Phương án dự phòng KHÔNG dùng AI — dùng khi Gemini lỗi/chậm/trả JSON hỏng. Sản phẩm đã mua
        // vẫn có thể xuất hiện nếu còn hàng, đang hoạt động và không nằm trong giỏ. Chỉ lấy
        // sản phẩm CÙNG DANH MỤC với thứ khách đã mua / đang trong giỏ (ưu tiên bán chạy, mới
        // nhất). Không có sản phẩm cùng danh mục thì trả rỗng — KHÔNG lấp bằng sản phẩm khác.
        private static List<(Product Product, string Reason)> BuildRuleBasedRecommendations(
            List<Product> candidates, List<Product> purchasedProducts, List<Product> cartProducts, int count)
        {
            var categories = new HashSet<string>(
                purchasedProducts.Concat(cartProducts)
                    .Where(p => !string.IsNullOrEmpty(p.Category))
                    .Select(p => p.Category));

            return candidates
                .Where(p => categories.Contains(p.Category))
                .OrderByDescending(p => p.IsBestSeller)
                .ThenByDescending(p => p.CreatedAt)
                .Take(count)
                .Select(p =>
                {
                    var bought = purchasedProducts.FirstOrDefault(s => s.Category == p.Category && s.Id != p.Id);
                    if (bought != null) return (p, $"Hợp với “{bought.Name}” bạn đã mua");
                    var inCart = cartProducts.FirstOrDefault(s => s.Category == p.Category && s.Id != p.Id);
                    return (p, inCart != null
                        ? $"Cùng danh mục với “{inCart.Name}” trong giỏ"
                        : $"Cùng danh mục {p.Category}");
                })
                .ToList();
        }

        // Kiểm tra nội dung "Thông tin chi tiết" (HTML từ trình soạn thảo bên Admin).
        // Trả về câu báo lỗi, hoặc null nếu hợp lệ.
        //   - Giới hạn độ dài để tránh nhét nội dung quá lớn vào database.
        //   - Không cho ảnh nhúng thẳng dạng base64 (data:image/...) vì làm nội dung phình rất to —
        //     ảnh phải được tải lên qua /api/Upload (trình soạn thảo bên Admin đã làm sẵn việc này).
        private static string? ValidateDetailContent(string? html)
        {
            if (string.IsNullOrEmpty(html)) return null;
            if (html.Length > 200_000)
                return "Nội dung thông tin chi tiết quá dài (tối đa 200.000 ký tự).";
            if (html.Contains("data:image/", StringComparison.OrdinalIgnoreCase))
                return "Thông tin chi tiết có ảnh dán trực tiếp. Vui lòng xoá ảnh đó và chèn lại bằng nút ảnh (tải lên từ máy) của trình soạn thảo.";
            return null;
        }

        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpGet("all")]
        public async Task<IActionResult> GetAll()
        {
            var products = await _context.Products.ToListAsync();
            return Ok(products);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var product = await _context.Products
                .Include(p => p.Variants)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (product == null) return NotFound();
            return Ok(product);
        }

        // POST: api/products
        // MỚI: nếu có biến thể, kiểm tra tổng Stock các biến thể KHÔNG được vượt quá
        // Stock tổng của sản phẩm gốc — Stock tổng coi như "sức chứa tối đa", các biến
        // thể cộng lại không được tràn ra ngoài con số đó.
        //
        // PHÂN QUYỀN: Admin và Nhân viên có quyền NHƯ NHAU với sản phẩm — thêm, sửa (kể cả giá, giảm giá, biến thể), ẩn/hiện.
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpPost]
        public async Task<IActionResult> Create(Product product)
        {
            if (product.Variants.Count > 0)
            {
                var totalVariantStock = product.Variants.Sum(v => v.Stock);
                if (totalVariantStock > product.Stock)
                    return BadRequest(new
                    {
                        message = $"Tổng tồn kho các biến thể ({totalVariantStock}) không được vượt quá tồn kho tổng ({product.Stock})."
                    });
            }

            var detailError = ValidateDetailContent(product.DetailContent);
            if (detailError != null) return BadRequest(new { message = detailError });

            product.CreatedAt = DateTime.Now;
            product.IsActive = true;
            foreach (var v in product.Variants) v.Id = 0;

            _context.Products.Add(product);
            await _context.SaveChangesAsync();
            return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
        }

        // PUT: api/products/5
        // PHÂN QUYỀN: Admin và Nhân viên có quyền NHƯ NHAU — sửa được mọi thông tin, kể cả giá, giảm giá và giá biến thể.
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, Product product)
        {
            if (id != product.Id) return BadRequest();

            var detailError = ValidateDetailContent(product.DetailContent);
            if (detailError != null) return BadRequest(new { message = detailError });

            var existing = await _context.Products.FindAsync(id);
            if (existing == null) return NotFound();

            if (product.Variants.Count > 0)
            {
                var totalVariantStock = product.Variants.Sum(v => v.Stock);
                if (totalVariantStock > product.Stock)
                    return BadRequest(new
                    {
                        message = $"Tổng tồn kho các biến thể ({totalVariantStock}) không được vượt quá tồn kho tổng ({product.Stock})."
                    });
            }

            existing.Name = product.Name;
            existing.Category = product.Category;
            existing.Price = product.Price;
            existing.Description = product.Description;
            existing.Image = product.Image;
            existing.Images = product.Images;
            existing.Stock = product.Stock;
            existing.IsBestSeller = product.IsBestSeller;
            existing.DiscountPercent = product.DiscountPercent;
            existing.Material = product.Material;
            existing.Color = product.Color;
            existing.DetailContent = product.DetailContent;

            var oldVariants = _context.ProductVariants.Where(v => v.ProductId == id);
            _context.ProductVariants.RemoveRange(oldVariants);
            foreach (var v in product.Variants)
            {
                _context.ProductVariants.Add(new ProductVariant
                {
                    ProductId = id,
                    Material = v.Material,
                    Color = v.Color,
                    Price = v.Price,
                    Stock = v.Stock,
                });
            }

            await _context.SaveChangesAsync();
            return NoContent();
        }

        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null) return NotFound();

            product.IsActive = false;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpPatch("{id}/restore")]
        public async Task<IActionResult> Restore(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null) return NotFound();

            product.IsActive = true;
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}
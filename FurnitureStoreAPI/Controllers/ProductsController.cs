using FurnitureStoreAPI.Data;
using FurnitureStoreAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
        public ProductsController(AppDbContext context, IHttpClientFactory httpClientFactory, IConfiguration config)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _config = config;
        }

        private async Task<List<object>> AttachRatingsAsync(List<Product> products)
        {
            if (products.Count == 0) return new List<object>();

            var productIds = products.Select(p => p.Id).ToList();

            var ratingStats = await _context.Reviews
                .Where(r => productIds.Contains(r.ProductId) && r.IsApproved)
                .GroupBy(r => r.ProductId)
                .Select(g => new { ProductId = g.Key, Avg = g.Average(r => r.Rating), Count = g.Count() })
                .ToDictionaryAsync(x => x.ProductId, x => x);

            var variants = await _context.ProductVariants
                .Where(v => productIds.Contains(v.ProductId))
                .ToListAsync();

            return products.Select(p =>
            {
                ratingStats.TryGetValue(p.Id, out var stat);

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

        // GET: api/products/recommendations?userId=5&cartIds=1,2,3
        // Gợi ý sản phẩm ở trang Giỏ hàng — GỌI THẬT Gemini (cùng cách ChatController đang
        // gọi): đưa cho AI toàn bộ danh sách sản phẩm đang bán (kèm ID), lịch sử đã mua (nếu
        // đăng nhập) và sản phẩm đang trong giỏ, AI trả về ĐÚNG 1 mảng JSON các ID gợi ý.
        // AI gọi ra mạng ngoài nên có thể chậm/lỗi — nếu vậy, TỰ ĐỘNG rơi về phương án dự
        // phòng BuildRuleBasedRecommendations (tự tính theo danh mục, không cần AI), để
        // phần gợi ý ở trang giỏ hàng KHÔNG BAO GIỜ trống trơn hay làm trang bị treo.
        [HttpGet("recommendations")]
        public async Task<IActionResult> GetRecommendations([FromQuery] int? userId, [FromQuery] string? cartIds)
        {
            const int COUNT = 8;

            var cartProductIds = ParseIds(cartIds);
            var activeProducts = await _context.Products.Where(p => p.IsActive).ToListAsync();

            List<Product> purchasedProducts = new();
            if (userId.HasValue)
            {
                var purchasedIds = await _context.OrderItems
                    .Where(oi => oi.Order != null && oi.Order.UserId == userId.Value)
                    .Select(oi => oi.ProductId)
                    .Distinct()
                    .ToListAsync();
                purchasedProducts = activeProducts.Where(p => purchasedIds.Contains(p.Id)).ToList();
            }

            var cartProducts = activeProducts.Where(p => cartProductIds.Contains(p.Id)).ToList();

            var aiResult = await TryGetAiRecommendationsAsync(activeProducts, purchasedProducts, cartProducts, cartProductIds, COUNT);

            var result = aiResult.Count > 0
                ? aiResult
                : BuildRuleBasedRecommendations(activeProducts, purchasedProducts, cartProducts, cartProductIds, COUNT);

            return Ok(await AttachRatingsAsync(result));
        }

        private static List<int> ParseIds(string? raw) =>
            (raw ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s.Trim(), out var n) ? n : (int?)null)
                .Where(n => n.HasValue)
                .Select(n => n!.Value)
                .Distinct()
                .ToList();

        // Gọi Gemini thật để chọn sản phẩm gợi ý. Trả về danh sách RỖNG (không throw) ở MỌI
        // trường hợp lỗi — thiếu API key, mất mạng, AI trả JSON hỏng, model chọn toàn ID
        // không tồn tại... — để nơi gọi (GetRecommendations) biết mà tự chuyển sang phương
        // án dự phòng theo quy tắc, không để lỗi này làm hỏng cả trang giỏ hàng.
        private async Task<List<Product>> TryGetAiRecommendationsAsync(
            List<Product> activeProducts, List<Product> purchasedProducts, List<Product> cartProducts,
            List<int> cartProductIds, int count)
        {
            var apiKey = _config["Gemini:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey)) return new List<Product>();
            if (activeProducts.Count == 0) return new List<Product>();

            var model = _config["Gemini:Model"];
            if (string.IsNullOrWhiteSpace(model)) model = "gemini-flash-lite-latest";

            var catalogLines = activeProducts.Select(p =>
            {
                var priceAfter = p.Price * (1 - p.DiscountPercent / 100m);
                return $"ID {p.Id} | {p.Name} | Danh mục: {p.Category} | Giá: {priceAfter:N0}đ | " +
                       $"Tồn kho: {(p.Stock > 0 ? p.Stock.ToString() : "hết hàng")} | Bán chạy: {(p.IsBestSeller ? "có" : "không")}";
            });

            var purchasedText = purchasedProducts.Count > 0
                ? string.Join(", ", purchasedProducts.Select(p => $"{p.Name} (ID {p.Id}, {p.Category})"))
                : "chưa có (khách vãng lai hoặc chưa từng mua hàng)";

            var cartText = cartProducts.Count > 0
                ? string.Join(", ", cartProducts.Select(p => $"{p.Name} (ID {p.Id}, {p.Category})"))
                : "giỏ hàng đang trống";

            var systemPrompt =
$@"Bạn là hệ thống gợi ý sản phẩm cho cửa hàng nội thất LuxWood. Nhiệm vụ DUY NHẤT: dựa
trên lịch sử mua hàng và giỏ hàng hiện tại của khách, chọn tối đa {count} sản phẩm PHÙ HỢP
NHẤT từ danh sách sản phẩm đang bán bên dưới để gợi ý thêm cho khách (bổ trợ, cùng phong
cách/phòng, hoặc thường được mua kèm nhau).

QUY TẮC BẮT BUỘC:
- TUYỆT ĐỐI KHÔNG chọn sản phẩm đang có sẵn trong giỏ hàng của khách.
- Chỉ chọn ID có thật trong danh sách sản phẩm đang bán bên dưới, không bịa ID.
- Nếu không đủ thông tin để suy luận (khách vãng lai, giỏ trống, chưa có lịch sử), ưu
  tiên các sản phẩm ""Bán chạy: có"".
- CHỈ trả lời bằng ĐÚNG 1 mảng JSON các số nguyên là ID sản phẩm, không kèm bất kỳ chữ
  giải thích, chú thích hay markdown nào khác. Ví dụ hợp lệ: [12,5,7,20]

DANH SÁCH SẢN PHẨM ĐANG BÁN:
{string.Join("\n", catalogLines)}

LỊCH SỬ ĐÃ MUA CỦA KHÁCH: {purchasedText}
SẢN PHẨM ĐANG CÓ TRONG GIỎ HÀNG: {cartText}";

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
            var payload = new
            {
                systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
                contents = new[]
                {
                    new { role = "user", parts = new[] { new { text = $"Hãy chọn tối đa {count} ID sản phẩm phù hợp nhất, trả về đúng định dạng JSON đã yêu cầu." } } }
                },
                // Ép Gemini trả đúng JSON, đỡ phải dò/cắt chữ thừa ở phần xử lý phía dưới.
                generationConfig = new { responseMimeType = "application/json" },
            };

            var client = _httpClientFactory.CreateClient();
            // AI gợi ý là phần BỔ TRỢ cho trang giỏ hàng, không phải nội dung chính — timeout
            // ngắn để lỡ Gemini chậm cũng không bắt khách chờ lâu, có sẵn phương án dự phòng.
            client.Timeout = TimeSpan.FromSeconds(12);

            string body;
            try
            {
                var response = await client.PostAsync(
                    url,
                    new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
                if (!response.IsSuccessStatusCode) return new List<Product>();
                body = await response.Content.ReadAsStringAsync();
            }
            catch
            {
                return new List<Product>();
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

            if (string.IsNullOrWhiteSpace(text)) return new List<Product>();

            List<int> ids;
            try
            {
                // AI đôi khi vẫn kèm chữ thừa dù đã dặn kỹ — cắt lấy đúng đoạn từ "[" tới "]"
                // đầu tiên tìm được, để việc parse JSON không bị vỡ vì vài ký tự lạc ở 2 đầu.
                var start = text.IndexOf('[');
                var end = text.LastIndexOf(']');
                var jsonPart = (start >= 0 && end > start) ? text.Substring(start, end - start + 1) : text;
                ids = JsonSerializer.Deserialize<List<int>>(jsonPart) ?? new List<int>();
            }
            catch
            {
                return new List<Product>();
            }

            var byId = activeProducts.ToDictionary(p => p.Id);
            var picked = new List<Product>();
            foreach (var id in ids)
            {
                if (picked.Count >= count) break;
                // Chặn lại lần nữa cho chắc, phòng khi AI lỡ chọn nhầm sản phẩm đang trong giỏ
                // dù đã dặn trong prompt.
                if (cartProductIds.Contains(id)) continue;
                if (byId.TryGetValue(id, out var product) && !picked.Contains(product))
                    picked.Add(product);
            }

            return picked;
        }

        // Phương án dự phòng KHÔNG dùng AI — dùng khi Gemini lỗi/chậm/trả JSON hỏng. Tự tính
        // theo danh mục: danh mục từ lịch sử đã mua + danh mục sản phẩm đang trong giỏ, ưu
        // tiên bán chạy/mới nhất, loại trừ sản phẩm đang có trong giỏ; thiếu thì lấp đầy bằng
        // sản phẩm bán chạy/mới nhất nói chung.
        private List<Product> BuildRuleBasedRecommendations(
            List<Product> activeProducts, List<Product> purchasedProducts, List<Product> cartProducts,
            List<int> cartProductIds, int count)
        {
            var categorySignals = new HashSet<string>();
            foreach (var p in purchasedProducts) if (!string.IsNullOrEmpty(p.Category)) categorySignals.Add(p.Category);
            foreach (var p in cartProducts) if (!string.IsNullOrEmpty(p.Category)) categorySignals.Add(p.Category);

            var result = new List<Product>();

            if (categorySignals.Count > 0)
            {
                var byCategory = activeProducts
                    .Where(p => categorySignals.Contains(p.Category) && !cartProductIds.Contains(p.Id))
                    .OrderByDescending(p => p.IsBestSeller)
                    .ThenByDescending(p => p.CreatedAt)
                    .Take(count)
                    .ToList();
                result.AddRange(byCategory);
            }

            if (result.Count < count)
            {
                var excludeIds = cartProductIds.Concat(result.Select(p => p.Id)).ToList();
                var filler = activeProducts
                    .Where(p => !excludeIds.Contains(p.Id))
                    .OrderByDescending(p => p.IsBestSeller)
                    .ThenByDescending(p => p.CreatedAt)
                    .Take(count - result.Count)
                    .ToList();
                result.AddRange(filler);
            }

            return result;
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

            product.CreatedAt = DateTime.Now;
            product.IsActive = true;
            foreach (var v in product.Variants) v.Id = 0;

            _context.Products.Add(product);
            await _context.SaveChangesAsync();
            return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
        }

        // PUT: api/products/5
        [Authorize(Roles = "Admin,Nhân viên")]
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, Product product)
        {
            if (id != product.Id) return BadRequest();

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

        [Authorize(Roles = "Admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null) return NotFound();

            product.IsActive = false;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        [Authorize(Roles = "Admin")]
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
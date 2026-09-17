using FurnitureStoreAPI.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace FurnitureStoreAPI.Controllers
{
    [ApiController]
    public class SitemapController : ControllerBase
    {
        private readonly AppDbContext _context;
        public SitemapController(AppDbContext context)
        {
            _context = context;
        }

        // ĐỔI URL NÀY thành domain THẬT của Client khi deploy — hiện đang để tạm domain
        // Vercel thấy trong CORS config (Program.cs). Sitemap chỉ có ý nghĩa khi liệt kê
        // đúng domain công khai thật, không phải localhost.
        private const string BaseUrl = "https://furniture-store-client-rouge.vercel.app";

        // GET /sitemap.xml — route tuyệt đối (bắt đầu bằng "/"), KHÔNG theo prefix
        // "api/[controller]" mặc định của các controller khác, để crawler tìm thấy đúng
        // ngay tại gốc domain — đây là vị trí quy ước mà Google/công cụ tìm kiếm mong đợi.
        [HttpGet("/sitemap.xml")]
        public async Task<IActionResult> GetSitemap()
        {
            var urls = new List<(string Loc, string? LastMod)>
            {
                (BaseUrl, null),
                ($"{BaseUrl}/products", null),
                ($"{BaseUrl}/sale", null),
                ($"{BaseUrl}/about", null),
                ($"{BaseUrl}/contact", null),
                ($"{BaseUrl}/blog", null),
            };

            // Chỉ đưa sản phẩm ĐANG BÁN (IsActive) vào sitemap — sản phẩm đã ẩn không nên
            // để Google index, vì khách bấm vào từ kết quả tìm kiếm sẽ gặp trang không
            // mua được, trải nghiệm xấu.
            var products = await _context.Products
                .Where(p => p.IsActive)
                .Select(p => new { p.Id, p.CreatedAt })
                .ToListAsync();

            foreach (var p in products)
                urls.Add(($"{BaseUrl}/products/{p.Id}", p.CreatedAt.ToString("yyyy-MM-dd")));

            var posts = await _context.BlogPosts
                .Select(b => new { b.Id, b.CreatedAt })
                .ToListAsync();

            foreach (var b in posts)
                urls.Add(($"{BaseUrl}/blog/{b.Id}", b.CreatedAt.ToString("yyyy-MM-dd")));

            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            sb.Append("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");

            foreach (var (loc, lastMod) in urls)
            {
                sb.Append("  <url>\n");
                sb.Append($"    <loc>{System.Security.SecurityElement.Escape(loc)}</loc>\n");
                if (lastMod != null)
                    sb.Append($"    <lastmod>{lastMod}</lastmod>\n");
                sb.Append("  </url>\n");
            }

            sb.Append("</urlset>\n");

            // Trả về đúng content-type "application/xml" — nếu trả về text/plain hoặc
            // để mặc định application/json, trình duyệt/crawler có thể không hiểu đúng
            // đây là 1 sitemap hợp lệ.
            return Content(sb.ToString(), "application/xml", Encoding.UTF8);
        }
    }
}
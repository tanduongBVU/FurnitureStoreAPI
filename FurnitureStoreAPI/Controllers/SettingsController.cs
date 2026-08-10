using FurnitureStoreAPI.Data;
using FurnitureStoreAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FurnitureStoreAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SettingsController : ControllerBase
    {
        private readonly AppDbContext _context;
        public SettingsController(AppDbContext context) { _context = context; }

        // Giá trị mặc định — khớp với những gì đang hard-code sẵn trong giao diện Client.
        // Nếu Admin chưa từng lưu key nào, Client vẫn nhận được giá trị hợp lý này.
        private static readonly Dictionary<string, string> Defaults = new()
        {
            ["theme.walnut"] = "#3b2a1a",
            ["theme.cream"] = "#f5f0e8",
            ["theme.gold"] = "#c8a96e",
            ["theme.goldDark"] = "#a8854a",

            ["hero.slide1.title"] = "Không gian sống\nđẳng cấp",
            ["hero.slide1.subtitle"] = "Nội thất cao cấp — tinh tế từng đường nét",
            ["hero.slide1.image"] = "https://loremflickr.com/900/700/livingroom,luxury",
            ["hero.slide1.accent"] = "#c8a96e",
            ["hero.slide1.bg"] = "#2c1f10",

            ["hero.slide2.title"] = "Chất liệu\ntự nhiên",
            ["hero.slide2.subtitle"] = "Gỗ óc chó, gỗ sồi nhập khẩu chính hãng",
            ["hero.slide2.image"] = "https://loremflickr.com/900/700/woodfurniture,walnut",
            ["hero.slide2.accent"] = "#7ab87a",
            ["hero.slide2.bg"] = "#1a2c20",

            ["hero.slide3.title"] = "Thiết kế\nriêng cho bạn",
            ["hero.slide3.subtitle"] = "Tư vấn & thi công theo yêu cầu",
            ["hero.slide3.image"] = "https://loremflickr.com/900/700/furniture,craftsman",
            ["hero.slide3.accent"] = "#6e9ec8",
            ["hero.slide3.bg"] = "#1a1f2c",

            ["about.image"] = "https://loremflickr.com/800/600/furniture,showroom",
            ["about.title"] = "Hơn 15 năm kiến tạo\nkhông gian sống đẹp",
            ["about.description"] = "Chúng tôi tin rằng một ngôi nhà đẹp bắt đầu từ những món đồ nội thất được làm ra với tâm huyết. Mỗi sản phẩm của LuxWood đều được chọn lọc từ gỗ tự nhiên cao cấp, gia công thủ công tỉ mỉ và qua kiểm định chất lượng nghiêm ngặt.",
            ["about.stat1Number"] = "500+",
            ["about.stat1Label"] = "Sản phẩm",
            ["about.stat2Number"] = "10.000+",
            ["about.stat2Label"] = "Khách hàng",
            ["about.stat3Number"] = "15+",
            ["about.stat3Label"] = "Năm kinh nghiệm",

            ["footer.address"] = "123 Đường Nội Thất, Quận 1, TP.HCM",
            ["footer.phone"] = "0909 123 456",
            ["footer.email"] = "hello@luxwood.vn",
            ["footer.hours"] = "Thứ 2 – Thứ 7: 8:00 – 20:00",

            // ── Trang Giới thiệu (/about) ──────────────────────────────

            // Banner đầu trang
            ["aboutpage.banner.image"] = "",
            ["aboutpage.banner.title"] = "Nghệ thuật kiến tạo\nkhông gian sống",
            ["aboutpage.banner.description"] = "Hơn 15 năm đồng hành cùng hàng nghìn gia đình Việt Nam trong hành trình tạo nên tổ ấm hoàn hảo.",
            ["aboutpage.banner.stat1Number"] = "15+",
            ["aboutpage.banner.stat1Label"] = "Năm kinh nghiệm",
            ["aboutpage.banner.stat2Number"] = "500+",
            ["aboutpage.banner.stat2Label"] = "Sản phẩm",
            ["aboutpage.banner.stat3Number"] = "10.000+",
            ["aboutpage.banner.stat3Label"] = "Khách hàng",
            ["aboutpage.banner.stat4Number"] = "200+",
            ["aboutpage.banner.stat4Label"] = "Dự án lớn",

            // Câu chuyện công ty
            ["aboutpage.company.image"] = "",
            ["aboutpage.company.year"] = "2009",
            ["aboutpage.company.title"] = "Từ xưởng mộc nhỏ đến thương hiệu nội thất hàng đầu",
            ["aboutpage.company.paragraph1"] = "LuxWood được thành lập năm 2009 bởi nghệ nhân Nguyễn Minh Khoa với niềm đam mê về gỗ và khát vọng mang đến những sản phẩm nội thất chất lượng cao cho người Việt.",
            ["aboutpage.company.paragraph2"] = "Từ một xưởng mộc nhỏ tại Bình Dương với 5 thợ lành nghề, chúng tôi đã phát triển thành doanh nghiệp với hơn 200 nhân sự, showroom tại TP.HCM và Hà Nội, phục vụ hàng nghìn khách hàng trên toàn quốc.",
            ["aboutpage.company.paragraph3"] = "Mỗi sản phẩm LuxWood là sự kết hợp giữa kỹ thuật gia công hiện đại và tay nghề thủ công tinh xảo — tạo nên những tác phẩm vừa đẹp, vừa bền, vừa mang hơi thở tự nhiên.",

            // Đội ngũ lãnh đạo
            ["aboutpage.team.member1.image"] = "",
            ["aboutpage.team.member1.name"] = "Nguyễn Minh Khoa",
            ["aboutpage.team.member1.role"] = "Giám đốc điều hành",
            ["aboutpage.team.member1.exp"] = "20 năm kinh nghiệm",
            ["aboutpage.team.member2.image"] = "",
            ["aboutpage.team.member2.name"] = "Trần Thị Lan Anh",
            ["aboutpage.team.member2.role"] = "Giám đốc Thiết kế",
            ["aboutpage.team.member2.exp"] = "15 năm kinh nghiệm",
            ["aboutpage.team.member3.image"] = "",
            ["aboutpage.team.member3.name"] = "Lê Hoàng Phúc",
            ["aboutpage.team.member3.role"] = "Trưởng xưởng sản xuất",
            ["aboutpage.team.member3.exp"] = "18 năm kinh nghiệm",
            ["aboutpage.team.member4.image"] = "",
            ["aboutpage.team.member4.name"] = "Phạm Thu Hà",
            ["aboutpage.team.member4.role"] = "Trưởng phòng Kinh doanh",
            ["aboutpage.team.member4.exp"] = "12 năm kinh nghiệm",
        };

        // GET: api/Settings — công khai, Client cần đọc để render giao diện
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var overrides = await _context.SiteSettings.ToDictionaryAsync(s => s.Key, s => s.Value);

            var result = new Dictionary<string, string>(Defaults);
            foreach (var kv in overrides)
            {
                result[kv.Key] = kv.Value;
            }

            return Ok(result);
        }

        // PUT: api/Settings — CHỈ Admin được sửa giao diện Client
        // Body: { "theme.walnut": "#123456", "footer.phone": "0909...", ... }
        [Authorize(Roles = "Admin")]
        [HttpPut]
        public async Task<IActionResult> Update([FromBody] Dictionary<string, string> values)
        {
            foreach (var kv in values)
            {
                var existing = await _context.SiteSettings.FirstOrDefaultAsync(s => s.Key == kv.Key);
                if (existing != null)
                {
                    existing.Value = kv.Value;
                }
                else
                {
                    _context.SiteSettings.Add(new SiteSetting { Key = kv.Key, Value = kv.Value });
                }
            }

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // POST: api/Settings/reset — CHỈ Admin, khôi phục về mặc định ban đầu
        [Authorize(Roles = "Admin")]
        [HttpPost("reset")]
        public async Task<IActionResult> Reset()
        {
            _context.SiteSettings.RemoveRange(_context.SiteSettings);
            await _context.SaveChangesAsync();
            return Ok(Defaults);
        }
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FurnitureStoreAPI.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FurnitureStoreAPI.Controllers
{
    // Dữ liệu SePay gửi tới webhook mỗi khi tài khoản ngân hàng có biến động số dư.
    // Chỉ khai báo các trường cần dùng; trường khác SePay gửi thêm sẽ được bỏ qua.
    public class SepayWebhookDto
    {
        public long Id { get; set; }                 // mã giao dịch phía SePay
        public string? Gateway { get; set; }         // tên ngân hàng
        public string? TransactionDate { get; set; }
        public string? AccountNumber { get; set; }   // số tài khoản nhận
        public string? Code { get; set; }            // mã thanh toán SePay tự nhận diện (nếu có cấu hình tiền tố)
        public string? Content { get; set; }         // nội dung chuyển khoản
        public string? TransferType { get; set; }    // "in" = tiền vào, "out" = tiền ra
        public decimal TransferAmount { get; set; }  // số tiền giao dịch
        public decimal? Accumulated { get; set; }
        public string? ReferenceCode { get; set; }
        public string? Description { get; set; }
    }

    // Tự động xác nhận thanh toán QR: ngân hàng báo có tiền → SePay gọi webhook này → đơn chuyển
    // sang "Đã thanh toán" → trang Thanh toán của khách (đang hỏi trạng thái mỗi 3 giây) tự chuyển sang
    // màn hình hoàn tất. Không cần khách bấm "Tôi đã chuyển khoản".
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentsController : ControllerBase
    {
        // Nội dung chuyển khoản do web sinh ra có dạng "DH{mã đơn}" (xem Checkout.jsx). Không dùng \b vì
        // một số ngân hàng dính liền mã giao dịch với nội dung (VD "FT123DH89").
        private static readonly Regex OrderCodeRegex =
            new(@"DH(\d{1,9})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly AppDbContext _context;
        private readonly IConfiguration _config;
        private readonly ILogger<PaymentsController> _logger;

        public PaymentsController(AppDbContext context, IConfiguration config, ILogger<PaymentsController> logger)
        {
            _context = context;
            _config = config;
            _logger = logger;
        }

        // POST: api/payments/sepay-webhook
        // Bảo vệ bằng API key (header "Authorization: Apikey ...") đặt trong appsettings: SePay:ApiKey.
        // Chưa cấu hình key → tính năng TẮT (503), không ai gọi giả được để đánh dấu đơn đã trả tiền.
        // Luôn trả 200 + { success: true } cho giao dịch hợp lệ dù không khớp đơn nào, để SePay không gửi lại mãi.
        [AllowAnonymous]
        [HttpPost("sepay-webhook")]
        public async Task<IActionResult> SepayWebhook([FromBody] SepayWebhookDto dto)
        {
            var apiKey = _config["SePay:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
                return StatusCode(503, new { success = false, message = "Chưa cấu hình tự động xác nhận thanh toán." });

            var provided = Request.Headers.Authorization.ToString();
            var expected = "Apikey " + apiKey;
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected)))
                return Unauthorized(new { success = false, message = "Sai API key." });

            // Chỉ quan tâm tiền VÀO
            if (!string.Equals(dto.TransferType, "in", StringComparison.OrdinalIgnoreCase))
                return Ok(new { success = true, message = "Bỏ qua giao dịch tiền ra." });

            var orderId = ExtractOrderId(dto.Code) ?? ExtractOrderId(dto.Content);
            if (orderId == null)
            {
                _logger.LogInformation("SePay #{Id}: nội dung '{Content}' không chứa mã đơn DH…, bỏ qua.", dto.Id, dto.Content);
                return Ok(new { success = true, message = "Không có mã đơn trong nội dung." });
            }

            var order = await _context.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId.Value);
            if (order == null)
            {
                _logger.LogWarning("SePay #{Id}: không tìm thấy đơn #{OrderId}.", dto.Id, orderId);
                return Ok(new { success = true, message = "Không tìm thấy đơn." });
            }
            if (order.PaymentMethod != "QR")
            {
                _logger.LogWarning("SePay #{Id}: đơn #{OrderId} không phải đơn QR, bỏ qua.", dto.Id, order.Id);
                return Ok(new { success = true, message = "Đơn không thanh toán QR." });
            }
            if (order.Status == "Huỷ")
            {
                // Khách chuyển tiền cho đơn đã huỷ → cần cửa hàng hoàn tiền thủ công
                _logger.LogWarning("SePay #{Id}: đã nhận {Amount} cho đơn #{OrderId} ĐÃ HUỶ — cần hoàn tiền.", dto.Id, dto.TransferAmount, order.Id);
                return Ok(new { success = true, message = "Đơn đã huỷ." });
            }
            if (dto.TransferAmount < order.Total)
            {
                // Chuyển thiếu → KHÔNG ghi nhận đã thanh toán; cửa hàng đối chiếu thủ công
                _logger.LogWarning("SePay #{Id}: đơn #{OrderId} cần {Total} nhưng chỉ nhận {Amount}.", dto.Id, order.Id, order.Total, dto.TransferAmount);
                return Ok(new { success = true, message = "Số tiền chưa đủ." });
            }

            // Cập nhật có điều kiện: SePay gửi lại cùng giao dịch (hoặc khách đã được ghi nhận) thì câu UPDATE
            // không ảnh hưởng dòng nào → an toàn khi webhook bị gọi lặp.
            var updated = await _context.Orders
                .Where(o => o.Id == order.Id && o.Status != "Huỷ" && o.PaymentStatus != "Đã thanh toán")
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.PaymentStatus, "Đã thanh toán"));

            _logger.LogInformation("SePay #{Id}: đơn #{OrderId} {Result} ({Amount}).",
                dto.Id, order.Id, updated > 0 ? "đã ghi nhận THANH TOÁN" : "đã được ghi nhận từ trước", dto.TransferAmount);

            return Ok(new { success = true });
        }

        private static int? ExtractOrderId(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var m = OrderCodeRegex.Match(text);
            return m.Success && int.TryParse(m.Groups[1].Value, out var id) ? id : null;
        }
    }
}

using FurnitureStoreAPI.Models;
using System.Text;

namespace FurnitureStoreAPI.Services
{
    // Tách riêng phần dựng HTML email ra khỏi Controller để OrdersController không bị phình to,
    // và dễ chỉnh sửa giao diện email sau này mà không đụng vào logic đặt hàng.
    public static class EmailTemplates
    {
        private static string FormatPrice(decimal n) => n.ToString("N0", new System.Globalization.CultureInfo("vi-VN")) + "₫";

        // Khung HTML dùng chung cho mọi email — tông màu be/nâu gỗ khớp thương hiệu LuxWood.
        private static string Wrap(string title, string bodyHtml) => $@"
<div style=""font-family:Arial,sans-serif;max-width:560px;margin:0 auto;background:#faf7f2;padding:32px 24px;"">
  <div style=""text-align:center;margin-bottom:24px;"">
    <span style=""font-size:20px;font-weight:700;color:#3b2a1a;"">⬡ LuxWood</span>
  </div>
  <div style=""background:#fff;border-radius:14px;padding:28px;box-shadow:0 4px 20px rgba(59,42,26,0.08);"">
    <h2 style=""color:#3b2a1a;font-size:18px;margin:0 0 16px;"">{title}</h2>
    {bodyHtml}
  </div>
  <p style=""text-align:center;color:#9a8f7f;font-size:12px;margin-top:20px;"">
    Đây là email tự động, vui lòng không trả lời trực tiếp email này.
  </p>
</div>";

        public static string OrderConfirmation(Order order)
        {
            var itemsHtml = new StringBuilder();
            foreach (var item in order.OrderItems)
            {
                itemsHtml.Append($@"
<tr>
  <td style=""padding:8px 0;border-bottom:1px solid #efe9de;font-size:13px;color:#3b2a1a;"">{item.ProductName} × {item.Quantity}</td>
  <td style=""padding:8px 0;border-bottom:1px solid #efe9de;font-size:13px;color:#3b2a1a;text-align:right;"">{FormatPrice(item.Price * item.Quantity)}</td>
</tr>");
            }

            var couponRow = string.IsNullOrEmpty(order.CouponCode) ? "" : $@"
<tr>
  <td colspan=""2"" style=""padding:6px 0;font-size:12px;color:#a8854a;"">🎟️ Đã áp dụng mã giảm giá: <strong>{order.CouponCode}</strong></td>
</tr>";

            var body = $@"
<p style=""color:#7a6a58;font-size:14px;line-height:1.6;"">
  Chào <strong>{order.CustomerName}</strong>, cảm ơn bạn đã đặt hàng tại LuxWood!
  Đơn hàng <strong>#{order.Id}</strong> của bạn đã được ghi nhận và đang chờ xác nhận.
</p>
<table style=""width:100%;border-collapse:collapse;margin:16px 0;"">
  {itemsHtml}
  {couponRow}
  <tr>
    <td style=""padding-top:12px;font-size:14px;font-weight:700;color:#3b2a1a;"">Tổng cộng</td>
    <td style=""padding-top:12px;font-size:14px;font-weight:700;color:#3b2a1a;text-align:right;"">{FormatPrice(order.Total)}</td>
  </tr>
</table>
<p style=""color:#7a6a58;font-size:13px;margin:0;"">
  <strong>Giao đến:</strong> {order.Address}<br/>
  <strong>Số điện thoại:</strong> {order.Phone}
</p>";

            return Wrap($"Xác nhận đơn hàng #{order.Id}", body);
        }

        public static string OrderStatusUpdate(Order order, string newStatus)
        {
            var body = $@"
<p style=""color:#7a6a58;font-size:14px;line-height:1.6;"">
  Chào <strong>{order.CustomerName}</strong>, đơn hàng <strong>#{order.Id}</strong> của bạn vừa được cập nhật trạng thái mới:
</p>
<div style=""background:#faf7f2;border-radius:10px;padding:16px;text-align:center;margin:16px 0;"">
  <span style=""font-size:16px;font-weight:700;color:#a8854a;"">{newStatus}</span>
</div>
<p style=""color:#7a6a58;font-size:13px;margin:0;"">
  Tổng giá trị đơn hàng: <strong>{FormatPrice(order.Total)}</strong>
</p>";

            return Wrap($"Cập nhật đơn hàng #{order.Id}", body);
        }

        // Email "Quên mật khẩu": nút dẫn tới trang đặt lại mật khẩu của website (link có hạn).
        public static string PasswordReset(string customerName, string resetLink, int expiresMinutes)
        {
            var name = System.Net.WebUtility.HtmlEncode(customerName);
            var link = System.Net.WebUtility.HtmlEncode(resetLink);

            var body = $@"
<p style=""color:#7a6a58;font-size:14px;line-height:1.6;"">
  Chào <strong>{name}</strong>, chúng tôi nhận được yêu cầu đặt lại mật khẩu cho tài khoản LuxWood của bạn.
  Bấm nút bên dưới để chọn mật khẩu mới. Liên kết có hiệu lực trong <strong>{expiresMinutes} phút</strong> và chỉ dùng được một lần.
</p>
<div style=""text-align:center;margin:24px 0;"">
  <a href=""{link}"" style=""display:inline-block;background:#a8854a;color:#ffffff;text-decoration:none;font-weight:700;font-size:14px;padding:12px 28px;border-radius:8px;"">Đặt lại mật khẩu</a>
</div>
<p style=""color:#7a6a58;font-size:12px;line-height:1.6;margin:0 0 8px;"">
  Nếu nút không bấm được, hãy sao chép liên kết sau vào trình duyệt:<br/>
  <span style=""word-break:break-all;color:#a8854a;"">{link}</span>
</p>
<p style=""color:#7a6a58;font-size:12px;line-height:1.6;margin:0;"">
  Nếu bạn <strong>không</strong> yêu cầu việc này, hãy bỏ qua email — mật khẩu của bạn vẫn giữ nguyên.
</p>";

            return Wrap("Đặt lại mật khẩu", body);
        }
    }
}
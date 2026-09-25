namespace FurnitureStoreAPI.Models
{
    public class Order
    {
        public int Id { get; set; }
        // Nullable: null = khách đặt hàng vãng lai (không đăng nhập), có giá trị = khách
        // đã đăng nhập. Trước đây field này là int không nullable nên khách vãng lai gửi
        // userId = null bị bind thành 0 — một giá trị "giả", không trỏ tới User nào thật,
        // khiến không thể thêm ràng buộc khoá ngoại (FK) chặt tới bảng Users. Đổi sang int?
        // để phản ánh đúng bản chất và cho phép FK hoạt động chính xác.
        public int? UserId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Status { get; set; } = "Chờ xác nhận";
        public decimal Total { get; set; }
        // Mã giảm giá đã áp dụng cho đơn này (nếu có) — lưu lại để hiện trong lịch sử đơn hàng
        // (MyOrders, Admin OrderDetail). null = đơn hàng không dùng mã giảm giá.
        public string? CouponCode { get; set; }

        // "COD" (thanh toán khi nhận hàng) hoặc "QR" (chuyển khoản quét mã VietQR).
        // TÁCH RIÊNG khỏi Status — Status theo dõi tiến trình GIAO HÀNG (Chờ xác nhận/Hoàn
        // thành/Huỷ), còn đây theo dõi việc THANH TOÁN đã nhận được tiền hay chưa, 2 khái
        // niệm độc lập nhau (VD: đơn QR có thể "Chờ xác nhận" NHƯNG đã "Đã thanh toán" rồi).
        public string PaymentMethod { get; set; } = "COD";

        // "Chưa thanh toán" hoặc "Đã thanh toán". Với QR, hệ thống KHÔNG tự động xác nhận
        // (không tích hợp cổng thanh toán/webhook ngân hàng) — Admin tự kiểm tra sao kê rồi
        // bấm xác nhận thủ công bên Admin. Với COD, mặc định vẫn "Chưa thanh toán" cho tới
        // khi khách trả tiền lúc nhận hàng, Admin cũng tự cập nhật tương tự.
        public string PaymentStatus { get; set; } = "Chưa thanh toán";

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public List<OrderItem> OrderItems { get; set; } = new();
    }
}
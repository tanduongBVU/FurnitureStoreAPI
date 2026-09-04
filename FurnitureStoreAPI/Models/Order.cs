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
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public List<OrderItem> OrderItems { get; set; } = new();
    }
}
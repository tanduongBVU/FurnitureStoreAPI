namespace FurnitureStoreAPI.Models
{
    public class Coupon
    {
        public int Id { get; set; }
        // Mã do Admin tự đặt (VD: "SALE10") — luôn lưu dạng UPPERCASE để so khớp
        // không phân biệt hoa/thường khi khách nhập ở Checkout.
        public string Code { get; set; } = string.Empty;
        public int DiscountPercent { get; set; } // 1 - 100
        // Số lần được dùng tối đa (toàn hệ thống, không phải theo từng khách).
        // null = không giới hạn số lần dùng.
        public int? MaxUsage { get; set; }
        // Đếm số lần đã áp dụng thành công — tăng dần mỗi khi có đơn hàng dùng mã này.
        public int UsedCount { get; set; } = 0;
        // Giá trị đơn hàng tối thiểu (trước giảm giá) để mã có hiệu lực. null = không yêu cầu.
        public decimal? MinOrderValue { get; set; }
        // Ngày hết hạn. null = không hết hạn.
        public DateTime? ExpiryDate { get; set; }
        // Admin có thể tắt mã thủ công mà không cần xoá hẳn (giữ lịch sử UsedCount)
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
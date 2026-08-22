namespace FurnitureStoreAPI.Models
{
    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Image { get; set; } = string.Empty;
        public int Stock { get; set; }
        public bool IsBestSeller { get; set; } = false;
        // Sản phẩm còn đang bán hay đã bị ẩn (soft-delete).
        // false = đã ẩn khỏi Client, nhưng dữ liệu vẫn giữ nguyên để không phá vỡ
        // lịch sử đơn hàng cũ đã tham chiếu tới sản phẩm này.
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Phần trăm giảm giá (0 - 100). 0 = không sale.
        // Giá sau giảm luôn TÍNH TỪ Price gốc (không lưu giá đã giảm sẵn),
        // để đổi Price gốc không làm sai lệch % giảm đã đặt.
        public int DiscountPercent { get; set; } = 0;
    }
}
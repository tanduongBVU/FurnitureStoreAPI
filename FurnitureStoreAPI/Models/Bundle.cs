namespace FurnitureStoreAPI.Models
{
    public class Bundle
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        // Ảnh riêng cho combo (URL string, giống pattern ảnh toàn site) — nếu để trống,
        // Client sẽ tự dùng ảnh của sản phẩm đầu tiên trong combo làm ảnh đại diện.
        public string? Image { get; set; }

        // % giảm THÊM khi mua trọn combo, tính trên tổng giá các sản phẩm trong bộ.
        // KHÔNG lưu giá cứng cho combo — giá luôn tính lại từ Price hiện tại của từng
        // Product (giống cách Product.DiscountPercent hoạt động), để combo tự động cập
        // nhật đúng khi Admin đổi giá sản phẩm gốc, không cần sửa combo thủ công.
        public int DiscountPercent { get; set; } = 0;

        // Soft-delete, giống pattern Product.IsActive.
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public List<BundleItem> Items { get; set; } = new();
    }
}
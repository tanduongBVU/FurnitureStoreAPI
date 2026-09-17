namespace FurnitureStoreAPI.Models
{
    public class OrderItem
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal Price { get; set; }

        // Biến thể khách đã chọn lúc đặt hàng (nếu sản phẩm có biến thể) — null nếu sản
        // phẩm không có biến thể. VariantId chỉ mang tính THAM KHẢO, CỐ Ý KHÔNG ràng buộc
        // khoá ngoại (FK) tới bảng ProductVariants — vì Admin có thể xoá/sửa/đổi tên biến
        // thể sau này, và lịch sử đơn hàng KHÔNG được phép bị ảnh hưởng theo. VariantName
        // lưu lại NGUYÊN VĂN tên biến thể tại thời điểm đặt hàng, đúng tinh thần đã làm
        // với ProductName (không JOIN lại bảng gốc để hiển thị lịch sử).
        public int? VariantId { get; set; }
        public string? VariantName { get; set; }

        public Order? Order { get; set; }
        public Product? Product { get; set; }
    }
}
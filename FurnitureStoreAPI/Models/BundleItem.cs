namespace FurnitureStoreAPI.Models
{
    // Bảng nối nhiều-nhiều giữa Bundle và Product, có thêm Quantity — 1 combo gồm nhiều
    // sản phẩm, mỗi sản phẩm có thể xuất hiện với số lượng khác 1 (VD: combo phòng ăn
    // gồm 1 bàn + 4 ghế).
    public class BundleItem
    {
        public int Id { get; set; }

        public int BundleId { get; set; }
        public Bundle? Bundle { get; set; }

        public int ProductId { get; set; }
        public Product? Product { get; set; }

        // Biến thể được chọn cho món này trong combo. BẮT BUỘC với sản phẩm CÓ biến thể (giá và tồn kho
        // tính theo đúng biến thể đó); null với sản phẩm không có biến thể. CỐ Ý không đặt khoá ngoại tới
        // ProductVariants (Admin có thể xoá/đổi biến thể) — combo trỏ tới biến thể đã xoá sẽ tự báo
        // "không còn hàng" cho tới khi Admin chọn lại biến thể.
        public int? VariantId { get; set; }

        public int Quantity { get; set; } = 1;
    }
}
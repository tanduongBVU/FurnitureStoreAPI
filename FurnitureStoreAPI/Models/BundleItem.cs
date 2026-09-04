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

        public int Quantity { get; set; } = 1;
    }
}
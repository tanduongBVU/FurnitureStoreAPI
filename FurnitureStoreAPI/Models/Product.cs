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

        // Ảnh phụ — TỐI ĐA 4 ảnh, lưu dạng chuỗi phân tách dấu phẩy "url1,url2,url3"
        // (giống hệt cách Project.Images đang lưu gallery dự án). Ảnh chính vẫn dùng
        // riêng field Image ở trên như cũ, không đổi gì — Images chỉ chứa các ảnh PHỤ,
        // hiện thành cột thumbnail bên trái ảnh chính ở trang chi tiết sản phẩm (Client).
        // Nullable/rỗng nếu sản phẩm không có ảnh phụ nào (mọi sản phẩm cũ trong DB).
        public string? Images { get; set; }

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

        // Chất liệu chính của sản phẩm (VD: "Gỗ tự nhiên", "Vải nỉ"...).
        // Nullable vì sản phẩm cũ trong DB chưa có dữ liệu này — Admin cần vào nhập lại.
        public string? Material { get; set; }

        // Màu sắc chủ đạo (VD: "Nâu gỗ", "Trắng"...). Nullable cùng lý do với Material.
        public string? Color { get; set; }

        // Danh sách biến thể — KHÔNG BẮT BUỘC. Nếu rỗng, sản phẩm bán theo đúng
        // Price/Stock gốc của chính nó như trước giờ (hoàn toàn tương thích ngược,
        // không cần sửa gì các sản phẩm cũ đã có). Nếu có ít nhất 1 biến thể, Client sẽ
        // bắt khách CHỌN 1 biến thể trước khi thêm vào giỏ, và dùng giá/tồn kho của
        // đúng biến thể đó thay vì Price/Stock gốc của Product.
        public List<ProductVariant> Variants { get; set; } = new();
    }
}
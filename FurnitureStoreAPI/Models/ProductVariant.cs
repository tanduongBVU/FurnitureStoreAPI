using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace FurnitureStoreAPI.Models
{
    public class ProductVariant
    {
        public int Id { get; set; }
        public int ProductId { get; set; }

        [JsonIgnore]
        public Product? Product { get; set; }

        // ĐỔI THIẾT KẾ: bỏ tên tự do, biến thể giờ xác định bằng Chất liệu + Màu sắc —
        // giống 2 danh sách MATERIALS/COLORS Admin đang chọn ở "Thông tin cơ bản" của
        // sản phẩm không có biến thể. Nullable vì Admin có thể chỉ set 1 trong 2 (VD:
        // chỉ khác màu, chất liệu giống nhau).
        public string? Material { get; set; }
        public string? Color { get; set; }

        public decimal Price { get; set; }
        public int Stock { get; set; }

        // [NotMapped]: KHÔNG lưu cột này vào DB, chỉ tính lúc trả JSON — tự ghép
        // "Material - Color" thành 1 chuỗi hiển thị (VD: "Da thật - Trắng"). Nhờ vậy,
        // toàn bộ code Client (giỏ hàng, trang chi tiết sản phẩm) vẫn đọc được field
        // "name" y hệt như trước khi đổi thiết kế này, không cần sửa lại logic hiển thị.
        [NotMapped]
        public string Name => string.Join(" - ", new[] { Material, Color }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }
}
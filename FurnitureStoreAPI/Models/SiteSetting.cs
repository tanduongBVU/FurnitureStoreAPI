namespace FurnitureStoreAPI.Models
{
    // Lưu cấu hình giao diện Client dạng key-value để linh hoạt mở rộng
    // mà không cần thêm migration mỗi khi thêm 1 field mới cần chỉnh sửa.
    // Ví dụ Key: "theme.walnut", "hero.slide1.title", "footer.phone" ...
    public class SiteSetting
    {
        public int Id { get; set; }
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}
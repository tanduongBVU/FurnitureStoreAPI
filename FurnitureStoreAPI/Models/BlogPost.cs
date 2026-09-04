namespace FurnitureStoreAPI.Models
{
    public class BlogPost
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        // Chủ đề bài viết — dùng danh sách cố định bên Client/Admin để lọc,
        // giống cách Category hoạt động ở Product (không cần bảng riêng).
        public string Category { get; set; } = string.Empty;
        public string Thumbnail { get; set; } = string.Empty;
        // Mô tả ngắn hiện ở trang danh sách (không phải toàn bộ nội dung)
        public string Excerpt { get; set; } = string.Empty;
        // Nội dung đầy đủ dạng HTML — xuất ra từ rich text editor (Quill) bên Admin
        public string Content { get; set; } = string.Empty;
        public string Author { get; set; } = "LuxWood";
        // true = đã đăng công khai, false = đã ẩn/nháp (soft-delete, giống IsActive của Product)
        public bool IsPublished { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
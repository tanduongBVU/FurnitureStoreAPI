namespace FurnitureStoreAPI.Models
{
    public class Project
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        // Ảnh đại diện — hiện ở card danh sách portfolio
        public string CoverImage { get; set; } = string.Empty;
        // Nhiều ảnh chi tiết dự án — lưu dạng CHUỖI các URL phân tách bằng dấu phẩy (giống
        // cách Product/BlogPost chỉ lưu URL ảnh dạng string, không có upload file thật lên
        // server). Admin dán nhiều dòng URL, Client tự tách chuỗi này ra thành mảng để hiển
        // thị gallery. Không dùng bảng con riêng (VD: ProjectImage) vì không cần query/lọc
        // theo từng ảnh — chỉ cần hiển thị nguyên cụm ảnh của 1 dự án.
        public string Images { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty; // VD: "Nhà phố", "Chung cư", "Biệt thự"
        public string Year { get; set; } = string.Empty;
        public string ShortDescription { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty; // Nội dung chi tiết dạng HTML
        public int DisplayOrder { get; set; } = 0;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
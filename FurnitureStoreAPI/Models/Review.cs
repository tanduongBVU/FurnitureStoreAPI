namespace FurnitureStoreAPI.Models
{
    public class Review
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public int UserId { get; set; }

        // Lưu lại tên khách tại thời điểm review, tránh phải join bảng Users mỗi lần hiển thị
        public string UserName { get; set; } = string.Empty;

        public int Rating { get; set; } // 1 - 5 sao
        public string Comment { get; set; } = string.Empty;

        // KHÔNG CÒN DÙNG để kiểm duyệt: đánh giá hiện công khai NGAY khi khách gửi, không qua Admin duyệt.
        // Cột này được giữ lại (luôn là true với đánh giá mới) để khỏi phải tạo migration xoá cột và để các
        // đoạn code cũ còn lọc theo nó (nếu có) vẫn hoạt động đúng.
        public bool IsApproved { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
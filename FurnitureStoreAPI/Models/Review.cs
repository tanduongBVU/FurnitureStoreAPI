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

        // Chỉ hiện công khai trên Client sau khi Admin/Nhân viên duyệt
        public bool IsApproved { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
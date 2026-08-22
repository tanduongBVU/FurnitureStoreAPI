namespace FurnitureStoreAPI.Models
{
    public class Service
    {
        public int Id { get; set; }
        // "thi-cong" hoặc "thiet-ke" — dùng để lọc đúng dịch vụ hiển thị ở mỗi trang Client,
        // giống cách Category lọc Product theo phòng. Không tách 2 bảng riêng vì cấu trúc
        // dữ liệu (tiêu đề, mô tả, ảnh) giống hệt nhau, chỉ khác nhãn phân loại.
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Image { get; set; } = string.Empty;
        // Mô tả ngắn hiện ở card danh sách
        public string ShortDescription { get; set; } = string.Empty;
        // Nội dung chi tiết dạng HTML (rich text), hiện khi khách xem chi tiết 1 dịch vụ
        public string Content { get; set; } = string.Empty;
        // Thứ tự hiển thị — số nhỏ hơn hiện trước. Cho phép Admin sắp xếp thủ công
        // thay vì luôn theo ngày tạo, vì dịch vụ thường muốn cố định thứ tự ưu tiên.
        public int DisplayOrder { get; set; } = 0;
        // true = đang hiển thị công khai, false = đã ẩn (soft-delete, giống IsActive của Product)
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
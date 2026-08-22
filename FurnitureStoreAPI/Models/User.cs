namespace FurnitureStoreAPI.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        // Rỗng ("") với tài khoản CHỈ đăng nhập qua Google (chưa từng đặt mật khẩu) — không
        // dùng null để tránh phải sửa các chỗ khác đang giả định PasswordHash luôn có giá trị.
        // BCrypt.Verify sẽ luôn trả false khi so với hash rỗng nên KHÔNG có rủi ro bảo mật:
        // các tài khoản này không thể đăng nhập bằng đường mật khẩu thường cho tới khi họ tự
        // đặt mật khẩu (VD: qua chức năng "Đặt mật khẩu" trong trang Tài khoản — làm sau nếu cần).
        public string PasswordHash { get; set; } = string.Empty;
        public string Role { get; set; } = "Khách hàng";
        public string Status { get; set; } = "Hoạt động";
        // "local" = đăng ký bằng email/mật khẩu thường; "google" = tài khoản được tạo/liên kết
        // qua đăng nhập Google. Dùng để hiển thị UI phù hợp (VD: ẩn form đổi mật khẩu nếu tài
        // khoản chưa từng có mật khẩu) — không dùng để chặn logic nghiệp vụ nào khác.
        public string AuthProvider { get; set; } = "local";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
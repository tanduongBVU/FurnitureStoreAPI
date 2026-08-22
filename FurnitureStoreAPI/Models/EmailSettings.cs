namespace FurnitureStoreAPI.Models
{
    // Bind trực tiếp từ section "Email" trong appsettings.json.
    public class EmailSettings
    {
        public string SmtpHost { get; set; } = string.Empty;
        public int SmtpPort { get; set; }
        public string SenderEmail { get; set; } = string.Empty;
        public string SenderName { get; set; } = string.Empty;
        // App Password của Gmail (16 ký tự, tạo trong Google Account > Security > App Passwords),
        // KHÔNG phải mật khẩu đăng nhập Gmail thường — Gmail chặn đăng nhập SMTP bằng mật khẩu
        // thường vì lý do bảo mật.
        public string AppPassword { get; set; } = string.Empty;
    }
}
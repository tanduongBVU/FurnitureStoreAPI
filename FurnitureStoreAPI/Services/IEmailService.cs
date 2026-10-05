namespace FurnitureStoreAPI.Services
{
    public interface IEmailService
    {
        // Gửi "lặng lẽ": mọi lỗi bị nuốt (chỉ log). Dùng cho email PHỤ như xác nhận đơn hàng.
        Task SendAsync(string toEmail, string subject, string htmlBody);

        // Gửi và TRẢ KẾT QUẢ THẬT (thành công hay thất bại + lý do). Dùng khi người dùng cần biết
        // chắc email đã đi hay chưa, VD: nhân viên trả lời khách từ trang Liên hệ.
        Task<(bool Success, string? Error)> SendWithResultAsync(string toEmail, string subject, string htmlBody);
    }
}
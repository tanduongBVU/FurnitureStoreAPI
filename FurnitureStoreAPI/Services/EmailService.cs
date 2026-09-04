using FurnitureStoreAPI.Models;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;

namespace FurnitureStoreAPI.Services
{
    // Gửi email qua Gmail SMTP dùng System.Net.Mail (có sẵn trong .NET, không cần cài thêm
    // package ngoài). Mọi lỗi gửi email đều bị NUỐT (catch, chỉ log ra console) — vì email
    // xác nhận là tính năng PHỤ, không được để lỗi SMTP (mạng chậm, sai App Password, Gmail
    // chặn...) làm hỏng luồng chính (đặt hàng / đổi trạng thái đơn vẫn phải thành công dù
    // email gửi thất bại).
    public class EmailService : IEmailService
    {
        private readonly EmailSettings _settings;

        public EmailService(IOptions<EmailSettings> options)
        {
            _settings = options.Value;
        }

        public async Task SendAsync(string toEmail, string subject, string htmlBody)
        {
            try
            {
                using var client = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
                {
                    Credentials = new NetworkCredential(_settings.SenderEmail, _settings.AppPassword),
                    EnableSsl = true,
                };

                using var message = new MailMessage
                {
                    From = new MailAddress(_settings.SenderEmail, _settings.SenderName),
                    Subject = subject,
                    Body = htmlBody,
                    IsBodyHtml = true,
                };
                message.To.Add(toEmail);

                await client.SendMailAsync(message);
            }
            catch (Exception ex)
            {
                // Chỉ log, không throw — xem lý do ở comment đầu class.
                Console.WriteLine($"[EmailService] Gửi email thất bại tới {toEmail}: {ex.Message}");
            }
        }
    }
}
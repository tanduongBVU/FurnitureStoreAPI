using FurnitureStoreAPI.Models;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace FurnitureStoreAPI.Services
{
    public class TokenService
    {
        // Token đặt lại mật khẩu dùng AUDIENCE KHÁC token đăng nhập. Cấu hình JWT Bearer trong
        // Program.cs chỉ chấp nhận audience đăng nhập (Jwt:Audience), nên token đặt lại mật khẩu
        // KHÔNG THỂ dùng để gọi API như một phiên đăng nhập — và ngược lại token đăng nhập
        // cũng không dùng được để đặt lại mật khẩu (ValidatePasswordResetToken kiểm tra audience này).
        private const string ResetAudience = "FurnitureStoreAPI.PasswordReset";

        private readonly IConfiguration _config;

        public TokenService(IConfiguration config)
        {
            _config = config;
        }

        private SymmetricSecurityKey GetKey()
        {
            var keyString = _config["Jwt:Key"]
                ?? throw new InvalidOperationException("Thiếu cấu hình Jwt:Key trong appsettings.json");
            return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyString));
        }

        public string CreateToken(User user)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Name, user.Name),
                new Claim(ClaimTypes.Role, user.Role),
            };

            var creds = new SigningCredentials(GetKey(), SecurityAlgorithms.HmacSha256);

            var expiresMinutes = double.TryParse(_config["Jwt:ExpiresMinutes"], out var m) ? m : 120;

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(expiresMinutes),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        // Mã xác nhận chuyển khoản của 1 đơn hàng QR: HMAC-SHA256(mã đơn) ký bằng khoá bí mật của server.
        // Backend trả mã này cho khách NGAY lúc đặt hàng; khi khách bấm "Tôi đã chuyển khoản", Client gửi
        // kèm mã → chỉ người vừa đặt đơn đó mới xác nhận được (đoán số đơn hàng thôi thì không đủ).
        // Không cần lưu vào database vì tính lại được từ mã đơn.
        public string CreateOrderConfirmCode(int orderId)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(
                _config["Jwt:Key"] ?? throw new InvalidOperationException("Thiếu cấu hình Jwt:Key trong appsettings.json")));
            return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"order-confirm:{orderId}")));
        }

        public bool VerifyOrderConfirmCode(int orderId, string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            var expected = Encoding.UTF8.GetBytes(CreateOrderConfirmCode(orderId));
            var actual = Encoding.UTF8.GetBytes(code.Trim().ToUpperInvariant());
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }

        // "Dấu vân tay" của mật khẩu hiện tại (băm SHA-256 của PasswordHash, lấy 16 ký tự đầu).
        // Nhúng vào token đặt lại mật khẩu: khi mật khẩu ĐÃ ĐỔI (hash khác đi) thì dấu vân tay
        // không còn khớp → link cũ tự vô hiệu, chỉ dùng được 1 lần mà KHÔNG cần lưu gì vào database.
        public static string PasswordFingerprint(User user) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user.PasswordHash ?? string.Empty)))[..16];

        // Token đặt lại mật khẩu, hết hạn sau expiresMinutes phút (mặc định 30).
        public string CreatePasswordResetToken(User user, int expiresMinutes = 30)
        {
            var claims = new List<Claim>
            {
                new Claim("uid", user.Id.ToString()),
                new Claim("pwd", PasswordFingerprint(user)),
                new Claim("purpose", "password-reset"),
            };

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: ResetAudience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(expiresMinutes),
                signingCredentials: new SigningCredentials(GetKey(), SecurityAlgorithms.HmacSha256)
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        // Kiểm tra token đặt lại mật khẩu (chữ ký, issuer, audience riêng, hạn dùng). Trả về
        // (UserId, dấu vân tay mật khẩu lúc tạo token), hoặc null nếu token sai/hết hạn/không phải loại này.
        public (int UserId, string Fingerprint)? ValidatePasswordResetToken(string token)
        {
            try
            {
                var parameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = _config["Jwt:Issuer"],
                    ValidateAudience = true,
                    ValidAudience = ResetAudience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = GetKey(),
                };

                var principal = new JwtSecurityTokenHandler().ValidateToken(token, parameters, out _);

                if (principal.FindFirst("purpose")?.Value != "password-reset") return null;
                if (!int.TryParse(principal.FindFirst("uid")?.Value, out var userId)) return null;
                var fingerprint = principal.FindFirst("pwd")?.Value;
                if (string.IsNullOrEmpty(fingerprint)) return null;

                return (userId, fingerprint);
            }
            catch
            {
                return null;
            }
        }
    }
}
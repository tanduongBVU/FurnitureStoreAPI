namespace FurnitureStoreAPI.Models.Dtos
{
    public class GoogleLoginDto
    {
        // ID Token JWT do Google Identity Services cấp ở phía Client sau khi khách chọn
        // tài khoản Google — KHÔNG phải access token, cũng không phải mật khẩu Google.
        public string IdToken { get; set; } = string.Empty;
    }
}
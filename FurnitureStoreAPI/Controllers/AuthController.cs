using FurnitureStoreAPI.Data;
using FurnitureStoreAPI.Models;
using FurnitureStoreAPI.Models.Dtos;
using FurnitureStoreAPI.Services;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FurnitureStoreAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly TokenService _tokenService;
        private readonly IConfiguration _config;
        public AuthController(AppDbContext context, TokenService tokenService, IConfiguration config)
        {
            _context = context;
            _tokenService = tokenService;
            _config = config;
        }

        // POST: api/Auth/login
        // Dùng chung cho cả Admin/Nhân viên (FurnitureStoreAdmin) và Khách hàng (FurnitureStoreClient)
        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginDto dto)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
            if (user == null)
                return Unauthorized(new { message = "Email hoặc mật khẩu không đúng!" });
            if (user.Status == "Bị khoá")
                return Unauthorized(new { message = "Tài khoản của bạn đã bị khoá!" });
            bool validPassword;
            try
            {
                validPassword = BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);
            }
            catch
            {
                validPassword = false;
            }
            if (!validPassword)
                return Unauthorized(new { message = "Email hoặc mật khẩu không đúng!" });
            var token = _tokenService.CreateToken(user);
            return Ok(new AuthResponseDto
            {
                Token = token,
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                Role = user.Role
            });
        }

        // POST: api/Auth/register
        // Công khai — dùng cho khách hàng tự đăng ký từ FurnitureStoreClient.
        // LUÔN ép role = "Khách hàng" bất kể client gửi gì lên, để tránh việc
        // người dùng tự đăng ký với quyền Admin/Nhân viên.
        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterDto dto)
        {
            if (await _context.Users.AnyAsync(u => u.Email == dto.Email))
                return BadRequest(new { message = "Email đã được sử dụng!" });
            var user = new User
            {
                Name = dto.Name,
                Email = dto.Email,
                Phone = dto.Phone,
                Address = dto.Address,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                Role = "Khách hàng",
                Status = "Hoạt động",
                AuthProvider = "local",
                CreatedAt = DateTime.Now
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            // Tự động đăng nhập luôn sau khi đăng ký thành công
            var token = _tokenService.CreateToken(user);
            return Ok(new AuthResponseDto
            {
                Token = token,
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                Role = user.Role
            });
        }

        // POST: api/Auth/google
        // Công khai — CHỈ dùng cho FurnitureStoreClient (khách hàng), không áp dụng cho Admin.
        // Nhận ID Token do Google Identity Services cấp ở phía trình duyệt, TỰ verify token đó
        // trực tiếp với Google (không tin bất kỳ thông tin nào khác gửi kèm từ Client — email/tên
        // đều lấy từ chính token đã verify, không lấy từ field rời rạc do Client tự gửi lên, để
        // tránh giả mạo danh tính).
        //
        // Quy tắc liên kết tài khoản:
        // - Email đã tồn tại (kể cả tài khoản đăng ký "local" bằng mật khẩu trước đó) → ĐĂNG NHẬP
        //   vào chính tài khoản đó luôn, không tạo trùng. Không đổi AuthProvider của tài khoản cũ
        //   (giữ nguyên "local" nếu trước đó là local) — chỉ đơn giản cho phép đăng nhập bằng cả
        //   2 đường (mật khẩu VÀ Google) trên cùng 1 tài khoản.
        // - Email chưa tồn tại → tạo User mới, AuthProvider = "google", PasswordHash để rỗng
        //   (xem giải thích ở User.cs).
        [HttpPost("google")]
        public async Task<IActionResult> GoogleLogin(GoogleLoginDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.IdToken))
                return BadRequest(new { message = "Thiếu ID Token." });

            GoogleJsonWebSignature.Payload payload;
            try
            {
                var clientId = _config["GoogleAuth:ClientId"]
                    ?? throw new InvalidOperationException("Thiếu cấu hình GoogleAuth:ClientId trong appsettings.json");

                // Validate chữ ký + audience (đảm bảo token được cấp CHO đúng ứng dụng LuxWood,
                // không phải token của app khác) + hạn sử dụng — toàn bộ do thư viện chính thức
                // Google.Apis.Auth xử lý, không tự parse JWT thủ công để tránh sai sót bảo mật.
                payload = await GoogleJsonWebSignature.ValidateAsync(dto.IdToken, new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { clientId },
                });
            }
            catch (InvalidJwtException ex)
            {
                Console.WriteLine("===== GOOGLE TOKEN ERROR =====");
                Console.WriteLine(ex.Message);
                Console.WriteLine("==============================");

                return Unauthorized(new
                {
                    message = ex.Message
                });
            }

            if (!payload.EmailVerified)
                return Unauthorized(new { message = "Email Google chưa được xác minh." });

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == payload.Email);

            if (user == null)
            {
                user = new User
                {
                    Name = payload.Name ?? payload.Email,
                    Email = payload.Email,
                    Phone = "",
                    Address = "",
                    PasswordHash = "", // Xem giải thích ở User.cs — chưa có mật khẩu, không đăng
                                       // nhập được qua đường thường cho tới khi tự đặt mật khẩu.
                    Role = "Khách hàng",
                    Status = "Hoạt động",
                    AuthProvider = "google",
                    CreatedAt = DateTime.Now,
                };
                _context.Users.Add(user);
                await _context.SaveChangesAsync();
            }
            else if (user.Status == "Bị khoá")
            {
                return Unauthorized(new { message = "Tài khoản của bạn đã bị khoá!" });
            }

            var token = _tokenService.CreateToken(user);
            return Ok(new AuthResponseDto
            {
                Token = token,
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                Role = user.Role
            });
        }

        // GET: api/Auth/me — Lấy thông tin tài khoản đang đăng nhập (dùng cho trang Tài khoản cá nhân)
        // Khác với UsersController (chỉ Admin xem được ai cũng được), endpoint này để CHÍNH CHỦ tự xem của mình.
        [Authorize]
        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(idClaim, out var userId)) return Unauthorized();

            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound();

            return Ok(new { user.Id, user.Name, user.Email, user.Phone, user.Address, user.Role });
        }

        // PUT: api/Auth/me — Cập nhật tên & số điện thoại của chính mình
        // KHÔNG cho đổi Email/Role/Status ở đây — tránh khách tự nâng quyền hoặc đổi email trùng lặp không kiểm tra.
        [Authorize]
        [HttpPut("me")]
        public async Task<IActionResult> UpdateMe(UpdateProfileDto dto)
        {
            var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(idClaim, out var userId)) return Unauthorized();

            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound();

            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(new { message = "Tên không được để trống!" });

            user.Name = dto.Name;
            user.Phone = dto.Phone;
            user.Address = dto.Address;
            await _context.SaveChangesAsync();

            return Ok(new { user.Id, user.Name, user.Email, user.Phone, user.Address, user.Role });
        }

        // PUT: api/Auth/change-password — Đổi mật khẩu, bắt buộc nhập đúng mật khẩu hiện tại trước
        [Authorize]
        [HttpPut("change-password")]
        public async Task<IActionResult> ChangePassword(ChangePasswordDto dto)
        {
            var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(idClaim, out var userId)) return Unauthorized();

            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound();

            bool validPassword;
            try
            {
                validPassword = BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash);
            }
            catch
            {
                validPassword = false;
            }
            if (!validPassword)
                return BadRequest(new { message = "Mật khẩu hiện tại không đúng!" });

            if (string.IsNullOrWhiteSpace(dto.NewPassword) || dto.NewPassword.Length < 6)
                return BadRequest(new { message = "Mật khẩu mới phải có ít nhất 6 ký tự!" });

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}
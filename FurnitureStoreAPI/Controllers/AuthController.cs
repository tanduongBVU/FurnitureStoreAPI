using FurnitureStoreAPI.Data;
using FurnitureStoreAPI.Models;
using FurnitureStoreAPI.Models.Dtos;
using FurnitureStoreAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FurnitureStoreAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly TokenService _tokenService;

        public AuthController(AppDbContext context, TokenService tokenService)
        {
            _context = context;
            _tokenService = tokenService;
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
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                Role = "Khách hàng",
                Status = "Hoạt động",
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
    }
}
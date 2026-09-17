using FurnitureStoreAPI.Data;
using FurnitureStoreAPI.Models;
using FurnitureStoreAPI.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// =====================================================
// CONTROLLERS
// =====================================================

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler =
            ReferenceHandler.IgnoreCycles;
    });


// =====================================================
// DATABASE
// =====================================================

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")
    )
);


// =====================================================
// CORS
// =====================================================

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontends", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:5173",
                "http://localhost:5174",
                "https://furniture-store-client-rouge.vercel.app"   // ← thêm dòng này
            )
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});


// =====================================================
// JWT
// =====================================================

var jwtSettings = builder.Configuration.GetSection("Jwt");

var jwtKey = jwtSettings["Key"]
    ?? throw new InvalidOperationException(
        "Thiếu cấu hình Jwt:Key trong appsettings.json"
    );

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme =
            JwtBearerDefaults.AuthenticationScheme;

        options.DefaultChallengeScheme =
            JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,

                ValidIssuer = jwtSettings["Issuer"],
                ValidAudience = jwtSettings["Audience"],

                IssuerSigningKey =
                    new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtKey)
                    )
            };
    });


// =====================================================
// AUTHORIZATION
// =====================================================

builder.Services.AddAuthorization();


// =====================================================
// TOKEN SERVICE
// =====================================================

// Nếu class của bạn tên là TokenService
builder.Services.AddSingleton<TokenService>();


// =====================================================
// EMAIL SERVICE
// =====================================================

// Bind section "Email" trong appsettings.json vào EmailSettings, để EmailService
// nhận qua IOptions<EmailSettings> (Options Pattern chuẩn của ASP.NET Core).
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("Email"));
// Scoped vì SmtpClient không nên dùng chung 1 instance xuyên suốt vòng đời ứng dụng
// (Singleton) — mỗi request/scope tự tạo instance riêng, an toàn hơn khi gửi email
// đồng thời từ nhiều request khác nhau.
builder.Services.AddScoped<IEmailService, EmailService>();


// =====================================================

// HTTP CLIENT (dùng cho ChatController gọi ra Gemini API)
// =====================================================

// Đăng ký IHttpClientFactory — cách chuẩn của ASP.NET Core để tạo HttpClient, tránh lỗi
// "socket exhaustion" nếu tự new HttpClient() thủ công nhiều lần. ChatController inject
// IHttpClientFactory qua constructor, không cần cấu hình gì thêm ở đây.
builder.Services.AddHttpClient();


// =====================================================

// SWAGGER
// =====================================================

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition(
        "Bearer",
        new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Description = "Nhập: Bearer {token}",
            Name = "Authorization",
            In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
            Scheme = "Bearer"
        }
    );

    options.AddSecurityRequirement(
        new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
        {
            {
                new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Reference =
                        new Microsoft.OpenApi.Models.OpenApiReference
                        {
                            Type =
                                Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,

                            Id = "Bearer"
                        }
                },

                Array.Empty<string>()
            }
        }
    );
});


// =====================================================
// BUILD APP
// =====================================================

var app = builder.Build();


// =====================================================
// SWAGGER
// =====================================================

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


// =====================================================
// HTTPS
// =====================================================

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}


// =====================================================
// STATIC FILES (ảnh upload từ Admin — SiteSettings, sản phẩm, v.v.)
// =====================================================

// Phục vụ nội dung tĩnh trong wwwroot/ (bao gồm wwwroot/uploads/ do UploadController
// tạo ra) ra ngoài qua URL dạng https://.../uploads/xxxx.jpg. KHÔNG có dòng này thì
// dù file có nằm sẵn trên ổ đĩa, mọi request GET tới đều nhận 404 — đây chính là lý do
// ảnh vừa upload xong không hiển thị được. Đặt TRƯỚC Authentication/Authorization vì
// ảnh (banner, sản phẩm...) cần công khai cho cả khách chưa đăng nhập ở Client xem được,
// không riêng gì Admin.
app.UseStaticFiles();


// =====================================================
// CORS
// =====================================================

app.UseCors("AllowFrontends");


// =====================================================
// AUTHENTICATION
// =====================================================

app.UseAuthentication();


// =====================================================
// AUTHORIZATION
// =====================================================

app.UseAuthorization();


// =====================================================
// CONTROLLERS
// =====================================================

app.MapControllers();


// =====================================================
// RUN
// =====================================================

app.Run();
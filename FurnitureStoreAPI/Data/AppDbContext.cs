using Microsoft.EntityFrameworkCore;
using FurnitureStoreAPI.Models;
namespace FurnitureStoreAPI.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options) { }
        public DbSet<Product> Products { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<Contact> Contacts { get; set; }
        public DbSet<SiteSetting> SiteSettings { get; set; }   // ← dòng mới thêm
        public DbSet<BlogPost> BlogPosts { get; set; }
        public DbSet<Review> Reviews { get; set; }
        public DbSet<NewsletterSubscriber> NewsletterSubscribers { get; set; } // ← dòng mới thêm
        public DbSet<Coupon> Coupons { get; set; }

        public DbSet<Service> Services { get; set; }
        public DbSet<Project> Projects { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Order → OrderItems
            modelBuilder.Entity<OrderItem>()
                .HasOne(oi => oi.Order)
                .WithMany(o => o.OrderItems)
                .HasForeignKey(oi => oi.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            // OrderItem → Product
            modelBuilder.Entity<OrderItem>()
                .HasOne(oi => oi.Product)
                .WithMany()
                .HasForeignKey(oi => oi.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            // Order → User (khách đã đăng nhập). UserId nullable = khách vãng lai (không có
            // User liên kết). IsRequired(false) khai báo rõ FK này được phép null.
            // Restrict: nếu User đó còn đơn hàng, chặn xoá — giữ toàn vẹn lịch sử đơn hàng,
            // tránh Order "mồ côi" trỏ tới một User không còn tồn tại.
            modelBuilder.Entity<Order>()
                .HasOne<User>()
                .WithMany()
                .HasForeignKey(o => o.UserId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            // Review → User. Review luôn được tạo bởi khách ĐÃ đăng nhập ([Authorize] ở
            // ReviewsController), nên UserId ở đây luôn có giá trị thật — FK bắt buộc (not null).
            // Restrict: chặn xoá User nếu còn Review, giữ toàn vẹn đánh giá đã có.
            modelBuilder.Entity<Review>()
                .HasOne<User>()
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Review → Product. Tương tự — Restrict để chặn xoá Product nếu còn Review,
            // tránh mất đánh giá hoặc Review trỏ tới sản phẩm không tồn tại. Lưu ý:
            // ProductsController hiện xoá sản phẩm bằng soft-delete (IsActive = false),
            // không xoá hẳn dòng dữ liệu, nên ràng buộc này chủ yếu phòng ngừa trường hợp
            // xoá cứng thủ công qua DB hoặc script khác trong tương lai.
            modelBuilder.Entity<Review>()
                .HasOne<Product>()
                .WithMany()
                .HasForeignKey(r => r.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            // Decimal precision
            modelBuilder.Entity<Product>()
                .Property(p => p.Price)
                .HasPrecision(18, 2);
            modelBuilder.Entity<Order>()
                .Property(o => o.Total)
                .HasPrecision(18, 2);
            modelBuilder.Entity<OrderItem>()
                .Property(oi => oi.Price)
                .HasPrecision(18, 2);
            modelBuilder.Entity<Coupon>()
                .Property(c => c.MinOrderValue)
                .HasPrecision(18, 2);
        }
    }
}
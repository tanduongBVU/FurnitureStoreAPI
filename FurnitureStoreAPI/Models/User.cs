namespace FurnitureStoreAPI.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string Role { get; set; } = "Khách hàng";
        public string Status { get; set; } = "Hoạt động";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}

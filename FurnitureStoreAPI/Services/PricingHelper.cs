using FurnitureStoreAPI.Models;

namespace FurnitureStoreAPI.Services
{
    // MỘT NƠI DUY NHẤT quy định cách tính giá — BundlesController (hiện giá combo cho khách)
    // và OrdersController (tính tiền khi đặt hàng) cùng gọi vào đây, nên giá khách thấy
    // luôn trùng giá được tính tiền, không lệch vài đồng do làm tròn khác nhau.
    public static class PricingHelper
    {
        // Giá bán thật của 1 sản phẩm (đã trừ giảm giá riêng của sản phẩm), làm tròn đồng.
        public static decimal SaleUnitPrice(Product p) =>
            Math.Round(p.Price * (1 - p.DiscountPercent / 100m), 0, MidpointRounding.AwayFromZero);

        // Giá 1 sản phẩm khi mua TRONG combo: lấy giá bán thật rồi giảm thêm % của combo.
        // Làm tròn từng đơn vị sản phẩm (không làm tròn tổng) để giá combo = tổng các dòng.
        public static decimal ComboUnitPrice(Product p, int bundleDiscountPercent) =>
            Math.Round(SaleUnitPrice(p) * (1 - bundleDiscountPercent / 100m), 0, MidpointRounding.AwayFromZero);
    }
}
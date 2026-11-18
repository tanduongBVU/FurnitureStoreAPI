using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FurnitureStoreAPI.Services
{
    // Cách xác nhận thanh toán KHÔNG cần SePay gọi vào máy mình (không cần ngrok / hosting):
    // Backend chủ động hỏi SePay "có giao dịch tiền vào nào chứa DH{mã đơn} chưa?" bằng API Token.
    // Chỉ cần máy chạy Backend ra được Internet (gọi đi), nên chạy thuần localhost vẫn dùng được.
    //
    // Cấu hình appsettings.json:  "SePay": { "ApiToken": "<token tạo ở my.sepay.vn → Cấu hình Công ty → API Access>" }
    // Giới hạn của SePay: 3 request/giây/token → có chặn gọi dồn ở dưới.
    public static class SePayPoller
    {
        private const string Endpoint = "https://userapi.sepay.vn/v2/transactions";
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
        private static readonly Regex OrderCodeRegex = new(@"DH(\d{1,9})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly ConcurrentDictionary<int, DateTime> LastCheck = new();
        private static readonly SemaphoreSlim Gate = new(1, 1);

        public static bool IsConfigured(IConfiguration config) =>
            !string.IsNullOrWhiteSpace(config["SePay:ApiToken"]);

        // true nếu đã thấy giao dịch tiền vào ghi đúng mã DH{orderId} với số tiền >= total.
        public static async Task<bool> HasPaidAsync(IConfiguration config, int orderId, decimal total, ILogger logger)
        {
            var token = config["SePay:ApiToken"];
            if (string.IsNullOrWhiteSpace(token)) return false;

            // Mỗi đơn tối đa 1 lần hỏi / 4 giây (Client hỏi trạng thái mỗi 3 giây, nhiều tab cũng không làm dồn).
            var now = DateTime.UtcNow;
            if (LastCheck.TryGetValue(orderId, out var last) && (now - last).TotalSeconds < 4) return false;
            LastCheck[orderId] = now;

            await Gate.WaitAsync();
            try
            {
                // Không dùng tham số q (cách SePay tìm kiếm chưa rõ): lấy 50 giao dịch tiền vào mới nhất rồi tự lọc theo mã DH.
                var url = $"{Endpoint}?transfer_type=in&transaction_date_sort=desc&per_page=50";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var res = await Http.SendAsync(req);
                await Task.Delay(350); // giữ dưới 3 request/giây khi nhiều đơn cùng chờ

                if (!res.IsSuccessStatusCode)
                {
                    logger.LogWarning("SePay API trả {Status} khi hỏi giao dịch cho đơn #{OrderId}.", (int)res.StatusCode, orderId);
                    return false;
                }

                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                    return false;

                logger.LogInformation("SePay: đơn #{OrderId} cần {Total}đ — SePay trả {Count} giao dịch tiền vào gần nhất.", orderId, total, data.GetArrayLength());
                foreach (var tx in data.EnumerateArray())
                {
                    var content = Str(tx, "transaction_content");
                    var code = Str(tx, "code");
                    var id = Extract(code) ?? Extract(content);
                    if (id != orderId) continue;                         // "DH930" không được tính cho đơn 93
                    if (Str(tx, "transfer_type") != "in") continue;
                    if (!tx.TryGetProperty("amount_in", out var a) || !TryDecimal(a, out var amount)) continue;
                    if (amount >= total) { logger.LogInformation("SePay: ĐÃ THẤY thanh toán cho đơn #{OrderId} ({Amount}đ).", orderId, amount); return true; }
                    logger.LogWarning("Đơn #{OrderId} cần {Total} nhưng giao dịch chỉ {Amount}.", orderId, total, amount);
                }
                logger.LogInformation("SePay: chưa có giao dịch nào ghi mã DH{OrderId}.", orderId);
                return false;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Không hỏi được SePay cho đơn #{OrderId}.", orderId);
                return false;
            }
            finally { Gate.Release(); }
        }

        private static string Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        private static bool TryDecimal(JsonElement v, out decimal d)
        {
            d = 0;
            return v.ValueKind switch
            {
                JsonValueKind.Number => v.TryGetDecimal(out d),
                JsonValueKind.String => decimal.TryParse(v.GetString(), System.Globalization.NumberStyles.Any,
                                            System.Globalization.CultureInfo.InvariantCulture, out d),
                _ => false
            };
        }

        private static int? Extract(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var m = OrderCodeRegex.Match(text);
            return m.Success && int.TryParse(m.Groups[1].Value, out var id) ? id : null;
        }
    }
}
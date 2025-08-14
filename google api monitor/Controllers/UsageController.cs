using Google.Apis.Auth.OAuth2;
using Google.Apis.Monitoring.v3;
using Google.Apis.Monitoring.v3.Data;
using Google.Apis.Services;
using Map.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Map.Controllers
{
    [ApiController]
    [Route("api/usage")]
    public class UsageController : ControllerBase
    {
        private readonly GoogleMapsSettings _settings;
        private readonly UsageService _usageService; // 加入本地用量服務

        // 把「產品 → 可能的 service label」集中定義（可再加）
        // 需要忽略計數的 Google 網域關鍵字
        private static readonly string[] IgnoredHosts = new[]
        {
    "monitoring.googleapis.com",
    "analytics.googleapis.com",
    "fonts.googleapis.com",
    "fonts.gstatic.com",
    "maps.gstatic.com",
    "clients1.google.com"
};

        // 改良版 API 分組表（貼近 GCP 計費邏輯）
        private static readonly Dictionary<string, string[]> ProductGroups = new Dictionary<string, string[]>
        {
            // === Places API ===
            ["Places API"] = new[]
            {
        //"places.googleapis.com",
        "places-backend.googleapis.com",
        //"places-nearbysearch.googleapis.com",
        //"places-autocomplete.googleapis.com",
        "place-details.googleapis.com",
        "place-photos.googleapis.com"
    },

            // === Directions API ===
            ["Directions API"] = new[]
            {
        "directions-backend.googleapis.com",
        //"routes.googleapis.com"
        // ⚠ 不直接把 maps.googleapis.com 全部算進來
        // 因為它還包含 Geocoding / Distance Matrix 等其他 API
    },

            // === Distance Matrix API ===
            ["Distance Matrix API"] = new[]
            {
        "distancematrix-backend.googleapis.com"
    },

            // === Geocoding API ===
            ["Geocoding API"] = new[]
            {
        "geocoding-backend.googleapis.com"
    },

            // === Roads API ===
            ["Roads API"] = new[]
            {
        "roads.googleapis.com"
    },

            // === Maps JavaScript API ===
            ["Maps JavaScript API"] = new[]
            {
        "maps-backend.googleapis.com"
        // ⚠ 這是地圖 JS 加載與互動，不同於 Directions API
    }
        };


        public static void ProcessHost(string host)
        {
            // 1. 過濾不需要計算的 host
            if (IgnoredHosts.Any(h => host.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return; // 直接跳過，不列入計算
            }

            // 2. 判斷屬於哪個 API 群組
            foreach (var group in ProductGroups)
            {
                if (group.Value.Any(apiHost => host.IndexOf(apiHost, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    // TODO: 在這裡加上你的計數邏輯，例如：
                    // IncrementApiUsage(group.Key);
                    Console.WriteLine($"計入：{group.Key} ({host})");
                    break;
                }
            }
        }


        public UsageController(IOptions<GoogleMapsSettings> settings, UsageService usageService)
        {
            _settings = settings.Value;
            _usageService = usageService;
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var projectId = _settings.GoogleCloud?.ProjectId;
            if (string.IsNullOrWhiteSpace(projectId))
                return BadRequest("GoogleCloud.ProjectId 未設定");

            var quotas = _settings.GoogleCloud?.Quota ?? new();

            var now = DateTime.UtcNow;
            var endUtc = now.AddMinutes(-15);
            var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            var cred = await GoogleCredential.GetApplicationDefaultAsync();
            if (cred.IsCreateScopedRequired) cred = cred.CreateScoped(MonitoringService.Scope.MonitoringRead);

            var svc = new MonitoringService(new BaseClientService.Initializer
            {
                HttpClientInitializer = cred,
                ApplicationName = "MapsUsageDashboard"
            });

            var perService = await FetchServiceCountsAsync(svc, projectId!, monthStart, endUtc);

            var servicesOut = new Dictionary<string, object?>();
            long sumUsed = 0, sumQuota = 0;

            foreach (var group in ProductGroups)
            {
                var product = group.Key;
                var labels = group.Value;

                long used = 0;
                foreach (var label in labels)
                {
                    if (perService.TryGetValue(label, out var cnt)) used += cnt;
                }

                var quota = quotas.TryGetValue(product, out var q) ? q : 0;
                var remaining = Math.Max(0, quota - used);

                servicesOut[product] = new
                {
                    used,
                    quota,
                    remaining,
                    labels
                };

                sumUsed += used;
                sumQuota += quota;
            }

            var payload = new
            {
                projectId,
                period = new { start = monthStart, end = endUtc },
                services = servicesOut,
                totals = new
                {
                    used = sumUsed,
                    quota = sumQuota,
                    remaining = Math.Max(0, sumQuota - sumUsed)
                },
                rawByService = perService
            };

            return Ok(payload);
        }

        // 讓前端手動 bump 用量
        [HttpPost("bump")]
        public async Task<IActionResult> Bump([FromBody] BumpRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Service))
                return BadRequest("Service 不能為空");

            await _usageService.BumpAsync(req.Service, req.Amount);
            return Ok(new { message = $"已增加 {req.Service} 用量 {req.Amount}" });
        }

        public class BumpRequest
        {
            public string Service { get; set; }
            public int Amount { get; set; } = 1;
            public string Reason { get; set; }
        }

        private static async Task<Dictionary<string, long>> FetchServiceCountsAsync(
            MonitoringService svc,
            string projectId,
            DateTime startUtc,
            DateTime endUtc)
        {
            var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            var req = svc.Projects.TimeSeries.List($"projects/{projectId}");
            req.Filter = "metric.type = \"serviceruntime.googleapis.com/api/request_count\" " +
                         "AND resource.type = \"consumed_api\"";
            req.IntervalStartTime = startUtc.ToString("o");
            req.IntervalEndTime = endUtc.ToString("o");

            string? pageToken = null;
            do
            {
                req.PageToken = pageToken;
                var resp = await req.ExecuteAsync();

                if (resp.TimeSeries != null)
                {
                    foreach (var ts in resp.TimeSeries)
                    {
                        var labels = ts.Resource?.Labels;
                        if (labels == null) continue;

                        labels.TryGetValue("service", out var serviceLabel);
                        if (string.IsNullOrEmpty(serviceLabel)) continue;

                        long sum = 0;
                        if (ts.Points != null)
                        {
                            foreach (var p in ts.Points)
                            {
                                if (p.Value?.Int64Value != null) sum += p.Value.Int64Value.Value;
                                else if (p.Value?.DoubleValue != null) sum += (long)Math.Round(p.Value.DoubleValue.Value);
                            }
                        }

                        map.TryGetValue(serviceLabel, out var prev);
                        map[serviceLabel] = prev + sum;
                    }
                }

                pageToken = resp.NextPageToken;
            } while (!string.IsNullOrEmpty(pageToken));

            return map;
        }
    }
}

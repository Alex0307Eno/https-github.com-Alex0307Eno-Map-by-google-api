// Services/UsageService.cs
using Microsoft.Extensions.Options;
using System.Text.Json;
using Map.Models;

public class UsageService
{
    private readonly IWebHostEnvironment _env;
    private readonly GoogleMapsSettings _opt;
    private readonly string _storePath;
    private readonly object _lock = new();

    // 我們統一用這 4 個名稱（跟前端一致）
    public static readonly string[] ApiNames = new[]
    {
        "Places API", "Maps JavaScript API", "Directions API", "Roads API"
    };

    public UsageService(IWebHostEnvironment env, IOptions<GoogleMapsSettings> opt)
    {
        _env = env;
        _opt = opt.Value;
        _storePath = Path.Combine(_env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(_storePath);
    }

    private string CurrentMonthKey => DateTime.UtcNow.ToString("yyyy-MM");
    private string FilePath => Path.Combine(_storePath, $"gmaps-usage-{CurrentMonthKey}.json");

    private Dictionary<string, int> Load()
    {
        lock (_lock)
        {
            if (!File.Exists(FilePath))
                return ApiNames.ToDictionary(n => n, _ => 0);
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<Dictionary<string, int>>(json)
                   ?? ApiNames.ToDictionary(n => n, _ => 0);
        }
    }

    private void Save(Dictionary<string, int> data)
    {
        lock (_lock)
        {
            var json = JsonSerializer.Serialize(data);
            File.WriteAllText(FilePath, json);
        }
    }

    public Task<Dictionary<string, int>> GetRawAsync()
    {
        return Task.FromResult(Load());
    }

    public Task BumpAsync(string apiName, int count)
    {
        if (count <= 0) count = 1;
        var data = Load();
        if (!data.ContainsKey(apiName)) data[apiName] = 0;
        data[apiName] += count;
        Save(data);
        return Task.CompletedTask;
    }

    public Task ResetAsync()
    {
        // 只清當月檔
        if (File.Exists(FilePath)) File.Delete(FilePath);
        return Task.CompletedTask;
    }

    public Task<object> GetSummaryAsync()
    {
        var used = Load();
        var quota = _opt.GoogleCloud.Quota ?? new();

        // 統一保證四個 key 都存在
        foreach (var n in ApiNames)
            if (!used.ContainsKey(n)) used[n] = 0;

        var rows = ApiNames.Select(n => {
            var q = quota.ContainsKey(n) ? quota[n] : 0;
            var u = used[n];
            var r = Math.Max(q - u, 0);
            var pct = (q > 0) ? Math.Round(u * 100.0 / q, 1) : 0;
            return new { name = n, used = u, quota = q, remain = r, pct };
        }).ToList();

        var totalQuota = rows.Sum(x => x.quota);
        var totalUsed = rows.Sum(x => x.used);
        var totalRemain = rows.Sum(x => x.remain);
        var totalPct = (totalQuota > 0) ? Math.Round(totalUsed * 100.0 / totalQuota, 1) : 0;

        return Task.FromResult<object>(new
        {
            month = CurrentMonthKey,
            rows,
            total = new { used = totalUsed, quota = totalQuota, remain = totalRemain, pct = totalPct }
        });
    }
}

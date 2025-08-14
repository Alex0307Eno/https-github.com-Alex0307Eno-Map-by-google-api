using Map.Models;

namespace Map.Services
{
    public interface IUsageStore
    {
        Task IncreaseAsync(string monthKey, string apiName, int count, CancellationToken ct = default);
        Task<Dictionary<string, long>> GetMonthAsync(string monthKey, CancellationToken ct = default);
    }
}

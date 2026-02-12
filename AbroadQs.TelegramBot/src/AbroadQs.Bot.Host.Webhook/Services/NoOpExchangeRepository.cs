using AbroadQs.Bot.Contracts;

namespace AbroadQs.Bot.Host.Webhook.Services;

public sealed class NoOpExchangeRepository : IExchangeRepository
{
    public Task<int> GetNextRequestNumberAsync(CancellationToken ct = default) => Task.FromResult(1);
    public Task<ExchangeRequestDto> CreateRequestAsync(ExchangeRequestDto request, CancellationToken ct = default) => Task.FromResult(request);
    public Task<ExchangeRequestDto?> GetRequestAsync(int id, CancellationToken ct = default) => Task.FromResult<ExchangeRequestDto?>(null);
    public Task<ExchangeRequestDto?> GetRequestByNumberAsync(int number, CancellationToken ct = default) => Task.FromResult<ExchangeRequestDto?>(null);
    public Task<IReadOnlyList<ExchangeRequestDto>> ListRequestsAsync(string? status = null, long? userId = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ExchangeRequestDto>>(Array.Empty<ExchangeRequestDto>());
    public Task UpdateStatusAsync(int id, string status, string? adminNote = null, int? channelMsgId = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<ExchangeRateDto>> GetRatesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ExchangeRateDto>>(Array.Empty<ExchangeRateDto>());
    public Task<ExchangeRateDto?> GetRateAsync(string currencyCode, CancellationToken ct = default) => Task.FromResult<ExchangeRateDto?>(null);
    public Task SaveRatesAsync(IEnumerable<ExchangeRateDto> rates, CancellationToken ct = default) => Task.CompletedTask;
    public Task SaveRateAsync(ExchangeRateDto rate, CancellationToken ct = default) => Task.CompletedTask;
}

namespace AbroadQs.Bot.Contracts;

/// <summary>
/// Persistence for exchange requests and cached rates.
/// </summary>
public interface IExchangeRepository
{
    // ── Requests ─────────────────────────────────────────────────────
    Task<int> GetNextRequestNumberAsync(CancellationToken ct = default);
    Task<ExchangeRequestDto> CreateRequestAsync(ExchangeRequestDto request, CancellationToken ct = default);
    Task<ExchangeRequestDto?> GetRequestAsync(int id, CancellationToken ct = default);
    Task<ExchangeRequestDto?> GetRequestByNumberAsync(int number, CancellationToken ct = default);
    Task<IReadOnlyList<ExchangeRequestDto>> ListRequestsAsync(string? status = null, long? userId = null, CancellationToken ct = default);
    Task<(IReadOnlyList<ExchangeRequestDto> Items, int TotalCount)> ListUserRequestsPagedAsync(long userId, int year, int month, int page, int pageSize, CancellationToken ct = default);
    Task UpdateStatusAsync(int id, string status, string? adminNote = null, int? channelMsgId = null, CancellationToken ct = default);

    /// <summary>Returns a dictionary of year → exchange count for a given user.</summary>
    Task<IReadOnlyDictionary<int, int>> GetUserExchangeCountByYearAsync(long userId, CancellationToken ct = default);

    /// <summary>Returns a dictionary of month (1-12) → exchange count for a given user in a specific year.</summary>
    Task<IReadOnlyDictionary<int, int>> GetUserExchangeCountByMonthAsync(long userId, int year, CancellationToken ct = default);

    // ── Rates ────────────────────────────────────────────────────────
    Task<IReadOnlyList<ExchangeRateDto>> GetRatesAsync(CancellationToken ct = default);
    Task<ExchangeRateDto?> GetRateAsync(string currencyCode, CancellationToken ct = default);
    Task SaveRatesAsync(IEnumerable<ExchangeRateDto> rates, CancellationToken ct = default);
    Task SaveRateAsync(ExchangeRateDto rate, CancellationToken ct = default);
}

public sealed record ExchangeRequestDto(
    int Id,
    int RequestNumber,
    long TelegramUserId,
    string Currency,
    string TransactionType,
    string DeliveryMethod,
    string? AccountType,
    string? Country,
    decimal Amount,
    decimal ProposedRate,
    string? Description,
    decimal FeePercent,
    decimal FeeAmount,
    decimal TotalAmount,
    string Status,
    int? ChannelMessageId,
    string? AdminNote,
    string? UserDisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record ExchangeRateDto(
    int Id,
    string CurrencyCode,
    string? CurrencyNameFa,
    string? CurrencyNameEn,
    decimal Rate,
    decimal Change,
    string Source,
    DateTimeOffset LastUpdatedAt);

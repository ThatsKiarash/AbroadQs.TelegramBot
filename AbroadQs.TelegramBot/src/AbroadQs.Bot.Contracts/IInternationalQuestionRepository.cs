namespace AbroadQs.Bot.Contracts;

public interface IInternationalQuestionRepository
{
    Task<IntlQuestionDto> CreateAsync(IntlQuestionDto dto, CancellationToken ct = default);
    Task<IntlQuestionDto?> GetAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<IntlQuestionDto>> ListAsync(string? status = null, string? country = null, long? userId = null, int page = 0, int pageSize = 10, CancellationToken ct = default);
    Task UpdateStatusAsync(int id, string status, CancellationToken ct = default);

    Task<QuestionAnswerDto> CreateAnswerAsync(QuestionAnswerDto dto, CancellationToken ct = default);
    Task<IReadOnlyList<QuestionAnswerDto>> ListAnswersAsync(int questionId, CancellationToken ct = default);
    Task UpdateAnswerStatusAsync(int id, string status, CancellationToken ct = default);

    /// <summary>Lists questions posted by the user with answer counts.</summary>
    Task<IReadOnlyList<IntlQuestionWithAnswerCountDto>> ListByUserWithAnswerCountAsync(long userId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Lists answers submitted by the user with question text.</summary>
    Task<IReadOnlyList<UserAnswerWithQuestionDto>> ListAnswersByUserAsync(long userId, int page, int pageSize, CancellationToken ct = default);
}

public sealed record IntlQuestionWithAnswerCountDto(
    int Id, long TelegramUserId, string QuestionText, string? TargetCountry,
    decimal BountyAmount, string Status, string? UserDisplayName, DateTimeOffset CreatedAt, int AnswerCount);

public sealed record UserAnswerWithQuestionDto(
    int AnswerId, int QuestionId, string QuestionText, string AnswerText,
    string Status, DateTimeOffset CreatedAt);

public sealed record IntlQuestionDto(
    int Id, long TelegramUserId, string QuestionText, string? TargetCountry,
    decimal BountyAmount, string Status, int? ChannelMessageId, string? UserDisplayName,
    DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

public sealed record QuestionAnswerDto(
    int Id, int QuestionId, long AnswererTelegramUserId, string AnswerText,
    string Status, DateTimeOffset CreatedAt);

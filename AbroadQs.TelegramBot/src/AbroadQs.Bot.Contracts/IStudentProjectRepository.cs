namespace AbroadQs.Bot.Contracts;

public interface IStudentProjectRepository
{
    Task<StudentProjectDto> CreateAsync(StudentProjectDto dto, CancellationToken ct = default);
    Task<StudentProjectDto?> GetAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<StudentProjectDto>> ListAsync(string? status = null, string? category = null, long? userId = null, int page = 0, int pageSize = 10, CancellationToken ct = default);
    Task UpdateStatusAsync(int id, string status, string? adminNote = null, int? channelMsgId = null, CancellationToken ct = default);
    Task AssignAsync(int id, long assignedToUserId, CancellationToken ct = default);
}

public interface IProjectBidRepository
{
    Task<ProjectBidDto> CreateAsync(ProjectBidDto dto, CancellationToken ct = default);
    Task<ProjectBidDto?> GetAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<ProjectBidDto>> ListForProjectAsync(int projectId, CancellationToken ct = default);
    Task<IReadOnlyList<ProjectBidDto>> ListByBidderAsync(long bidderUserId, int page = 0, int pageSize = 10, CancellationToken ct = default);
    Task UpdateStatusAsync(int id, string status, CancellationToken ct = default);
}

public sealed record StudentProjectDto(
    int Id, long TelegramUserId, string Title, string? Description, string Category,
    decimal Budget, string? Currency, DateTimeOffset? Deadline, string? RequiredSkills,
    string Status, int? ChannelMessageId, long? AssignedToUserId, string? AdminNote,
    string? UserDisplayName, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

public sealed record ProjectBidDto(
    int Id, int ProjectId, long BidderTelegramUserId, string? BidderDisplayName,
    decimal ProposedAmount, string? ProposedDuration, string? CoverLetter, string? PortfolioLink,
    string Status, DateTimeOffset CreatedAt);

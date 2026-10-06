using Shared.DataTransferObjects;

namespace BamboeUp.Mobile.Shell.Models;

public sealed class UserSession
{
    public string Token { get; set; } = string.Empty;
    public DateTime Expired { get; set; }
    public long UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public long? ActiveCompanyId { get; set; }
    public long? ActiveOfficeId { get; set; }
    public Guid? ActiveCompanyGuid { get; set; }
    public Guid? ActiveOfficeGuid { get; set; }
    public Guid UserGuid { get; set; }
    public long? PersonId { get; set; }
    public string? PersonName { get; set; }
    public string? ActiveCompanyName { get; set; }
    public string? ActiveOfficeName { get; set; }

    public static UserSession FromToken(TokenDto token) => new()
    {
        Token = token.Token,
        Expired = token.Expired.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(token.Expired, DateTimeKind.Utc)
            : token.Expired.ToUniversalTime(),
        UserId = token.UserId,
        UserName = token.UserName,
        FullName = token.FullName,
        Role = token.Role,
        IsAdmin = token.IsAdmin,
        ActiveCompanyId = token.ActiveCompanyId,
        ActiveOfficeId = token.ActiveOfficeId,
        ActiveCompanyGuid = token.ActiveCompanyGuid,
        ActiveOfficeGuid = token.ActiveOfficeGuid,
        UserGuid = token.UserGuid,
        PersonId = token.PersonId,
        PersonName = token.PersonName,
        ActiveCompanyName = token.ActiveCompanyName,
        ActiveOfficeName = token.ActiveOfficeName
    };
}

public sealed class DashboardKpi
{
    public string Title { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public string Tone { get; init; } = "red";
}

public sealed class ProjectSummary
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Progress { get; init; }
    public string UpdatedLabel { get; init; } = string.Empty;
}

public sealed class ModuleTile
{
    public string Title { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public string? Href { get; init; }
}

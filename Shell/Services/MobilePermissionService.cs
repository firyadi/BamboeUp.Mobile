using System.Net.Http.Json;
using Shared.DataTransferObjects;

namespace BamboeUp.Mobile.Shell.Services;

public sealed class MobilePageRight
{
    public bool CanAdd { get; init; }
    public bool CanEdit { get; init; }
    public bool CanDelete { get; init; }
    public bool CanApprove { get; init; }
    public bool CanUnApprove { get; init; }
    public bool CanVoid { get; init; }
    public bool CanUnVoid { get; init; }
    public bool CanPrint { get; init; }

    public static MobilePageRight Granted { get; } = new()
    {
        CanAdd = true,
        CanEdit = true,
        CanDelete = true,
        CanApprove = true,
        CanUnApprove = true,
        CanVoid = true,
        CanUnVoid = true,
        CanPrint = true
    };

    public static MobilePageRight Denied { get; } = new();
}

/// <summary>
/// Same program rights as the web toolbar, loaded from the signed-in user group.
/// </summary>
public sealed class MobilePermissionService
{
    private readonly IHttpClientFactory _factory;
    private readonly MobileAuthService _auth;
    private readonly Dictionary<string, MobilePageRight> _cache = new(StringComparer.OrdinalIgnoreCase);
    private string? _cacheKey;

    public MobilePermissionService(IHttpClientFactory factory, MobileAuthService auth)
    {
        _factory = factory;
        _auth = auth;
    }

    public async Task<MobilePageRight> GetAsync(string programCode, CancellationToken cancellationToken = default)
    {
        var user = _auth.CurrentUser;
        if (user is null || !_auth.IsAuthenticated)
            return MobilePageRight.Denied;

        if (user.UserGuid == Guid.Empty)
            return user.IsAdmin ? MobilePageRight.Granted : MobilePageRight.Denied;

        var scopeKey = $"{user.UserGuid:N}:{user.ActiveCompanyId}:{user.ActiveOfficeId}";
        if (_cacheKey != scopeKey)
        {
            _cache.Clear();
            _cacheKey = scopeKey;
        }

        if (_cache.TryGetValue(programCode, out var cached))
            return cached;

        var right = await LoadAsync(user, programCode, cancellationToken);
        _cache[programCode] = right;
        return right;
    }

    private async Task<MobilePageRight> LoadAsync(Models.UserSession user, string programCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(programCode))
            return user.IsAdmin ? MobilePageRight.Granted : MobilePageRight.Denied;

        try
        {
            var query = new List<string>();
            var company = user.ActiveCompanyGuid is Guid companyGuid && companyGuid != Guid.Empty
                ? companyGuid.ToString()
                : user.ActiveCompanyId is > 0 ? user.ActiveCompanyId.Value.ToString() : null;
            var office = user.ActiveOfficeGuid is Guid officeGuid && officeGuid != Guid.Empty
                ? officeGuid.ToString()
                : user.ActiveOfficeId is > 0 ? user.ActiveOfficeId.Value.ToString() : null;
            if (!string.IsNullOrWhiteSpace(company))
                query.Add($"companyId={Uri.EscapeDataString(company)}");
            if (!string.IsNullOrWhiteSpace(office))
                query.Add($"officeId={Uri.EscapeDataString(office)}");

            var url = $"api/users/{user.UserGuid}/allowed-menus";
            if (query.Count > 0)
                url += "?" + string.Join("&", query);

            var http = _factory.CreateClient("BamboeUp.Mobile.Authenticated");
            var programs = await http.GetFromJsonAsync<List<ProgramDto>>(url, cancellationToken) ?? [];
            var program = programs.FirstOrDefault(item =>
                !string.IsNullOrWhiteSpace(item.ProgramCode) &&
                item.ProgramCode.Trim().Equals(programCode.Trim(), StringComparison.OrdinalIgnoreCase));

            if (program is null)
                return user.IsAdmin ? MobilePageRight.Granted : MobilePageRight.Denied;

            return new MobilePageRight
            {
                CanAdd = program.IsProgramAddAble ?? false,
                CanEdit = program.IsProgramEditAble ?? false,
                CanDelete = program.IsProgramDeleteAble ?? false,
                CanApprove = program.IsProgramApprovalAble ?? false,
                CanUnApprove = program.IsProgramUnApprovalAble ?? false,
                CanVoid = program.IsProgramVoidAble ?? false,
                CanUnVoid = program.IsProgramUnVoidAble ?? false,
                CanPrint = program.IsProgramPrintAble ?? false
            };
        }
        catch
        {
            return user.IsAdmin ? MobilePageRight.Granted : MobilePageRight.Denied;
        }
    }
}

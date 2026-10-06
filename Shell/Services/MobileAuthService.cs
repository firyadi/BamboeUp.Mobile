using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using BamboeUp.Mobile.Shell.Models;
using Shared.DataTransferObjects;
using Shared.Security;
using Shared.Settings;

namespace BamboeUp.Mobile.Shell.Services;

public interface IBamboeUpApiClient
{
    Task<TokenDto?> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default);
    Task<LoginScopesPreviewDto?> GetLoginScopesAsync(LoginRequestDto request, CancellationToken cancellationToken = default);
    Task<TokenDto?> DevLoginAsync(CancellationToken cancellationToken = default);
}

public sealed class BamboeUpApiClient : IBamboeUpApiClient
{
    private readonly HttpClient _http;
    private readonly ApiSettings _settings;

    public BamboeUpApiClient(HttpClient http, Microsoft.Extensions.Options.IOptions<ApiSettings> settings)
    {
        _http = http;
        _settings = settings.Value;
    }

    public async Task<TokenDto?> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
    {
        var endpoint = string.IsNullOrWhiteSpace(_settings.AuthEndpoint) ? "api/auth/login" : _settings.AuthEndpoint;
        var response = await _http.PostAsJsonAsync(endpoint, request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Login failed ({(int)response.StatusCode}): {error}");
        }

        return await response.Content.ReadFromJsonAsync<TokenDto>(cancellationToken: cancellationToken);
    }

    public async Task<LoginScopesPreviewDto?> GetLoginScopesAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync("api/auth/login-scopes", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<LoginScopesPreviewDto>(cancellationToken: cancellationToken);
    }

    public async Task<TokenDto?> DevLoginAsync(CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsync("api/auth/dev-login", content: null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Dev login failed ({(int)response.StatusCode}): {error}");
        }

        return await response.Content.ReadFromJsonAsync<TokenDto>(cancellationToken: cancellationToken);
    }
}

public sealed class BearerAuthHandler : DelegatingHandler
{
    private readonly MobileAuthService _auth;

    public BearerAuthHandler(MobileAuthService auth) => _auth = auth;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = _auth.CurrentUser?.Token;
        if (!string.IsNullOrEmpty(token) && _auth.IsOnlineAuthenticated)
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return base.SendAsync(request, cancellationToken);
    }
}

public sealed class MobileAuthService
{
    private const string SessionKey = "bamboeup_mobile_session";
    private const string PermitKey = "bamboeup_offline_permit";
    private const string PublicKeyKey = "bamboeup_offline_permit_public_key";
    private const string LastServerTimeKey = "bamboeup_last_server_time";
    private static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);
    private readonly IBamboeUpApiClient _api;
    private readonly IPhysicalInventoryApi _inventory;
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public MobileAuthService(IBamboeUpApiClient api, IPhysicalInventoryApi inventory)
    {
        _api = api;
        _inventory = inventory;
    }

    public UserSession? CurrentUser { get; private set; }
    public OfflineAccessPermitDto? OfflinePermit { get; private set; }
    public bool IsOfflineUnlocked { get; private set; }

    public bool IsOnlineAuthenticated =>
        CurrentUser is not null
        && !string.IsNullOrEmpty(CurrentUser.Token)
        && CurrentUser.Expired > DateTime.UtcNow;

    public bool IsAuthenticated => IsOnlineAuthenticated;

    public bool CanEnterApp => IsOnlineAuthenticated || IsOfflineUnlocked;

    public bool CanCapturePhysicalInventory =>
        CanEnterApp
        && OfflinePermit is { CanCapture: true }
        && IsPermitUsable(OfflinePermit);

    public bool CanVerifyPhysicalInventory =>
        IsOnlineAuthenticated
        && OfflinePermit is { CanVerify: true }
        && IsPermitUsable(OfflinePermit);

    public event Action? AuthStateChanged;

    public async Task InitializeAsync()
    {
        try
        {
            var json = await SecureStorage.Default.GetAsync(SessionKey);
            if (!string.IsNullOrWhiteSpace(json))
                CurrentUser = JsonSerializer.Deserialize<UserSession>(json, _json);

            var permitJson = await SecureStorage.Default.GetAsync(PermitKey);
            if (!string.IsNullOrWhiteSpace(permitJson))
                OfflinePermit = JsonSerializer.Deserialize<OfflineAccessPermitDto>(permitJson, _json);

            if (IsOnlineAuthenticated)
                IsOfflineUnlocked = true;
            AuthStateChanged?.Invoke();
        }
        catch
        {
            // SecureStorage may throw on first run / unsupported platform
        }
    }

    public async Task<UserSession> LoginAsync(string username, string password, long? companyId = null, long? officeId = null)
    {
        var token = await _api.LoginAsync(new LoginRequestDto
        {
            Username = username,
            Password = password,
            CompanyId = companyId,
            OfficeId = officeId
        }) ?? throw new InvalidOperationException("Empty login response.");

        return await CompleteOnlineLoginAsync(token);
    }

    public Task<LoginScopesPreviewDto?> GetLoginScopesAsync(string username, string password) =>
        _api.GetLoginScopesAsync(new LoginRequestDto
        {
            Username = username,
            Password = password
        });

    public async Task<UserSession> DevLoginAsync()
    {
        var token = await _api.DevLoginAsync()
            ?? throw new InvalidOperationException("Empty dev-login response.");
        return await CompleteOnlineLoginAsync(token);
    }

    public async Task RefreshPermitAsync(Guid deviceGuid)
    {
        if (!IsOnlineAuthenticated)
            return;
        var publicKey = await _inventory.GetPublicKeyAsync();
        if (!string.IsNullOrWhiteSpace(publicKey))
            await SecureStorage.Default.SetAsync(PublicKeyKey, publicKey);

        var permit = await _inventory.IssuePermitAsync(deviceGuid);
        if (permit is null)
            throw new InvalidOperationException("The server did not return an offline permit.");
        await StorePermitAsync(permit);
        AuthStateChanged?.Invoke();
    }

    public async Task<bool> TryUnlockOfflineAsync()
    {
        if (CurrentUser is null || OfflinePermit is null)
            return false;
        if (!IsPermitUsable(OfflinePermit))
            return false;
        if (!await MatchesStoredPublicKeyAsync(OfflinePermit))
            return false;
        if (OfflinePermit.UserGuid != CurrentUser.UserGuid)
            return false;
        if (OfflinePermit.CompanyId != CurrentUser.ActiveCompanyId)
            return false;
        IsOfflineUnlocked = true;
        AuthStateChanged?.Invoke();
        return true;
    }

    public string? OfflineUnlockFailureReason()
    {
        if (CurrentUser is null)
            return "Sign in with password once, then fingerprint / Face ID can unlock the selected offline app.";
        if (OfflinePermit is null)
            return "No offline permit is stored. Sign in online to enroll this device.";
        if (!IsClockSafe())
            return "The device clock moved backwards. Connect to the network and sign in again.";
        if (OfflinePermit.IsExpired(DateTimeOffset.UtcNow))
            return "The 7-day offline permit has expired. Sign in online to renew it.";
        if (OfflinePermit.UserGuid != CurrentUser.UserGuid || OfflinePermit.CompanyId != CurrentUser.ActiveCompanyId)
            return "The stored permit belongs to a different user or company.";
        if (!string.Equals(OfflinePermit.AppCode, PhysicalInventoryApps.AppCode, StringComparison.OrdinalIgnoreCase))
            return "This device is not enrolled for Physical Inventory Count.";
        return "The stored offline permit is not valid.";
    }

    public async Task LogoutAsync()
    {
        IsOfflineUnlocked = false;
        if (CurrentUser is not null)
        {
            CurrentUser.Token = string.Empty;
            await PersistSessionAsync();
        }
        AuthStateChanged?.Invoke();
    }

    private async Task<UserSession> CompleteOnlineLoginAsync(TokenDto token)
    {
        CurrentUser = UserSession.FromToken(token);
        IsOfflineUnlocked = true;
        await PersistSessionAsync();
        AuthStateChanged?.Invoke();
        return CurrentUser;
    }

    private async Task PersistSessionAsync()
    {
        if (CurrentUser is null)
            return;
        var json = JsonSerializer.Serialize(CurrentUser);
        await SecureStorage.Default.SetAsync(SessionKey, json);
    }

    private async Task StorePermitAsync(OfflineAccessPermitDto permit)
    {
        OfflinePermit = permit;
        await SecureStorage.Default.SetAsync(PermitKey, JsonSerializer.Serialize(permit));
        await SecureStorage.Default.SetAsync(LastServerTimeKey, permit.ServerTime.UtcDateTime.ToString("O"));
    }

    private bool IsPermitUsable(OfflineAccessPermitDto permit)
    {
        if (!string.Equals(permit.AppCode, PhysicalInventoryApps.AppCode, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!IsClockSafe())
            return false;
        return OfflinePermitSigner.IsClockSkewSafe(permit, DateTimeOffset.UtcNow, MaxClockSkew);
    }

    private bool IsClockSafe()
    {
        try
        {
            var stored = SecureStorage.Default.GetAsync(LastServerTimeKey).GetAwaiter().GetResult();
            if (string.IsNullOrWhiteSpace(stored) || !DateTimeOffset.TryParse(stored, out var lastServer))
                return true;
            return DateTimeOffset.UtcNow + MaxClockSkew >= lastServer;
        }
        catch
        {
            return true;
        }
    }

    private async Task<bool> MatchesStoredPublicKeyAsync(OfflineAccessPermitDto permit)
    {
        var publicKey = await SecureStorage.Default.GetAsync(PublicKeyKey);
        if (string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(permit.Signature))
            return false;
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            return OfflinePermitSigner.Verify(permit, rsa);
        }
        catch
        {
            return false;
        }
    }
}

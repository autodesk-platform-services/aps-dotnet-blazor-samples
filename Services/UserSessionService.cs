namespace ApsSamples.Services;

public interface IUserSessionService
{
    string? AccessToken { get; set; }
    string? RefreshToken { get; set; }
    string? UserName { get; set; }
    string? UserEmail { get; set; }
    string? UserId { get; set; }
    bool IsAuthenticated { get; }
    void ClearSession();
}

public class UserSessionService : IUserSessionService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private const string AccessTokenKey = "APS_AccessToken";
    private const string RefreshTokenKey = "APS_RefreshToken";
    private const string UserNameKey = "APS_UserName";
    private const string UserEmailKey = "APS_UserEmail";
    private const string UserIdKey = "APS_UserId";

    public UserSessionService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? AccessToken
    {
        get => _httpContextAccessor.HttpContext?.Session.GetString(AccessTokenKey);
        set
        {
            if (_httpContextAccessor.HttpContext != null)
            {
                if (string.IsNullOrEmpty(value))
                    _httpContextAccessor.HttpContext.Session.Remove(AccessTokenKey);
                else
                    _httpContextAccessor.HttpContext.Session.SetString(AccessTokenKey, value);
            }
        }
    }

    public string? RefreshToken
    {
        get => _httpContextAccessor.HttpContext?.Session.GetString(RefreshTokenKey);
        set
        {
            if (_httpContextAccessor.HttpContext != null)
            {
                if (string.IsNullOrEmpty(value))
                    _httpContextAccessor.HttpContext.Session.Remove(RefreshTokenKey);
                else
                    _httpContextAccessor.HttpContext.Session.SetString(RefreshTokenKey, value);
            }
        }
    }

    public string? UserName
    {
        get => _httpContextAccessor.HttpContext?.Session.GetString(UserNameKey);
        set
        {
            if (_httpContextAccessor.HttpContext != null)
            {
                if (string.IsNullOrEmpty(value))
                    _httpContextAccessor.HttpContext.Session.Remove(UserNameKey);
                else
                    _httpContextAccessor.HttpContext.Session.SetString(UserNameKey, value);
            }
        }
    }

    public string? UserEmail
    {
        get => _httpContextAccessor.HttpContext?.Session.GetString(UserEmailKey);
        set
        {
            if (_httpContextAccessor.HttpContext != null)
            {
                if (string.IsNullOrEmpty(value))
                    _httpContextAccessor.HttpContext.Session.Remove(UserEmailKey);
                else
                    _httpContextAccessor.HttpContext.Session.SetString(UserEmailKey, value);
            }
        }
    }

    public string? UserId
    {
        get => _httpContextAccessor.HttpContext?.Session.GetString(UserIdKey);
        set
        {
            if (_httpContextAccessor.HttpContext != null)
            {
                if (string.IsNullOrEmpty(value))
                    _httpContextAccessor.HttpContext.Session.Remove(UserIdKey);
                else
                    _httpContextAccessor.HttpContext.Session.SetString(UserIdKey, value);
            }
        }
    }

    public bool IsAuthenticated => !string.IsNullOrEmpty(AccessToken);

    public void ClearSession()
    {
        _httpContextAccessor.HttpContext?.Session.Clear();
    }
}

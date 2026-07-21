using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;

namespace CustomLauncher.Core;

public interface IAuthService
{
    Task<MSession?> AuthenticateAsync(CancellationToken cancellationToken = default);
    Task<MSession?> TryRestoreAsync(CancellationToken cancellationToken = default);
}

public sealed class AuthService : IAuthService
{
    private readonly JELoginHandler _handler = JELoginHandlerBuilder.BuildDefault();

    public async Task<MSession?> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _handler.Authenticate();
    }

    public async Task<MSession?> TryRestoreAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await _handler.AuthenticateSilently();
        }
        catch { return null; }
    }
}

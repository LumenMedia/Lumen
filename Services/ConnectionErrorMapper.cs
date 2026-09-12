using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

namespace Lumen.Services;

public static class ConnectionErrorMapper
{
    public static string ToFriendlyMessage(Exception exception, string? serverUrl = null)
    {
        var ex = Unwrap(exception);
        var endpoint = TryEndpoint(serverUrl);

        if (ex is JellyfinApiException api)
        {
            return api.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Jellyfin rejected the username or password.",
                HttpStatusCode.Forbidden => "Jellyfin found the account, but it is not allowed to sign in.",
                HttpStatusCode.NotFound => $"A server answered at {endpoint}, but it does not appear to be a Jellyfin server.",
                _ => $"Jellyfin responded with {(int)api.StatusCode} ({api.StatusCode})."
            };
        }

        if (ex is UriFormatException)
            return "Enter a valid Jellyfin address, for example http://192.168.1.50:8096.";

        if (ex is TaskCanceledException or TimeoutException)
            return $"The Jellyfin server at {endpoint} did not respond in time.";

        if (ex is AuthenticationException)
            return $"A secure connection to {endpoint} could not be verified. Check the server certificate.";

        if (ex is HttpRequestException http)
        {
            if (Find<SocketException>(http) is { } socket)
            {
                return socket.SocketErrorCode switch
                {
                    SocketError.ConnectionRefused => $"The server at {endpoint} was found, but the Jellyfin port refused the connection.",
                    SocketError.HostUnreachable or SocketError.NetworkUnreachable => $"The Jellyfin server at {endpoint} is not reachable from this PC.",
                    SocketError.HostNotFound or SocketError.NoData => $"The server name for {endpoint} could not be resolved.",
                    SocketError.TimedOut => $"The Jellyfin server at {endpoint} did not respond in time.",
                    _ => $"Lumen could not reach Jellyfin at {endpoint}."
                };
            }

            if (http.InnerException is AuthenticationException)
                return $"A secure connection to {endpoint} could not be verified. Check the server certificate.";

            return $"Lumen could not connect to Jellyfin at {endpoint}.";
        }

        return string.IsNullOrWhiteSpace(ex.Message)
            ? $"Lumen could not connect to Jellyfin at {endpoint}."
            : ex.Message;
    }

    private static string TryEndpoint(string? url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        return string.IsNullOrWhiteSpace(url) ? "the configured server" : url.Trim();
    }

    private static Exception Unwrap(Exception ex)
    {
        while (ex is AggregateException aggregate && aggregate.InnerExceptions.Count == 1)
            ex = aggregate.InnerExceptions[0];
        return ex;
    }

    private static T? Find<T>(Exception? ex) where T : Exception
    {
        while (ex is not null)
        {
            if (ex is T value) return value;
            ex = ex.InnerException;
        }
        return null;
    }
}

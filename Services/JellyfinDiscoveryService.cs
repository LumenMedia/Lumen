using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Lumen.Services;

public sealed class DiscoveredJellyfinServer
{
    public string Name { get; init; } = "Jellyfin";
    public string Address { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string AdvertisedAddress { get; init; } = string.Empty;
    public bool IsReachable { get; set; }
    public string HealthText { get; set; } = "Checking…";
    public string LatencyText { get; set; } = string.Empty;
}

public static class JellyfinDiscoveryService
{
    private const int DiscoveryPort = 7359;
    private static readonly byte[] Probe = Encoding.UTF8.GetBytes("Who is JellyfinServer?");

    private sealed record DiscoverySocket(UdpClient Client, IPAddress LocalAddress, IPAddress BroadcastAddress);

    public static async Task<IReadOnlyList<DiscoveredJellyfinServer>> DiscoverAsync(
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var results = new Dictionary<string, DiscoveredJellyfinServer>(StringComparer.OrdinalIgnoreCase);
        var sockets = CreateDiscoverySockets();

        // Probe directed broadcasts plus the global broadcast fallback.
        UdpClient? globalSocket = null;
        try
        {
            globalSocket = CreateSocket(IPAddress.Any);
            sockets.Add(new DiscoverySocket(globalSocket, IPAddress.Any, IPAddress.Broadcast));
        }
        catch
        {
            globalSocket?.Dispose();
        }

        if (sockets.Count == 0)
            return [];

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout ?? TimeSpan.FromSeconds(3.5));
        var token = timeoutCts.Token;

        try
        {
            // Repeat the lightweight UDP probe to tolerate dropped broadcasts.
            for (var pass = 0; pass < 2; pass++)
            {
                foreach (var socket in sockets)
                {
                    try
                    {
                        await socket.Client.SendAsync(
                            Probe,
                            Probe.Length,
                            new IPEndPoint(socket.BroadcastAddress, DiscoveryPort));
                    }
                    catch (SocketException)
                    {
                    }
                    catch (ObjectDisposedException) { }
                }

                if (pass == 0)
                {
                    try { await Task.Delay(180, token); }
                    catch (OperationCanceledException) { break; }
                }
            }

            var receiveTasks = sockets
                .Select(socket => ReceiveResponsesAsync(socket.Client, results, token))
                .ToArray();

            try
            {
                await Task.WhenAll(receiveTasks);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }
        finally
        {
            foreach (var socket in sockets)
                socket.Client.Dispose();
        }

        var servers = results.Values
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        await Task.WhenAll(servers.Select(server => CheckHealthAsync(server, ct)));
        return servers;
    }

    private static async Task CheckHealthAsync(DiscoveredJellyfinServer server, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var started = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var response = await http.GetAsync(
                server.Address.TrimEnd('/') + "/System/Info/Public",
                HttpCompletionOption.ResponseHeadersRead,
                ct);
            started.Stop();
            server.IsReachable = response.IsSuccessStatusCode;
            server.HealthText = response.IsSuccessStatusCode ? "Ready" : $"HTTP {(int)response.StatusCode}";
            server.LatencyText = response.IsSuccessStatusCode ? $"{started.ElapsedMilliseconds} ms" : string.Empty;
        }
        catch
        {
            started.Stop();
            server.IsReachable = false;
            server.HealthText = "Unreachable";
            server.LatencyText = string.Empty;
        }
    }

    private static List<DiscoverySocket> CreateDiscoverySockets()
    {
        var sockets = new List<DiscoverySocket>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
                continue;
            if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                continue;

            IPInterfaceProperties properties;
            try { properties = networkInterface.GetIPProperties(); }
            catch { continue; }

            foreach (var unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork ||
                    unicast.IPv4Mask is null)
                    continue;

                var local = unicast.Address;
                if (IPAddress.IsLoopback(local) || IsAutomaticPrivateAddress(local))
                    continue;

                var broadcast = GetDirectedBroadcast(local, unicast.IPv4Mask);
                if (broadcast is null)
                    continue;

                var key = $"{local}/{unicast.IPv4Mask}";
                if (!seen.Add(key))
                    continue;

                try
                {
                    sockets.Add(new DiscoverySocket(
                        CreateSocket(local),
                        local,
                        broadcast));
                }
                catch
                {
                }
            }
        }

        return sockets;
    }

    private static UdpClient CreateSocket(IPAddress localAddress)
    {
        var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.EnableBroadcast = true;
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(localAddress, 0));
        return udp;
    }

    private static async Task ReceiveResponsesAsync(
        UdpClient udp,
        Dictionary<string, DiscoveredJellyfinServer> results,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var response = await udp.ReceiveAsync(ct);
                var json = Encoding.UTF8.GetString(response.Buffer);

                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;

                var address = GetString(root, "Address");
                if (string.IsNullOrWhiteSpace(address))
                    continue;

                address = address.Trim().TrimEnd('/');
                var name = GetString(root, "Name");
                var id = GetString(root, "Id");

                var advertisedAddress = address;
                var reachableAddress = BuildReachableAddress(
                    advertisedAddress,
                    response.RemoteEndPoint.Address);

                var server = new DiscoveredJellyfinServer
                {
                    Name = string.IsNullOrWhiteSpace(name)
                        ? response.RemoteEndPoint.Address.ToString()
                        : name.Trim(),
                    Address = reachableAddress,
                    Id = id?.Trim() ?? string.Empty,
                    AdvertisedAddress = advertisedAddress
                };

                var key = !string.IsNullOrWhiteSpace(server.Id)
                    ? server.Id
                    : server.Address;

                lock (results)
                    results[key] = server;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (JsonException)
            {
            }
            catch (SocketException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    private static IPAddress? GetDirectedBroadcast(IPAddress address, IPAddress subnetMask)
    {
        var addressBytes = address.GetAddressBytes();
        var maskBytes = subnetMask.GetAddressBytes();
        if (addressBytes.Length != 4 || maskBytes.Length != 4)
            return null;

        var broadcast = new byte[4];
        for (var i = 0; i < 4; i++)
            broadcast[i] = (byte)(addressBytes[i] | (maskBytes[i] ^ 255));

        if (broadcast.SequenceEqual(addressBytes))
            return null;

        return new IPAddress(broadcast);
    }

    private static bool IsAutomaticPrivateAddress(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
    }

    private static string BuildReachableAddress(string advertisedAddress, IPAddress responderAddress)
    {
        // Docker may advertise an unreachable container IP; keep the responder host.
        if (Uri.TryCreate(advertisedAddress, UriKind.Absolute, out var advertisedUri))
        {
            try
            {
                var builder = new UriBuilder(advertisedUri)
                {
                    Host = responderAddress.ToString()
                };
                return builder.Uri.ToString().TrimEnd('/');
            }
            catch
            {
            }
        }

        // Older responses may omit a full URL; fall back to Jellyfin's HTTP port.
        return $"http://{responderAddress}:8096";
    }

    private static string? GetString(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : property.Value.ToString();
        }
        return null;
    }
}

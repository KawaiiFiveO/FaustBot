using System.Net;
using System.Net.NetworkInformation;
using FaustBot.Options;
using FaustBot.Vpn;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FaustBot.Services;

/// <summary>Pings each connected player's IP from this host, at most once per PingInterval. The VPN server runs on
/// the same host, so this approximates each player's latency to it. IPs are kept in memory only, and never logged.</summary>
public sealed class PlayerPingService(
    IOptionsMonitor<BotOptions> options,
    TimeProvider timeProvider,
    ILogger<PlayerPingService> logger)
{
    private const int EchoCount = 3;
    private static readonly TimeSpan EchoTimeout = TimeSpan.FromSeconds(1);

    // Keyed by IP, so players sharing a connection are pinged once. Only used from the monitoring loop.
    private readonly Dictionary<string, Measurement> _measurements = [];
    private int _failureLogged;

    /// <summary>Pings the players that are due and returns the round-trip time of every IP that answered.</summary>
    public async Task<IReadOnlyDictionary<string, TimeSpan>> RefreshAsync(
        IReadOnlyList<HubSnapshot> hubs, CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromMinutes(options.CurrentValue.PingInterval);
        if (interval <= TimeSpan.Zero)
        {
            _measurements.Clear();
            return new Dictionary<string, TimeSpan>();
        }

        var connected = hubs.SelectMany(h => h.Sessions)
            .Select(s => s.ClientIp)
            .OfType<string>()
            .Where(IsPingable)
            .ToHashSet();

        // Forget players who have left.
        foreach (var ip in _measurements.Keys.Where(ip => !connected.Contains(ip)).ToList())
        {
            _measurements.Remove(ip);
        }

        var now = timeProvider.GetUtcNow();
        var due = connected
            .Where(ip => !_measurements.TryGetValue(ip, out var last) || now - last.MeasuredAt >= interval)
            .ToList();
        if (due.Count > 0)
        {
            var results = await Task.WhenAll(due.Select(ip => MeasureAsync(IPAddress.Parse(ip), cancellationToken)));
            for (var i = 0; i < due.Count; i++)
            {
                _measurements[due[i]] = new Measurement(results[i], now);
            }
        }

        return _measurements
            .Where(m => m.Value.RoundTrip is not null)
            .ToDictionary(m => m.Key, m => m.Value.RoundTrip!.Value);
    }

    /// <summary>Loopback means the player is on this host, where the round trip says nothing about their connection.</summary>
    private static bool IsPingable(string ip) =>
        IPAddress.TryParse(ip, out var address) && !IPAddress.IsLoopback(address)
        && !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.IPv6Any);

    /// <summary>The median of the echoes that were answered, or null if none were (many routers drop pings).</summary>
    private async Task<TimeSpan?> MeasureAsync(IPAddress address, CancellationToken cancellationToken)
    {
        using var ping = new Ping();
        List<long> roundTrips = [];
        for (var i = 0; i < EchoCount; i++)
        {
            try
            {
                var reply = await ping.SendPingAsync(address, EchoTimeout, cancellationToken: cancellationToken);
                if (reply.Status == IPStatus.Success)
                {
                    roundTrips.Add(reply.RoundtripTime);
                }
            }
            catch (PingException ex)
            {
                // Usually a host that can't send pings at all, so say so once rather than every round.
                // The message doesn't include the address.
                if (Interlocked.Exchange(ref _failureLogged, 1) == 0)
                {
                    logger.LogWarning("Could not ping players; ping times won't be shown: {Error}",
                        ex.InnerException?.Message ?? ex.Message);
                }
                return null;
            }
        }

        if (roundTrips.Count == 0)
        {
            return null;
        }

        roundTrips.Sort();
        return TimeSpan.FromMilliseconds(roundTrips[(roundTrips.Count - 1) / 2]);
    }

    private sealed record Measurement(TimeSpan? RoundTrip, DateTimeOffset MeasuredAt);
}

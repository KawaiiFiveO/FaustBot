using Discord;
using FaustBot.State;
using FaustBot.Vpn;
using Microsoft.Extensions.Logging;

namespace FaustBot.Services;

/// <summary>The game each hub is hosting, set by moderators and cleared once the hub empties.</summary>
public sealed class HostedGameService(StateStore state, TimeProvider timeProvider, ILogger<HostedGameService> logger)
{
    public Task<IReadOnlyDictionary<string, HostedGame>> GetAllAsync() =>
        state.ReadAsync<IReadOnlyDictionary<string, HostedGame>>(s =>
            new Dictionary<string, HostedGame>(s.HostedGames, StringComparer.OrdinalIgnoreCase));

    /// <summary>Shows a game on a hub, replacing any game it was already hosting.</summary>
    public async Task HostAsync(string hub, string game, IUser hostedBy)
    {
        var hosted = new HostedGame(game, timeProvider.GetUtcNow().UtcDateTime);
        await state.UpdateAsync(s =>
        {
            RemoveHub(s, hub);
            s.HostedGames[hub] = hosted;
        });
        logger.LogInformation("{User} ({UserId}) set hub {Hub} as hosting {Game}.", hostedBy.Username, hostedBy.Id, hub, game);
    }

    /// <summary>Returns the game that was cleared, or null if the hub wasn't hosting one.</summary>
    public async Task<HostedGame?> ClearAsync(string hub, IUser clearedBy)
    {
        if (await state.ReadAsync(s => FindKey(s, hub)) is null)
        {
            return null;
        }

        HostedGame? cleared = null;
        await state.UpdateAsync(s => cleared = RemoveHub(s, hub));
        logger.LogInformation("{User} ({UserId}) cleared the game on hub {Hub}.", clearedBy.Username, clearedBy.Id, hub);
        return cleared;
    }

    /// <summary>Clears games on hubs that are offline, empty or no longer configured. Unreachable hubs keep
    /// theirs, so an outage doesn't end a game. Games set after <paramref name="queriedUtc"/> are kept, since
    /// the snapshots predate them.</summary>
    public async Task ClearFinishedAsync(IReadOnlyList<HubSnapshot> hubs, DateTime queriedUtc)
    {
        bool IsFinished(KeyValuePair<string, HostedGame> entry) =>
            entry.Value.StartedUtc < queriedUtc
            && hubs.FirstOrDefault(h => string.Equals(h.Name, entry.Key, StringComparison.OrdinalIgnoreCase)) switch
            {
                null => true,
                { Status: HubStatus.Offline } => true,
                { Status: HubStatus.Online } hub => hub.Sessions.Count == 0,
                _ => false,
            };

        // Check before updating, so state.json isn't rewritten on every tick.
        if (!await state.ReadAsync(s => s.HostedGames.Any(IsFinished)))
        {
            return;
        }

        await state.UpdateAsync(s =>
        {
            foreach (var (hub, hosted) in s.HostedGames.Where(IsFinished).ToList())
            {
                s.HostedGames.Remove(hub);
                logger.LogInformation("Hub {Hub} stopped hosting {Game}: it's offline, empty or no longer configured.", hub, hosted.Game);
            }
        });
    }

    // state.json deserializes to a case-sensitive dictionary, so match hub names by hand.
    private static string? FindKey(BotState state, string hub) =>
        state.HostedGames.Keys.FirstOrDefault(k => string.Equals(k, hub, StringComparison.OrdinalIgnoreCase));

    private static HostedGame? RemoveHub(BotState state, string hub) =>
        FindKey(state, hub) is { } key && state.HostedGames.Remove(key, out var removed) ? removed : null;
}

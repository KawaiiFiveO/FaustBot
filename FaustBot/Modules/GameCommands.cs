using Discord.Interactions;
using FaustBot.Options;
using FaustBot.Services;
using FaustBot.Vpn;
using Microsoft.Extensions.Options;

namespace FaustBot.Modules;

[Group("game", "Show which game a hub is hosting.")]
public sealed class GameCommands(
    HostedGameService games,
    VpnServerClient vpn,
    VpnMonitorService monitor,
    IOptionsMonitor<BotOptions> options) : InteractionModuleBase<SocketInteractionContext>
{
    // Replies are ephemeral: the status embed is where everyone sees the game.
    [RequireModerator]
    [SlashCommand("host", "Show a game on a hub's status until the hub empties.")]
    public async Task Host(
        [Summary("hub", "Hub hosting the game."), Autocomplete(typeof(HubAutocompleteHandler))] string hubName,
        [Summary("game", "Game being hosted."), Autocomplete(typeof(GameAutocompleteHandler))] string gameName)
    {
        var hub = vpn.FindHub(hubName);
        if (hub is null)
        {
            await RespondAsync(UnknownHubMessage(hubName), ephemeral: true);
            return;
        }

        var configuredGames = options.CurrentValue.Games;
        if (configuredGames.Count == 0)
        {
            await RespondAsync("No games are configured.", ephemeral: true);
            return;
        }

        // Autocomplete only suggests, so the name can still be anything.
        var game = configuredGames.FirstOrDefault(g => string.Equals(g, gameName, StringComparison.OrdinalIgnoreCase));
        if (game is null)
        {
            await RespondAsync($"\"{gameName}\" isn't in the game list. Pick one of the suggestions.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        // The game is cleared as soon as an update finds the hub empty, so require someone to be on it already.
        var snapshot = await vpn.QueryHubAsync(hub, CancellationToken.None);
        var problem = snapshot.Status switch
        {
            HubStatus.Unreachable => "Can't reach the VPN server right now.",
            HubStatus.Offline => $"The {hub.Name} hub is offline.",
            _ when snapshot.Sessions.Count == 0 => $"Nobody is connected to {hub.Name}. Connect first, then host the game.",
            _ => null,
        };
        if (problem is not null)
        {
            await FollowupAsync(problem, ephemeral: true);
            return;
        }

        await games.HostAsync(hub.Name, game, Context.User);
        monitor.RequestRefresh();
        await FollowupAsync($"{hub.Name} is now shown as hosting {game}.{PausedNote()}", ephemeral: true);
    }

    [RequireModerator]
    [SlashCommand("clear", "Stop showing a game on a hub's status.")]
    public async Task Clear(
        [Summary("hub", "Hub to clear."), Autocomplete(typeof(HubAutocompleteHandler))] string hubName)
    {
        var hub = vpn.FindHub(hubName);
        if (hub is null)
        {
            await RespondAsync(UnknownHubMessage(hubName), ephemeral: true);
            return;
        }

        var cleared = await games.ClearAsync(hub.Name, Context.User);
        if (cleared is null)
        {
            await RespondAsync($"{hub.Name} isn't hosting a game.", ephemeral: true);
            return;
        }

        monitor.RequestRefresh();
        await RespondAsync($"{hub.Name} is no longer shown as hosting {cleared.Game}.", ephemeral: true);
    }

    private string PausedNote() => monitor.IsRunning
        ? ""
        : " VPN monitoring is paused, so the status message will show it once monitoring resumes.";

    private string UnknownHubMessage(string hubName) =>
        $"There's no hub called \"{hubName}\". Hubs: {string.Join(", ", vpn.Hubs.Select(h => h.Name))}";
}

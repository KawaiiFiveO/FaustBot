using System.Globalization;
using System.Text;
using Discord;
using FaustBot.Options;
using FaustBot.State;
using FaustBot.Vpn;
using Microsoft.Extensions.Options;

namespace FaustBot.Services;

/// <summary>Builds the persistent status embed, keeping within Discord's embed limits.</summary>
public sealed class StatusEmbedBuilder(IOptionsMonitor<BotOptions> options)
{
    private const string TerminalBadge = " :regional_indicator_d::regional_indicator_t:";

    private BotOptions Settings => options.CurrentValue;

    /// <param name="hostedGames">The game each hub is hosting, keyed by hub name.</param>
    /// <param name="pingTimes">Round-trip times keyed by client IP, for the players that answered.</param>
    /// <param name="unreachableSince">When every hub stopped answering, or null if any hub is reachable.</param>
    public Embed Build(
        IReadOnlyList<HubSnapshot> hubs,
        IReadOnlyDictionary<string, HostedGame> hostedGames,
        IReadOnlyDictionary<string, TimeSpan> pingTimes,
        DateTimeOffset now,
        DateTimeOffset? unreachableSince = null)
    {
        var embed = CreateBase(now);

        if (!string.IsNullOrWhiteSpace(Settings.FooterText))
        {
            embed.WithFooter(Truncate(Settings.FooterText, EmbedFooterBuilder.MaxFooterTextLength));
        }

        if (unreachableSince is { } since)
        {
            embed.WithDescription(
                $"⚠️ Can't reach the VPN server (since {TimestampTag.FormatFromDateTimeOffset(since, TimestampTagStyles.Relative)}).");
        }

        var fields = hubs.Take(EmbedBuilder.MaxFieldCount)
            .Select(hub => (Hub: hub, Name: Truncate(BuildFieldName(hub), EmbedFieldBuilder.MaxFieldNameLength)))
            .ToList();

        // Discord also caps the whole embed at 6000 characters, so share what's left between the field values.
        var fixedLength = embed.Length + fields.Sum(f => f.Name.Length);
        var valueBudget = fields.Count == 0
            ? 0
            : Math.Min(EmbedFieldBuilder.MaxFieldValueLength, (EmbedBuilder.MaxEmbedLength - fixedLength) / fields.Count);

        foreach (var (hub, name) in fields)
        {
            embed.AddField(name, BuildFieldValue(hub, hostedGames.GetValueOrDefault(hub.Name), pingTimes, now, valueBudget));
        }

        return embed.Build();
    }

    /// <summary>An embed with no hub data, shown while monitoring is paused or the bot is offline.</summary>
    public Embed BuildNotice(string message, DateTimeOffset now) =>
        CreateBase(now).WithDescription(message).Build();

    private EmbedBuilder CreateBase(DateTimeOffset now) => new EmbedBuilder()
        .WithTitle(Truncate(Settings.TitleText, EmbedBuilder.MaxTitleLength))
        .WithColor(Color.Green)
        .WithTimestamp(now);

    private string BuildFieldName(HubSnapshot hub)
    {
        var status = hub.Status switch
        {
            HubStatus.Online => Settings.CustomEmojis ? Settings.HubOnlineEmoji : "[Online]",
            HubStatus.Offline => Settings.CustomEmojis ? Settings.HubOfflineEmoji : "[Offline]",
            _ => Settings.CustomEmojis ? "⚠️" : "[Unreachable]",
        };

        if (hub.Status == HubStatus.Unreachable)
        {
            return $"{status} {hub.Name}";
        }

        var capacity = Settings.MaxPlayersPerHub > 0 ? $"/{Settings.MaxPlayersPerHub}" : "";
        var name = $"{status} {hub.Name}: {hub.Sessions.Count}{capacity} Players";
        return hub.HasTerminal ? name + TerminalBadge : name;
    }

    private string BuildFieldValue(
        HubSnapshot hub, HostedGame? game, IReadOnlyDictionary<string, TimeSpan> pingTimes, DateTimeOffset now, int maxLength)
    {
        switch (hub.Status)
        {
            case HubStatus.Offline:
                return "Hub Offline";
            case HubStatus.Unreachable:
                return "Status Unknown";
        }

        var value = new StringBuilder();
        if (game is not null)
        {
            // Capped so the player list always keeps some room. Escaping can double the length, so truncate first.
            value.Append($"🎮 **{Format.Sanitize(Truncate(game.Game, maxLength / 4))}**\n");
        }

        if (hub.Sessions.Count == 0)
        {
            return value.Append("No Players").ToString();
        }

        for (var i = 0; i < hub.Sessions.Count; i++)
        {
            var line = FormatSession(hub.Sessions[i], pingTimes, now);

            // Leave room for the "...and N more" line if the rest won't fit.
            var remaining = hub.Sessions.Count - i - 1;
            var reserve = remaining > 0 ? $"\n...and {remaining} more".Length : 0;
            if (value.Length + line.Length + 1 + reserve > maxLength)
            {
                value.Append($"...and {hub.Sessions.Count - i} more");
                break;
            }

            value.Append(line).Append('\n');
        }

        return value.ToString().TrimEnd('\n');
    }

    private string FormatSession(VpnSession session, IReadOnlyDictionary<string, TimeSpan> pingTimes, DateTimeOffset now)
    {
        var line = Settings.MentionUserIds ? $"<@{session.Username}>" : session.Username;
        if (Settings.DisplaySessionTime)
        {
            var duration = now.UtcDateTime - session.CreatedUtc;
            line += string.Create(CultureInfo.InvariantCulture,
                $" - {(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}");
        }

        if (session.ClientIp is { } ip && pingTimes.TryGetValue(ip, out var roundTrip))
        {
            line += string.Create(CultureInfo.InvariantCulture, $" - {(int)roundTrip.TotalMilliseconds}ms");
        }

        return line;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
}

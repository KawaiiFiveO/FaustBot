using System.Text;
using Discord;
using Discord.Interactions;
using FaustBot.Options;
using FaustBot.Services;
using Microsoft.Extensions.Options;

namespace FaustBot.Modules;

[Group("mod", "Manage who can set the hosted game.")]
public sealed class ModCommands(AdminService admins, IOptionsMonitor<BotOptions> options) : InteractionModuleBase<SocketInteractionContext>
{
    [RequireBotAdmin]
    [SlashCommand("add", "Let a user set the hosted game.")]
    public async Task Add([Summary(description: "The user to make a moderator")] IUser user)
    {
        if (user.IsBot)
        {
            await RespondAsync("Bots can't be moderators.", ephemeral: true);
            return;
        }

        if (!options.CurrentValue.EnableRuntimeModerators)
        {
            await RespondAsync("Adding moderators from Discord is turned off. Add them to ModeratorUserIds in config.json.", ephemeral: true);
            return;
        }

        if (await admins.IsAdminAsync(user))
        {
            await RespondAsync($"{user.Mention} is a bot admin, so they can already moderate.",
                ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        var added = await admins.AddModeratorAsync(user.Id);
        await RespondAsync(added ? $"{user.Mention} is now a moderator." : $"{user.Mention} is already a moderator.",
            ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    [RequireBotAdmin]
    [SlashCommand("remove", "Stop a user from setting the hosted game.")]
    public async Task Remove([Summary(description: "The moderator to remove")] IUser user)
    {
        var message = await admins.RemoveModeratorAsync(user.Id) switch
        {
            RemoveResult.Removed => $"{user.Mention} is no longer a moderator.",
            RemoveResult.InConfig => $"{user.Mention} is listed in ModeratorUserIds in config.json, so they can only be removed there.",
            _ => $"{user.Mention} isn't a moderator added with `/mod add`.",
        };
        await RespondAsync(message, ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    [RequireModerator]
    [SlashCommand("list", "Show who can set the hosted game.")]
    public async Task List()
    {
        var config = options.CurrentValue;
        var text = new StringBuilder();
        AdminCommands.AppendSection(text, "Moderators (config.json)", config.ModeratorUserIds.Select(MentionUtils.MentionUser));
        AdminCommands.AppendSection(text, "Moderator roles (config.json)", config.ModeratorRoleIds.Select(MentionUtils.MentionRole));
        AdminCommands.AppendSection(text, config.EnableRuntimeModerators
                ? "Moderators (added with /mod add)"
                : "Moderators (added with /mod add, ignored: EnableRuntimeModerators is off)",
            (await admins.GetRuntimeModeratorIdsAsync()).Select(MentionUtils.MentionUser));
        text.Append("Bot admins are also moderators.");

        // Mentions render as names without pinging anyone.
        await RespondAsync(text.ToString(), ephemeral: true, allowedMentions: AllowedMentions.None);
    }
}

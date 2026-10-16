using Discord;
using Discord.Interactions;
using FaustBot.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FaustBot.Modules;

/// <summary>Limits a command to moderators and bot admins (see <see cref="AdminService"/>).</summary>
public sealed class RequireModeratorAttribute : PreconditionAttribute
{
    public override async Task<PreconditionResult> CheckRequirementsAsync(
        IInteractionContext context, ICommandInfo commandInfo, IServiceProvider services)
    {
        return await services.GetRequiredService<AdminService>().IsModeratorAsync(context.User)
            ? PreconditionResult.FromSuccess()
            : PreconditionResult.FromError("You need to be a moderator to use this command.");
    }
}

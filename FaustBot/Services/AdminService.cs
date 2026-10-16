using Discord;
using Discord.WebSocket;
using FaustBot.Options;
using FaustBot.State;
using Microsoft.Extensions.Options;

namespace FaustBot.Services;

public enum RemoveResult
{
    Removed,
    NotListed,

    /// <summary>The user is listed in config.json, which can't be changed from Discord.</summary>
    InConfig,
}

/// <summary>Decides who can control the bot and who can moderate: the owner, config.json users and roles, and
/// users added at runtime.</summary>
public sealed class AdminService(DiscordSocketClient client, StateStore state, IOptionsMonitor<BotOptions> options)
{
    private BotOptions Settings => options.CurrentValue;

    /// <summary>The application owner, or every member of the team that owns it.</summary>
    public async Task<IReadOnlyList<ulong>> GetOwnerIdsAsync()
    {
        // Discord.Net caches the application info after the first request.
        var application = await client.GetApplicationInfoAsync();
        return application.Team is { } team
            ? team.TeamMembers.Select(m => m.User.Id).ToList()
            : [application.Owner.Id];
    }

    public async Task<bool> IsOwnerAsync(IUser user) => (await GetOwnerIdsAsync()).Contains(user.Id);

    public async Task<bool> IsAdminAsync(IUser user)
    {
        if (Settings.AdminUserIds.Contains(user.Id)
            || (user is IGuildUser member && member.RoleIds.Any(Settings.AdminRoleIds.Contains))
            || (Settings.EnableRuntimeAdmins && await state.ReadAsync(s => s.AdminUserIds.Contains(user.Id))))
        {
            return true;
        }

        return await IsOwnerAsync(user);
    }

    /// <summary>Moderators can set the hosted game. Every admin is also a moderator.</summary>
    public async Task<bool> IsModeratorAsync(IUser user) =>
        Settings.ModeratorUserIds.Contains(user.Id)
        || (user is IGuildUser member && member.RoleIds.Any(Settings.ModeratorRoleIds.Contains))
        || (Settings.EnableRuntimeModerators && await state.ReadAsync(s => s.ModeratorUserIds.Contains(user.Id)))
        || await IsAdminAsync(user);

    public Task<IReadOnlyList<ulong>> GetRuntimeAdminIdsAsync() =>
        state.ReadAsync<IReadOnlyList<ulong>>(s => s.AdminUserIds.ToList());

    public Task<IReadOnlyList<ulong>> GetRuntimeModeratorIdsAsync() =>
        state.ReadAsync<IReadOnlyList<ulong>>(s => s.ModeratorUserIds.ToList());

    /// <summary>Returns false if the user was already an admin by user ID.</summary>
    public Task<bool> AddAdminAsync(ulong userId) => AddAsync(userId, Settings.AdminUserIds, s => s.AdminUserIds);

    public Task<RemoveResult> RemoveAdminAsync(ulong userId) =>
        RemoveAsync(userId, Settings.AdminUserIds, s => s.AdminUserIds);

    /// <summary>Returns false if the user was already a moderator by user ID.</summary>
    public Task<bool> AddModeratorAsync(ulong userId) =>
        AddAsync(userId, Settings.ModeratorUserIds, s => s.ModeratorUserIds);

    public Task<RemoveResult> RemoveModeratorAsync(ulong userId) =>
        RemoveAsync(userId, Settings.ModeratorUserIds, s => s.ModeratorUserIds);

    private async Task<bool> AddAsync(ulong userId, List<ulong> configIds, Func<BotState, List<ulong>> runtimeIds)
    {
        if (configIds.Contains(userId) || await state.ReadAsync(s => runtimeIds(s).Contains(userId)))
        {
            return false;
        }

        await state.UpdateAsync(s => runtimeIds(s).Add(userId));
        return true;
    }

    private async Task<RemoveResult> RemoveAsync(ulong userId, List<ulong> configIds, Func<BotState, List<ulong>> runtimeIds)
    {
        if (await state.ReadAsync(s => runtimeIds(s).Contains(userId)))
        {
            await state.UpdateAsync(s => runtimeIds(s).Remove(userId));
            return RemoveResult.Removed;
        }

        return configIds.Contains(userId) ? RemoveResult.InConfig : RemoveResult.NotListed;
    }
}

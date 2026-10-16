using Discord;
using Discord.Interactions;
using FaustBot.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FaustBot.Modules;

/// <summary>Suggests configured game names as the user types.</summary>
public sealed class GameAutocompleteHandler : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter,
        IServiceProvider services)
    {
        var typed = autocompleteInteraction.Data.Current.Value as string ?? "";
        var suggestions = services.GetRequiredService<IOptionsMonitor<BotOptions>>().CurrentValue.Games
            .Where(g => g.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Take(SlashCommandBuilder.MaxOptionsCount)
            .Select(g => new AutocompleteResult(g, g));

        return Task.FromResult(AutocompletionResult.FromSuccess(suggestions));
    }
}

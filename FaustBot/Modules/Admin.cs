using Discord.Interactions;

namespace FaustBot.Services
{
    public class Admin : InteractionModuleBase<SocketInteractionContext>
    {
        public InteractionService Commands { get; set; }
        private CommandHandler _handler;
        public Admin(CommandHandler handler)
        {
            _handler = handler;
        }

        [RequireOwner]
        [SlashCommand("kys", "Shut down the bot.")]
        public async Task Kys()
        {
            await RespondAsync("The system will shut down now.");
            Program.StopBot();
        }

        [SlashCommand("ping", "Ping the bot.")]
        public async Task Ping()
        {
            await RespondAsync("Pong!");
        }
    }
}

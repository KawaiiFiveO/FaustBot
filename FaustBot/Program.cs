﻿using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using FaustBot.Services;

namespace FaustBot
{
    class Program
    {
        // setup our fields we assign later
        private readonly IConfiguration _config;
        private DiscordSocketClient _client;
        private InteractionService _commands;
        private ulong _testGuildId;

        public static Task Main(string[] args) => new Program().MainAsync();

        public Program()
        {
            // create the configuration
            var _builder = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile(path: "config.json");

            // build the configuration and assign to _config          
            _config = _builder.Build();
            _testGuildId = ulong.Parse(_config["GuildId"]);
        }

        public async Task MainAsync()
        {

            // call ConfigureServices to create the ServiceCollection/Provider for passing around the services
            using (var services = ConfigureServices())
            {
                //var config = new DiscordSocketConfig()
                //{
                //    GatewayIntents = GatewayIntents.AllUnprivileged
                //};

                // get the client and assign to client 
                // you get the services via GetRequiredService<T>
                var client = services.GetRequiredService<DiscordSocketClient>();
                //var client = new DiscordSocketClient(config);
                var commands = services.GetRequiredService<InteractionService>();
                _client = client;
                _commands = commands;

                // setup logging and the ready event
                client.Log += LogAsync;
                commands.Log += LogAsync;
                client.Ready += ReadyAsync;

                // this is where we get the Token value from the configuration file, and start the bot
                await client.LoginAsync(TokenType.Bot, _config["Token"]);
                await client.StartAsync();

                // we get the CommandHandler class here and call the InitializeAsync method to start things up for the CommandHandler service
                await services.GetRequiredService<CommandHandler>().InitializeAsync();

                CancellationTokenSource = new CancellationTokenSource();

                try
                {
                    await Task.Delay(Timeout.Infinite, CancellationTokenSource.Token);
                }
                catch (TaskCanceledException)
                {
                    Console.WriteLine("Bot is shutting down.");
                }
            }
        }

        public static CancellationTokenSource CancellationTokenSource { get; private set; }

        public static void StopBot()
        {
            CancellationTokenSource.Cancel();
        }

        private Task LogAsync(LogMessage log)
        {
            Console.WriteLine(log.ToString());
            return Task.CompletedTask;
        }

        private async Task ReadyAsync()
        {
            if (IsDebug())
            {
                // this is where you put the id of the test discord guild
                Console.WriteLine($"In debug mode, adding commands to {_testGuildId}...");
                await _commands.RegisterCommandsToGuildAsync(_testGuildId);
            }
            else
            {
                // this method will add commands globally, but can take around an hour
                await _commands.RegisterCommandsGloballyAsync(true);
            }
            Console.WriteLine($"Connected as -> [{_client.CurrentUser.Username}#{_client.CurrentUser.Discriminator}] :)");
        }

        // this method handles the ServiceCollection creation/configuration, and builds out the service provider we can call on later
        private ServiceProvider ConfigureServices()
        {
            // this returns a ServiceProvider that is used later to call for those services
            // we can add types we have access to here, hence adding the new using statement:
            // using csharpi.Services;
            return new ServiceCollection()
                .AddSingleton(_config)
                .AddSingleton<DiscordSocketClient>()
                .AddSingleton(x => new InteractionService(x.GetRequiredService<DiscordSocketClient>()))
                .AddSingleton<CommandHandler>()
                .BuildServiceProvider();
        }

        static bool IsDebug()
        {
            return true;
        }
    }
}
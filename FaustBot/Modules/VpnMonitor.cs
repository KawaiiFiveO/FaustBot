using Discord.Interactions;
using Microsoft.Extensions.Configuration;
using SoftEther.VPNServerRpc;
using System.Globalization;
using System.Timers;

namespace FaustBot.Services
{
    public class VpnMonitor : InteractionModuleBase<SocketInteractionContext>
    {
        public InteractionService Commands { get; set; }
        private CommandHandler _handler;

        private static System.Timers.Timer countdownTimer;

        VpnServerRpc api;
        string hubName;
        ulong guildId;
        ulong channelId;
        private static Dictionary<string, DateTime> _currentUsernames = new Dictionary<string, DateTime>();

        public VpnMonitor(CommandHandler handler, IServiceProvider services, IConfiguration config)
        {
            _handler = handler;
            var serverIp = config["VpnServerIp"];
            var serverPassword = config["VpnServerPassword"];
            hubName = config["VpnHubName"];
            guildId = ulong.Parse(config["TestGuildId"]);
            channelId = ulong.Parse(config["TestChannelId"]);
            api = new VpnServerRpc(serverIp, 443, serverPassword, "");
        }

        [RequireOwner]
        [SlashCommand("start", "Start VPN monitoring service.")]
        public async Task StartVpnMonitor()
        {
            Console.WriteLine("Starting VPN monitoring service.");
            VpnRpcEnumSession in_rpc_enum_session = new VpnRpcEnumSession()
            {
                HubName_str = hubName,
            };
            VpnRpcEnumSession out_rpc_enum_session = api.EnumSession(in_rpc_enum_session);
            SaveUsernames(out_rpc_enum_session);
            SetTimer();
            await RespondAsync("VPN monitoring service started. Printing logs to channel.");
        }

        [RequireOwner]
        [SlashCommand("stop", "Stop VPN monitoring service.")]
        public async Task StopVpnMonitor()
        {
            Console.WriteLine("Stopping VPN monitoring service.");
            DisposeTimer();
            await RespondAsync("VPN monitoring service stopped.");
        }

        [SlashCommand("list", "List current VPN sessions.")]
        public async Task ListVpnSessions()
        {
            Console.WriteLine("Listing current VPN sessions...");
            VpnRpcEnumSession out_rpc_enum_session = Get_EnumSession();
            var usernameAndCreatedTimePairs = out_rpc_enum_session.SessionList
                .Select(session => new
                {
                    Username = session.Username_str,
                    CreatedTime = session.CreatedTime_dt
                })
                .ToList();

            bool isOnlySecureNAT = usernameAndCreatedTimePairs.Count == 1 &&
                                   usernameAndCreatedTimePairs[0].Username.Equals("SecureNAT", StringComparison.OrdinalIgnoreCase);

            string output;
            if (isOnlySecureNAT)
            {
                TimeZoneInfo pstZone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
                DateTime pstTime = TimeZoneInfo.ConvertTimeFromUtc(usernameAndCreatedTimePairs[0].CreatedTime, pstZone);
                string humanReadableTime = pstTime.ToString("dddd, MMMM dd, h:mm:ss tt", CultureInfo.InvariantCulture);
                output = $"The {hubName} hub has been online since {humanReadableTime} PST.\nNo users are currently connected.";
            }
            else
            {
                output = string.Join(Environment.NewLine,
                    usernameAndCreatedTimePairs.Select(pair =>
                    {
                        if (pair.Username.Equals("SecureNAT", StringComparison.OrdinalIgnoreCase))
                        {
                            TimeZoneInfo pstZone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
                            DateTime pstTime = TimeZoneInfo.ConvertTimeFromUtc(pair.CreatedTime, pstZone);
                            string humanReadableTime = pstTime.ToString("dddd, MMMM dd, h:mm:ss tt", CultureInfo.InvariantCulture);
                            return $"The {hubName} hub has been online since {humanReadableTime} PST.";
                        }
                        else
                        {
                            TimeZoneInfo pstZone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
                            DateTime pstTime = TimeZoneInfo.ConvertTimeFromUtc(pair.CreatedTime, pstZone);
                            string humanReadableTime = pstTime.ToString("dddd, MMMM dd, h:mm:ss tt", CultureInfo.InvariantCulture);
                            return $"Username: {pair.Username}, Session Created: {humanReadableTime} PST";
                        }
                    }));
            }
            await RespondAsync(output);
        }

        [SlashCommand("status", "Print VPN hub status.")]
        public async Task VpnStatus()
        {
            VpnRpcHubStatus out_rpc_hub_status = Test_GetHubStatus();
            bool onlineStatus = out_rpc_hub_status.Online_bool;
            string serverStatus = onlineStatus ? "online" : "offline";
            string message = $"The {hubName} hub is currently {serverStatus}.";
            Console.WriteLine(message);
            await RespondAsync(message);
        }

        public static void SaveUsernames(VpnRpcEnumSession vpnRpcEnumSession)
        {
            _currentUsernames = vpnRpcEnumSession.SessionList
                .ToDictionary(session => session.Username_str, session => session.CreatedTime_dt);
        }

        public async void CheckForUserChanges(VpnRpcEnumSession newVpnRpcEnumSession)
        {
            Console.WriteLine("Checking for user changes...");

            var newUsernames = newVpnRpcEnumSession.SessionList
                .ToDictionary(session => session.Username_str, session => session.CreatedTime_dt);

            var joinedUsers = newUsernames
                .Where(pair => !_currentUsernames.ContainsKey(pair.Key))
            .ToList();

            foreach (var pair in joinedUsers)
            {
                TimeZoneInfo pstZone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
                DateTime pstTime = TimeZoneInfo.ConvertTimeFromUtc(pair.Value, pstZone);
                string humanReadableTime = pstTime.ToString("dddd, MMMM dd, h:mm:ss tt", CultureInfo.InvariantCulture);
                string message = $"User {pair.Key} has joined the {hubName} hub at {humanReadableTime} PST.";
                await Context.Client.GetGuild(guildId).GetTextChannel(channelId).SendMessageAsync(message);
                Console.WriteLine(message);
            }

            var leftUsers = _currentUsernames
                .Where(pair => !newUsernames.ContainsKey(pair.Key))
                .ToList();
            foreach (var pair in leftUsers)
            {
                TimeZoneInfo pstZone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
                DateTime pstTime = TimeZoneInfo.ConvertTimeFromUtc(pair.Value, pstZone);
                string humanReadableTime = pstTime.ToString("dddd, MMMM dd, h:mm:ss tt", CultureInfo.InvariantCulture);
                string message = $"User {pair.Key} has left the {hubName} hub. Last seen at {humanReadableTime} PST.";
                await Context.Client.GetGuild(guildId).GetTextChannel(channelId).SendMessageAsync(message);
                Console.WriteLine(message);
            }

            _currentUsernames = newUsernames;
        }

        private void SetTimer()
        {
            countdownTimer = new System.Timers.Timer
            {
                Interval = 30000, // 30 second interval
                AutoReset = true,
                Enabled = true
            };
            countdownTimer.Elapsed += OnTimedEvent;
        }

        private void DisposeTimer()
        {
            countdownTimer.Stop();
            countdownTimer.Dispose();
            countdownTimer = null;
        }

        private void OnTimedEvent(object source, ElapsedEventArgs e)
        {
            VpnRpcEnumSession in_rpc_enum_session = new VpnRpcEnumSession()
            {
                HubName_str = hubName,
            };
            VpnRpcEnumSession out_rpc_enum_session = api.EnumSession(in_rpc_enum_session);
            CheckForUserChanges(out_rpc_enum_session);
        }

        public VpnRpcEnumSession Get_EnumSession()
        {
            //Console.WriteLine("Begin: Test_EnumSession");

            VpnRpcEnumSession in_rpc_enum_session = new VpnRpcEnumSession()
            {
                HubName_str = hubName,
            };
            VpnRpcEnumSession out_rpc_enum_session = api.EnumSession(in_rpc_enum_session);

            //print_object(out_rpc_enum_session);

            //Console.WriteLine("End: Test_EnumSession");
            //Console.WriteLine("-----");
            //Console.WriteLine();

            return out_rpc_enum_session;
        }

        public VpnRpcHubStatus Test_GetHubStatus()
        {
            //Console.WriteLine("Begin: Test_GetHubStatus");

            VpnRpcHubStatus in_rpc_hub_status = new VpnRpcHubStatus()
            {
                HubName_str = hubName,
            };
            VpnRpcHubStatus out_rpc_hub_status = api.GetHubStatus(in_rpc_hub_status);

            return(out_rpc_hub_status);

            //Console.WriteLine("End: Test_GetHubStatus");
            //Console.WriteLine("-----");
            //Console.WriteLine();
        }
        public void print_object(object obj)
        {
            var setting = new Newtonsoft.Json.JsonSerializerSettings()
            {
                NullValueHandling = Newtonsoft.Json.NullValueHandling.Include,
                ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Error,
            };
            string str = Newtonsoft.Json.JsonConvert.SerializeObject(obj, Newtonsoft.Json.Formatting.Indented, setting);
            Console.WriteLine(str);
        }
    }
}

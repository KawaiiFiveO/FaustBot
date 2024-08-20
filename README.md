# FaustBot

A Discord bot for monitoring SoftEther VPN hubs.

## Commands

`/ping` - Pings the bot.

`/kys` - Shut down the bot. (Owner only)

`/status` - Print status of VPN hub.

`/list` - List all sessions on VPN hub.

`/start` - Start VPN monitoring service and print logs to `TestChannelId`. (Owner only)

`/stop` - Stop VPN monitoring service. (Owner only)

## Config

In `config.json`:

```
{
    "Token": "PutYourBotTokenHere",
    "TestGuildId": "1234567890",
    "TestChannelId": "1234567890",
    "VpnServerIp": "123.456.789.10",
    "VpnServerPassword": "PasswordHere",
    "VpnHubName": "HubNameHere"
}
```
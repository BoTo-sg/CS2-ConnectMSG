using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Entities;
using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace ConnectMSG;

public class ConnectMSGConfig : BasePluginConfig
{
    //[JsonPropertyName("PlayerWelcomeMessage")] public bool PlayerWelcomeMessage { get; set; } = true;
    //[JsonPropertyName("Timer")] public float Timer { get; set; } = 5.0f;
    [JsonPropertyName("LogMessagesToDiscord")] public bool LogMessagesToDiscord { get; set; } = true;
    [JsonPropertyName("DiscordWebhook")] public string DiscordWebhook { get; set; } = "";
}

public class ConnectMSG : BasePlugin, IPluginConfig<ConnectMSGConfig>
{
    public override string ModuleName => "ConnectMSG";
    public override string ModuleDescription => "Simple connect/disconnect messages";
    public override string ModuleAuthor => "verneri";
    public override string ModuleVersion => "1.6";

    public ENetworkDisconnectionReason Reason { get; set; }
    public static Dictionary<ulong, bool> LoopConnections = new Dictionary<ulong, bool>();

    public ConnectMSGConfig Config { get; set; } = new();

    public void OnConfigParsed(ConnectMSGConfig config)
    {
        Config = config;
    }

    [GameEventHandler]
    public HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        if (@event == null)
            return HookResult.Continue;

        var player = @event.Userid;

        if (player == null || !player.IsValid || player.IsBot)
            return HookResult.Continue;

        var steamid = player.SteamID;
        var steamid2 = player.AuthorizedSteamID.SteamId2;
        var Name = player.PlayerName;

        string country = GetCountry(player.IpAddress?.Split(":")[0] ?? "Unknown Country");
        //string playerip = player.IpAddress?.Split(":")[0] ?? "Unknown";

        if (LoopConnections.ContainsKey(steamid))
        {
            LoopConnections.Remove(steamid);
        }

        //Console.WriteLine($"[{ModuleName}] {Name} has connected!");
        Server.PrintToChatAll($"{Localizer["playerconnect", Name, steamid2, country]}");

        if (Config.LogMessagesToDiscord)
        {
            _ = SendWebhookMessageAsEmbedConnected(player.PlayerName, player.SteamID, country);
        }

        /*if (Config.PlayerWelcomeMessage)
        {
            AddTimer(Config.Timer, () =>
            {
                player.PrintToChat($"{Localizer["playerwelcomemsg", Name]}");
                player.PrintToChat($"{Localizer["playerwelcomemsgnextline"]}");
            });
        }*/

        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Pre)]
    private HookResult OnPlayerDisconnectPre(EventPlayerDisconnect @event, GameEventInfo info)
    {
        if (@event == null)
            return HookResult.Continue;

        info.DontBroadcast = true;
        return HookResult.Continue;
    }

    [GameEventHandler]
    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        if (@event == null)
            return HookResult.Continue;

        var player = @event.Userid;

        if (player == null || !player.IsValid || player.IsBot)
            return HookResult.Continue;

        var reason = @event.Reason;
        var steamid2 = player.AuthorizedSteamID.SteamId2;
        var Name = player.PlayerName;

        string country = GetCountry(player.IpAddress?.Split(":")[0] ?? "Unknown");
        string disconnectReason = NetworkDisconnectionReasonHelper.GetDisconnectReasonString((ENetworkDisconnectionReason)reason);
        //string playerip = player.IpAddress?.Split(":")[0] ?? "Unknown";

        if (reason == 54 || reason == 55 || reason == 57)
        {
            if (!LoopConnections.ContainsKey(player.SteamID))
            {
                LoopConnections.Add(player.SteamID, true);
            }
            
            if (LoopConnections.ContainsKey(player.SteamID))
            {
                return HookResult.Continue;
            }
        }

        //Console.WriteLine($"[{ModuleName}] {Name} has disconnected!");
        //Console.WriteLine($"[{ModuleName}] {Name} [{steamid2}] has disconnected from {country} ({disconnectReason})!");
        Server.PrintToChatAll($"{Localizer["playerdisconnect", Name, steamid2, country, disconnectReason]}");

        if (Config.LogMessagesToDiscord)
        {
            _ = SendWebhookMessageAsEmbedDisconnected(player.PlayerName, player.SteamID, country, disconnectReason);
        }

        return HookResult.Continue;
    }

    public string GetCountry(string ipAddress)
    {
        try
        {
            using var reader = new DatabaseReader(Path.Combine(ModuleDirectory, "GeoLite2-Country.mmdb"));
            var response = reader.Country(ipAddress);
            return response?.Country?.Name ?? "Unknown Country";
        }
        
        catch (AddressNotFoundException)
        {
            return "Unknown Country";
        }

        catch
        {
            return "Unknown Country";
        }
    }

    public async Task SendWebhookMessageAsEmbedConnected(string playerName, ulong steamID, string country)
    {
        using (var httpClient = new HttpClient())
        {
            var embed = new
            {
                type = "rich",
                title = $"{Localizer["Discord.ConnectTitle", playerName]}",
                url = $"https://steamcommunity.com/profiles/{steamID}",
                description = $"{Localizer["Discord.ConnectDescription", country, steamID]}",
                color = 65280

                /*footer = new
                {
                    text = $"{Localizer["Discord.Footer"]}"
                }*/
            };

            var payload = new
            {
                embeds = new[] { embed }
            };

            var jsonPayload = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync(Config.DiscordWebhook, content);

            if (!response.IsSuccessStatusCode)
            {
                Logger.LogInformation($"Failed to send message to Discord! code: {response.StatusCode}");
            }
        }
    }

    public async Task SendWebhookMessageAsEmbedDisconnected(string playerName, ulong steamID, string country, string reason)
    {
        using (var httpClient = new HttpClient())
        {
            var embed = new
            {
                type = "rich",
                title = $"{Localizer["Discord.DisconnectTitle", playerName]}",
                url = $"https://steamcommunity.com/profiles/{steamID}",
                description = $"{Localizer["Discord.DisconnectDescription", country, steamID, reason]}",
                color = 16711680

                /*footer = new
                {
                    text = $"{Localizer["Discord.Footer"]}"
                }*/
            };

            var payload = new
            {
                embeds = new[] { embed }
            };

            var jsonPayload = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync(Config.DiscordWebhook, content);

            if (!response.IsSuccessStatusCode)
            {
                Logger.LogInformation($"Failed to send message to Discord! code: {response.StatusCode}");
            }
        }
    }
}

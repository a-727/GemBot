using Discord;
using Discord.WebSocket;
using Newtonsoft.Json;
using InvalidOperationException = System.InvalidOperationException;

namespace GemBot;

public class UserExistsException(string username = "") : Exception($"Username {username} already exists")
{
    
}
public static class Tools
{
    private static string RandomPasswordGenerator()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()+-=";
        Random random = new Random();
        string toReturn = "";

        for (int i = 0; i < 10; i++)
        {
            toReturn += chars[random.Next(chars.Length)];
        }

        return toReturn;
    }
    public static async Task<User> UserCreator(ulong id)
    {
        if (File.Exists($"../../../Data/Users/{id}"))
        {
            throw new UserExistsException(id.ToString());
        }
        User user = new User();
        user.ID = id;
        user.Inventory = new List<int>();
        user.Gems = [20, 5, 1, 0, 0];
        user.CurrentPassword = RandomPasswordGenerator();
        user.CoolDowns = new Dictionary<string, long>();
        user.DailyQuestsDay = 0;
        user.DailyQuestsProgress = new Dictionary<string, ulong>();
        user.Stats = new Dictionary<string, UInt128>();
        await File.WriteAllTextAsync($"../../../Data/Users/{id}",JsonConvert.SerializeObject(user));
        return user;
    }
    public static bool ShowEmojis(SocketSlashCommand command, ulong botID, DiscordSocketClient client)
        //The SocketSlashCommand is required for several checks, and the botID is required if the bot is used within a server. The client is required to get the SocketGuildUser.
    {
        SocketGuildUser user;
        if (command.Channel == null) 
            //The channel is only ever null IF it is run in a server it doesn't have access to the channels. IE: User Apps run as a User App.
        {
            return false; //Since it is a User App, we can say it can't show custom emojis, hence the false return.
        }
        try
        {
            user = client.GetGuild(command.GuildId ?? throw new InvalidOperationException()).GetUser(botID); 
            //This sets us up for future code by getting the SocketGuildUser of the bot, which we will check later for "Use External Emojis" permission.
            //In addition, it raises a DivideByZeroException immediately handled below if it's not in a guild.
        }
        catch (InvalidOperationException)
        {
            //So far we know:
            //It's not in a guild (that would be what raised the DivideByZeroException).
            //It's not a user app (that would have returned false already).
            //So we can conclude that it is a DM, in which case use external emojis is always turned on.
            return true; //In DMs, you can always use External Emojis, so let's send some...
        }
        if (user.GuildPermissions.Has(GuildPermission.UseExternalEmojis))
            //remember user is the current bot in the current guild
        {
            return true; //The bot is in a server where it has "Use External Emojis" permission, so those emojis will show.
        }
        return false; //Since every other case has returned, the bot is in a server where it doesn't have permissions to use emojis. So, let's return that.
    }
}
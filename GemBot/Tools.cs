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
    public static ulong CurrentTime()
    {
        ulong t = (ulong)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        return t;
    }
    public static async Task<CachedUser> UpdateTutorial(string done, List<Tutorial> tutorials, CachedUser user, SocketSlashCommand command)
    {
        if (user.TutorialOn == null) return user;
        Tutorial tutorial = tutorials[(int)user.TutorialOn];
        Step step = tutorial.Steps[user.TutorialPage];
        user.TutorialProgress ??= new bool[step.Requirements.Length];
        if (step.Flexible)
        {
            for (int i = 0; i < step.Requirements.Length; i++)
            {
                if (step.Requirements[i] != done) continue;
                user.TutorialProgress[i] = true;
            }
        }
        else
        {
            for (int i = 0; i < step.Requirements.Length; i++)
            {
                if (user.TutorialProgress[i]) continue;
                if (step.Requirements[i] == done)
                {
                    user.TutorialProgress[i] = true;
                }
                break;
            }
        }

        if (!user.TutorialProgress.All(c => c)) return user;
        if (user.TutorialPage < tutorial.Steps.Count - 1)
        {
            user.TutorialPage++;
            step = tutorial.Steps[user.TutorialPage];
            user.TutorialProgress = new bool[step.Requirements.Length];
            EmbedBuilder embay = new EmbedBuilder()
                .WithTitle($"{tutorial.Name}: {step.Name}")
                .WithDescription(step.Description);
            await command.Channel.SendMessageAsync($"<@{user.User.ID}> you have completed a step in the following tutorial! Below is what to do next:", embed: embay.Build());
        }
        else
        {
            user.TutorialPage = 0;
            user.TutorialProgress = null;
            user.TutorialOn = null;
            await command.Channel.SendMessageAsync($"<@{user.User.ID}> you have completed your current tutorial! Use `/start` to start a new one!");
        }
        return user;
    }
    public static async Task<User> UserCreator(ulong id)
    {
        if (File.Exists($"../../../Data/Users/{id}"))
        {
            throw new UserExistsException(id.ToString());
        }
        User user = new User { ID = id };
        await File.WriteAllTextAsync($"../../../Data/Users/{id}",JsonConvert.SerializeObject(user));
        return user;
    }
    public static async Task<int> CharmEffect(string[] effects, List<Item> items, User user)
    {
        int toReturn = 0;
        for (int i = 0; i < items.Count; i++)
        {
            Item item = items[i];
            bool has = await user.ItemAmount(i) > 0;
            if (!has) continue;
            foreach (Charm charm in item.Charms)
            {
                if (!effects.Contains(charm.Effect)) continue;
                toReturn += charm.Amount;
            }
        }
        return toReturn;
    }
    public static int GetCharm(Dictionary<string, List<int>> itemLists, int startingRarity = 0, int upgradeDiff = 11, Random? randomParam = null, string[]? rarityToListParam = null)
    {
        Random random = randomParam ?? new Random();
        string[] rarityToList = rarityToListParam ?? ["CommonCharms", "UncommonCharms", "RareCharms", "EpicCharms", "LegendaryCharms"];
        if (random.Next(0, upgradeDiff + 1) == upgradeDiff && startingRarity < 4)
        {
            return GetCharm(itemLists, startingRarity + 1, upgradeDiff);
        }
        List<int> itemChoice = itemLists[rarityToList[startingRarity]];
        return itemChoice[random.Next(itemChoice.Count)];
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
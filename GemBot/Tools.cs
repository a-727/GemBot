using Discord;
using Discord.Interactions;
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
    public static uint CurrentDay()
    {
        uint t = (uint)Math.Floor((DateTime.UtcNow - new DateTime(2024, 6, 1)).TotalDays);
        return t;
    }
    public static string ProgressBar(int done, int total, int width)
    {
        string[] start = ["<:progress1E:1287084674648375419>", "<:progress1P:1287084753438244906>", "<:progress1H:1287084728268226690>", "<:progress1F:1287084706667561062>", "<:progress1C:1287084653379063818>"];
        string[] middle = ["<:progress2E:1287084821515993270>", "<:progress2P:1287084937635172372>", "<:progress2H:1287084885617541263>", "<:progress2M:1287084910271795341>", "<:progress2F:1287084849227894915>", "<:progress2C:1287084797490888848>"];
        string[] end = ["<:progress3E:1287084980190838959>", "<:progress3P:1287085204875513886>", "<:progress3H:1287085077305753671>", "<:progress3M:1287085165037748357>", "<:progress3F:1287085036998361181>"];
        int tPips = width * 4 - 1;
        int cPips = (done*tPips) / total;
        int cIndex =0;
        if (width < 2)
        {
            throw new ArgumentException("Width must be at least 2");
        }
        string toReturn = string.Empty;
        cIndex = Math.Min(start.Length - 2, cPips);
        cPips -= cIndex;
        if (cPips > 0) cIndex++;
        toReturn += start[cIndex];
        for (int i = 1; i < width - 1; i++)
        {
            cIndex = Math.Min(middle.Length - 2, cPips);
            cPips -= cIndex;
            if (cPips > 0) cIndex++;
            toReturn += middle[cIndex];
        }
        toReturn += end[cPips];
        return toReturn;
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
            await command.Channel.SendMessageAsync(
                $"<@{user.User.ID}> you have completed a step in the following tutorial! Below is what to do next:",
                embed: embay.Build());
        }
        else
        {
            user.TutorialPage = 0;
            user.TutorialProgress = null;
            user.TutorialOn = null;
            await command.Channel.SendMessageAsync(
                $"<@{user.User.ID}> you have completed your current tutorial! Use `/start` to start a new one!");
        }

        return user;
    }
    public static async Task<User> UserCreator(ulong id)
    {
        if (File.Exists($"Data/Users/{id}"))
        {
            Console.WriteLine("User exists!");
            throw new UserExistsException(id.ToString());
        }

        User user = new User { ID = id };
        await File.WriteAllTextAsync($"Data/Users/{id}", JsonConvert.SerializeObject(user));
        return user;
    }
    public static int CharmEffect(string[] effects, List<Item> items, User user)
    {
        int toReturn = 0;
        for (int i = 0; i < items.Count; i++)
        {
            Item item = items[i];
            bool has = user.ItemAmount(i) > 0;
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
        string[] rarityToList = rarityToListParam ??
                                ["CommonCharms", "UncommonCharms", "RareCharms", "EpicCharms", "LegendaryCharms"];
        if (random.Next(0, upgradeDiff) == upgradeDiff - 1 && startingRarity < 4)
        {
            return GetCharm(itemLists, startingRarity + 1, upgradeDiff, random, rarityToList);
        }

        List<int> itemChoice = itemLists[rarityToList[startingRarity]];
        return itemChoice[random.Next(itemChoice.Count)];
    }
    public static bool ShowEmojis(SocketSlashCommand command, ulong botID, DiscordSocketClient client)
        //The SocketSlashCommand is required for several checks, and the botID is required if the bot is used within a server. The client is required to get the SocketGuildUser.
    {
        if (Settings.AppEmoji())
        {
            return true;
        }

        SocketGuildUser user;
        if (command.Channel == null)
            //The channel is only ever null IF it is run in a server it doesn't have access to the channels. IE: User Apps run as a User App.
        {
            return true; //Since it is a User App, we can say it can show custom emojis, hence the true return.
            //It used to be the false but now user apps can use emojis.
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
            return
                true; //The bot is in a server where it has "Use External Emojis" permission, so those emojis will show.
        }

        return
            false; //Since every other case has returned, the bot is in a server where it doesn't have permissions to use emojis. So, let's return that.
    }
    public static string IDString(int id, string directory = "Data/Items")
    {
        return id switch
        {
            >= 1000 => throw new InvalidArgumentException(),
            >= 100 => $"{directory}/{id}.json",
            >= 10 => $"{directory}/0{id}.json",
            >= 0 => $"{directory}/00{id}.json",
            _ => throw new InvalidArgumentException()
        };
    }
    public static bool VerifyOriginalUse(SocketMessageComponent component) //ONLY WORKS IF COMPONENT IS IN INCLUDED IN AN INTERACTION
    {
        try
        {
            return component.User.Id == component.Message.Interaction.User.Id;
        }
        catch (NullReferenceException)
        {
            return true; //If anything is null, this means that the message is ephemeral or deleted (most likely ephemeral)
        }
    }
    public static int TotalEmbedLength(EmbedBuilder embed)
    {
        int t = embed.Length;
        foreach (EmbedFieldBuilder embedField in embed.Fields)
        {
            t += embedField.Name.Length + embedField.Value.ToString().Length;
        }

        return t;
    }
    public class ItemAutocompleteHandler : AutocompleteHandler
    {
        public List<Item> Items { get; set; }

        public ItemAutocompleteHandler(List<Item> items)
        {
            Items = items;
        }

        public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
            IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
        {
            // Create a collection with suggestions for autocomplete
            List<AutocompleteResult> results = new List<AutocompleteResult>();
            Console.WriteLine(context.Interaction.Data.ToString());

            // max - 25 suggestions at a time (API limit)
            return AutocompletionResult.FromSuccess(results.Take(25));
        }
    }
    public static string ArrayToText<T>(T[] array, string delimiter = ",", string startWith = "[", string endWith = "]")
    {
        string toReturn = startWith;
        foreach (T item in array)
        {
            if (toReturn != startWith) toReturn += delimiter;
            if (item == null) { toReturn += "null"; }
            else { toReturn += item.ToString(); }
        }
        toReturn += endWith;
        return toReturn;
    }

    public static int AprilFoolsYear()
    {
        if (DateTime.UtcNow.Day == 1 && DateTime.UtcNow.Month == 4) return DateTime.UtcNow.Year;
        return 0;
    }
    public static IEmote ParseEmote(string emote)
    {
        try
        {
            return Emote.Parse(emote);
        }
        catch (ArgumentException)
        {
            return Emoji.Parse(emote);
        }
    }
}
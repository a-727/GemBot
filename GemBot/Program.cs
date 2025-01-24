using System.Diagnostics;
using System.Threading.Channels;
using Discord;
using Discord.Net;
using Discord.Rest;
using Discord.WebSocket;
using Newtonsoft.Json;

namespace GemBot;

public class NoTokenError : Exception
{
    
}
public class UserNotFoundError() : Exception("The user was not found"){}
public class InvalidArgumentException: Exception{}

public class Cooldown : Exception
{
    public Cooldown() : base($"You are on cooldown.") { }
    public Cooldown(string until) : base($"You are on cooldown. Please try again in {until}."){}
    public Cooldown(ulong endSeconds): base($"You are on cooldown. Please try again <t:{endSeconds}:R>"){}
    public Cooldown (int secondsLeft): base($"You are on cooldown. Please try again <t:{secondsLeft+DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:R>"){}
}
public class ButtonValueError() :Exception("An internal code error due to how the button is defined means that the button does not work."){}

public static class Program
{
    public static async Task Main()
    {
        while (!Directory.GetCurrentDirectory().EndsWith(Settings.CurrentDirectoryName()))
        {
            Directory.SetCurrentDirectory("..");
        }
        GemBot gemBot = new GemBot();
        await gemBot.Main();
    }
}

public class GemBot
{
    private const int FurnaceConst = 1; //Number of default furnaces without charms.
    private Dictionary<string, List<int>> _itemLists = new();
    private readonly Random _rand = new();
    private readonly DiscordSocketClient _client = new();
    private Dictionary<string, List<string>> _dataLists = new();
    private List<Item> _items = [];
    private List<Tutorial> _tutorials = [];
    private Dictionary<ulong, CachedUser> _users = [];
    private readonly string[] _currency = ["<:diamond:1287084308485640288>", "<:emerald:1287084632428515338>", "<:sapphire:1287086790137876530>", "<:ruby:1287086175974199347>", "<:amber:1287084015135756289>"];
    private readonly string[] _currencyNoEmoji = [" **diamonds**", " **emeralds**", " **sapphires**", " **rubies**", " **amber**"];
    private List<DailyQuest> _quests = [];
    private List<List<DailyQuest>> _allQuests = [];
    private MineData _mineData = null!;
    private List<CraftingRecipe> _craftingRecipes = [];
    public async Task Main()
    {
        _client.Log += Log;
        _client.SlashCommandExecuted += CommandHandlerStartup;
        _client.MessageReceived += TextMessageHandlerSetup;
        _client.ButtonExecuted += ButtonHandlerSetup;
        string token;
        try
        {
            token = await File.ReadAllTextAsync("token.txt");
        }
        catch
        {
            throw new NoTokenError();
        }
        await GetItems();
        foreach (string path in Directory.GetFiles("Data/CraftingRecipes"))
        {
            string recipeData = await File.ReadAllTextAsync(path);
            CraftingRecipe? recipe = JsonConvert.DeserializeObject<CraftingRecipe>(recipeData);
            if (recipe is not null)
            {
                _craftingRecipes.Add(recipe);
            }
        }
        _craftingRecipes = _craftingRecipes.OrderBy(o => o.ID).ToList();
        _mineData = await MineData.LoadMineData();
        _ = Task.Run(() => Task.FromResult(RunTicks()));
        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();
        await Task.Delay(-1);
    }
    
    private async Task GetItems()
    {
        await _client.SetGameAsync("Updating items...");
        _items = [];
        _users = new Dictionary<ulong, CachedUser>();
        _tutorials = await Tutorial.LoadAll(2);
        foreach (string path in Directory.GetFiles("Data/Items"))
        {
            string itemData = await File.ReadAllTextAsync(path);
            Item? item = JsonConvert.DeserializeObject<Item>(itemData);
            if (item is not null)
            {
                _items.Add(item);
            }
        }
        _items = _items.OrderBy(o => o.ID).ToList();

        _itemLists = new Dictionary<string, List<int>>();
        foreach (string path in Directory.GetFiles("Data/ItemLists"))
        {
            string name = path.Split('/')[^1].Split('.')[0];
            List<int>? list = JsonConvert.DeserializeObject<List<int>>(await File.ReadAllTextAsync(path));
            await Task.Delay(1);
            if (list is not null)
            {
                _itemLists.Add(name, list);
            }
        }

        _dataLists = new Dictionary<string, List<string>>();
        foreach (string path in Directory.GetFiles("Data/Lists"))
        {
            string name = path.Split('/')[^1].Split('.')[0];
            List<string>? list = JsonConvert.DeserializeObject<List<string>>(await File.ReadAllTextAsync(path));
            await Task.Delay(1);
            if (list is not null)
            {
                _dataLists.Add(name, list);
            }
        }

        _allQuests = [];
        List<string> paths = [..Directory.GetDirectories("Data/DailyQuests")];
        paths.Sort();
        foreach (string directory in paths)
        {
            _allQuests.Add([]);
            foreach (string path in Directory.GetFiles(directory))
            {
                DailyQuest? quest = JsonConvert.DeserializeObject<DailyQuest>(await File.ReadAllTextAsync(path));
                await Task.Delay(1);
                if (quest is not null)
                {
                    _allQuests[^1].Add(quest);
                }
            }
            _allQuests[^1] = _allQuests[^1].OrderBy(o => o.ID).ToList();
        }

        _quests = [];
        string tempString = await File.ReadAllTextAsync("Data/DailyQuests/DateQuestsMap.txt");
        List<int> tempListInt = JsonConvert.DeserializeObject<List<int>>(tempString) ?? [0];
        if (tempListInt[0] == Tools.CurrentDay())
        {
            for (int i = 0; i < 5; i++)
            {
                _quests.Add(_allQuests[i][tempListInt[i+1]]);
            }

            foreach (DailyQuest quest in _quests)
            {
                quest.Date = Tools.CurrentDay();
            }
        }
        else
        {
            Console.WriteLine("Changing quests...");
            _quests = [];
            List<int> mapToQuests = [(int)Tools.CurrentDay()];
            foreach (List<DailyQuest> questSet in _allQuests)
            {
                int page = _rand.Next(questSet.Count);
                DailyQuest quest = questSet[page];
                quest.Date = Tools.CurrentDay();
                _quests.Add(quest);
                mapToQuests.Add(page);
            }
            await File.WriteAllTextAsync("Data/DailyQuests/DateQuestsMap.txt", JsonConvert.SerializeObject(mapToQuests));
        }
        await _client.SetGameAsync("/start");
    }
    private async Task<CachedUser> GetUser(ulong id)
    {
        if (_users.TryGetValue(id, out CachedUser? cachedUser))
        {
            Debug.Assert(cachedUser != null, "cachedUser is null");
            cachedUser.InactiveSince = Tools.CurrentTime();
            return cachedUser;
        }
        try
        {
            string baseData = await File.ReadAllTextAsync($"Data/Users/{id}");
            User loadedUser = JsonConvert.DeserializeObject<User>(baseData) ??
                              throw new Exception("Somehow your save file is bad.");
            CachedUser cached = new CachedUser(loadedUser, Tools.CurrentTime());
            _users.Add(id, cached);
            return cached;
        }
        catch (FileNotFoundException)
        {
            throw new UserNotFoundError();
        }
    }
    private static Task Log(LogMessage msg)
    {
        Console.WriteLine(msg.ToString());
        return Task.CompletedTask;
    }
    
    
    private Task CommandHandlerStartup(SocketSlashCommand command)
    {
        _ = Task.Run(() => Task.FromResult(CommandHandler(command)));
        return Task.CompletedTask;
    }
    //Slash Command Handler Below
    private async Task CommandHandler(SocketSlashCommand command)
    {
        try
        {
            switch (command.Data.Name)
            {
                case "balance":
                    try
                    {
                        string temp = command.Data.Options.First().Value.ToString() ??
                                      throw new InvalidOperationException();
                        if (temp == "False")
                        {
                            await Balance(command, ephemeral: false);
                            break;
                        }

                        await Balance(command);
                    }
                    catch
                    {
                        await Balance(command);
                    }

                    break;
                case "item":
                    await GetItem(command);
                    break;
                case "stats":
                    await Stats(command);
                    break;
                case "beg":
                    await Beg(command);
                    break;
                case "magik":
                    await Magik(command);
                    break;
                case "work":
                    await Work(command);
                    break;
                case "inventory":
                    await InventoryCommand(command);
                    break;
                case "help":
                    await HelpCommand(command);
                    break;
                case "start":
                    await SetTutorial(command);
                    break;
                case "bank":
                    await Bank(command);
                    break;
                case "theme":
                    await SetTheme(command);
                    break;
                case "quests":
                    await DailyQuests(command);
                    break;
                case "give":
                    await Give(command);
                    break;
                case "play":
                    await Play(command);
                    break;
                case "mine":
                    await Mine(command);
                    break;
                case "craft":
                    await Craft(command);
                    break;
                default:
                    await command.RespondAsync($"Command {command.Data.Name} not found", ephemeral: true);
                    break;
            }

            if (_users.TryGetValue(command.User.Id, out CachedUser? value))
            {
                _users[command.User.Id] = await Tools.UpdateTutorial(command.Data.Name, _tutorials, value, command);
            }
            try
            {
                User user = await GetUser(command.User.Id);
                int delay = (int)await user.GetSetting("delayBeforeDelete", 60);
                if (delay == 0)
                {
                    return;
                }
                await Task.Delay(TimeSpan.FromMinutes(delay));
                await command.DeleteOriginalResponseAsync();
            }
            catch (HttpException) { }
        }
        catch (Cooldown cool)
        {
            await command.RespondAsync(cool.Message, ephemeral: true);
        }
        catch (UserNotFoundError)
        {
            try
            {
                await UserSetupSlash(command);
            }
            catch (UserExistsException)
            {
                await command.RespondAsync(
                    "A user you are trying to interact with doesn't exists. You can only create accounts for yourself.");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            var embay = new EmbedBuilder()
                .WithTitle("Error")
                .WithAuthor(command.User)
                .WithColor(255, 0, 0)
                .AddField("Your command generated an error", $"**Full Details**: `{e}`");
            await command.RespondAsync(embed: embay.Build());
        }

    }
    //Slash Command Handler Above
    private async Task HelpCommand(SocketSlashCommand command)
    {
        EmbedBuilder embay = new EmbedBuilder()
            .WithTitle("All commands")
            .WithDescription("The main commands of gemBOT will be listed here, organized into groups.")
            .AddField("Grind commands",
                "`/beg`: Beg for gems every 5 seconds! You can get 5-8 diamonds. \n`/work`: Work for gems every 5 minutes! You can get 12-15 emeralds! \n`/magik`: Technically a grind command, you can magik up gems every 12 seconds. Better with a wand!")
            .AddField("Other economy Commands",
                "`/balance`: view your balance in gems! \n`/inventory`: view your inventory, split up into multiple pages")
            .AddField("Info commands",
                "`/help`: List (almost) all gemBOT commands! \n`/start`: View any of gemBOT's many tutorials. \n`/item`: View details about an item");
        await command.RespondAsync(embed: embay.Build());
    }
    private async Task<string> BalanceRaw(bool showEmojis, ulong userID, string atStartInfo = "**Your balance**:")
    {
        bool compact = showEmojis;
        User user = await GetUser(userID);
        string text =
            $"{atStartInfo} {user.Gems[0]}{_currency[0]}, {user.Gems[1]}{_currency[1]}, {user.Gems[2]}{_currency[2]}, {user.Gems[3]}{_currency[3]}, {user.Gems[4]}{_currency[4]}";
        if (!compact)
        {
            text =
                $"{atStartInfo}\n > **Diamonds**: {user.Gems[0]}\n > **Emeralds**: {user.Gems[1]}\n > **Sapphires**: {user.Gems[2]}\n > **Rubies**: {user.Gems[3]}\n > **Amber**: {user.Gems[4]}";
        }

        return text;
    }
    private async Task Balance(SocketSlashCommand command, string title = "Your balance:", bool? compactArg = null, bool ephemeral = true)
    {
        bool compact = Tools.ShowEmojis(command, Settings.BotID(), _client);
        if (compactArg == true)
        {
            compact = true;
        }
        else if (compactArg == false)
        {
            compact = false;
        }
        User user = await GetUser(command.User.Id);
        await user.Increase("commands", 1);
        EmbedBuilder embay = new EmbedBuilder()
            .WithTitle(title)
            .WithDescription(await BalanceRaw(compact, command.User.Id, ""))
            .WithColor((uint)await user.GetSetting("uiColor", 3287295));
        await command.RespondAsync(embed: embay.Build(), ephemeral: ephemeral);
        if (await Tools.CharmEffect(["betterAutoRefresh"], _items, user) == 0) return;
        ulong upperTime = Tools.CurrentTime() + 1800;
        while (Tools.CurrentTime() < upperTime)
        {
            user = await GetUser(command.User.Id);
            embay = new EmbedBuilder()
                .WithTitle(title)
                .WithDescription(await BalanceRaw(compact, command.User.Id, ""))
                .WithColor((uint)await user.GetSetting("uiColor", 3287295));
            await command.ModifyOriginalResponseAsync(p =>
            {
                p.Embed = embay.Build();
                p.Content = "View your balance! This message auto-updates for an hour.";
            });
            await Task.Delay(2000);
        }
    }
    private async Task GetItem(SocketSlashCommand command)
    {
        try
        {
            await command.RespondAsync(
                _items[
                        int.Parse(command.Data.Options.First().Value.ToString() ??
                                  throw new Exception("Bad parameters - there's probably an error in the code."))]
                    .ToString(), ephemeral:true);
        }
        catch (ArgumentOutOfRangeException)
        {
            await command.RespondAsync("This item does not exist", ephemeral:true);
        }
    }
    private async Task UserSetupSlash(SocketSlashCommand command)
    {
        var id = command.User.Id;
        await Tools.UserCreator(id);
        await Balance(command, "Welcome to gemBOT! Here's your starting balance:");
    }
    private async Task Stats(SocketSlashCommand command)
    {
        User user = await GetUser(command.User.Id);
        await user.Increase("commands", 1);
        EmbedBuilder embay = new EmbedBuilder()
            .WithTitle($"{command.User.Username}'s stats")
            .WithDescription($"View all the stats for {command.User.Username}.")
            .WithFooter(new EmbedFooterBuilder().WithText("GemBOT Stats may not be 100% accurate."))
            .AddField("Commands Ran", $"""
                                       {user.GetStat("commands")} commands in total
                                        > {user.GetStat("beg")} begs!
                                        > {user.GetStat("work")} works!
                                        > {user.GetStat("magik")} magiks!
                                        > {user.GetStat("play")} plays!
                                       """)
            .AddField("Gems Earned/Gifted",
                $"Earned {user.GetStat("earned")} diamond-equivalents by grinding only."
                + $"\n > Gifted {user.GetStat("gifted")} diamond-equivelents to other users only."
                + "\n > Diamond Equivalents: one diamond is 1, one emerald is 10, one sapphire is 100, one ruby is 1,000, and one amber is 10,000")
            .WithColor(new Color((uint)await user.GetSetting("uiColor", 3287295)));
        await command.RespondAsync(embed: embay.Build());
    }
    private async Task Beg(SocketSlashCommand command)
    {
        User user = await GetUser(command.User.Id);
        if (command.Data.Options.Count >= 1)
        {
            await user.SetSetting("hideBeg", (ulong)(long)command.Data.Options.First().Value);
        }
        ulong t = Tools.CurrentTime();
        uint timeoutFor = (await Tools.CharmEffect(["fasterCooldown", "positive"], _items, user)) switch
        {
            <= 100 => 5,
            <= 180 => 4,
            <= 275 => 3,
            <= 399 => 2,
            <= 500 => 1,
            _ => 0
        };
        if (await user.OnCoolDown("beg", t, timeoutFor))
        {
            throw new Cooldown(user.CoolDowns["beg"]);
        }

        int mn = 5 + await Tools.CharmEffect(["BegMin", "Beg", "GrindMin", "Grind", "Positive"], _items, user);
        int mx = 9 + await Tools.CharmEffect(["BegMax", "Beg", "GrindMax", "Grind", "Positive"], _items, user);
        int amnt = _rand.Next(mn, mx);
        int chanceRoll = _rand.Next(0, 500) + 1; //random number from 1 to 500
        await user.Increase("commands", 1, false);
        await user.Increase("beg", 1);
        int sucsessChance = 300 - (int) user.GetProgress("begSuccess") + await Tools.CharmEffect(["BegChance", "Beg"], _items, user);
        if (sucsessChance < 50) sucsessChance = 50;
        if (chanceRoll > sucsessChance)
        {
            uint color = (uint)(await user.GetSetting("begRandom", 0) switch
            {
                0 => await user.GetSetting("begColor", 65525),
                1 => (ulong)_rand.Next(16777216), 
                _ => (ulong)3342180
            });
            if (await user.GetSetting("begFailRed", 1) == 1) color = 16711680;
            await user.Increase("begFail", 1);
            EmbedBuilder embayFail = new EmbedBuilder()
                .WithTitle("Beg failure!")
                .WithDescription($"You failed and didn't get any gems. \n > You had a {sucsessChance/5}.{(sucsessChance%5) * 2}% chance to succeed.")
                .WithColor(new Color(color));
            await command.RespondAsync(embed: embayFail.Build(), ephemeral:await user.GetSetting("hideBeg", 0) == 1);
            return;
        }
        await user.Add(amnt, 0, false);
        string text = $"You gained {amnt} **Diamonds**.";
        if (Tools.ShowEmojis(command, Settings.BotID(), _client))
        {
            text = $"You gained {amnt}{_currency[0]}!";
        }
        await user.Increase("earned", amnt, false);
        await user.Increase("begSuccess", 1);
        List<string> begChoices = _dataLists["BegEffects"];
        EmbedBuilder embay = new EmbedBuilder()
            .WithTitle(text)
            .WithDescription(begChoices[_rand.Next(0, begChoices.Count)])
            .WithColor(new Color((uint)(await user.GetSetting("begRandom", 0) switch
            {
                0 => await user.GetSetting("begColor", 65525), 1 => (ulong)_rand.Next(16777216), _ => (ulong)3342180
            })));
        await command.RespondAsync(embed: embay.Build(), ephemeral:await user.GetSetting("hideBeg", 0) switch {1 => true, _ => false});
    }
    private async Task Work(SocketSlashCommand command)
    {
        CachedUser cached = await GetUser(command.User.Id);
        User user = cached.User;
        ulong t = Tools.CurrentTime();
        int effect = await Tools.CharmEffect(["fasterCooldown", "positive"], _items, user);
        uint timeoutFor = effect switch
        {
            <= 0 => 300,
            <= 60 => 300-(uint)effect,
            <= 180 => 240-((uint)effect-60)/2,
            <= 360 => 180-((uint)effect-180)/3,
            <= 600 => 120-((uint)effect-360)/4,
            <= 900 => 60-((uint)effect-600)/5,
            _ => 0
        };
        if (await user.OnCoolDown("work", t, timeoutFor))
        {
            throw new Cooldown(user.CoolDowns["work"]);
        }
        int workNum = _rand.Next(255) + 1;
        cached.LastWork = (byte) workNum;
        int jobRandom = _rand.Next(1);
        EmbedBuilder embay = new EmbedBuilder().WithTitle("Work!");
        ComponentBuilder components = new ComponentBuilder();
        string text = "View embed for more information.";
        switch (jobRandom)
        {
            case 0:
                List<string> hearts = [":heart:", ":orange_heart:", ":yellow_heart:", ":green_heart:", ":blue_heart:", ":purple_heart:"];
                List<string> squares = [":red_square:", ":orange_square:", ":yellow_square:", ":green_square:", ":blue_square:", ":purple_square:"];
                List<string> circles = [":red_circle:", ":orange_circle:", ":yellow_circle:", ":green_circle:", ":blue_circle:", ":purple_circle:"];
                List<string> types = ["heart", "square", "circle"];
                int choiceShape = _rand.Next(3);
                int heartID = _rand.Next(hearts.Count);
                int squareID = _rand.Next(squares.Count);
                int circleID = _rand.Next(circles.Count);
                embay.WithDescription($"Remember the color of the following shapes\n > {hearts[heartID]} {squares[squareID]} {circles[circleID]}");
                await command.RespondAsync(text, embed: embay.Build());
                await Task.Delay(8000);
                embay.WithDescription($"What was the color of the {types[choiceShape]}?");
                int chosenID = choiceShape switch { 0 => heartID, 1 => squareID, _ => circleID };
                List<string> chosenList = choiceShape switch { 0 => hearts, 1=> squares, _ => circles };
                int excludedShape = _rand.Next(5);
                if (excludedShape >= chosenID) excludedShape++;
                ActionRowBuilder rowShapes = new ActionRowBuilder();
                for (int i = 0; i < chosenList.Count; i++)
                {
                    if (i == excludedShape) continue;
                    string customIDShapes = "work-" + (i == chosenID) switch { true => "success", false => "failure" } + $"|{workNum}|{Tools.ShowEmojis(command, Settings.BotID(), _client)}|{i}";
                    // ReSharper disable once GrammarMistakeInComment
                    //I is added to prevent custom ID duplication.
                    rowShapes.WithButton(customId: customIDShapes, emote: Emoji.Parse(chosenList[i]), style: ButtonStyle.Primary);
                }
                components.AddRow(rowShapes);
                await command.ModifyOriginalResponseAsync((properties) =>
                {
                    properties.Embed = embay.Build();
                    properties.Components = components.Build();
                });
                break;
        }
        /*
        await user.Add(amnt, 1, false);
        string text = $"You gained {amnt} **Emeralds**.";
        if (Tools.ShowEmojis(command, Settings.BotID(), _client))
        {
            text = $"You gained {amnt}{_currency[1]}!!!";
        }
        await user.Increase("commands",1, false);
        await user.Increase("earned", amnt*10, false);
        await user.Increase("work", 1);
        List<string> workChoices = _dataLists["WorkEffect"];
        EmbedBuilder embay = new EmbedBuilder()
            .WithTitle(text)
            .WithDescription(workChoices[_rand.Next(0, workChoices.Count)])
            .WithColor(new Color(50, 255, 100));
        await command.RespondAsync(embed: embay.Build());
        */
    }
    private async Task<EmbedBuilder> InventoryRawEmbed(ulong id, string options)
    {
        User user = await GetUser(id);
        List<Tuple<int, int>> data = [];
        for (int i = 0; i < user.Inventory.Count; i++)
        {
            int amount = await user.ItemAmount(i);
            data.Add(new Tuple<int, int>(i, amount));
        }

        List<Tuple<int, int>> sortedData = data.OrderBy(o => -o.Item2 * _items[o.Item1].Value).ToList();
        string[] optionsSplit = options.Split("|");
        try
        {
            string text = "View all your items:";
            int page = int.Parse(optionsSplit[0]);
            bool showEmojis = optionsSplit[1].StartsWith('y') || optionsSplit[1].StartsWith('t') ||
                              optionsSplit[1].StartsWith('T');
            for (int i = page * 8; i < sortedData.Count && i < (page + 1) * 8; i++)
            {
                int amount = sortedData[i].Item2;
                Item item = _items[sortedData[i].Item1];
                text += "\n ";
                if (amount == 0)
                {
                    text += "0";
                }
                else
                {
                    text += $"__{amount}__";
                }

                if (showEmojis)
                {
                    text += $" {item.Emoji} ({item.Name})";
                }
                else
                {
                    text += $" **{item.Name}**";
                }
            }

            return new EmbedBuilder().WithTitle("Inventory").WithDescription(text).WithColor((uint)await user.GetSetting("uiColor", 3287295));
        }
        catch (FormatException)
        {
            throw new ButtonValueError();
        }
    }
    private async Task InventoryCommand(SocketSlashCommand command)
    {
        string emoj = Tools.ShowEmojis(command, Settings.BotID(), _client).ToString();
        ComponentBuilder builder = new ComponentBuilder()
            .WithButton("<-- Left", "disabledL", disabled: true)
            .WithButton("Refresh", $"inv-0|{emoj}", ButtonStyle.Secondary)
            .WithButton("Right -->", $"inv-1|{emoj}");
        EmbedBuilder embay = await InventoryRawEmbed(command.User.Id, $"0|{emoj}");
        await command.RespondAsync(embed: embay.Build(), components: builder.Build());
    }
    private async Task Magik(SocketSlashCommand command)
    {
        User user = await GetUser(command.User.Id);
        ulong t = (ulong)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        uint timeoutFor = (await Tools.CharmEffect(["fasterCooldown", "positive"], _items, user)) switch
        {
            <= 30 => 12,
            <= 60 => 11,
            <= 90 => 10,
            <= 120 => 9,
            <= 160 => 8,
            <= 200 => 7,
            <= 250 => 6,
            <= 300 => 5,
            <= 350 => 4,
            <= 450 => 3,
            <= 550 => 2,
            <= 650 => 1,
            _ => 0
        };
        if (await user.OnCoolDown("magik", t, timeoutFor))
        {
            throw new Cooldown(user.CoolDowns["magik"]);
        }

        int power = await Tools.CharmEffect(["Magik", "Unlocker", "Positive"], _items, user);
        bool badInput = false;
        ulong targetID;
        try
        {
            targetID = ((SocketGuildUser)command.Data.Options.First().Value).Id;
            if (command.Data.Options.Last().Value.ToString() == "yes")
            {
                await user.SetSetting("magikID", targetID);
            }
        }
        catch (InvalidOperationException)
        {
            targetID = await user.GetSetting("magikID", user.ID);
        }
        catch (InvalidCastException)
        {
            targetID = await user.GetSetting("magikID", user.ID);
            badInput = true;
        }

        User target = await GetUser(targetID);
        if (targetID != user.ID)
        {
            power += 3;
        }

        power += _rand.Next(0, 2);
        if (targetID != await user.GetSetting("magikID", user.ID))
        {
            power += 1;
        }

        List<Tuple<string, int, int, int, int, int>> chances =
        [
            new Tuple<string, int, int, int, int, int>("You gained 8$diamonds.", 8, 0, 1, 0, 9),
            new Tuple<string, int, int, int, int, int>("You gained 1$emeralds.", 1, 0, 0, 0, 4),
            new Tuple<string, int, int, int, int, int>("Nothing happened", 0, 0, 0, 0, 7),
            new Tuple<string, int, int, int, int, int>("$target gained 10$diamonds", 0, 0, 10, 0, 6)
        ];
        if (power >= 1)
        {
            chances.Add(new Tuple<string, int, int, int, int, int>("$user and $target both gained 7$diamonds", 7, 0, 7,
                0, 10));
            chances.Add(new Tuple<string, int, int, int, int, int>("$user and $target both gained 8$diamonds", 8, 0, 8,
                0, 4));
        }

        if (power >= 3)
        {
            chances.Add(new Tuple<string, int, int, int, int, int>("$user and $target both gained 11$diamonds!", 11, 0,
                11, 0, 12));
            chances.Add(new Tuple<string, int, int, int, int, int>("$user gained 2$emeralds!", 2, 1, 0, 0, 8));
            chances.Add(new Tuple<string, int, int, int, int, int>("$target gained 2$emeralds", 0, 0, 2, 1, 6));
            chances.Add(new Tuple<string, int, int, int, int, int>($"$user gained {power}$emeralds!", power, 1, 0, 0,
                power));
            chances.Add(new Tuple<string, int, int, int, int, int>($"$target gained {power}$emeralds", 0, 0, power, 1,
                power));
        }

        if (power >= 4)
        {
            chances.Add(
                new Tuple<string, int, int, int, int, int>("$user and $target gained 1$emeralds", 1, 1, 1, 1, 15));
            chances.Add(
                new Tuple<string, int, int, int, int, int>("$user and $target gained 2$emeralds", 2, 1, 2, 1, 5));
            chances.Add(new Tuple<string, int, int, int, int, int>("$user and $target both gained 3$emeralds", 3, 1, 3,
                1, 2));
            chances.Add(new Tuple<string, int, int, int, int, int>("$user gained 1$sapphires", 1, 2, 0, 0, 1));
            chances.Add(new Tuple<string, int, int, int, int, int>("$target gained 1$sapphires", 0, 0, 1, 2, 1));
            chances.Add(new Tuple<string, int, int, int, int, int>("$user gained $user_wand", 0, 0, 0, 0, 1));
            chances.Add(new Tuple<string, int, int, int, int, int>("$target gained $target_wand", 0, 0, 0, 0, 1));
            chances.Add(new Tuple<string, int, int, int, int, int>($"$user gained {power}$emeralds!", power, 1, 0, 0,
                power));
            chances.Add(new Tuple<string, int, int, int, int, int>($"$target gained {power}$emeralds", 0, 0, power, 1,
                power));
        }

        if (power >= 5)
        {
            chances.Add(new Tuple<string, int, int, int, int, int>("$user gained 1$sapphires", 1, 2, 0, 0, 4));
            chances.Add(new Tuple<string, int, int, int, int, int>("$target gained 1$sapphires", 0, 0, 1, 2, 4));
        }

        if (power >= 6)
        {
            chances.Add(new Tuple<string, int, int, int, int, int>("$user and $target gained 80$diamonds", 80, 0, 80, 0,
                12));
            chances.Add(new Tuple<string, int, int, int, int, int>("$user gained 120$diamonds", 120, 0, 0, 0, 6));
            chances.Add(new Tuple<string, int, int, int, int, int>("$target gained 120$diamonds", 0, 0, 120, 0, 6));
            chances.Add(new Tuple<string, int, int, int, int, int>($"$user gained {power}$emeralds!", power, 1, 0, 0,
                power));
            chances.Add(new Tuple<string, int, int, int, int, int>($"$target gained {power}$emeralds", 0, 0, power, 1,
                power));
        }

        if (power >= 8)
        {
            chances.Add(
                new Tuple<string, int, int, int, int, int>("$user gained $user_wand\n$target gained $target_wand", 0, 0,
                    0, 0, 1));
        }

        if (power >= 9)
        {
            chances.Add(new Tuple<string, int, int, int, int, int>("$user gained $user_charm", 0, 0, 0, 0, 5));
            chances.Add(new Tuple<string, int, int, int, int, int>("$target gained $target_charm", 0, 0, 0, 0, 4));
            chances.Add(
                new Tuple<string, int, int, int, int, int>("$user gained $user_charm and $target gained $target_charm",
                    0, 0, 0, 0, 1));
        }

        if (power >= 10)
        {
            chances.Add(new Tuple<string, int, int, int, int, int>("$user gained $user_charm", 0, 0, 0, 0, 5));
            chances.Add(new Tuple<string, int, int, int, int, int>("$target gained $target_charm", 0, 0, 0, 0, 4));
            chances.Add(
                new Tuple<string, int, int, int, int, int>("$user gained $user_charm, $user_charm2", 0, 0, 0, 0, 2));
            chances.Add(new Tuple<string, int, int, int, int, int>("$target gained $target_charm, $target_charm2", 0, 0,
                0, 0, 1));
        }

        List<int> pickFrom = [];
        for (int i = 0; i < chances.Count; i++)
        {
            for (int j = 0; j < chances[i].Item6; j++)
            {
                pickFrom.Add(i);
            }
        }
        
        Tuple<string, int, int, int, int, int> tuple = chances[pickFrom[_rand.Next(pickFrom.Count)]];
        await user.Increase("commands", 1, false);
        await user.Increase("magik", 1, false);
        await user.Increase("earned", (int)(tuple.Item2*Math.Pow(10, tuple.Item3)), false);
        await user.Add(tuple.Item2, tuple.Item3);
        await target.Increase("earned", (int)(tuple.Item4 * Math.Pow(10, tuple.Item5)), false);
        await target.Add(tuple.Item4, tuple.Item5);
        string diamonds = " **diamonds**";
        string emeralds = " **emeralds**";
        string sapphires = " **sapphires**";
        bool emoji = Tools.ShowEmojis(command, Settings.BotID(), _client);
        if (emoji)
        {
            diamonds = _currency[0];
            emeralds = _currency[1];
            sapphires = _currency[2];
        }

        string toRespond = tuple.Item1
            .Replace("$diamonds", diamonds)
            .Replace("$emeralds", emeralds)
            .Replace("$sapphires", sapphires)
            .Replace("$user", $"<@{user.ID}>")
            .Replace("$target", $"<@{target.ID}>")
            .Replace("$user_charm", "`")
            .Replace("$user_charm2", "~")
            .Replace("$target_charm", "%")
            .Replace("$target_charm2", "¡")
            .Replace("$user_wand", "*")
            .Replace("$target_ward", "•");
        foreach (char c in toRespond)
        {
            switch (c)
            {
                case '`':
                {
                    int itemID = Tools.GetCharm(_itemLists, 0, 99);
                    await user.GainItem(itemID, 1);
                    toRespond = toRespond.Replace("`",
                        emoji ? $"1{_items[itemID].Emoji}" : $"1 **{_items[itemID].Name}**");
                    break;
                }
                case '~':
                {
                    int itemID = Tools.GetCharm(_itemLists, 0, 99);
                    await user.GainItem(itemID, 1);
                    toRespond = toRespond.Replace("~",
                        emoji ? $"1{_items[itemID].Emoji}" : $"1 **{_items[itemID].Name}**");
                    break;
                }
                case '%':
                {
                    int itemID = Tools.GetCharm(_itemLists, 0, 99);
                    await target.GainItem(itemID, 1);
                    toRespond = toRespond.Replace("%",
                        emoji ? $"1{_items[itemID].Emoji}" : $"1 **{_items[itemID].Name}**");
                    break;
                }
                case '¡':
                {
                    int itemID = Tools.GetCharm(_itemLists, 0, 199);
                    await target.GainItem(itemID, 1);
                    toRespond = toRespond.Replace("¡",
                        emoji ? $"1{_items[itemID].Emoji}" : $"1 **{_items[itemID].Name}**");
                    break;
                }
                case '*':
                {
                    await user.GainItem(10, 1);
                    toRespond = toRespond.Replace("¡", emoji ? $"1{_items[10].Emoji}" : $"1 **{_items[10].Name}**");
                    break;
                }
                case '•':
                {
                    await target.GainItem(10, 1);
                    toRespond = toRespond.Replace("¡", emoji ? $"1{_items[10].Emoji}" : $"1 **{_items[10].Name}**");
                    break;
                }
            }
        }

        EmbedBuilder embay = new EmbedBuilder()
            .WithTitle("Magik Time!")
            .WithDescription(toRespond)
            .WithFooter($"Magik by gemBOT: {power} power!")
            .WithColor(new Color((uint)(await user.GetSetting("magikRandomColor", 1) switch
            {
                0 => await user.GetSetting("magikColor", 13107400), _ => (ulong)_rand.Next(16777216)
            })));
        string topText = power switch
        {
            0 => "Good Magik!",
            1 => "Thin air Magiked!",
            2 => "Magik Power!",
            3 => "Double thin air Magiked!",
            4 => "Magik! Magik! Magik!",
            5 => "Sparks shoot out of your wand, and...",
            6 => "Magik sparks shoot out of your wand, and...",
            7 => "Okay, I know you did an elaborate setup to get this text.",
            8 => "A Magik ball shoots out of your wand, and...",
            9 => "A large Magik Ball shoots ouf of your Magik wand, and Magik happened...",
            10 => "AI AI AI AI AI AI - every big tech CEO ever, because they can't do Magik as good as you...",
            _ => "You're probably cheating at this point..."
        };
        await command.RespondAsync(topText, embed: embay.Build());
        if (badInput)
            await command.FollowupAsync("You can't set a target as default if you don't specify a target. Sorry.",
                ephemeral: true);
    }
    private async Task SetTutorial(SocketSlashCommand command)
    {
        IReadOnlyCollection<SocketSlashCommandDataOption> options = command.Data.Options;
        ushort i = (ushort)options.Count;
        switch (i)
        {
            case 0:
                CachedUser user1 = await GetUser(command.User.Id);
                if (user1.TutorialOn == null)
                {
                    await command.RespondAsync("You do not have an active tutorial");
                    return;
                }

                Tutorial tutorial = _tutorials[(int)user1.TutorialOn];
                Step step = tutorial.Steps[user1.TutorialPage];
                EmbedBuilder embay = new EmbedBuilder()
                    .WithTitle($"{tutorial.Name}: {step.Name}")
                    .WithDescription(step.Description);
                await command.RespondAsync("Here is your tutorial progress:", embed: embay.Build());
                break;
            case 1:
                CachedUser user2 = await GetUser(command.User.Id);
                long value = (long)options.First().Value;
                if (value == -1)
                {
                    user2.TutorialOn = null;
                    user2.TutorialPage = 0;
                    user2.TutorialProgress = null;
                    await command.RespondAsync("Deleted current tutorial!");
                }
                else
                {
                    user2.TutorialOn = Convert.ToInt32(value);
                    user2.TutorialPage = 0;
                    user2.TutorialProgress = null;
                    if (user2.TutorialOn == null)
                    {
                        await command.RespondAsync("You do not have an active tutorial");
                        return;
                    }

                    Tutorial tutorial2 = _tutorials[(int)user2.TutorialOn];
                    Step step2 = tutorial2.Steps[user2.TutorialPage];
                    EmbedBuilder embay2 = new EmbedBuilder()
                        .WithTitle($"{tutorial2.Name}: {step2.Name}")
                        .WithDescription(step2.Description);
                    await command.RespondAsync("Here is your tutorial progress:", embed: embay2.Build());
                }
                break;
        }
    }
    private async Task SetTheme(SocketSlashCommand command)
    {
        User user = await GetUser(command.User.Id);
        string themeName = "Theme not found";
        switch ((long)command.Data.Options.First().Value)
        {
            //default = 0
            case 0:
                await user.SetSetting("bankLeftStyle", 0);
                await user.SetSetting("bankRightStyle", 2);
                await user.SetSetting("bankShowRed", 1);
                await user.SetSetting("begRandom", 0);
                await user.SetSetting("begColor", 65525);
                await user.SetSetting("magikRandomColor", 1);
                await user.SetSetting("uiColor", 3287295);
                themeName = "default";
                break;
            //discord = 1
            case 1:
                await user.SetSetting("bankLeftStyle", 0);
                await user.SetSetting("bankRightStyle", 0);
                await user.SetSetting("bankShowRed", 0);
                await user.SetSetting("begRandom", 0);
                await user.SetSetting("begColor", 5793266);
                await user.SetSetting("magikRandomColor", 0);
                await user.SetSetting("magikColor", 6584831);
                await user.SetSetting("uiColor", 6566600);
                themeName = "Discord";
                break;
            //green = 2
            case 2:
                await user.SetSetting("bankLeftStyle", 1);
                await user.SetSetting("bankRightStyle", 1);
                await user.SetSetting("bankShowRed", 1);
                await user.SetSetting("begRandom", 0);
                await user.SetSetting("begColor", 3342180);
                await user.SetSetting("magikRandomColor", 1);
                await user.SetSetting("uiColor", 57640);
                themeName = "Green";
                break;
            //grey = 3
            case 3:
                await user.SetSetting("bankLeftStyle", 2);
                await user.SetSetting("bankRightStyle", 2);
                await user.SetSetting("bankShowRed", 1);
                await user.SetSetting("begRandom", 0);
                await user.SetSetting("begColor", 8224125);
                await user.SetSetting("magikRandomColor", 0);
                await user.SetSetting("magikColor", 0);
                await user.SetSetting("uiColor", 5260890);
                themeName = "Grey";
                break;
            //random = 4
            case 4:
                await user.SetSetting("bankLeftStyle", (ulong)_rand.Next(0, 3));
                await user.SetSetting("bankShowRed", (ulong)_rand.Next(0, 2));
                await user.SetSetting("begRandom", 1);
                await user.SetSetting("magikRandomColor", 1);
                await user.SetSetting("uiColor", (ulong)_rand.Next(16777216));
                await user.SetSetting("bankRightStyle", (ulong)_rand.Next(0, 3));
                themeName = "Random (the uiColor is randomized just once: now)";
                break;
            //OG = 5
            case 5:
                await user.SetSetting("bankLeftStyle", 0);
                await user.SetSetting("bankRightStyle", 0);
                await user.SetSetting("bankShowRed", 0);
                await user.SetSetting("begRandom", 0);
                await user.SetSetting("begColor", 65535);
                await user.SetSetting("magikRandomColor", 1);
                await user.SetSetting("uiColor", 65535);
                themeName = "OG Gembot";
                break;
        }

        await command.RespondAsync(embed: new EmbedBuilder().WithTitle("Theme Changed")
            .WithDescription($"Your gemBOT theme has been changed to {themeName}.")
            .WithColor((uint)await user.GetSetting("uiColor", 3287295)).Build());
    }
    private async Task<Tuple<EmbedBuilder, ComponentBuilder, string>> BankRaw(bool showEmojis, ulong userId)
    {
        bool compact = showEmojis;
        User user = await GetUser(userId);
        await user.Increase("commands", 1);
        string balanceText = await BalanceRaw(showEmojis, userId);
        string diamonds = _currency[0];
        string emeralds = _currency[1];
        string sapphires = _currency[2];
        string rubies = _currency[3];
        string ambers = _currency[4];
        if (!compact)
        {
            diamonds = " **diamonds**";
            emeralds = " **emeralds**";
            sapphires = " **sapphires**";
            rubies = " **rubies**";
            ambers = " **amber**";
        }

        int upgradePrice = 11;
        int downgradeReward = 9;
        if (await Tools.CharmEffect(["BetterBankTrades"], _items, user) >= 1)
        {
            upgradePrice = 10;
            downgradeReward = 10;
        }

        ButtonStyle leftMain = await user.GetSetting("bankLeftStyle", 0) switch
        {
            0 => ButtonStyle.Primary, 1 => ButtonStyle.Success, _ => ButtonStyle.Secondary
        };
        ButtonStyle rightMain = await user.GetSetting("bankRightStyle", 2) switch
        {
            0 => ButtonStyle.Primary, 1 => ButtonStyle.Success, _ => ButtonStyle.Secondary
        };
        ButtonStyle leftSecondary =
            await user.GetSetting("bankShowRed", 1) switch { 1 => ButtonStyle.Danger, _ => leftMain };
        ButtonStyle rightSecondary =
            await user.GetSetting("bankShowRed", 1) switch { 1 => ButtonStyle.Danger, _ => rightMain };
        ButtonStyle b1 = (user.Gems[0] >= 11) switch { true => leftMain, false => leftSecondary };
        ButtonStyle b2 = (user.Gems[1] >= 11) switch { true => leftMain, false => leftSecondary };
        ButtonStyle b3 = (user.Gems[2] >= 11) switch { true => leftMain, false => leftSecondary };
        ButtonStyle b4 = (user.Gems[3] >= 11) switch { true => leftMain, false => leftSecondary };
        ButtonStyle b5 = (user.Gems[1] >= 1) switch { true => rightMain, false => rightSecondary };
        ButtonStyle b6 = (user.Gems[2] >= 1) switch { true => rightMain, false => rightSecondary };
        ButtonStyle b7 = (user.Gems[3] >= 1) switch { true => rightMain, false => rightSecondary };
        ButtonStyle b8 = (user.Gems[4] >= 1) switch { true => rightMain, false => rightSecondary };
        string emoj = showEmojis switch { true => "yes", false => "no" };
        EmbedBuilder embay = new EmbedBuilder()
            .WithTitle("Open Trades!")
            .WithDescription("View all the trades you can use!")
            .AddField(new EmbedFieldBuilder()
                .WithName("Upgrades")
                .WithValue($"""
                            {upgradePrice}{diamonds} --> 1{emeralds}
                            {upgradePrice}{emeralds} --> 1{sapphires}
                            {upgradePrice}{sapphires} --> 1{rubies}
                            {upgradePrice}{rubies} --> 1{ambers}
                            """)
                .WithIsInline(true))
            .AddField(new EmbedFieldBuilder()
                .WithName("Downgrades")
                .WithValue($"""
                            1{emeralds} --> {downgradeReward}{diamonds}
                            1{sapphires} --> {downgradeReward}{emeralds}
                            1{rubies} --> {downgradeReward}{sapphires}
                            1{ambers} --> {downgradeReward}{rubies}
                            """)
                .WithIsInline(true))
            .WithColor(new Color((uint)await user.GetSetting("uiColor", 3287295)));
        ComponentBuilder button = new ComponentBuilder()
                .AddRow(new ActionRowBuilder()
                    .WithButton(new ButtonBuilder().WithLabel("1").WithStyle(b1).WithCustomId($"bank-0|{emoj}"))
                    .WithButton(new ButtonBuilder().WithLabel("5").WithStyle(b5).WithCustomId($"bank-4|{emoj}"))
                )
                .AddRow(new ActionRowBuilder()
                    .WithButton(new ButtonBuilder().WithLabel("2").WithStyle(b2).WithCustomId($"bank-1|{emoj}"))
                    .WithButton(new ButtonBuilder().WithLabel("6").WithStyle(b6).WithCustomId($"bank-5|{emoj}"))
                )
                .AddRow(new ActionRowBuilder()
                    .WithButton(new ButtonBuilder().WithLabel("3").WithStyle(b3).WithCustomId($"bank-2|{emoj}"))
                    .WithButton(new ButtonBuilder().WithLabel("7").WithStyle(b7).WithCustomId($"bank-6|{emoj}"))
                )
                .AddRow(new ActionRowBuilder()
                    .WithButton(new ButtonBuilder().WithLabel("4").WithStyle(b4).WithCustomId($"bank-3|{emoj}"))
                    .WithButton(new ButtonBuilder().WithLabel("8").WithStyle(b8).WithCustomId($"bank-7|{emoj}"))
                )
            ;
        return new Tuple<EmbedBuilder, ComponentBuilder, string>(embay, button, balanceText);
    }
    private async Task Bank(SocketSlashCommand command)
    {
        Tuple<EmbedBuilder, ComponentBuilder, string> results =
            await BankRaw(Tools.ShowEmojis(command, Settings.BotID(), _client), command.User.Id);
        await command.RespondAsync(results.Item3, ephemeral: false, embed: results.Item1.Build(),
            components: results.Item2.Build());
    }
    private async Task<Tuple<EmbedBuilder, List<string>>> QuestsEmbed(CachedUser user, bool showEmoji = true)
    {
        List<string> text = [];
        EmbedBuilder embay = new EmbedBuilder()
            .WithTitle("Your quests!")
            .WithDescription("View your daily quests!")
            .WithColor((uint)await user.User.GetSetting("uiColor", 3287295));
        user.User.UpdateDay(Tools.CurrentDay());
        if (_quests[0].Date < Tools.CurrentDay())
        {
            _quests = [];
            List<int> mapToQuests = [(int)Tools.CurrentDay()];
            foreach (List<DailyQuest> questSet in _allQuests)
            {
                int page = _rand.Next(questSet.Count);
                DailyQuest quest = questSet[page];
                quest.Date = Tools.CurrentDay();
                _quests.Add(quest);
                mapToQuests.Add(page);
            }
            await File.WriteAllTextAsync("Data/DailyQuests/DateQuestsMap.txt", JsonConvert.SerializeObject(mapToQuests));
        }
        bool isSmall = await user.User.GetSetting("smallProgress", 0) switch {0 => false, _ => true };
        for (int i = 0; i < _quests.Count; i++)
        {
            DailyQuest quest = _quests[i];
            
            string additionalInfo = $"""
                                     {quest.Description}
                                      > **Quest completed**!
                                      > {Tools.ProgressBar(1,1, !isSmall)}
                                     """;
            if (!user.User.DailyQuestsCompleted[i])
            {
                if (user.User.GetProgress(quest.Requirement) >= quest.Amount)
                {
                    user.User.DailyQuestsCompleted[i] = true;
                    switch (i)
                    {
                        case 0:
                            if (await Tools.CharmEffect(["CommonQuests"], _items, user) > 0)
                            {
                                await user.User.Add(120, 0);
                                await user.User.Increase("earned", 120);
                                string currency = showEmoji switch { true => _currency[0], false => " **diamonds**" };
                                text.Add($"<@{user.User.ID}> earned 120{currency}");
                            }
                            else
                            {
                                await user.User.Add(60, 0);
                                await user.User.Increase("earned", 60);
                                string currency = showEmoji switch { true => _currency[0], false => " **diamonds**" };
                                text.Add($"<@{user.User.ID}> earned 60{currency}");
                            }
                            break;
                        case 1:
                            if (await Tools.CharmEffect(["UncommonQuests"], _items, user) > 0)
                            {
                                await user.User.Add(20, 1);
                                await user.User.Increase("earned", 200);
                                int charmID = Tools.GetCharm(_itemLists, 0, 50, _rand);
                                await user.User.GainItem(charmID, 1);
                                string currency = showEmoji switch { true => _currency[1], false => " **emeralds**" };
                                string charm1 = showEmoji switch { true => _items[charmID].Emoji, false => $" **{_items[charmID].Name}**" };
                                text.Add($"<@{user.User.ID}> earned 20{currency} + 1{charm1}");
                            }
                            else
                            {
                                await user.User.Add(40, 1);
                                await user.User.Increase("earned", 400);
                                string currency = showEmoji switch { true => _currency[1], false => " **emeralds**" };
                                text.Add($"<@{user.User.ID}> earned 40{currency}");
                            }
                            break;
                        case 2:
                            if (await Tools.CharmEffect(["RareQuests"], _items, user) > 0)
                            {
                                await user.User.Add(19, 2);
                                await user.User.Increase("earned", 1900);
                                int charmID = Tools.GetCharm(_itemLists, 0, 32, _rand);
                                await user.User.GainItem(charmID, 1);
                                string currency = showEmoji switch { true => _currency[2], false => " **sapphires**" };
                                string charm1 = showEmoji switch { true => _items[charmID].Emoji, false => $" **{_items[charmID].Name}**" };
                                int charmID2 = Tools.GetCharm(_itemLists, 0, 22, _rand);
                                await user.User.GainItem(charmID2, 1);
                                string charm2 = showEmoji switch { true => _items[charmID2].Emoji, false => $" **{_items[charmID2].Name}**"};
                                text.Add($"<@{user.User.ID}> earned 19{currency} + 1{charm1} + 1{charm2}");
                            }
                            else
                            {
                                await user.User.Add(19, 2);
                                await user.User.Increase("earned", 1900);
                                int charmID = Tools.GetCharm(_itemLists, 0, 28, _rand);
                                await user.User.GainItem(charmID, 1);
                                string currency = showEmoji switch { true => _currency[2], false => " **sapphires**" };
                                string charm1 = showEmoji switch { true => _items[charmID].Emoji, false => $" **{_items[charmID].Name}**" };
                                text.Add($"<@{user.User.ID}> earned 19{currency} + 1{charm1}");
                            }
                            break;
                        case 3:
                            if (await Tools.CharmEffect(["EpikQuests"], _items, user) > 0)
                            {
                                await user.User.Add(5, 3);
                                await user.User.Increase("earned", 5000);
                                int charmID = Tools.GetCharm(_itemLists, 0, 9, _rand);
                                await user.User.GainItem(charmID, 1);
                                string currency = showEmoji switch { true => _currency[3], false => " **rubies**" };
                                string charm1 = showEmoji switch { true => _items[charmID].Emoji, false => $" **{_items[charmID].Name}**" };
                                int charmID2 = Tools.GetCharm(_itemLists, 1, 17, _rand);
                                await user.User.GainItem(charmID2, 1);
                                string charm2 = showEmoji switch { true => _items[charmID2].Emoji, false => $" **{_items[charmID2].Name}**"};
                                text.Add($"<@{user.User.ID}> earned 5{currency} + 1{charm1} + 1{charm2}");
                            }
                            else
                            {
                                await user.User.Add(3, 3);
                                await user.User.Increase("earned", 3000);
                                int charmID = Tools.GetCharm(_itemLists, 0, 9, _rand);
                                await user.User.GainItem(charmID, 1);
                                string currency = showEmoji switch { true => _currency[3], false => " **rubies**" };
                                string charm1 = showEmoji switch { true => _items[charmID].Emoji, false => $" **{_items[charmID].Name}**" };
                                text.Add($"<@{user.User.ID}> earned 3{currency} + 1{charm1}");
                            }
                            break;
                        case 4:
                            if (await Tools.CharmEffect(["LegendaryQuests"], _items, user) > 0)
                            {
                                await user.User.Add(1, 4);
                                await user.User.Increase("earned", 10000);
                                int charmID = Tools.GetCharm(_itemLists, 1, 9, _rand);
                                await user.User.GainItem(charmID, 1);
                                string currency = showEmoji switch { true => _currency[4], false => " **ambers**" };
                                string charm1 = showEmoji switch { true => _items[charmID].Emoji, false => $" **{_items[charmID].Name}**" };
                                int charmID2 = Tools.GetCharm(_itemLists, 1, 7, _rand);
                                await user.User.GainItem(charmID2, 1);
                                string charm2 = showEmoji switch { true => _items[charmID2].Emoji, false => $" **{_items[charmID2].Name}**"};
                                int charmID3 = Tools.GetCharm(_itemLists, 2, 17, _rand);
                                await user.User.GainItem(charmID3, 1);
                                string charm3 = showEmoji switch { true => _items[charmID3].Emoji, false => $" **{_items[charmID3].Name}**"};
                                text.Add($"<@{user.User.ID}> earned 1{currency} + 1{charm1} + 1{charm2} + 1{charm3}");
                            }
                            else
                            {
                                await user.User.Add(1, 4);
                                await user.User.Increase("earned", 10000);
                                int charmID = Tools.GetCharm(_itemLists, 1, 7, _rand);
                                await user.User.GainItem(charmID, 1);
                                string currency = showEmoji switch { true => _currency[4], false => " **ambers**" };
                                string charm1 = showEmoji switch { true => _items[charmID].Emoji, false => $" **{_items[charmID].Name}**" };
                                text.Add($"<@{user.User.ID}> earned 1{currency} + 1{charm1}");
                            }
                            break;
                    }
                }
                else
                {
                    uint done = (uint)user.User.GetProgress(quest.Requirement);
                    uint amount = quest.Amount;
                    additionalInfo = $"""
                                      {quest.Description}
                                       > {done}/{amount}
                                       > {Tools.ProgressBar((int)done, (int)amount, !isSmall)}
                                      """;
                }
            }
            embay.AddField(quest.Name, additionalInfo);
        }
        return new Tuple<EmbedBuilder, List<string>>(embay, text);
    }
    private async Task DailyQuests(SocketSlashCommand command)
    {
        for (int i= 0; i < (await Tools.CharmEffect(["betterAutoRefresh"], _items, await GetUser(command.User.Id))) switch {0 => 16, _ => (60*12)+1}; i++)
        {
            CachedUser cUser = await GetUser(command.User.Id);
            User user = cUser;
            Tuple<EmbedBuilder, List<string>> main = await QuestsEmbed(cUser,
                Tools.ShowEmojis(command, Settings.BotID(), _client));
            try{await command.RespondAsync("View your daily quests!", embed: main.Item1.Build());}
            catch (Exception ex) when (ex is  HttpException or TimeoutException or InvalidOperationException)
            {
                try
                {
                    await command.ModifyOriginalResponseAsync(properties =>
                    {
                        properties.Embed = main.Item1.Build();
                        properties.Content = "View your daily quests (auto-refresh)!";
                    });
                }
                catch (HttpException)
                {
                    return;
                }
            }
            foreach (string toSend in main.Item2)
            {
                await Task.Delay(500);
                await command.FollowupAsync(toSend);
            }
            await Task.Delay((await Tools.CharmEffect(["betterAutoRefresh"], _items, user)) switch {0 => 60000, _ => 4800});
        }
    }
    private async Task Give(SocketSlashCommand command)
    {
        bool gem = command.Data.Options.First().Name == "gems";
        if (gem)
        {
            List<SocketSlashCommandDataOption> temp = command.Data.Options.First().Options.ToList();
            IUser iUser = (IUser)temp[0].Value;
            ushort index = (ushort)(uint)(ulong)(long)temp[1].Value;
            int amount = (int)(long)temp[2].Value;
            User main = await GetUser(command.User.Id);
            User target = await GetUser(iUser.Id);
            if (main.Gems[index] < amount)
            {
                await command.RespondAsync("You can't afford this.", ephemeral: true);
                return;
            }
            if (main.ID == target.ID)
            {
                await command.RespondAsync("You can't gift gems to yourself", ephemeral: true);
                return;
            }
            await main.Add(-amount, index);
            await main.Increase("gifted", (int)(amount * Math.Pow(10, index)));
            await main.Increase("commands", 1);
            await target.Add(amount, index);
            bool emoj = Tools.ShowEmojis(command, Settings.BotID(), _client);
            EmbedBuilder embay = new EmbedBuilder()
                .WithTitle("Transaction successful")
                .WithFooter("GemBOT economy!")
                .WithColor((uint)await main.GetSetting("uiColor", 3287295))
                .WithDescription(
                    $"You have successfully transferred {amount}{(emoj switch { true => _currency, false => _currencyNoEmoji })[index]} from <@{main.ID}> to <@{target.ID}>");
            await command.RespondAsync($"Thank you for giving gems to <@{target.ID}>", embed: embay.Build());
        }
        else
        {
            List<SocketSlashCommandDataOption> temp = command.Data.Options.First().Options.ToList();
            IUser iUser = (IUser)temp[0].Value;
            int index = (int)(ulong)(long)temp[1].Value;
            int amount = (int)(long)temp[2].Value;
            User main = await GetUser(command.User.Id);
            User target = await GetUser(iUser.Id);
            if (main.Inventory[index] < amount)
            {
                await command.RespondAsync("You can't afford this.", ephemeral: true);
                return;
            }
            if (main.ID == target.ID)
            {
                await command.RespondAsync("You can't gift items to yourself", ephemeral: true);
                return;
            }
            await main.GainItem(index, -amount);
            await main.Increase("gifted", amount * _items[index].Value);
            await main.Increase("commands", 1);
            await target.GainItem(index, amount);
            bool emoj = Tools.ShowEmojis(command, Settings.BotID(), _client);
            EmbedBuilder embay = new EmbedBuilder()
                .WithTitle("Item Gifting successful")
                .WithFooter("GemBOT economy!")
                .WithColor((uint)await target.GetSetting("uiColor", 3287295))
                .WithDescription(
                    $"You have successfully gifted {amount}{emoj switch { true => _items[index].Emoji, false => $"  **{_items[index].Name}**" }} from <@{main.ID}> to <@{target.ID}>");
            await command.RespondAsync($"Thank you for giving items to <@{target.ID}>", embed: embay.Build());
        }
    }
    private async Task Play(SocketSlashCommand command)
    {
        User user = await GetUser(command.User.Id);
        if (await Tools.CharmEffect(["unlockPlay"], _items, user) <= 0)
        {
            await command.RespondAsync("You need to have a controller to play video games in gemBOT.", ephemeral: true);
            return;
        }
        int effect = await Tools.CharmEffect(["fasterCooldown", "positive"], _items, user);
        uint timeoutFor = effect switch
        {
            <= 0 => 45,
            <= 40 => 45-(uint)effect/4,
            <= 120 => 35-((uint)effect-40)/8,
            <= 240 => 25-((uint)effect-120)/12,
            <= 400 => 15-((uint)effect-240)/16,
            <= 500 => 5-((uint)effect-400)/20,
            _ => 0
        };
        if (await user.OnCoolDown("play", Tools.CurrentTime(), timeoutFor))
        {
            throw new Cooldown(user.CoolDowns["play"]);
        }
        user.IncreaseStat("play", 1);
        UInt128 power = user.GetStat("play");
        int roll = _rand.Next(20)+1;
        string text = $"gemBOT could not resolve text for power {power} and d20 roll {roll}";
        string header = "Play Video Games!";
        bool emoji = Tools.ShowEmojis(command, Settings.BotID(), _client);
        // ReSharper disable once StringLiteralTypo
        string diamonds = emoji switch { true => _currency[0], false => " DIAMONDZ" };
        // ReSharper disable once StringLiteralTypo
        string emeralds = emoji switch { true => _currency[1], false => " EMERALDZ" };
        // ReSharper disable once StringLiteralTypo
        string sapphires = emoji switch { true => _currency[2], false => " SAPPHIERS" };
        // ReSharper disable once StringLiteralTypo
        string rubies = emoji switch { true => _currency[3], false => " RUBEES" };
        // ReSharper disable once StringLiteralTypo
        string ambers = emoji switch { true => _currency[0], false => " AMBURRRRRZ" };
        string[] cur = [diamonds, emeralds, sapphires, rubies, ambers];
        string footer = roll switch
        {
            1 => "TIP: Run grinding commands for gems",
            2 => "TIP: Charms with positive effects have positive effects",
            3 => "TIP: Daily quests contain rewards",
            4 => "TIP: `/magik` is better with a wand",
            5 => "TIP: Play more, get better at the game, earn more",
            6 => "TIP: These tips can randomly appear!",
            7 => $"You have played video games {power} times.",
            8 => "TIP: limited-time drops are limited in amount, not time",
            9 => $"You are too addicted to video games! You played a whopping {power} times",
            10 => "Did you know?: GemBOT was coded with code",
            11 => "TIP: At the bottom of `/play` useless tips appear, make sure to read them instead of grinding.",
            12 => "TIP: You can give items gems to friends with `/give`",
            13 => "TIP: Try using `/play` to play video games. It can get you some nifty rewards!",
            14 => "TIP: You can't run gemBOT commands when it is offline",
            15 => "Did you know?: You rolled a 15 on your d20 to randomly determine the effect.",
            16 => "Did you know?: a727 has a discord account",
            17 => "Did you know?: These represent a loading screen tip",
            18 => "TIP: The more gems you have, the more gems you have.",
            19 => "TIP: Items can be obtained",
            20 => $"You rolled a {roll} and you have played video games {power} times.\nDid you know?: This references the roll variable despite it only occuring when roll variable is set to 20 in the switch statement.",
            _ => "TIP: Did you know? This should never happen. If this does happen, please report this to a727 and you might get half a diamond\n(okay, fine, he'll have to give you one diamond) ||PRONOUNS REVEAL!!!||"
        };
        if (power <= 0)
        {
            await command.RespondAsync("glitched gemBOT", ephemeral: true);
            return;
        }
        if (power <= 1)
        {
            header = "Welcome to play";
            text = "This is your first time playing, so you had to do the tutorial. You didn't earn anything";
        }
        else if (power <= 2)
        {
            text = "You're still learning, but you got a single diamond";
            await user.Add(1, 0);
            await user.Increase("earned", 1);
        }
        else if (power <= 4)
        {
            text = "You're still learning, but you got two diamonds";
            await user.Add(2, 0, false);
            await user.Increase("earned", 2);
        }
        else if (power <= 8)
        {
            switch (roll)
            {
                case 1 or 2 or 3 or 4:
                    text = "You forgot to stream, so you gained nothing";
                    break;
                case 5 or 6 or 7 or 8 or 9:
                    text = "Your stream got 0 viewers because a very popular stream was going on in the moment.";
                    break;
                case 10 or 11 or 12:
                    text = "You got 3 viewers, but you weren't able to make any bank from your ad break."
                        + "\nAnd, you lost all your viewers because it was a crypto scam ad.";
                    break;
                case 13 or 14 or 15 or 16:
                    text = "You got 3 viewers, and were able to make 1 diamond from the ad break."
                       + "\nHowever, the ad was another hero wars fake ad, and you lost all your viewers.";
                    await user.Add(1, 0);
                    await user.Increase("earned", 1);
                    break;
                case 17 or 18 or 19:
                    text = "You got 5 viewers! You did an ad break! +2 diamonds!";
                    await user.Add(2, 0);
                    await user.Increase("earned", 2);
                    break;
                case 20:
                    text = "You got 12 viewers! Some guy donated 3 diamonds!";
                    await user.Add(3, 0);
                    await user.Increase("earned", 3);
                    break;
            }
        }
        else if (power <= 16)
        {
            switch (roll)
            {
                case 1 or 2 or 3:
                    text = "You forgot to stream, so you gained nothing";
                    break;
                case 4 or 5 or 6:
                    text = "Your stream got 0 viewers because a very popular stream was going on in the moment.";
                    break;
                case 7 or 8:
                    text = "You got 3 viewers, but you weren't able to make any bank from your ad break."
                           + "\nAnd, you lost all your viewers because it was a crypto scam ad.";
                    break;
                case 9 or 10 or 11 or 12 or 13 or 14:
                    text = "You got 3 viewers, and were able to make 1 diamond from the ad break."
                           + "\nHowever, the ad was another hero wars fake ad, and you lost all your viewers.";
                    await user.Add(1, 0);
                    await user.Increase("earned", 1);
                    break;
                case 15 or 16 or 17:
                    text = "You got 5 viewers! You did an ad break! +2 diamonds!";
                    await user.Add(2, 0);
                    await user.Increase("earned", 2);
                    break;
                case 18 or 19:
                    text = "You got 12 viewers! Some guy donated 3 diamonds!";
                    await user.Add(3, 0);
                    await user.Increase("earned", 3);
                    break;
                case 20:
                    text = $"You somehow got 2{emeralds}";
                    await user.Add(2, 1);
                    await user.Increase("earned", 20);
                    break;
            }
        }
        else if (power <= 32)
        {
             switch (roll)
            {
                case 1 or 2 or 3:
                    text = "You forgot to stream, so you gained nothing";
                    break;
                case 4 or 5:
                    text = "Your stream got 0 viewers because a very popular stream was going on in the moment.";
                    break;
                case 6:
                    text = "You got 3 viewers, but you weren't able to make any bank from your ad break."
                        + "\nAnd, you lost all your viewers because it was a crypto scam ad.";
                    break;
                case 7 or 8 or 9:
                    text = "You got 3 viewers, and were able to make 1 diamond from the ad break."
                       + "\nHowever, the ad was another hero wars fake ad, and you lost all your viewers.";
                    await user.Add(1, 0);
                    await user.Increase("earned", 1);
                    break;
                case 10 or 11:
                    text = "You got 5 viewers! You did an ad break! +2 diamonds!";
                    await user.Add(2, 0);
                    await user.Increase("earned", 2);
                    break;
                case 12:
                    text = "You got 12 viewers! Some guy donated 3 diamonds!";
                    await user.Add(3, 0);
                    await user.Increase("earned", 3);
                    break;
                case 13:
                    text = "You got 12 viewers! two guys donated 4 diamonds each!";
                    await user.Add(8, 0);
                    await user.Increase("earned", 8);
                    break;
                case 14:
                    text = $"You got 12 viewers! Somebody donated 20{diamonds}";
                    await user.Add(20, 0);
                    await user.Increase("earned", 20);
                    break;
                case 15 or 16:
                    text = $"You got 25 viewers! You ran an ad break for 2{emeralds}";
                    await user.Add(2, 1);
                    await user.Increase("earned", 20);
                    break;
                case 17 or 18 or 19:
                    text = $"You got 12 viewers! You ran 3 ad breaks, with one emerald each. Plus, somebody donated 2{diamonds}!"
                        + $"\nIn total:\n> 3{emeralds}\n> 2{diamonds}";
                    await user.Add(3, 1);
                    await user.Add(2, 0);
                    await user.Increase("earned", 32);
                    break;
                case 20:
                    text = $"You didn't live stream, but you got something rare in the game and sold it for 4{emeralds}";
                    await user.Add(4, 1);
                    await user.Increase("earned", 40);
                    break;
            }
        }
        else if (power <= 64)
        {
            switch (roll)
            {
                case 0:
                    text = "NOTHING!!! NADA!";
                    break;
                case 1:
                    text = $"You got only ONE viewer, so logic implies you get 000{diamonds}";
                    break;
                case 2:
                    text = "**Stream Stats**:" +
                           "\n> 0 viewers" +
                           "\n> 2 rng" +
                           $"\n> 0 ad breaks (0 diamonds each): 0{diamonds}" +
                           $"\n> 0 viewers donated a total of 0{diamonds}";
                    break;
                default:
                    int viewers = (int)power + 2*roll;
                    int adBreaks = 3;
                    int adbBeakMoney = viewers/25;
                    int adBreakValue = 0;
                    int donaters = 0;
                    int donated = 0;
                    for (int i = 0; i < viewers; i++)
                    {
                        int chance = _rand.Next(roll);
                        int max = _rand.Next(40);
                        if (chance <= max) continue;
                        donaters += 1;
                        donated += chance;
                    }
                    text = "**Stream Stats**:" +
                           $"\n> {viewers} viewers" +
                           $"\n> {roll} rng" +
                           $"\n> {adBreaks} ad breaks ({adbBeakMoney} diamonds each): {adBreaks*adbBeakMoney}{diamonds}" +
                           $"\n> {donaters} viewers donated a total of {donated}{diamonds}";
                    await user.Add(adBreaks * adbBeakMoney, adBreakValue, false);
                    await user.Increase("earned", adBreaks * adbBeakMoney * (int)Math.Pow(10, adBreakValue), false);
                    await user.Add(donated, 0, false);
                    await user.Increase("earned", donated);
                    break;
            }
        }
        else if (power <= 128)
        {
            int viewers = (int)power*2 + 5*roll;
            int adBreaks = 4;
            int adbBeakMoney = viewers/20;
            int adBreakValue = 0;
            int donaters = 0;
            int donated = 0;
            for (int i = 0; i < viewers; i++)
            {
                int chance = _rand.Next(roll);
                int max = _rand.Next(39);
                if (chance <= max) continue;
                donaters += 1;
                donated += chance;
            }
            text = "**Stream Stats**:" +
                   $"\n> {viewers} viewers" +
                   $"\n> {roll} rng" +
                   $"\n> {adBreaks} ad breaks ({adbBeakMoney} diamonds each): {adBreaks*adbBeakMoney}{diamonds}" +
                   $"\n> {donaters} viewers donated a total of {donated}{diamonds}";
            await user.Add(adBreaks * adbBeakMoney, adBreakValue, false);
            await user.Increase("earned", adBreaks * adbBeakMoney * (int)Math.Pow(10, adBreakValue), false);
            await user.Add(donated, 0, false);
            await user.Increase("earned", donated);
        }
        else if (power <= 256)
        {
            int viewers = (int)power*3 + 6*roll;
            const int adBreaks = 4;
            int adbBeakMoney = viewers/200;
            const int adBreakValue = 1;
            int donaters = 0;
            int donated = 0;
            for (int i = 0; i < viewers; i++)
            {
                int chance = _rand.Next(roll);
                int max = _rand.Next(39);
                if (chance <= max) continue;
                donaters += 1;
                donated += chance;
            }
            text = "**Stream Stats**:" +
                   $"\n> {viewers} viewers" +
                   $"\n> {roll} rng" +
                   $"\n> {adBreaks} ad breaks ({adbBeakMoney} diamonds each): {adBreaks*adbBeakMoney}{cur[adBreakValue]}" +
                   $"\n> {donaters} viewers donated a total of {donated}{diamonds}";
            await user.Add(adBreaks * adbBeakMoney, adBreakValue, false);
            await user.Increase("earned", adBreaks * adbBeakMoney * (int)Math.Pow(10, adBreakValue), false);
            await user.Add(donated, 0, false);
            await user.Increase("earned", donated);
        }
        else if (power <= 512)
        {
            int viewers = (int)power*5 + 6*roll;
            const int adBreaks = 4;
            int adbBeakMoney = viewers/190;
            const int adBreakValue = 1;
            int donaters = 0;
            int donated = 0;
            for (int i = 0; i < viewers; i++)
            {
                int chance = _rand.Next(roll);
                int max = _rand.Next(38);
                if (chance <= max) continue;
                donaters += 1;
                donated += chance;
            }
            text = "**Stream Stats**:" +
                   $"\n> {viewers} viewers" +
                   $"\n> {roll} rng" +
                   $"\n> {adBreaks} ad breaks ({adbBeakMoney} diamonds each): {adBreaks*adbBeakMoney}{cur[adBreakValue]}" +
                   $"\n> {donaters} viewers donated a total of {donated}{diamonds}";
            await user.Add(adBreaks * adbBeakMoney, adBreakValue, false);
            await user.Increase("earned", adBreaks * adbBeakMoney * (int)Math.Pow(10, adBreakValue), false);
            await user.Add(donated, 0, false);
            await user.Increase("earned", donated);
        }
        else if (power <= 1024)
        {
            int viewers = (int)power*7 + 100*roll;
            const int adBreaks = 4;
            int adbBeakMoney = viewers/190;
            const int adBreakValue = 1;
            int donaters = 0;
            int donated = 0;
            for (int i = 0; i < viewers; i++)
            {
                int chance = _rand.Next(roll);
                int max = _rand.Next(45);
                if (chance <= max) continue;
                donaters += 1;
                donated += chance;
            }
            text = "**Stream Stats**:" +
                   $"\n> {viewers} viewers" +
                   $"\n> 2{roll} rng" +
                   $"\n> {adBreaks} ad breaks ({adbBeakMoney} diamonds each): {adBreaks*adbBeakMoney}{cur[adBreakValue]}" +
                   $"\n> {donaters} viewers donated a total of {donated}{diamonds}";
            await user.Add(adBreaks * adbBeakMoney, adBreakValue, false);
            await user.Increase("earned", adBreaks * adbBeakMoney * (int)Math.Pow(10, adBreakValue), false);
            await user.Add(donated, 0, false);
            await user.Increase("earned", donated);
        }
        else if (power <= 2048)
        {
            int viewers = (int)power*10 + 100*roll;
            const int adBreaks = 5;
            int adbBeakMoney = viewers/1650;
            const int adBreakValue = 2;
            int donaters = 0;
            int donated = 0;
            for (int i = 0; i < viewers; i++)
            {
                int chance = _rand.Next(roll);
                int max = _rand.Next(35);
                if (chance <= max) continue;
                donaters += 1;
                donated += chance;
            }
            text = "**Stream Stats**:" +
                   $"\n> {viewers} viewers" +
                   $"\n> {roll} rng" +
                   $"\n> {adBreaks} ad breaks ({adbBeakMoney} diamonds each): {adBreaks*adbBeakMoney}{cur[adBreakValue]}" +
                   $"\n> {donaters} viewers donated a total of {donated}{diamonds}";
            await user.Add(adBreaks * adbBeakMoney, adBreakValue, false);
            await user.Increase("earned", adBreaks * adbBeakMoney * (int)Math.Pow(10, adBreakValue), false);
            await user.Add(donated, 0, false);
            await user.Increase("earned", donated);
        }
        else
        {
            int viewers = (int)power*12 + 500*roll;
            const int adBreaks = 5;
            int adbBeakMoney = viewers/1200;
            const int adBreakValue = 2;
            int donaters = 0;
            int donated = 0;
            for (int i = 0; i < viewers; i++)
            {
                int chance = _rand.Next(roll);
                int max = _rand.Next(35);
                if (chance <= max) continue;
                donaters += 1;
                donated += chance;
            }
            text = "**Stream Stats**:" +
                   $"\n> {viewers} viewers" +
                   $"\n> {roll} rng" +
                   $"\n> {adBreaks} ad breaks ({adbBeakMoney} diamonds each): {adBreaks*adbBeakMoney}{cur[adBreakValue]}" +
                   $"\n> {donaters} viewers donated a total of {donated}{diamonds}";
            await user.Add(adBreaks * adbBeakMoney, adBreakValue, false);
            await user.Increase("earned", adBreaks * adbBeakMoney * (int)Math.Pow(10, adBreakValue), false);
            await user.Add(donated, 0, false);
            await user.Increase("earned", donated);
        }
        EmbedBuilder embay = new EmbedBuilder()
            .WithTitle(header)
            .WithDescription(text)
            .WithFooter(footer)
            .WithColor((uint)await user.GetSetting("uiColor", 3287295));
        await command.RespondAsync(embed: embay.Build());
    }
    private async Task<Tuple<bool, string, Embed, MessageComponent, string>> MineRaw(ulong userId, bool showEmojis)
    {
        User user = await GetUser(userId);
        if (!showEmojis)
        {
            EmbedBuilder embayError = new EmbedBuilder()
                .WithTitle("Missing Permissions")
                .WithDescription($"GemBot needs one of the following permissions to run this command.")
                .AddField("Use External Emojis", "This version of the bot has not been configured with application emojis (if this is false, please notify the owner of the bot to check settings.cs), so it requires \"Use External Emojis\" Permission.")
                .WithFooter("Please grant gemBOT these permissions or use user apps.");
            return new Tuple<bool, string, Embed, MessageComponent, string>(false, "Missing Use External Emojis Permission", embayError.Build(), new ComponentBuilder().WithButton("Error", "error", ButtonStyle.Danger, disabled: true).Build(), "no debug info");
        }
        int mineMonth = DateTime.Today.Month;
        if (await user.GetData("MineMonth", mineMonth) != mineMonth)
        {
            await user.SetData("MineMonth", mineMonth, false);
            await user.SetData("mineY", 0, false);
            await user.SetData("mineX", _rand.Next(_mineData.MineChunks.Count * 20), false);
            await user.SetData("mining", 0);
        }
        string description = await user.GetData("mining", 0) switch
        {
            0 => "Click any button to start mining!",
            1 => "You are currently mining a block.",
            _ => "Your data doesn't seem to be saved correctly."
        };
        int mineY = await user.GetData("mineY", 0);
        int top = mineY-2;
        if (top <= 0)
        {
            top = 0;
        }
        int mineX = await user.GetData("mineX", _rand.Next(_mineData.MineChunks.Count * 20));
        int left = mineX-2;
        if (left <= 0)
        {
            left = 0;
        }

        if (left + 4 >= _mineData.MineChunks.Count * 20)
        {
            left = _mineData.MineChunks.Count * 20 - 5;
        }
        string blocks = "Block ids:";
        if (top + 5 > 250)
        {
            top = 246;
        }
        ComponentBuilder buttons = new ComponentBuilder();
        for (int i = 0; i < 5; i++)
        {
            blocks += "\n >";
            ActionRowBuilder row = new ActionRowBuilder();
            for (int j = 0; j < 5; j++)
            {
                ButtonBuilder button = new ButtonBuilder();
                int y = top + i;
                int x = left + j;
                button.WithCustomId($"mine-{x}|{y}|{await user.GetData("MineMonth", mineMonth)}");
                blocks += " ";
                blocks += button.CustomId;
                MineBlock block = _mineData.GetBlock(x, y);
                button.WithDisabled(Math.Abs(mineX - x) + Math.Abs(mineY - y) != 1);
                if (x == mineX && y == mineY)
                {
                    button.WithStyle(ButtonStyle.Secondary)
                        .WithEmote(Emote.Parse("<:you:1287157766871580833>"));
                    row.AddComponent(button.Build());
                    continue;
                }
                switch (block.Type)
                {
                    case BlockType.Air:
                        button.WithStyle(ButtonStyle.Secondary)
                            .WithEmote(Emote.Parse("<:air:1287157701905743903>"));
                        break;
                    case BlockType.Stone:
                        button.WithStyle(ButtonStyle.Primary)
                            .WithEmote(Emote.Parse("<:stone:1287086951215796346>"));
                        break;
                    case BlockType.Diamonds:
                        button.WithStyle(ButtonStyle.Success)
                            .WithEmote(Emote.Parse(_currency[0]));
                        break;
                    case BlockType.Emeralds:
                        button.WithStyle(ButtonStyle.Success)
                            .WithEmote(Emote.Parse(_currency[1]));
                        break;
                    case BlockType.Sapphires:
                        button.WithStyle(ButtonStyle.Success)
                            .WithEmote(Emote.Parse(_currency[2]));
                        break;
                    case BlockType.Rubies:
                        button.WithStyle(ButtonStyle.Success)
                            .WithEmote(Emote.Parse(_currency[3]));
                        break;
                    case BlockType.Amber:
                        button.WithStyle(ButtonStyle.Success)
                            .WithEmote(Emote.Parse(_currency[4]));
                        break;
                    default:
                        button.WithStyle(ButtonStyle.Danger)
                            .WithEmote(Emote.Parse("<:stone:1287086951215796346>"))
                            .WithLabel("Glitched Block");
                        break;
                }
                row.AddComponent(button.Build());
            }
            buttons.AddRow(row);
        }
        Embed embay = new EmbedBuilder()
            .WithTitle("Mine!")
            .WithDescription(description)
            .WithFooter("Click any button to mine that location!")
            .Build();
        return new Tuple<bool, string, Embed, MessageComponent, string>(true, "Mine", embay, buttons.Build(), blocks);
    }
    private async Task Mine(SocketSlashCommand command)
    {
        Tuple<bool, string, Embed, MessageComponent, string> result = await MineRaw(command.User.Id, Tools.ShowEmojis(command, Settings.BotID(), _client));
        if (!result.Item1)
        {
            await command.RespondAsync(result.Item2, embed: result.Item3);
            return;
        }
        await command.RespondAsync(result.Item2, embed: result.Item3, components: result.Item4);
    }
    private async Task<Tuple<string, Embed, MessageComponent>> FurnacesRaw(ulong userId, bool emoj)
    {
        User user = await GetUser(userId);
        int craftSlots = await Tools.CharmEffect(["extra_craft_slots"], _items, user) + FurnaceConst;
        await user.CheckFurnaces(craftSlots);
        EmbedBuilder embay = new EmbedBuilder()
            .WithTitle("Craft!")
            .WithDescription("View your current crafting slots, and click on the buttons to craft new things!");
        foreach (CraftingRecipe.Furnace furnace in user.Furnaces)
        {
            if (!furnace.Crafting)
            {
                embay.AddField("Available Slot", " > Press a button below to begin crafting something in this slot.");
            }
            else
            {
                string itemText = emoj switch { true => _items[furnace.NextItem].Emoji, false => " *" + _items[furnace.NextItem].Name + "*" };
                embay.AddField(_items[furnace.NextItem].Name,
                    $" > Crafting: {furnace.Amount}{itemText}\n > Progress: {Tools.ProgressBar((int)(furnace.TimeRequired - furnace.TimeLeft), (int)furnace.TimeRequired)}");
            }
        }

        ComponentBuilder components = new ComponentBuilder();
        ActionRowBuilder refresh = new ActionRowBuilder()
            .WithButton("Refresh", "craft-home", ButtonStyle.Secondary);
        ActionRowBuilder favorites = new ActionRowBuilder()
            .WithButton("Favorites", "craft-page|fav", ButtonStyle.Success);
        ActionRowBuilder recents = new ActionRowBuilder()
            .WithButton("Recents", "craft-page|recent", ButtonStyle.Success);
        ActionRowBuilder craftable = new ActionRowBuilder()
            .WithButton("Max Craftable", "craft-page|craftable", ButtonStyle.Success);
        List<int> faveRecipes = await user.GetListData("craft_favorites");
        for (int i = 0; i < 4 && i < faveRecipes.Count; i++)
        {
            Item crafted = _items[_craftingRecipes[faveRecipes[i]].ItemCrafted];
            favorites.WithButton(new ButtonBuilder().WithLabel(crafted.Name).WithStyle(ButtonStyle.Primary)
                .WithEmote(Emote.Parse(crafted.Emoji)).WithCustomId($"craft-recipe|{faveRecipes[i]}|f"));
        }
        List<int> recentRecipes = await user.GetListData("craft_recents");
        for (int i = 0; i < 4 && i < recentRecipes.Count; i++)
        {
            Item crafted = _items[_craftingRecipes[recentRecipes[i]].ItemCrafted];
            recents.WithButton(new ButtonBuilder()
                .WithLabel(crafted.Name)
                .WithStyle(ButtonStyle.Primary)
                .WithEmote(Emote.Parse(crafted.Emoji))
                .WithCustomId($"craft-recipe|{recentRecipes[i]}|r"));
        }
        List<CraftingRecipe> craftableRecipes = _craftingRecipes.ToArray().ToList();
        craftableRecipes.Sort((recipeA, recipeB) => (recipeA.AmountCraftable(user) - recipeB.AmountCraftable(user)));
        for (int i = 0; i < 4 && i < craftableRecipes.Count; i++)
        {
            Item crafted = _items[craftableRecipes[i].ItemCrafted];
            craftable.WithButton(new ButtonBuilder()
                .WithLabel(crafted.Name)
                .WithStyle(ButtonStyle.Primary)
                .WithEmote(Emote.Parse(crafted.Emoji))
                .WithCustomId($"craft-recipe|{craftableRecipes[i].ID}|c"));
        }

        components.AddRow(refresh);
        components.AddRow(favorites);
        components.AddRow(recents);
        components.AddRow(craftable);
        return new Tuple<string, Embed, MessageComponent>("Crafting text:", embay.Build(), components.Build());
    }
    private async Task Craft(SocketSlashCommand command)
    {
        Tuple<string, Embed, MessageComponent> furnaces = await FurnacesRaw(command.User.Id, Tools.ShowEmojis(command, Settings.BotID(), _client));
        await command.RespondAsync(furnaces.Item1, embed: furnaces.Item2, components: furnaces.Item3);
    }
    
    
    private async Task InventoryButton(SocketMessageComponent component, string settings)
    {
        ulong id = component.User.Id;
        try
        {
            await ((User)await GetUser(id)).ItemAmount(_items.Count - 1);
        }
        catch
        {
            await Tools.UserCreator(id);
        }

        ulong oID = component.Message.Interaction.User.Id;
        if (id != oID)
        {
            await component.RespondAsync("This is not your inventory. You cannot click any buttons", ephemeral: true);
            return;
        }

        string[] settings2 = settings.Split("|");
        EmbedBuilder embay = await InventoryRawEmbed(id, settings);
        int page = int.Parse(settings.Split("|")[0]);
        ComponentBuilder builder = new ComponentBuilder();
        if (page > 0)
        {
            builder.WithButton("<-- Left", $"inv-{int.Parse(settings2[0]) - 1}|{settings2[1]}");
        }
        else
        {
            builder.WithButton("<-- Left", "disabledL", disabled: true);
        }

        builder.WithButton("Refresh", $"inv-{settings}", ButtonStyle.Secondary);
        if (page < (int)Math.Ceiling(_items.Count / 8.0) - 1)
        {
            builder.WithButton("Right -->", $"inv-{int.Parse(settings2[0]) + 1}|{settings2[1]}");
        }
        else
        {
            builder.WithButton("Right -->", "disabledR", disabled: true);
        }

        await component.UpdateAsync(Modify);
        return;

        void Modify(MessageProperties properties)
        {
            properties.Embed = embay.Build();
            properties.Components = builder.Build();
        }
    }
    private async Task BankButton(SocketMessageComponent component, string settings)
    {
        //step 1: complete transaction
        User user = await GetUser(component.User.Id);
        string[] temp = settings.Split("|");
        int transaction = int.Parse(temp[0]);
        string showEmojisText = temp[1];
        bool showEmojis = showEmojisText switch { "no" => false, "yes" => true, _ => false };
        int bottomValue = transaction % 4;
        int type = transaction / 4;
        int upgradeFor = 11;
        int downgradeTo = 9;
        if (await Tools.CharmEffect(["BetterBankTrades"], _items, user) >= 1)
        {
            upgradeFor = 10;
            downgradeTo = 10;
        }

        switch (type)
        {
            case 0:
                if (user.Gems[bottomValue] < upgradeFor)
                {
                    await component.RespondAsync($"You can't afford this trade. This message will auto-delete in <t:{Tools.CurrentTime()+8}:R>", ephemeral: true);
                    await Task.Delay(7800);
                    await component.DeleteOriginalResponseAsync();
                    return;
                }

                await user.Add(-1 * upgradeFor, bottomValue, false);
                await user.Add(1, bottomValue + 1);
                break;
            case 1:
                if (user.Gems[bottomValue + 1] < 1)
                {
                    await component.RespondAsync($"You can't afford this trade. This message will auto-delete in <t:{Tools.CurrentTime()+8}:R>", ephemeral: true);
                    await Task.Delay(7800);
                    await component.DeleteOriginalResponseAsync();
                    return;
                }

                await user.Add(-1, bottomValue + 1, false);
                await user.Add(downgradeTo, bottomValue);
                break;
        }

        //step 2: refresh bank if original user, otherwise send message.
        if (component.Message.Interaction.User.Id != user.ID)
        {
            await component.RespondAsync(await BalanceRaw(showEmojis, component.User.Id, "The transaction was completed successfully!\n**Your balance**:") + $"\n||This message will auto-delete in <t:{Tools.CurrentTime() + 15}:R>||");
            await Task.Delay(1490);
            await component.DeleteOriginalResponseAsync();
        }
        else
        {
            Tuple<EmbedBuilder, ComponentBuilder, string> dat = await BankRaw(showEmojis, user.ID);
            await component.UpdateAsync(properties =>
            {
                properties.Content = dat.Item3;
                properties.Embed = dat.Item1.Build();
                properties.Components = dat.Item2.Build();
            });
        }
    }
    private async Task MineButton(SocketMessageComponent component, string settings)
    {
        string[] temp = settings.Split("|");
        if (temp.Length < 3) throw new ButtonValueError();
        User user = await GetUser(component.User.Id);
        if (component.Message.Interaction.User.Id != user.ID)
        {
            await component.RespondAsync("This is not your mine page! Use /mine to see your options!", ephemeral: true);
            return;
        }

        if (temp[2] != _mineData.MonthName)
        {
            await component.RespondAsync("This button is old; from last month. Updating data...");
            Tuple<bool, string, Embed, MessageComponent, string> result = await MineRaw(user.ID, true);
            await component.Message.ModifyAsync((properties) =>
            {
                properties.Content = result.Item2;
                properties.Embed = result.Item3;
                properties.Components = result.Item4;
            });
            return;

        }
        int x = 0;
        int y = 0;
        try
        {
            x = int.Parse(temp[0]);
            y = int.Parse(temp[1]);
        }
        catch (FormatException)
        {
            throw new ButtonValueError();
        }
        MineBlock block = _mineData.GetBlock(x, y);
        if (await user.GetData("mining", 0) == 1)
        {
            block = _mineData.GetBlock(await user.GetData("miningAtX", x, false), await user.GetData("miningAtY", y, false));
            string progressBar = Tools.ProgressBar((int)block.Durability - (block.Left ?? block.GetLeft()), (int)block.Durability);
            if ((block.Left ?? block.GetLeft()) == 0)
            {
                await user.SetData("mining", 0);
            }
            string etl = "Calculating... (you should not see this)";
            int secondsLeft = (block.Left ?? block.GetLeft())/(5+ await Tools.CharmEffect(["minePower"], _items, user));
            switch (secondsLeft)
            {
                case > 86400:
                {
                    int days = secondsLeft / 86400;
                    secondsLeft -= days * 86400;
                    int hours = secondsLeft / 3600;
                    secondsLeft -= hours * 3600;
                    int minutes = secondsLeft / 60;
                    secondsLeft -= minutes * 60;
                    etl = $"{days} days, {hours} hours, {minutes} minutes, and {secondsLeft} seconds left";
                    break;
                }
                case > 3600:
                {
                    int hours = secondsLeft / 3600;
                    secondsLeft -= hours * 3600;
                    int minutes = secondsLeft / 60;
                    secondsLeft -= minutes * 60;
                    etl = $"{hours} hours, {minutes} minutes, and {secondsLeft} left";
                    break;
                }
                case >= 60:
                {
                    int minutes = secondsLeft / 60;
                    secondsLeft -= minutes * 60;
                    string secondsProgress = secondsLeft < 10 ? '0' + secondsLeft.ToString() : secondsLeft.ToString();
                    etl = $"{minutes}:{secondsProgress} (minutes:seconds) left";
                    break;
                }
                case >= 1:
                    etl = $"{secondsLeft} seconds remaining!";
                    break;
                default:
                    etl = "Almost none!";
                    break;
            }
            await component.RespondAsync($"You are already mining; you cannot start mining another block.\n > **Progress**: {progressBar}\n > **Estimated Time Remaining**: {etl}", ephemeral: true);
            if (await Tools.CharmEffect(["betterAutoRefresh"], _items, user) == 0) {return;}
            block = _mineData.GetBlock(await user.GetData("miningAtX", x, false), await user.GetData("miningAtY", y, false));
            secondsLeft = block.GetLeft();
            while (secondsLeft >= 0)
            {
                await Task.Delay(10 * secondsLeft);
                user = await GetUser(component.User.Id);
                block = _mineData.GetBlock(await user.GetData("miningAtX", x, false), await user.GetData("miningAtY", y, false)); 
                etl = "Calculating... (you should not see this)";
                secondsLeft = (block.Left ?? block.GetLeft())/(5+ await Tools.CharmEffect(["minePower"], _items, user));
                switch (secondsLeft)
                {
                    case > 86400:
                    {
                        int days = secondsLeft / 86400;
                        secondsLeft -= days * 86400;
                        int hours = secondsLeft / 3600;
                        secondsLeft -= hours * 3600;
                        int minutes = secondsLeft / 60;
                        secondsLeft -= minutes * 60;
                        etl = $"{days} days, {hours} hours, {minutes} minutes, and {secondsLeft} seconds left";
                        break;
                    }
                    case > 3600:
                    {
                        int hours = secondsLeft / 3600;
                        secondsLeft -= hours * 3600;
                        int minutes = secondsLeft / 60;
                        secondsLeft -= minutes * 60;
                        etl = $"{hours} hours, {minutes} minutes, and {secondsLeft} left";
                        break;
                    }
                    case >= 60:
                    {
                        int minutes = secondsLeft / 60;
                        secondsLeft -= minutes * 60;
                        etl = $"{minutes}:{secondsLeft} (minutes:seconds) left";
                        break;
                    }
                    case >= 1:
                        etl = $"{secondsLeft} seconds remaining!";
                        break;
                    default:
                        etl = "Almost none!";
                        break;
                }
                await component.ModifyOriginalResponseAsync((properties) =>
                {
                    properties.Content = $"You are already mining; you cannot start mining another block.\n > **Progress**: {progressBar}\n > **Estimated Time Remaining**: {etl}";
                });
            }
            return;
        }
        if (block.Type == BlockType.Air)
        {
            await user.SetData("mineX", x, false);
            await user.SetData("mineY", y);
            Tuple<bool, string, Embed, MessageComponent, string> result = await MineRaw(user.ID, true);
            await component.UpdateAsync((properties) => { 
                properties.Embed = result.Item3;
                properties.Content = result.Item2;
                properties.Components = result.Item4;
            });
        }
        else
        {
            try
            {
                block.Mine(component.User.Id, 0); //Mining with power will be determined during MineTick();
                await _mineData.GetChunk(x / 20).Save(x / 20);
                await user.SetData("miningAtX", x, false);
                await user.SetData("miningAtY", y, false);
                await user.SetData("mining", 1);
                await component.RespondAsync("Successfully started mining this block!", ephemeral: true);
                if (await Tools.CharmEffect(["betterAutoRefresh", "mineAutoRefresh"], _items, user) == 0) {return;}
                await Task.Delay(1000*((int)(block.Durability / (await Tools.CharmEffect(["minePower"], _items, user) + 5))));
                Tuple<bool, string, Embed, MessageComponent, string> result = await MineRaw(user.ID, true);
                await component.Message.ModifyAsync((properties) =>
                {
                    properties.Content = result.Item2;
                    properties.Embed = result.Item3;
                    properties.Components = result.Item4;
                });
            }
            catch (SomeoneElseIsMiningError)
            {
                await component.RespondAsync("Someone else is mining this block. Try another block.", ephemeral: true);
            }
            catch (BlockIsAirError)
            {
                await component.RespondAsync("This block is air in some cases but not others. Check save data/code.", ephemeral: true);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                await component.RespondAsync("```\ne\n```", ephemeral:true);
            }
        }
    }
    private async Task CraftButton(SocketMessageComponent component, string settings)
    {
        string[] args = settings.Split("|");
        ulong id = component.User.Id;
        CachedUser user = await GetUser(id);
        ulong oID = component.Message.Interaction.User.Id;
        if (id != oID)
        {
            await component.RespondAsync("This is not your craft page. You cannot click any buttons", ephemeral: true);
            return;
        }
        switch (args[0])
        {
            case "page":
                if (args.Length < 2)
                {
                    throw new ButtonValueError();
                }
                switch (args[1])
                {
                    case "fav":
                        List<int> faveRecipes = await user.User.GetListData("craft_favorites");
                        MessageComponent faveButtons = PageLogic(faveRecipes);
                        await component.UpdateAsync((MessageProperties properties) =>
                        {
                            properties.Components = faveButtons;
                        });
                        break;
                    case "recent":
                        List<int> recentRecipes = await user.User.GetListData("craft_recents");
                        MessageComponent recentButtons = PageLogic(recentRecipes);
                        await component.UpdateAsync((MessageProperties properties) =>
                        {
                            properties.Components = recentButtons;
                        });
                        break;
                    case "profit":
                        List<int> profitRecipes = new List<int>();
                        for (int i = 0; i < _craftingRecipes.Count; i++)
                        {
                            CraftingRecipe recipeP = _craftingRecipes[i];
                            if (recipeP.AmountCraftable(user) > 0)
                            {
                                profitRecipes.Add(i);
                            }
                        }
                        profitRecipes.Sort((int a, int b) =>
                        {
                            CraftingRecipe recipeA = _craftingRecipes[a];
                            CraftingRecipe recipeB = _craftingRecipes[b];
                            return recipeA.CompareRecipeProfit(recipeB, _items);
                        });
                        MessageComponent profitButtons = PageLogic(profitRecipes);
                        await component.UpdateAsync((MessageProperties properties) =>
                        {
                            properties.Components = profitButtons;
                        });
                        break;
                    case "craftable":
                        List<int> craftableRecipes = [];
                        for (int i = 0; i < _craftingRecipes.Count; i++)
                        {
                            craftableRecipes.Add(i);
                        }
                        craftableRecipes.Sort((a, b) => (_craftingRecipes[a].AmountCraftable(user) - _craftingRecipes[b].AmountCraftable(user)));
                        MessageComponent craftableButtons = PageLogic(craftableRecipes);
                        await component.UpdateAsync((MessageProperties properties) =>
                        {
                            properties.Components = craftableButtons;
                        });
                        break;
                    default:
                        throw new ButtonValueError();
                }
                break;
            case "recipe":
                if (args.Length < 2) throw new ButtonValueError();
                CraftingRecipe recipe = _craftingRecipes[int.Parse(args[1])];
                EmbedBuilder embay = new EmbedBuilder();
                Item crafted = _items[recipe.ItemCrafted];
                Emote emoj = Emote.Parse(crafted.Emoji);
                int craftable = recipe.AmountCraftable(user);
                embay.WithTitle($"Crafting Recipe {recipe.ID}");
                embay.AddField("Details", recipe.ToString(_items));
                ActionRowBuilder topRow = new ActionRowBuilder()
                    .WithButton("home", "craft-home", ButtonStyle.Secondary);
                if ((await user.User.GetListData("craft_favorites")).Contains(recipe.ID))
                    topRow.WithButton("un-favorite", $"craft-fav|{recipe.ID}|n", ButtonStyle.Danger);
                else topRow.WithButton("favorite", $"craft-fav|{recipe.ID}|y", ButtonStyle.Success);
                ActionRowBuilder craftRow = new ActionRowBuilder()
                    .WithButton("x1", $"craft-craft|{recipe.ID}|1", ButtonStyle.Primary, emoj, disabled: craftable < 1)
                    .WithButton("x5", $"craft-craft|{recipe.ID}|5", ButtonStyle.Primary, emoj, disabled: craftable < 5)
                    .WithButton("x10", $"craft-craft|{recipe.ID}|10", ButtonStyle.Primary, emoj, disabled: craftable < 10)
                    .WithButton("x40", $"craft-craft|{recipe.ID}|40", ButtonStyle.Primary, emoj, disabled: craftable < 40)
                    .WithButton($"x{craftable}", $"craft-craft|{recipe.ID}|{craftable}|c", ButtonStyle.Primary, emoj, disabled: craftable <= 0);
                Embed realEmbed = embay.Build();
                MessageComponent recipeComponent = new ComponentBuilder().AddRow(topRow).AddRow(craftRow).Build();
                await component.UpdateAsync((properties) =>
                {
                    properties.Embed = realEmbed;
                    properties.Components = recipeComponent;
                });
                break;
            case "home":
                Tuple<string, Embed, MessageComponent> dat = await FurnacesRaw(id, true);
                await component.UpdateAsync((properties) =>
                {
                    properties.Content = dat.Item1;
                    properties.Embed = dat.Item2;
                    properties.Components = dat.Item3;
                });
                break;
            case "craft":
                if (args.Length < 3) throw new ButtonValueError();
                try
                {
                    user.NextCrafting ??= new List<Tuple<int, int>>();
                    user.NextCrafting.Add(new Tuple<int, int>(int.Parse(args[1]), int.Parse(args[2])));
                }
                catch (FormatException)
                {
                    throw new ButtonValueError();
                }
                CraftingRecipe recipeCrafted = _craftingRecipes[int.Parse(args[1])];
                Item itemCrafted = _items[recipeCrafted.ItemCrafted];
                await component.RespondAsync($"Successfully started crafting {int.Parse(args[2])*recipeCrafted.AmountCrafted}x{itemCrafted.Emoji}", ephemeral: true);
                break;
            case "fav":
                if (args.Length < 3) throw new ButtonValueError();
                if (!int.TryParse(args[1], out int recipeID)) throw new ButtonValueError();
                List<int> favorites = await user.User.GetListData("craft_favorites");
                switch (args[2])
                {
                    case "y":
                        if (favorites.Contains(recipeID))
                        {
                            await component.RespondAsync($"Crafting recipe {recipeID} is already favorited", ephemeral:true);
                            return;
                        }
                        favorites.Add(recipeID);
                        await user.User.SetListData("craft_favorites", favorites);
                        await component.RespondAsync($"Crafting recipe {recipeID} successfully favorited!", ephemeral:true);
                        break;
                    case "n":
                        if (!favorites.Contains(recipeID))
                        {
                            await component.RespondAsync($"Crafting recipe {recipeID} is already not favorited", ephemeral:true);
                            return;
                        }
                        favorites.Remove(recipeID);
                        await user.User.SetListData("craft_favorites", favorites);
                        await component.RespondAsync($"Crafting recipe {recipeID} successfully un-favorited!", ephemeral:true);
                        break;
                    default:
                        throw new ButtonValueError();
                }
                break;
            default:
                throw new ButtonValueError();
        }
        return;

        MessageComponent PageLogic(List<int> recipeIDs)
        {
            int maximum = 1 + (recipeIDs.Count)/ 5;
            if (maximum > 5) maximum = 5;
            ActionRowBuilder[] rows = new ActionRowBuilder[maximum];
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = new ActionRowBuilder();
            }
            rows[0].WithButton(
                new ButtonBuilder()
                    .WithLabel("home")
                    .WithStyle(ButtonStyle.Secondary)
                    .WithCustomId("craft-home")
                    );
            foreach (var t in recipeIDs)
            {
                CraftingRecipe recipe = _craftingRecipes[t];
                Item itemCrafted = _items[recipe.ItemCrafted];
                foreach (ActionRowBuilder row in rows)
                {
                    if (row.Components.Count >= 5) continue;
                    row.WithButton(
                        new ButtonBuilder()
                            .WithLabel(itemCrafted.Name)
                            .WithEmote(Emote.Parse(itemCrafted.Emoji))
                            .WithStyle(ButtonStyle.Primary)
                            .WithCustomId($"craft-recipe|{t}"));
                }
            }
            ComponentBuilder toReturn = new ComponentBuilder();
            foreach (ActionRowBuilder row in rows)
            {
                toReturn.AddRow(row);
            }
            return toReturn.Build();
        }
        
    }
    private async Task WorkButton(SocketMessageComponent component, string settings)
    {
        string[] args = settings.Split("|");
        if (args.Length < 3) throw new ButtonValueError();
        ulong id = component.User.Id;
        CachedUser user = await GetUser(id);
        ulong oID = component.Message.Interaction.User.Id;
        if (id != oID)
        {
            await component.RespondAsync("This is not your work page. You cannot click any buttons", ephemeral: true);
            return;
        }
        if (!int.TryParse(args[1], out int workID)) throw new ButtonValueError();
        if (workID != user.LastWork)
        {
            EmbedBuilder wrongIdEmbay = new EmbedBuilder()
                .WithTitle("Old Work")
                .WithDescription("This button is outdated.");
            await component.UpdateAsync((properties) => { properties.Embed = wrongIdEmbay.Build(); });
            return;
        }
        user.LastWork = 0;
        int mn = 10 + await Tools.CharmEffect(["WorkMin", "Work", "GrindMin", "Grind", "Positive"], _items, user);
        int mx = 16 + await Tools.CharmEffect(["WorkMax", "Work", "GrindMax", "Grind", "Positive"], _items, user);
        int amnt = _rand.Next(mn, mx);
        switch (args[0])
        {
            case "success":
                await user.User.Add(amnt, 1, false);
                string text = $"You gained {amnt} **Emeralds**.";
                if (args[3] == "True")
                {
                    text = $"You gained {amnt}{_currency[1]}!!!";
                }
                await user.User.Increase("commands",1, false);
                await user.User.Increase("earned", amnt*10, false);
                await user.User.Increase("workSuccess", 1, false);
                await user.User.Increase("work", 1);
                List<string> workChoices = _dataLists["WorkEffect"];
                Embed embay = new EmbedBuilder()
                    .WithTitle(text)
                    .WithDescription(workChoices[_rand.Next(0, workChoices.Count)])
                    .WithColor(new Color(50, 255, 100))
                    .Build();
                await component.UpdateAsync((properties) =>
                {
                    properties.Embed = embay;
                    properties.Components = null;
                });
                break;
            case "failure":
                amnt = _rand.Next(1, amnt);
                await user.User.Add(amnt, 1, false);
                string textFail = $"You gained {amnt} **Emeralds**.";
                if (args[3] == "true")
                {
                    text = $"You gained {amnt}{_currency[1]}!!!";
                }
                await user.User.Increase("commands",1, false);
                await user.User.Increase("earned", amnt*10, false);
                await user.User.Increase("workSuccess", 1, false);
                await user.User.Increase("work", 1);
                Embed embayFail = new EmbedBuilder()
                    .WithTitle(textFail)
                    .WithDescription("you failed!")
                    .WithColor(new Color(255, 0, 0))
                    .Build();
                await component.UpdateAsync((properties) =>
                {
                    properties.Embed = embayFail;
                    properties.Components = null;
                });
                break;
            default:
                throw new ButtonValueError();
        }
    }
    private Task ButtonHandlerSetup(SocketMessageComponent component)
    {
        _ = Task.Run(() => Task.FromResult(ButtonHandler(component)));
        return Task.CompletedTask;
    }
    private async Task ButtonHandler(SocketMessageComponent component)
    {
        string[] realID = component.Data.CustomId.Split("-");
        try
        {
            switch (realID[0])
            {
                case "inventory" or "inv":
                    await InventoryButton(component, realID[1]);
                    break;
                case "bankExchange" or "bank":
                    await BankButton(component, realID[1]);
                    break;
                case "mine":
                    await MineButton(component, realID[1]);
                    break;
                case "craft":
                    await CraftButton(component, realID[1]);
                    break;
                case "work":
                    await WorkButton(component, realID[1]);
                    break;
                default:
                    await component.RespondAsync(
                        $"**Button type not found.**\n > `Button of id {realID[0]} and options {realID[1]} was not able to be executed because id {realID[0]} was not found.`",
                        ephemeral: true);
                    break;
            }
        }
        catch (ButtonValueError)
        {
            await component.RespondAsync($"An internal error due to button definition prevented this button to be handled. \n > `Button of id {realID[0]} was found, but arguments {realID[1]} were not written correctly`\n**This usually happens when the button you clicked was old. Please run the command that gave you the button again and try again**", ephemeral: true);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            var embay = new EmbedBuilder()
                .WithTitle("Error")
                .WithAuthor(component.User)
                .WithColor(255, 0, 0)
                .AddField("Your command generated an error", $"**Full Details**: `{e}`");
            await component.RespondAsync(embed: embay.Build());
        }
    }
    
    
    private Task TextMessageHandlerSetup(SocketMessage message)
    {
        _ = Task.Run(() => Task.FromResult(TextMessageHandler(message)));
        return Task.CompletedTask;
    }
    private async Task TextMessageHandler(SocketMessage socketMessage)
    {
        if (!Settings.OwnerIDs().Contains(socketMessage.Author.Id))
        {
            return;
        }
        string message = socketMessage.ToString();
        if (message.Length == 0)
        {
            return;
        }
        string[] command;
        if (message.StartsWith($"<@{Settings.BotID()}>$"))
        {
            command = message.Split($"<@{Settings.BotID()}>$")[1].Split(' ');
        }
        else
        {
            return;
        }
        try
        {
            switch (command[0])
            {
                case "reset":
                    await ResetCommands(socketMessage);
                    break;
                case "reload":
                    await GetItems();
                    await socketMessage.Channel.SendMessageAsync("Re-loaded items and lists!");
                    break;
                case "item":
                    await SetItem(socketMessage);
                    break;
                case "all_items":
                    await AllItemsText(socketMessage);
                    break;
                case "add_item":
                    await AddItem(socketMessage);
                    break;
                case "charm":
                    await SetCharm(socketMessage);
                    break;
                case "add_charm":
                    await AddCharm(socketMessage);
                    break;
                case "iList":
                    await AddItemList(socketMessage);
                    break;
                case "dList":
                    await AddDataList(socketMessage);
                    break;
                case "color":
                    await ColorToValue(socketMessage);
                    break;
                case "add_quest":
                    await AddQuest(socketMessage);
                    break;
                case "quest":
                    await ModifyQuest(socketMessage);
                    break;
                case "money":
                    await CreateMoney(socketMessage);
                    break;
                case "give":
                    await CreateItems(socketMessage);
                    break;
                case "help":
                    await TextHelp(socketMessage);
                    break;
                case "add_recipe":
                    await CreateCraftingRecipe(socketMessage);
                    break;
                case "edit_recipe":
                    await EditCraftingRecipe(socketMessage);
                    break;
                default:
                    await socketMessage.Channel.SendMessageAsync("This command was not found");
                    break;
            }
            try { await socketMessage.DeleteAsync(); }
            catch (HttpException)
            {
                RestUserMessage msg = await socketMessage.Channel.SendMessageAsync("Managing gemBOT? Give it the Manage Messages permission!");
                await Task.Delay(2500);
                await msg.DeleteAsync();
            }
        }
        catch (Exception e)
        {
            await socketMessage.Channel.SendMessageAsync(e.ToString());
        }
    }
    private async Task ResetCommands(SocketMessage message)
    {
        await message.Channel.SendMessageAsync("Deleting old commands (1/6)...");
        await _client.SetGameAsync("resting commands...");
        string[] names = ["item", "balance", "beg", "stats", "inventory", "work", "magik", "hep", "start", "bank", "theme", "setting", "quests", "give", "mine", "play", "help", "craft"];
        List<string> existingCommands = new List<string>();
        string[] forceUpdateCommands = message.Content.Split(" ")[1..];
        bool forceUpdateAll = forceUpdateCommands.Contains("all");
        Console.WriteLine(forceUpdateAll);
        IReadOnlyCollection<RestGlobalCommand>? commands = await  _client.Rest.GetGlobalApplicationCommands();
        foreach (RestGlobalCommand command in commands)
        {
            if (names.Contains(command.Name)) continue;
            Console.WriteLine($"Command {command.Name} deleted!");
            await command.DeleteAsync();
        }
        await message.Channel.SendMessageAsync("Building new commands (2/6)...");
        SlashCommandBuilder itemInfo = new SlashCommandBuilder()
            .WithName("item")
            .WithDescription("Get information about an item.")
            .WithIntegrationTypes([ApplicationIntegrationType.GuildInstall])
            .AddOption("item", ApplicationCommandOptionType.Integer,
                "The item id of the item you would like to access.", true);
        SlashCommandBuilder balance = new SlashCommandBuilder()
            .WithName("balance")
            .WithDescription("Find out your balance")
            .WithIntegrationTypes([ApplicationIntegrationType.GuildInstall, ApplicationIntegrationType.UserInstall])
            .AddOption(new SlashCommandOptionBuilder()
                .WithType(ApplicationCommandOptionType.Boolean)
                .WithName("private")
                .WithDescription(
                    "Whether to keep your balance private (ephemeral message) or show it to everyone (normal message).")
                .WithRequired(false)
            );
        SlashCommandBuilder stats = new SlashCommandBuilder()
            .WithName("stats")
            .WithDescription("Figure out your stats");
        SlashCommandBuilder beg = new SlashCommandBuilder()
            .WithName("beg")
            .WithIntegrationTypes([ApplicationIntegrationType.GuildInstall, ApplicationIntegrationType.UserInstall])
            .WithDescription("Beg for diamonds!")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("ephemeral")
                .WithType(ApplicationCommandOptionType.Integer)
                .WithDescription("Show beg results to everyone? This option auto-saves as default.")
                .WithRequired(false)
                .AddChoice("Yeah - Private Beg!", 1)
                .AddChoice("Make it private - it's cleaner", 1)
                .AddChoice("No - everyone needs to see this!", 0)
                .AddChoice("No - I prefer real messages", 0)
                .AddChoice("Get rid of that blue tint around my begs!", 0)
                .AddChoice("I really like the blue around my beg responses", 1));
        SlashCommandBuilder inventory = new SlashCommandBuilder()
            .WithName("inventory")
            .WithIntegrationTypes([ApplicationIntegrationType.GuildInstall, ApplicationIntegrationType.UserInstall])
            .WithDescription("View your inventory!");
        SlashCommandBuilder work = new SlashCommandBuilder()
            .WithName("work")
            .WithIntegrationTypes([ApplicationIntegrationType.GuildInstall, ApplicationIntegrationType.UserInstall])
            .WithDescription("Work for emeralds every 5 minutes!");
        SlashCommandBuilder magik = new SlashCommandBuilder()
            .WithName("magik")
            .WithIntegrationTypes([ApplicationIntegrationType.GuildInstall, ApplicationIntegrationType.UserInstall])
            .WithDescription("Magik up gems (target a friend to get better results)")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("target")
                .WithDescription("Who else should get gems?")
                .WithType(ApplicationCommandOptionType.User)
            )
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("save")
                .WithDescription("Save the specified target and set it as default?")
                .WithType(ApplicationCommandOptionType.String)
                .AddChoice("yes", "yes")
                .AddChoice("no", "no")
            );
        SlashCommandBuilder help = new SlashCommandBuilder()
            .WithName("help")
            .WithDescription("Get a list of the main gemBOT commands!");
        SlashCommandBuilder start = new SlashCommandBuilder()
            .WithName("start")
            .WithDescription("View the tutorial and start using gemBOT")
            .AddOption(new SlashCommandOptionBuilder()
                .WithType(ApplicationCommandOptionType.Integer)
                .WithName("tutorial")
                .WithDescription("Which tutorial would you like to start")
                .AddChoice("Default", 0)
                .AddChoice("Get Started", 0)
                .AddChoice("Grinding", 1)
                .AddChoice("Remove Current Tutorial", -1)
                .WithRequired(false)
            );
        SlashCommandBuilder bank = new SlashCommandBuilder()
            .WithName("bank")
            .WithIntegrationTypes([ApplicationIntegrationType.GuildInstall, ApplicationIntegrationType.UserInstall])
            .WithDescription("Convert your currency to other values");
        SlashCommandBuilder theme = new SlashCommandBuilder()
            .WithName("theme")
            .WithIntegrationTypes([ApplicationIntegrationType.GuildInstall, ApplicationIntegrationType.UserInstall])
            .WithDescription("Choose a theme to show gemBOT in")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("theme")
                .WithDescription("The theme to set it to.")
                .WithRequired(true)
                .WithType(ApplicationCommandOptionType.Integer)
                .AddChoice("Default", 0)
                .AddChoice("Discord", 1)
                .AddChoice("Green", 2)
                .AddChoice("Grey", 3)
                .AddChoice("Gray", 3)
                .AddChoice("Random", 4)
                .AddChoice("OG GemBOT", 5));
        SlashCommandBuilder quests = new SlashCommandBuilder()
            .WithName("quests")
            .WithIntegrationTypes([ApplicationIntegrationType.GuildInstall, ApplicationIntegrationType.UserInstall])
            .WithDescription("View your daily quests! 5 new quests every day!");
        SlashCommandBuilder give = new SlashCommandBuilder()
            .WithName("give")
            .WithDescription("Give gems/items to another user!")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("gems")
                .WithDescription("Give gems to another user! Used to be called donate!")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("user")
                    .WithDescription("Select a user to give gems to")
                    .WithType(ApplicationCommandOptionType.User)
                    .WithRequired(true)
                )
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("value")
                    .WithType(ApplicationCommandOptionType.Integer)
                    .WithDescription("How many gems to add?")
                    .AddChoice("Diamonds", 0)
                    .AddChoice("Emeralds", 1)
                    .AddChoice("Sapphires", 2)
                    .AddChoice("Rubies", 3)
                    .AddChoice("Ambers", 4)
                    .WithRequired(true)
                )
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("amount")
                    .WithDescription("How many gems would you like to give the user?")
                    .WithType(ApplicationCommandOptionType.Integer)
                    .WithRequired(true)
                )
            )
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("items")
                .WithDescription("Give items to another user! Used to be just /give!")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("user")
                    .WithDescription("Select a user to give gems to")
                    .WithType(ApplicationCommandOptionType.User)
                    .WithRequired(true)
                )
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("item")
                    .WithType(ApplicationCommandOptionType.Integer)
                    .WithDescription("The item ID of the item to give. Use /item <id> to get info about an item.")
                    .WithRequired(true)
                )
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("amount")
                    .WithDescription("How many gems would you like to give the user?")
                    .WithType(ApplicationCommandOptionType.Integer)
                    .WithRequired(true)
                )
            );
        SlashCommandBuilder mine = new SlashCommandBuilder()
            .WithName("mine")
            .WithDescription("Use your pickaxes to mine")
            .WithIntegrationTypes([ApplicationIntegrationType.GuildInstall, ApplicationIntegrationType.UserInstall]);
        SlashCommandBuilder play = new SlashCommandBuilder()
            .WithName("play")
            .WithDescription("Use a controller to play video games")
            .WithIntegrationTypes([ApplicationIntegrationType.GuildInstall, ApplicationIntegrationType.UserInstall]);
        SlashCommandBuilder settings = new SlashCommandBuilder()
            .WithName("setting")
            .WithDescription("Set a setting")
            .AddOption(new SlashCommandOptionBuilder().WithName("theme")
                .WithDescription("Settings related to your theme")
                .WithType(ApplicationCommandOptionType.SubCommandGroup)
                .AddOption(new SlashCommandOptionBuilder().WithName("bank_left")
                    .WithDescription("Set the color of the left row - upgrades of the bank.")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption(new SlashCommandOptionBuilder().WithName("color")
                        .WithType(ApplicationCommandOptionType.Integer)
                        .WithDescription("What color should the left row of the bank be?")
                        .AddChoice("blue", 0)
                        .AddChoice("green", 1)
                        .AddChoice("grey", 2)
                        .WithRequired(true)
                    )
                )
                .AddOption(new SlashCommandOptionBuilder().WithName("bank_right")
                    .WithDescription("Set the color of the right row - downgrades of the bank.")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption(new SlashCommandOptionBuilder().WithName("color")
                        .WithType(ApplicationCommandOptionType.Integer)
                        .WithDescription("What color should the right row of the bank be?")
                        .WithRequired(true)
                        .AddChoice("blue", 0)
                        .AddChoice("green", 1)
                        .AddChoice("grey", 2)
                    )
                )
                .AddOption(new SlashCommandOptionBuilder().WithName("bank_show_red")
                    .WithDescription("Whether to show red when you can't afford a button in the bank.")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption(new SlashCommandOptionBuilder().WithName("value")
                        .WithDescription("Set the value of setting: bankShowRed")
                        .WithRequired(true)
                        .WithType(ApplicationCommandOptionType.Boolean)
                    )
                )
                .AddOption(new SlashCommandOptionBuilder().WithName("beg_randomize_color")
                    .WithDescription("Randomize the color of the embed when you beg?")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption(new SlashCommandOptionBuilder().WithName("value")
                        .WithDescription("Set the value of setting: begRandom")
                        .WithRequired(true)
                        .WithType(ApplicationCommandOptionType.Boolean)
                    )
                )
                .AddOption(new SlashCommandOptionBuilder().WithName("beg_color")
                    .WithDescription("The color of the embed of `/beg`")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption(new SlashCommandOptionBuilder().WithName("red")
                        .WithDescription("Set the Red value of the R, G, B triplet.")
                        .WithType(ApplicationCommandOptionType.Integer)
                        .WithRequired(true)
                        .WithMaxValue(255)
                        .WithMinValue(0)
                    )
                    .AddOption(new SlashCommandOptionBuilder().WithName("green")
                        .WithDescription("Set the Green value of the R, G, B triplet.")
                        .WithType(ApplicationCommandOptionType.Integer)
                        .WithRequired(true)
                        .WithMaxValue(255)
                        .WithMinValue(0)
                    )
                    .AddOption(new SlashCommandOptionBuilder().WithName("blue")
                        .WithType(ApplicationCommandOptionType.Integer)
                        .WithDescription("Set the Blue value of the R, G, B triplet.")
                        .WithRequired(true)
                        .WithMaxValue(255)
                        .WithMinValue(0)
                    )
                )
                .AddOption(new SlashCommandOptionBuilder().WithName("magik_randomize_color")
                    .WithDescription("Randomize the color of the embed when you magik?")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption(new SlashCommandOptionBuilder().WithName("value")
                        .WithDescription("Set the value of setting: magikRandomColor")
                        .WithRequired(true)
                        .WithType(ApplicationCommandOptionType.Boolean)
                    )
                )
                .AddOption(new SlashCommandOptionBuilder().WithName("magik_color")
                    .WithDescription("The color of the embed of `/magik`")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption(new SlashCommandOptionBuilder().WithName("red")
                        .WithDescription("Set the Red value of the R, G, B triplet.")
                        .WithType(ApplicationCommandOptionType.Integer)
                        .WithRequired(true)
                        .WithMaxValue(255)
                        .WithMinValue(0)
                    )
                    .AddOption(new SlashCommandOptionBuilder().WithName("green")
                        .WithDescription("Set the Green value of the R, G, B triplet.")
                        .WithType(ApplicationCommandOptionType.Integer)
                        .WithRequired(true)
                        .WithMaxValue(255)
                        .WithMinValue(0)
                    )
                    .AddOption(new SlashCommandOptionBuilder().WithName("blue")
                        .WithRequired(true)
                        .WithDescription("Set the Blue value of the R, G, B triplet.")
                        .WithType(ApplicationCommandOptionType.Integer)
                        .WithMaxValue(255)
                        .WithMinValue(0)
                    )
                )
                .AddOption(new SlashCommandOptionBuilder().WithName("color")
                    .WithDescription("The color of most embeds sent by the bot")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption(new SlashCommandOptionBuilder().WithName("red")
                        .WithDescription("Set the Red value of the R, G, B triplet.")
                        .WithType(ApplicationCommandOptionType.Integer)
                        .WithRequired(true)
                        .WithMaxValue(255)
                        .WithMinValue(0)
                    )
                    .AddOption(new SlashCommandOptionBuilder().WithName("green")
                        .WithDescription("Set the Green value of the R, G, B triplet.")
                        .WithType(ApplicationCommandOptionType.Integer)
                        .WithRequired(true)
                        .WithMaxValue(255)
                        .WithMinValue(0)
                    )
                    .AddOption(new SlashCommandOptionBuilder().WithName("blue")
                        .WithType(ApplicationCommandOptionType.Integer)
                        .WithRequired(true)
                        .WithDescription("Set the Blue value of the R, G, B triplet.")
                        .WithMaxValue(255)
                        .WithMinValue(0)
                    )
                )
            )
            .AddOption(new SlashCommandOptionBuilder().WithName("main")
                .WithType(ApplicationCommandOptionType.SubCommandGroup)
                .WithDescription("The main settings for gemBOT")
                .AddOption(new SlashCommandOptionBuilder().WithName("auto_delete")
                    .WithDescription("How long before command responses sent by gemBOT auto-delete (after last auto-update)")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption(new SlashCommandOptionBuilder().WithName("value")
                        .WithDescription("Set Setting: delayBeforeDelete. WARNING: commands will not delete after bot refresh.")
                        .WithType(ApplicationCommandOptionType.Integer)
                        .AddChoice("Never delete it", 0)
                        .AddChoice("2 minutes", 2)
                        .AddChoice("5 minutes", 5)
                        .AddChoice("15 minutes", 15)
                        .AddChoice("30 minutes", 30)
                        .AddChoice("1 hour", 60)
                        .AddChoice("2 hours", 120)
                        .AddChoice("3 hours", 180)
                        .AddChoice("5 hours", 300)
                        .AddChoice("8 hours", 480)
                        .AddChoice("12 hours", 720)
                        .AddChoice("1 day", 1440)
                        .AddChoice("2 days", 2880)
                    )
                )
            )
            ;
        SlashCommandBuilder craft = new SlashCommandBuilder()
            .WithName("craft")
            .WithDescription("Craft items from other items")
            .WithIntegrationTypes([ApplicationIntegrationType.GuildInstall, ApplicationIntegrationType.UserInstall]);
        try
        {
            await message.Channel.SendMessageAsync("Pushing grinding commands (3/6)...");
            if (forceUpdateAll || forceUpdateCommands.Contains("beg") || !existingCommands.Contains("beg")) 
                await _client.CreateGlobalApplicationCommandAsync(beg.Build());
            if (forceUpdateAll || forceUpdateCommands.Contains("work") || !existingCommands.Contains("work")) 
                await _client.CreateGlobalApplicationCommandAsync(work.Build());
            if (forceUpdateAll || forceUpdateCommands.Contains("magik") || !existingCommands.Contains("magik")) 
                await _client.CreateGlobalApplicationCommandAsync(magik.Build());
            if (forceUpdateAll || forceUpdateCommands.Contains("mine") || !existingCommands.Contains("mine")) 
                await _client.CreateGlobalApplicationCommandAsync(mine.Build()); 
            if (forceUpdateAll || forceUpdateCommands.Contains("play") || !existingCommands.Contains("play")) 
                await _client.CreateGlobalApplicationCommandAsync(play.Build());
            await message.Channel.SendMessageAsync("Pushing economy commands (4/6)....");
            if (forceUpdateAll || forceUpdateCommands.Contains("balance") || !existingCommands.Contains("balance")) 
                await _client.CreateGlobalApplicationCommandAsync(balance.Build());
            if (forceUpdateAll || forceUpdateCommands.Contains("inventory") || !existingCommands.Contains("inventory")) 
                await _client.CreateGlobalApplicationCommandAsync(inventory.Build());
            if (forceUpdateAll || forceUpdateCommands.Contains("quests") || !existingCommands.Contains("quests")) 
                await _client.CreateGlobalApplicationCommandAsync(quests.Build());
            if (forceUpdateAll || forceUpdateCommands.Contains("give") || !existingCommands.Contains("give")) 
                await _client.CreateGlobalApplicationCommandAsync(give.Build());
            if (forceUpdateAll || forceUpdateCommands.Contains("bank") || !existingCommands.Contains("bank")) 
                await _client.CreateGlobalApplicationCommandAsync(bank.Build());
            if (forceUpdateAll || forceUpdateCommands.Contains("craft") || !existingCommands.Contains("craft"))
                await _client.CreateGlobalApplicationCommandAsync(craft.Build());
            await message.Channel.SendMessageAsync("Pushing info commands (5/6)...");
            if (forceUpdateAll || forceUpdateCommands.Contains("item") || !existingCommands.Contains("item")) 
                await _client.CreateGlobalApplicationCommandAsync(itemInfo.Build());
            if (forceUpdateAll || forceUpdateCommands.Contains("stats") || !existingCommands.Contains("stats")) 
                await _client.CreateGlobalApplicationCommandAsync(stats.Build());
            if (forceUpdateAll || forceUpdateCommands.Contains("help") || !existingCommands.Contains("help")) 
                await _client.CreateGlobalApplicationCommandAsync(help.Build());
            if (forceUpdateAll || forceUpdateCommands.Contains("start") || !existingCommands.Contains("start"))
                await _client.CreateGlobalApplicationCommandAsync(start.Build());
            await message.Channel.SendMessageAsync("Pushing customization commands(6/6)...");
            if (forceUpdateAll || forceUpdateCommands.Contains("theme") || !existingCommands.Contains("theme"))
                await _client.CreateGlobalApplicationCommandAsync(theme.Build());
            if (forceUpdateAll || forceUpdateCommands.Contains("setting") || !existingCommands.Contains("setting"))
                await _client.CreateGlobalApplicationCommandAsync(settings.Build());
        }
        catch (HttpException exception)
        {
            string json = JsonConvert.SerializeObject(exception.Errors, Formatting.Indented);
            Console.WriteLine(json);
            await message.Channel.SendMessageAsync(json);
        }
        await message.Channel.SendMessageAsync("Reset commands!");
        await _client.SetGameAsync("Commands recently restarted.");
    }
    private async Task SetItem(SocketMessage message)
    {
        string[] command = message.ToString().Split(" ");
        if (command.Length < 4)
        {
            RestUserMessage msg = await message.Channel.SendMessageAsync("Please enter the command in a proper format");
            await Task.Delay(5000);
            await msg.DeleteAsync();
            return;
        }

        if (int.Parse(command[1]) >= _items.Count)
        {
            RestUserMessage msg = await message.Channel.SendMessageAsync("Please use $add_item to add another item to the list.");
            await Task.Delay(5000);
            await msg.DeleteAsync();
            return;
        }

        try
        {
            Item item = _items[int.Parse(command[1])];
            switch (command[2])
            {
                case "id":
                    item.ID = int.Parse(command[3]);
                    await File.WriteAllTextAsync(Tools.IDString(int.Parse(command[1])),
                        JsonConvert.SerializeObject(item, Formatting.Indented));
                    await message.Channel.SendMessageAsync($"## Item saved! \n {item}");
                    //await GetItems();
                    break;
                case "value":
                    item.Value = int.Parse(command[3]);
                    await File.WriteAllTextAsync(Tools.IDString(int.Parse(command[1])),
                        JsonConvert.SerializeObject(item, Formatting.Indented));
                    await message.Channel.SendMessageAsync($"## Item saved! \n {item}");
                    //await GetItems();
                    break;
                case "name":
                    item.Name = String.Join(" ", command[3..^0]);
                    await File.WriteAllTextAsync(Tools.IDString(int.Parse(command[1])),
                        JsonConvert.SerializeObject(item, Formatting.Indented));
                    await message.Channel.SendMessageAsync($"## Item saved! \n {item}");
                    //await GetItems();
                    break;
                case "emoji":
                    item.Emoji = command[3];
                    await File.WriteAllTextAsync(Tools.IDString(int.Parse(command[1])),
                        JsonConvert.SerializeObject(item, Formatting.Indented));
                    await message.Channel.SendMessageAsync($"## Item saved! \n {item}");
                    //await GetItems();
                    break;
                case "description":
                    item.Description = String.Join(" ", command[3..^0]);
                    await File.WriteAllTextAsync(Tools.IDString(int.Parse(command[1])),
                        JsonConvert.SerializeObject(item, Formatting.Indented));
                    await message.Channel.SendMessageAsync($"## Item saved! \n {item}");
                    //await GetItems();
                    break;
                default:
                    RestUserMessage msg = await message.Channel.SendMessageAsync($"Could not find option for {command[2]}.");
                    await Task.Delay(5000);
                    await msg.DeleteAsync();
                    break;
            }
        }
        catch (Exception e)
        {
            await message.Channel.SendMessageAsync(e.ToString());
        }
    }
    private async Task SetCharm(SocketMessage message)
    {
        string[] command = message.ToString().Split(" ");
        int itemID = int.Parse(command[1]);
        int charmID = int.Parse(command[2]);
        if (command.Length < 5)
        {
            RestUserMessage msg = await message.Channel.SendMessageAsync("Please enter the command in a proper format");
            await Task.Delay(5000);
            await msg.DeleteAsync();
            return;
        }
        if (itemID >= _items.Count)
        {
            RestUserMessage msg = await message.Channel.SendMessageAsync("Please use $add_item to add another item to the list.");
            await Task.Delay(5000);
            await msg.DeleteAsync();
            return;
        }

        Item item = _items[itemID];
        if (charmID >= item.Charms.Count)
        {
            RestUserMessage msg = await message.Channel.SendMessageAsync("Please use $add_charm to add another charm to the list.");
            await Task.Delay(5000);
            await msg.DeleteAsync();
            return;
        }

        try
        {
            Charm charm = item.Charms[charmID];
            switch (command[3])
            {
                case "name":
                    charm.Effect = command[4];
                    await File.WriteAllTextAsync(Tools.IDString(int.Parse(command[1])),
                        JsonConvert.SerializeObject(item, Formatting.Indented));
                    await message.Channel.SendMessageAsync($"## Charm saved! \n {charm}");
                    //await GetItems();
                    break;
                case "effect":
                    charm.Amount = int.Parse(command[4]);
                    await File.WriteAllTextAsync(Tools.IDString(int.Parse(command[1])),
                        JsonConvert.SerializeObject(item, Formatting.Indented));
                    await message.Channel.SendMessageAsync($"## Charm saved! \n {charm}");
                    //await GetItems();
                    break;
                default:
                    await message.Channel.SendMessageAsync($"Could not find option for {command[3]}.");
                    break;
            }
        }
        catch (Exception e)
        {
            await message.Channel.SendMessageAsync(e.ToString());
        }
    }
    private async Task AddItem(SocketMessage message)
    {
        _items.Add(new Item(_items.Count));
        await message.Channel.SendMessageAsync(_items[^1].ToString());
        await File.WriteAllTextAsync(Tools.IDString(_items.Count - 1), JsonConvert.SerializeObject(_items[^1], Formatting.Indented));
    }
    private async Task AddCharm(SocketMessage message)
    {
        string[] command = message.ToString().Split(" ");
        if (command.Length < 2)
        {
            RestUserMessage msg =await message.Channel.SendMessageAsync("Please enter the command in a proper format");
            await Task.Delay(5000);
            await msg.DeleteAsync();
            return;
        }
        if (int.Parse(command[1]) >= _items.Count)
        {
            RestUserMessage msg = await message.Channel.SendMessageAsync("Please use $add_item to add another item to the list.");
            await Task.Delay(5000);
            await msg.DeleteAsync();
            return;
        }

        List<Charm> charms = _items[int.Parse(command[1])].Charms;
        charms.Add(new Charm("SetThis", 1));
        await message.Channel.SendMessageAsync($"# Charm {charms.Count - 1}:\n{charms[^1]}");
    }
    private async Task AllItemsText(SocketMessage socketMessage)
    {
        foreach (Item item in _items)
        {
            await socketMessage.Channel.SendMessageAsync(item.ToString());
        }
    }
    private async Task AddItemList(SocketMessage message)
    {
        string[] dat = message.Content.Split(" ");
        if (dat.Length < 4)
        {
            RestUserMessage msg = await message.Channel.SendMessageAsync("Please enter the command correctly.");
            await Task.Delay(5000);
            await msg.DeleteAsync();
            return;
        }

        string listName = dat[1];
        if (!_itemLists.TryGetValue(listName, out List<int>? list))
        {
            list = ( []);
            _itemLists[listName] = list;
            await message.Channel.SendMessageAsync("Created list!");
            return;
        }

        string action = dat[2];
        int item = int.Parse(dat[3]);
        switch (action)
        {
            case "add":
                list.Add(item);
                await File.WriteAllTextAsync($"Data/ItemLists/{listName}.json",
                    JsonConvert.SerializeObject(list));
                string text = "[";
                foreach (int i in list)
                {
                    text += i + ", ";
                }

                text = text.TrimEnd(',', ' ');
                text += "]";
                await message.Channel.SendMessageAsync(text);
                break;
            case "remove":
                list.Remove(item);
                await File.WriteAllTextAsync($"Data/ItemLists/{listName}.json",
                    JsonConvert.SerializeObject(list));
                string text2 = "[";
                foreach (int i in list)
                {
                    text2 += i + ", ";
                }

                text2 = text2.TrimEnd(',', ' ');
                text2 += "]";
                await message.Channel.SendMessageAsync(text2);
                break;
            default:
                await message.Channel.SendMessageAsync("Please use add or remove to add/remove items.");
                break;
        }
    }
    private async Task AddDataList(SocketMessage message)
    {
        string[] dat = message.Content.Split(" ");
        if (dat.Length < 4)
        {
            RestUserMessage msg = await message.Channel.SendMessageAsync("Please enter the command correctly.");
            await Task.Delay(5000);
            await msg.DeleteAsync();
            return;
        }

        string listName = dat[1];
        if (!_dataLists.TryGetValue(listName, out List<string>? list))
        {
            list = ( []);
            _dataLists[listName] = list;
            await message.Channel.SendMessageAsync("Created list!");
            return;
        }

        string action = dat[2];
        string item = string.Join(" ", dat[3..]);
        switch (action)
        {
            case "add":
                list.Add(item);
                await File.WriteAllTextAsync($"Data/Lists/{listName}.json", JsonConvert.SerializeObject(list));
                string text = "[";
                foreach (string i in list)
                {
                    text += $"\"{i}\", ";
                }

                text = text.TrimEnd(',', ' ');
                text += "]";
                await message.Channel.SendMessageAsync(text);
                break;
            case "remove":
                list.Remove(item);
                await File.WriteAllTextAsync($"Data/Lists/{listName}.json", JsonConvert.SerializeObject(list));
                string text2 = "[";
                foreach (string i in list)
                {
                    text2 += $"\"{i}\", ";
                }

                text2 = text2.TrimEnd(',', ' ');
                text2 += "]";
                await message.Channel.SendMessageAsync(text2);
                break;
            default:
                await message.Channel.SendMessageAsync("Please use add or remove to add/remove items.");
                break;
        }
    }
    private static async Task ColorToValue(SocketMessage message)
    {
        string[] args = message.Content.Split(" ");
        if (args.Length < 4)
        {
            RestUserMessage msg = await message.Channel.SendMessageAsync("Please enter 4 arguments");
            await Task.Delay(5000);
            await msg.DeleteAsync();
            return;
        }

        try
        {
            int r = int.Parse(args[1]);
            int g = int.Parse(args[2]);
            int b = int.Parse(args[3]);
            uint value = new Color(r, g, b).RawValue;
            Embed embay = new EmbedBuilder()
                .WithTitle("Color Result")
                .WithDescription($"Color ({r}, {g}, {b}) has int value {value}.")
                .WithColor(new Color(value))
                .Build();
            await message.Channel.SendMessageAsync("View embed for more details", embed: embay);
        }
        catch (FormatException)
        {
            await message.Channel.SendMessageAsync("Arguments 2-4 must be integers.");
        }
    }
    private async Task AddQuest(SocketMessage message)
    {
        string[] args = message.ToString().Split(" ");
        if (args.Length < 2)
        {
            RestUserMessage msg = await message.Channel.SendMessageAsync("Please specify the rarity of the quest to add.");
            await Task.Delay(5000);
            await msg.DeleteAsync();
            return;
        }
        int rarity = args[1] switch
        {
            "common" => 0,
            "uncommon" => 1,
            "rare" => 2,
            "epik" => 3,
            "legendary" => 4,
            _ => -1
        };
        if (rarity == -1)
        {
            RestUserMessage? test = await message.Channel.SendMessageAsync("Please specify a correct rarity!");
            await Task.Delay(5000);
            await test.DeleteAsync();
            return;
        }
        _allQuests[rarity].Add(new DailyQuest(_allQuests[rarity].Count, char.ToUpper(args[1][0]) + args[1][1..]));
        await message.Channel.SendMessageAsync(_allQuests[rarity][^1].ToString());
    }
    private async Task ModifyQuest(SocketMessage message)
    {
        string[] command = message.ToString().Split(" ");
        if (command.Length < 5)
        {
            RestUserMessage msg = await message.Channel.SendMessageAsync("Please enter the command in a proper format (you need 5 arguments)");
            await Task.Delay(5000);
            await msg.DeleteAsync();
            return;
        }
        int rarity = command[1].ToLower() switch {
            "common" => 0,
            "uncommon" => 1,
            "rare" => 2,
            "epik" => 3,
            "legendary" => 4,
            _ => -1
        };
        if (rarity == -1)
        {
            RestUserMessage msg = await message.Channel.SendMessageAsync("Please enter a rarity  (common, uncommon, rare, epic/epik, legendary");
            await Task.Delay(5000);
            await msg.DeleteAsync();
            return;
        }
        int questID = int.Parse(command[2]);
        if (questID >= _allQuests[rarity].Count)
        {
            RestUserMessage msg = await message.Channel.SendMessageAsync("Please use $add_quest to add another quest to the list.");
            await Task.Delay(5000);
            await msg.DeleteAsync();
            return;
        }
        DailyQuest quest = _allQuests[rarity][questID];
        string fileRarity = $"Data/DailyQuests/{rarity}{command[1].ToLower()}";
        switch (command[3].ToLower())
        {
            case "requirement":
                quest.Requirement = command[4];
                quest.Date = Tools.CurrentDay();
                await File.WriteAllTextAsync(Tools.IDString(questID, fileRarity), JsonConvert.SerializeObject(quest, Formatting.Indented));
                await message.Channel.SendMessageAsync($"## Quest saved! \n {quest}");
                break;
            case "description":
                quest.Description = string.Join(" ", command[4..]);
                quest.Date = Tools.CurrentDay();
                await File.WriteAllTextAsync(Tools.IDString(questID, fileRarity), JsonConvert.SerializeObject(quest, Formatting.Indented));
                await message.Channel.SendMessageAsync($"## Quest saved! \n {quest}");
                break;
            case "amount":
                quest.Amount = uint.Parse(command[4]);
                quest.Date = Tools.CurrentDay();
                await File.WriteAllTextAsync(Tools.IDString(questID, fileRarity), JsonConvert.SerializeObject(quest, Formatting.Indented));
                await message.Channel.SendMessageAsync($"## Quest saved! \n {quest}");
                break;
            case "name":
                quest.Name = string.Join(" ", command[4..]);
                quest.Date = Tools.CurrentDay();
                await File.WriteAllTextAsync(Tools.IDString(questID, fileRarity), JsonConvert.SerializeObject(quest, Formatting.Indented));
                await message.Channel.SendMessageAsync($"## Quest saved! \n {quest}");
                break;
            default:
                RestUserMessage msg = await message.Channel.SendMessageAsync($"Could not find option for {command[3]}.");
                await Task.Delay(5000);
                await msg.DeleteAsync();
                break;
            }
    }
    private async Task CreateMoney(SocketMessage message)
    {
        string[] args = message.Content.Split(" ");
        if (args.Length < 4)
        {
            RestUserMessage msg3 = await message.Channel.SendMessageAsync("Please enter enough arguments");
            await Task.Delay(5000);
            await msg3.DeleteAsync();
            return;
        }
        IUser? iUser = null;
        string[] temp = args[1].Split("<@");
        if (temp.Length >= 2)
        {
            temp = temp[1].Split(">");
            try
            {
                iUser = await _client.GetUserAsync(ulong.Parse(temp[0]));
            }
            catch (FormatException)
            {
                
            }
        }
        try
        {
            iUser = await _client.GetUserAsync(ulong.Parse(args[1]));
        }
        catch (FormatException)
        {
                
        }
        if (iUser == null)
        {
            RestUserMessage msg = await message.Channel.SendMessageAsync("Please enter a valid user: @mention or id.");
            await Task.Delay(5000);
            await msg.DeleteAsync();
            return;
        }
        User user = await GetUser(iUser.Id);
        await user.Add(int.Parse(args[3]), int.Parse(args[2]));
        RestUserMessage msg2 = await message.Channel.SendMessageAsync(
            $"You have successfully added {args[3]} {_currency[int.Parse(args[2])]}",
            embed:new EmbedBuilder()
                .WithTitle("New balance!")
                .WithDescription(await BalanceRaw(true, iUser.Id, ""))
                .Build());
        await Task.Delay(5000);
        await msg2.DeleteAsync();
    }
    private async Task CreateItems(SocketMessage message)
    {
        string[] args = message.Content.Split(" ");
        if (args.Length < 4)
        {
            RestUserMessage msg3 = await message.Channel.SendMessageAsync("Please enter enough arguments");
            await Task.Delay(5000);
            await msg3.DeleteAsync();
            return;
        }
        IUser? iUser = null;
        string[] temp = args[1].Split("<@");
        if (temp.Length >= 2)
        {
            temp = temp[1].Split(">");
            try
            {
                iUser = await _client.GetUserAsync(ulong.Parse(temp[0]));
            }
            catch (FormatException)
            {
                
            }
        }
        try
        {
            iUser = await _client.GetUserAsync(ulong.Parse(args[1]));
        }
        catch (FormatException)
        {
                
        }
        if (iUser == null)
        {
            RestUserMessage msg = await message.Channel.SendMessageAsync("Please enter a valid user: @mention or id.");
            await Task.Delay(5000);
            await msg.DeleteAsync();
            return;
        }
        User user = await GetUser(iUser.Id);
        await user.GainItem(int.Parse(args[2]), int.Parse(args[3]));
        RestUserMessage msg2 = await message.Channel.SendMessageAsync(
            $"You have successfully added {args[3]} {_items[int.Parse(args[2])].Emoji}");
        await Task.Delay(5000);
        await msg2.DeleteAsync();
    }
    private async Task TextHelp(SocketMessage message)
    {
        Embed embay0 = new EmbedBuilder()
            .WithTitle("Uncategorized commands")
            .WithDescription("These commands do not have a category")
            .AddField("$reset", " > **Params**: *none*\n > **Descriptions**: Reset the commands")
            .AddField("$dList", " > **Params**: <listName> <add/remove> <string> [<string>]\n > **Description**: Add/remove <string> to data list <listName>")
            .Build();
        Embed embay1 = new EmbedBuilder()
            .WithTitle("Item")
            .WithDescription("Commands that modify, show, add, or reload items.")
            .AddField("$reload", " > **Params**: *none*\n > **Descriptions**: Reload users, items, and lists. Useful if you modify data directly")
            .AddField("$item", " > **Params**: <itemID> <property> <value>\n > **Description**: Set Item <itemID>'s <property> to <value>")
            .AddField("$add_item", " > **Params**: *none*\n > **Description**: Add another item to the list. WARNING: this action is non-reversible.")
            .AddField("$charm", " > **Params**: <itemID> <charmID> <property> <value>\n > **Description**: Set Item <itemID>'s Charm <charmID>'s <property> to <value>.")
            .AddField("$add_charm", " > **Params**: <itemID>\n > **Description**: Add another charm to Item <itemID>")
            .AddField("$iList", " > **Params**: <listName> <add/remove> <itemID>\n > **Description**: Add/remove <itemID> to item list <listName>. (creates List <listName> if it doesn't exist)")
            .AddField("$add_recipe", " > **Params**: *none*\n > **Description**: Creates a new crafting recipe")
            .AddField("$edit_recipe", " > **Params**: <recipeID> <property> <value>\n > **Description**: Set Crafting Recipe <recipeID>'s <property> to <value>")
            .Build();
        Embed embay2 = new EmbedBuilder()
            .WithTitle("Utility")
            .WithDescription("Commands that don't do anything, just show useful stuff.")
            .AddField("$help", " > **Params**: *none*\n > **Description**: List all admin commands, their parameters, and arguments.")
            .AddField("$all_items", " > **Params**: *none*\n > **Descriptions**: List all the items.")
            .AddField("$color", " > **Params**: <r> <g> <b>\n > **Description**: Turns a color in rgb format into discord format.")
            .Build();
        Embed embay3 = new EmbedBuilder()
            .WithTitle("User")
            .WithDescription("Commands that modify data of users (or servers)")
            .AddField("$money", " > **Params**: <user> <value> <amount>\n > **Description** Add <amount> of <value> (integer) currency to user <user>")
            .AddField("$give", " > **Params**: <user> <item> <amount>\n > **Description**: Add <amount> of Item <item> to User <user>")
            .Build();
        Embed embay4 = new EmbedBuilder()
            .WithTitle("Quests")
            .WithDescription("Commands that modify, add, or delete quests.")
            .AddField("$add_quest", " > **Params**: <rarity>\n> **Description**: Create a new quest of rarity rarity")
            .AddField("$quest", " > **Params**: <rarity> <questID> <property> <value>\n **Description**: Set Quest <rarity> <questId>'s <property> to <value>")
            .Build();
        await message.Channel.SendMessageAsync("View Embeds for details!", embeds:[embay1, embay2, embay3, embay4, embay0]);
    }
    private async Task CreateCraftingRecipe(SocketMessage message)
    {
        string[] args = message.ToString().Split(" ");
        int id = _craftingRecipes.Count;
        _craftingRecipes.Add(new CraftingRecipe(){ID = id});
        RestUserMessage msg = await message.Channel.SendMessageAsync($"Created new crafting recipe with id {id}");
        await Task.Delay(5000);
        await msg.DeleteAsync();
    }
    private async Task EditCraftingRecipe(SocketMessage message)
    {
        string[] args = message.ToString().Split(" ");
        if (args.Length < 4)
        {
            RestUserMessage msg = await message.Channel.SendMessageAsync("Usage of this command is: $edit_recipe <recipeID> <property> <value>");
            await Task.Delay(5000);
            await msg.DeleteAsync();
            return;
        }
        int recipeID = int.Parse(args[1]);
        if (recipeID >= _craftingRecipes.Count)
        {
            RestUserMessage msg = await message.Channel.SendMessageAsync("Please use $add_recipe to add another crafting recipe to the list.");
            await Task.Delay(5000);
            await msg.DeleteAsync();
            return;
        }
        CraftingRecipe recipe = _craftingRecipes[recipeID];
        switch (args[2])
        {
            case "time":
                recipe.TimeRequired = uint.Parse(args[3]);
                await recipe.Save();
                break;
            case "amount":
                recipe.AmountCrafted = int.Parse(args[3]);
                await recipe.Save();
                break;
            case "item":
                int item = int.Parse(args[3]);
                if (item >= _items.Count)
                {
                    RestUserMessage msgItem = await message.Channel.SendMessageAsync("This item does not exist.");
                    await Task.Delay(5000);
                    await msgItem.DeleteAsync();
                    return;
                }
                recipe.ItemCrafted = item;
                await recipe.Save();
                break;
            case "requirements":
                switch (args[3])
                {
                    case "add":
                        recipe.Requirements.Add(new CraftingRecipe.RecipeRequirements());
                        break;
                    case "remove":
                        recipe.Requirements.RemoveAt(recipe.Requirements.Count - 1);
                        break;
                    default:
                        int requirementID = 0;
                        try
                        {
                            requirementID = int.Parse(args[3]);
                            if (requirementID >= recipe.Requirements.Count || requirementID < 0)
                            {
                                throw new IndexOutOfRangeException();
                            }
                        }
                        catch (FormatException)
                        {
                            RestUserMessage msgReqNotInt =
                                await message.Channel.SendMessageAsync(
                                    "You can either \"add\" a requirement, \"remove\" the last requirement, or enter a number to edit that requirement.");
                            await Task.Delay(5000);
                            await msgReqNotInt.DeleteAsync();
                            return;
                        }
                        catch (IndexOutOfRangeException)
                        {
                            RestUserMessage msgReqOutOfRange = await message.Channel.SendMessageAsync(
                                $"The specified requirement id is outside of the range (0 - {recipe.Requirements.Count - 1}).\nUse `$edit_recipe {recipeID} requirements add` to add a requirement.");
                            await Task.Delay(5000);
                            await msgReqOutOfRange.DeleteAsync();
                            return;
                        }
                        if (args.Length < 6)
                        {
                            RestUserMessage msgReqTooShort = await message.Channel.SendMessageAsync("Please specify at least 5 arguments: $edit_recipe <recipeID> requirements <requirementID> <property> <value>");
                            await Task.Delay(5000);
                            await msgReqTooShort.DeleteAsync();
                            return;
                        }
                        switch (args[4])
                        {
                            case "item":
                                try
                                {
                                    int itemID = int.Parse(args[5]);
                                    if (itemID >= _items.Count || itemID < 0)
                                    {
                                        throw new IndexOutOfRangeException();
                                    }
                                    recipe.Requirements[requirementID].Item = itemID;
                                    break;
                                }
                                catch (FormatException)
                                {
                                    RestUserMessage msgReqItemNotInt =
                                        await message.Channel.SendMessageAsync(
                                            "Argument 5 (after \"item\") expects an item ID, which is an integer.");
                                    await Task.Delay(5000);
                                    await msgReqItemNotInt.DeleteAsync();
                                    return;
                                }
                                catch (IndexOutOfRangeException)
                                {
                                    RestUserMessage msgReqItemOutOfRange =
                                        await message.Channel.SendMessageAsync($"The item ID {int.Parse(args[5])} was outside of the range of items (0-{_items.Count -1}");
                                    await Task.Delay(5000);
                                    await msgReqItemOutOfRange.DeleteAsync();
                                    return;
                                }
                            case "amount":
                                try
                                {
                                    int amount = int.Parse(args[5]);
                                    if (amount < 0)
                                    {
                                        throw new IndexOutOfRangeException();
                                    }

                                    recipe.Requirements[requirementID].Amount = amount;
                                    break;
                                }
                                catch (FormatException)
                                {
                                    RestUserMessage msgReqAmountNotInt =
                                        await message.Channel.SendMessageAsync(
                                            "Argument 5 (after \"item\") expects an item ID, which is an integer.");
                                    await Task.Delay(5000);
                                    await msgReqAmountNotInt.DeleteAsync();
                                    return;
                                }
                                catch (IndexOutOfRangeException)
                                {
                                    RestUserMessage msgReqAmountOutOfRange = await message.Channel.SendMessageAsync(
                                        "A recipe must require a positive number of each ingredient");
                                    await Task.Delay(5000);
                                    await msgReqAmountOutOfRange.DeleteAsync();
                                    return;
                                }
                        }

                        await recipe.Save();
                        break;
                }
                break;
            default:
                RestUserMessage msgNone = await message.Channel.SendMessageAsync("Please specify the property you would like to edit:\n> **time**, **amount**, **item**, or **requirements**.");
                await Task.Delay(5000);
                await msgNone.DeleteAsync();
                return;
        }
        await message.Channel.SendMessageAsync(recipe.ToString(_items));
    }


    private async Task RunTicks()
    {
        Console.WriteLine("Starting ticking...");
        Stopwatch stopwatch = new Stopwatch();
        stopwatch.Start();
        uint taskTimes = 0;
        while (true)
        {
            Console.WriteLine($"Tick {taskTimes}...");
            if (true) //every tick
            {
                _ = Task.Run(() => Task.FromResult(MineTick()));
                _ = Task.Run(() => Task.FromResult(CraftTick()));
            }
            if (taskTimes % 60 == 0) //every minute
            {
                _ = Task.Run(() => Task.FromResult(_mineData.SaveMineData()));
            }
            if (taskTimes % 120 == 0) //Every two minutes
            {
                _ = Task.Run(() => Task.FromResult(RemoveInactiveUsersTick()));
            }
            if (taskTimes % 60 * 60 == 0) //every hour
            {
                if (DateTime.Today.Month.ToString() != _mineData.MonthName)
                {
                    await Task.Delay(100);
                    _mineData = await MineData.LoadMineData();
                }
            }
            taskTimes++;
            await Task.Delay(1000 - (int)stopwatch.ElapsedMilliseconds);
            stopwatch.Restart();
        }
    }
    private async Task MineTick()
    {
        foreach (MineChunk chunk in _mineData.MineChunks)
        {
            foreach (List<MineBlock> layer in chunk.Blocks)
            {
                foreach (MineBlock block in layer)
                {
                    if (block.Type == BlockType.Air || block.MinerID is null) continue;
                    try
                    {
                        User user = await GetUser((ulong)block.MinerID);
                        Tuple<bool, int, BlockType> result = block.Mine((ulong)block.MinerID, await Tools.CharmEffect(["minePower"], _items, user)+5);
                        if (result.Item1)
                        {
                            await user.SetData("mining", 0, false);
                            await user.Increase("mined", 1);
                            _mineData.TimesMined++;
                            BlockType type = result.Item3;
                            bool dropsItem = type switch
                            {
                                BlockType.Air => true,
                                BlockType.Stone => true,
                                BlockType.Diamonds => false,
                                BlockType.Emeralds => false,
                                BlockType.Rubies => false,
                                BlockType.Sapphires => false,
                                BlockType.Amber => false,
                                _ => true
                            };
                            if (dropsItem)
                            {
                                int itemId = type switch
                                {
                                    BlockType.Air => 39,
                                    BlockType.Stone => 39,
                                    _ => 39
                                };
                                await user.GainItem(itemId, result.Item2);
                            }
                            else
                            {
                                int value = type switch
                                {
                                    BlockType.Diamonds => 0,
                                    BlockType.Emeralds => 1,
                                    BlockType.Sapphires => 2,
                                    BlockType.Rubies => 3,
                                    BlockType.Amber => 4,
                                    _ => 0
                                };
                                await user.Add(result.Item2, value);
                            }
                        }
                    }
                    catch(Exception e){Console.WriteLine(e);}
                }
            }
        }
    }
    private async Task RemoveInactiveUsersTick()
    {
        ulong now = Tools.CurrentTime();
        const ulong inactiveLoaded = 60 * 60 * 24; //24 hours
        ulong oldTime = now - inactiveLoaded;
        List<ulong> usersToRemove = new List<ulong>();
        foreach (ulong id in _users.Keys)
        {
            CachedUser user = _users[id];
            if (user.InactiveSince >= oldTime) continue;
            await user.User.Save();
            usersToRemove.Add(id);
        }
        foreach (ulong id in usersToRemove)
        {
            _users.Remove(id);
        }
    }
    private async Task CraftTick()
    {
        foreach (CachedUser user in _users.Values)
        {
           await user.User.CheckFurnaces(await Tools.CharmEffect(["FurnaceSlots"], _items, user) + FurnaceConst);
           foreach (CraftingRecipe.Furnace furnace in user.User.Furnaces)
           {
               if (furnace.Crafting)
               {
                   if (furnace.TimeLeft > 1) furnace.TimeLeft--;
                   else
                   {
                       await user.User.GainItem(furnace.NextItem, furnace.Amount);
                       furnace.Crafting = false;
                   }
               }
               else
               {
                   if (user.NextCrafting == null)
                   {
                       // ReSharper disable once UseCollectionExpression
                       user.NextCrafting = new List<Tuple<int, int>>();
                       continue;
                   }
                   
                   if (user.NextCrafting.Count <= 0) continue;
                   if (user.NextCrafting[0].Item2 <= 0)
                   {
                       user.NextCrafting.RemoveAt(0);
                       continue;
                   }

                   CraftingRecipe recipe = _craftingRecipes[user.NextCrafting[0].Item1];
                   bool failedRecipe = false;
                   foreach (CraftingRecipe.RecipeRequirements requirement in recipe.Requirements)
                   {
                       if (user.User.Inventory[requirement.Item] >= requirement.Amount) continue;
                       Tuple<int, int> failedCraft = user.NextCrafting[0];
                       user.NextCrafting.RemoveAt(0);
                       user.NextCrafting.Add(failedCraft);
                       failedRecipe = true;
                   }
                   if (failedRecipe) continue;
                   user.NextCrafting[0] = 
                       new Tuple<int, int>(user.NextCrafting[0].Item1, user.NextCrafting[0].Item2 - 1);
                   if (user.NextCrafting[0].Item2 <= 0)
                   {
                       user.NextCrafting.RemoveAt(0);
                   }

                   foreach (CraftingRecipe.RecipeRequirements requirement in recipe.Requirements)
                   {
                       await user.User.GainItem(requirement.Item, -1 * requirement.Amount, false);
                   }
                   await user.User.Save();
                   furnace.UpdateFromCraftingRecipe(recipe);
               }
           }
        }
    }
}
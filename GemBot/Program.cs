using System.Diagnostics;
using System.Diagnostics.SymbolStore;
using System.Net;
using System.Reflection;
using Discord;
using Discord.Net;
using Discord.Rest;
using Discord.WebSocket;
using Newtonsoft.Json;

namespace GemBot;

/*
TODO *
* Other tasks
 * Add DM notifications
* Done this update:
 * Changes to `/magik` loot table
 * Added gold as an ore you can mine
 * Fixed a bug that causes the mine button (better) auto-refresh to wait forever for the first duration update
 * Fixed a bug where the mine button (better) auto-refresh does not update the progress bar
 * Fixed a bug where the mine button auto-refresh says you have full duration after mining a block
 * Fixed a bug where the mine (mine) auto-refresh does not trigger.
 * Fixed a bug where the craft command only accepts custom emojis (and not discord ones)
 * Added flexible progress bar size
 * Fixed a bug where auto-delete of not have enough money in bank takes 1/10th the expected time
 * Fixed a bug with daily token reward shop auto-stacking being only for diamonds
 * You can now do a bank trade multiple at a time with stone coins
 * Increase magik craft skip and mine skip again
 * Fixed a bug where you can /exchange regardless of how many gems you have
 * Added events (portal summons a mine chunk)
 * Added new items (52 - 57)
 */
public class NoTokenError : Exception { }
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

        string toWrite = $"Booting up GemBOT v{GemBot.MainVersion}.{GemBot.SmallVersion}.{GemBot.BugfixVersion}";
        if (Settings.InDev()) toWrite += " DEVELOPMENT";
        Console.WriteLine(toWrite);
        GemBot gemBot = new GemBot();
        await gemBot.Main();
    }
}

public partial class GemBot
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
    private readonly string[] _currencyNoEmojiNoFormatting = ["diamonds", "emeralds", "sapphires", "rubies", "amber"];
    private bool _running;
    private List<DailyQuest> _quests = [];
    private List<List<DailyQuest>> _allQuests = [];
    private MineData _mineData = null!;
    private List<CraftingRecipe> _craftingRecipes = [];
    private List<Drop> _drops = [];
    private DailyTokenShop _tokenShop = null!;
    private Dictionary<ulong, Server> _servers = [];
    public const int MainVersion = 1;
    public const int SmallVersion = 6;
    public const int BugfixVersion = 0;
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
        _mineData = await MineData.LoadMineData();
        _tokenShop = DailyTokenShop.Generate(_itemLists["CharmSets"], Tools.CurrentDay(), _rand);
        _ = Task.Run(() => Task.FromResult(RunTicks()));
        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();
        _running = true;
        await Task.Delay(-1);
    }
    
    private async Task GetItems()
    {
        await _client.SetGameAsync("Updating items...");
        _items = [];
        _users = new Dictionary<ulong, CachedUser>();
        _servers = new Dictionary<ulong, Server>();
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

        _craftingRecipes = [];
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

        _drops = [];
        foreach (string path in Directory.GetFiles("Data/Drops"))
        {
            string dropData = await File.ReadAllTextAsync(path);
            Drop? drop = JsonConvert.DeserializeObject<Drop>(dropData);
            if (drop is null) continue;
            if (drop.Left[0] <= 0 && drop.Left[1] <= 0 && drop.Left[2] <= 0 && drop.Left[3] <= 0 && drop.Left[4] <= 0)
            { //File.Delete(path);
              continue;
            }
            _drops.Add(drop);
        }
        _drops = _drops.OrderBy(o => o.DropID).ToList();
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
    private async Task<Server> GetServer(ulong id)
    {
        if (_servers.TryGetValue(id, out Server? server))
        {
            Debug.Assert(server != null, "server is null");
            return server;
        }
        try
        {
            string baseData = await File.ReadAllTextAsync($"Data/Servers/{id}.json");
            Server loadedServer = JsonConvert.DeserializeObject<Server>(baseData) ??
                                  throw new Exception("Somehow your save file is bad.");
            _servers.Add(id, loadedServer);
            return loadedServer;
        }
        catch (FileNotFoundException)
        {
            return new Server(id);
        }
    }
    private static Task Log(LogMessage msg)
    {
        Console.WriteLine(msg.ToString());
        return Task.CompletedTask;
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
    private async Task<EmbedBuilder> InventoryRawEmbed(ulong id, string options)
    {
        User user = await GetUser(id);
        bool showAll = user.GetSetting("ShowAllItemsInventory", 1) == 1;
        List<Tuple<int, int>> data = [];
        for (int i = 0; i < user.Inventory.Count; i++)
        {
            int amount = user.ItemAmount(i);
            if (!showAll && amount == 0) continue;
            data.Add(new Tuple<int, int>(i, amount));
        }

        List<Tuple<int, int>> sortedData = data.OrderBy(o => -o.Item2 * _items[o.Item1].Value).ToList();
        try
        {
            string text = "View all your items:";
            string[] optionsSplit = options.Split("|");
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

            return new EmbedBuilder().WithTitle("Inventory").WithDescription(text).WithColor((uint)user.GetSetting("uiColor", 3287295));
        }
        catch (FormatException)
        {
            throw new ButtonValueError();
        }
    }
    private async Task<Tuple<EmbedBuilder, ComponentBuilder, string>> BankRaw(bool showEmojis, ulong userId, bool fastUpgrade)
    {
        bool compact = showEmojis;
        User user = await GetUser(userId);
        user.Increase("commands", 1);
        string balanceText = await BalanceRaw(showEmojis, userId);
        string diamonds = _currency[0];
        string emeralds = _currency[1];
        string sapphires = _currency[2];
        string rubies = _currency[3];
        string ambers = _currency[4];
        if (!compact)
        {
            diamonds = _currencyNoEmoji[0];
            emeralds = _currencyNoEmoji[1];
            sapphires = _currencyNoEmoji[2];
            rubies = _currencyNoEmoji[3];
            ambers = _currencyNoEmoji[0];
        }
        int upgradePrice = 11;
        int downgradeReward = 9;
        int upgradePriceB = 105;
        int downgradeRewardB = 96;
        if (Tools.CharmEffect(["BetterBankTrades"], _items, user) >= 1)
        {
            upgradePrice = 10;
            downgradeReward = 10;
            upgradePriceB = 100;
            downgradeRewardB = 100;
        }

        ButtonStyle leftMain = user.GetSetting("bankLeftStyle", 0) switch
        {
            0 => ButtonStyle.Primary, 1 => ButtonStyle.Success, _ => ButtonStyle.Secondary
        };
        ButtonStyle rightMain = user.GetSetting("bankRightStyle", 2) switch
        {
            0 => ButtonStyle.Primary, 1 => ButtonStyle.Success, _ => ButtonStyle.Secondary
        };
        ButtonStyle leftSecondary = user.GetSetting("bankShowRed", 1) switch { 1 => ButtonStyle.Danger, _ => leftMain };
        ButtonStyle rightSecondary = user.GetSetting("bankShowRed", 1) switch { 1 => ButtonStyle.Danger, _ => rightMain };
        ButtonStyle b1 = (user.Gems[0] >= upgradePrice) switch { true => leftMain, false => leftSecondary };
        ButtonStyle b2 = (user.Gems[1] >= upgradePrice) switch { true => leftMain, false => leftSecondary };
        ButtonStyle b3 = (user.Gems[2] >= upgradePrice) switch { true => leftMain, false => leftSecondary };
        ButtonStyle b4 = (user.Gems[3] >= upgradePrice) switch { true => leftMain, false => leftSecondary };
        ButtonStyle b5 = (user.Gems[1] >= 1) switch { true => rightMain, false => rightSecondary };
        ButtonStyle b6 = (user.Gems[2] >= 1) switch { true => rightMain, false => rightSecondary };
        ButtonStyle b7 = (user.Gems[3] >= 1) switch { true => rightMain, false => rightSecondary };
        ButtonStyle b8 = (user.Gems[4] >= 1) switch { true => rightMain, false => rightSecondary };
        ButtonStyle b9 = (user.Gems[0] >= upgradePriceB) switch { true => leftMain, false => leftSecondary };
        ButtonStyle b10 = (user.Gems[1] >= upgradePriceB) switch { true => leftMain, false => leftSecondary };
        ButtonStyle b11 = (user.Gems[2] >= upgradePriceB) switch { true => leftMain, false => leftSecondary };
        ButtonStyle b12 = (user.Gems[3] >= upgradePriceB) switch { true => leftMain, false => leftSecondary };
        ButtonStyle b13 = (user.Gems[1] >= 10) switch {true => rightMain, false => rightSecondary};
        ButtonStyle b14 = (user.Gems[2] >= 10) switch {true => rightMain, false => rightSecondary};
        ButtonStyle b15 = (user.Gems[3] >= 10) switch {true => rightMain, false => rightSecondary};
        ButtonStyle b16 = (user.Gems[4] >= 10) switch {true => rightMain, false => rightSecondary};
        string emoj = showEmojis switch { true => "yes", false => "no" };
        EmbedBuilder embay = new EmbedBuilder()
            .WithTitle("Open Trades!")
            .WithDescription("View all the trades you can use!");
        if (fastUpgrade)
        {
            embay.AddField("Fast Upgrade Enabled", "Allows upgrades to be completed quickly, but uses stone coins");
        }
        embay
            .AddField(new EmbedFieldBuilder()
                .WithName("Upgrades")
                .WithValue($"""
                            1: {upgradePrice}{diamonds} --> 1{emeralds} {(fastUpgrade ? $"(320/{_items[46].Emoji})" : string.Empty)}
                            2: {upgradePrice}{emeralds} --> 1{sapphires} {(fastUpgrade ? $"(80/{_items[46].Emoji})" : string.Empty)}
                            3: {upgradePrice}{sapphires} --> 1{rubies} {(fastUpgrade ? $"(20/{_items[46].Emoji})" : string.Empty)}
                            4: {upgradePrice}{rubies} --> 1{ambers} {(fastUpgrade ? $"(5/{_items[46].Emoji})" : string.Empty)}
                            
                            9: {upgradePriceB}{diamonds} --> 10{emeralds} {(fastUpgrade ? $"(64/{_items[46].Emoji})" : string.Empty)}
                            10: {upgradePriceB}{emeralds} --> 10{sapphires} {(fastUpgrade ? $"(16/{_items[46].Emoji})" : string.Empty)}
                            11: {upgradePriceB}{sapphires} --> 10{rubies} {(fastUpgrade ? $"(4/{_items[46].Emoji})" : string.Empty)}
                            12: {upgradePriceB}{rubies} --> 10{ambers} {(fastUpgrade ? $"(1/{_items[46].Emoji})" : string.Empty)}
                            """)
                .WithIsInline(true))
            .AddField(new EmbedFieldBuilder()
                .WithName("Downgrades")
                .WithValue($"""
                            5: 1{emeralds} --> {downgradeReward}{diamonds} {(fastUpgrade ? $"(960/{_items[46].Emoji})" : string.Empty)}
                            6: 1{sapphires} --> {downgradeReward}{emeralds} {(fastUpgrade ? $"(240/{_items[46].Emoji})" : string.Empty)}
                            7: 1{rubies} --> {downgradeReward}{sapphires} {(fastUpgrade ? $"(60/{_items[46].Emoji})" : string.Empty)}
                            8: 1{ambers} --> {downgradeReward}{rubies} {(fastUpgrade ? $"(15/{_items[46].Emoji})" : string.Empty)}
                            
                            13: 10{emeralds} --> {downgradeRewardB}{diamonds} {(fastUpgrade ? $"(192/{_items[46].Emoji})" : string.Empty)}
                            14: 10{sapphires} --> {downgradeRewardB}{emeralds} {(fastUpgrade ? $"(48/{_items[46].Emoji})" : string.Empty)}
                            15: 10{rubies} --> {downgradeRewardB}{sapphires} {(fastUpgrade ? $"(12/{_items[46].Emoji})" : string.Empty)}
                            16: 10{ambers} --> {downgradeRewardB}{rubies} {(fastUpgrade ? $"(3/{_items[46].Emoji})" : string.Empty)}
                            """)
                .WithIsInline(true))
            .WithColor(new Color((uint) user.GetSetting("uiColor", 3287295)));
        ComponentBuilder button = new ComponentBuilder()
                .AddRow(new ActionRowBuilder()
                    .WithButton(new ButtonBuilder().WithLabel("1").WithStyle(b1).WithCustomId($"bank-{(!fastUpgrade ? 0 : 100)}|{emoj}"))
                    .WithButton(new ButtonBuilder().WithLabel("5").WithStyle(b5).WithCustomId($"bank-{(!fastUpgrade ? 4 : 104)}|{emoj}"))
                    .WithButton(new ButtonBuilder().WithLabel("-").WithStyle(ButtonStyle.Secondary).WithCustomId("disabled-a").WithDisabled(true))
                    .WithButton(new ButtonBuilder().WithLabel("9").WithStyle(b9).WithCustomId($"bank-{(!fastUpgrade ? 8 : 108)}|{emoj}"))
                    .WithButton(new ButtonBuilder().WithLabel("13").WithStyle(b13).WithCustomId($"bank-{(!fastUpgrade ?12:112)}|{emoj}"))
                )
                .AddRow(new ActionRowBuilder()
                    .WithButton(new ButtonBuilder().WithLabel("2").WithStyle(b2).WithCustomId($"bank-{(!fastUpgrade ? 1 : 101)}|{emoj}"))
                    .WithButton(new ButtonBuilder().WithLabel("6").WithStyle(b6).WithCustomId($"bank-{(!fastUpgrade ? 5 : 105)}|{emoj}"))
                    .WithButton(new ButtonBuilder().WithLabel("-").WithStyle(ButtonStyle.Secondary).WithCustomId("disabled-b").WithDisabled(true))
                    .WithButton(new ButtonBuilder().WithLabel("10").WithStyle(b10).WithCustomId($"bank-{(!fastUpgrade ? 9 : 109)}|{emoj}"))
                    .WithButton(new ButtonBuilder().WithLabel("14").WithStyle(b14).WithCustomId($"bank-{(!fastUpgrade ?13:113)}|{emoj}"))
                )
                .AddRow(new ActionRowBuilder()
                    .WithButton(new ButtonBuilder().WithLabel("3").WithStyle(b3).WithCustomId($"bank-{(!fastUpgrade ? 2 : 102)}|{emoj}"))
                    .WithButton(new ButtonBuilder().WithLabel("7").WithStyle(b7).WithCustomId($"bank-{(!fastUpgrade ? 6 : 106)}|{emoj}"))
                    .WithButton(new ButtonBuilder().WithLabel("-").WithStyle(ButtonStyle.Secondary).WithCustomId("disabled-c").WithDisabled(true))
                    .WithButton(new ButtonBuilder().WithLabel("11").WithStyle(b11).WithCustomId($"bank-{(!fastUpgrade ?10:110)}|{emoj}"))
                    .WithButton(new ButtonBuilder().WithLabel("15").WithStyle(b15).WithCustomId($"bank-{(!fastUpgrade ?14:114)}|{emoj}"))
                )
                .AddRow(new ActionRowBuilder()
                    .WithButton(new ButtonBuilder().WithLabel("4").WithStyle(b4).WithCustomId($"bank-{(!fastUpgrade ? 3 : 103)}|{emoj}"))
                    .WithButton(new ButtonBuilder().WithLabel("8").WithStyle(b8).WithCustomId($"bank-{(!fastUpgrade ? 7 : 107)}|{emoj}"))
                    .WithButton(new ButtonBuilder().WithLabel("-").WithStyle(ButtonStyle.Secondary).WithCustomId("disabled-d").WithDisabled(true))
                    .WithButton(new ButtonBuilder().WithLabel("12").WithStyle(b12).WithCustomId($"bank-{(!fastUpgrade ?11:111)}|{emoj}"))
                    .WithButton(new ButtonBuilder().WithLabel("16").WithStyle(b16).WithCustomId($"bank-{(!fastUpgrade ?15:115)}|{emoj}"))
                )
            ;
        return new Tuple<EmbedBuilder, ComponentBuilder, string>(embay, button, balanceText);
    }
    private async Task<Tuple<EmbedBuilder, List<string>>> QuestsEmbed(CachedUser user, bool showEmoji = true)
    {
        List<string> text = [];
        EmbedBuilder embay = new EmbedBuilder()
            .WithTitle("Your quests!")
            .WithDescription("View your daily quests!")
            .WithColor((uint)user.User.GetSetting("uiColor", 3287295));
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
        int pbWidth = (int)user.User.GetSetting("progressBar", 5);
        for (int i = 0; i < _quests.Count; i++)
        {
            DailyQuest quest = _quests[i];
            
            string additionalInfo = $"""
                                     {quest.Description}
                                      > **Quest completed**!
                                      > {Tools.ProgressBar(1,1, pbWidth)}
                                     """;
            if (!user.User.DailyQuestsCompleted[i])
            {
                if (user.User.GetProgress(quest.Requirement) >= quest.Amount)
                {
                    user.User.DailyQuestsCompleted[i] = true;
                    switch (i)
                    {
                        case 0: 
                            user.User.Add(120, 0);
                            user.User.Increase("earned", 120);
                            string currencyDiamonds = showEmoji switch { true => _currency[0], false => " **diamonds**" };
                            if (Tools.CharmEffect(["CommonQuests"], _items, user) > 0)
                            {
                                user.User.GainItem(0, 5);
                                string coinCommon = showEmoji switch { true => _items[0].Emoji, false => $" **{_items[0].Name}**" };
                                text.Add($"<@{user.User.ID}> earned 120{currencyDiamonds} + 5{coinCommon}!");
                            }
                            else text.Add($"<@{user.User.ID}> earned 120{currencyDiamonds}!");
                            break;
                        case 1:
                            user.User.Add(20, 1);
                            user.User.Increase("earned", 200);
                            int charmIDUncommon = Tools.GetCharm(_itemLists, 0, 50, _rand);
                            user.User.GainItem(charmIDUncommon, 1);
                            string currencyUncommon = showEmoji switch { true => _currency[1], false => " **emeralds**" };
                            string charm1Uncommon = showEmoji switch { true => _items[charmIDUncommon].Emoji, false => $" **{_items[charmIDUncommon].Name}**" };
                            if (Tools.CharmEffect(["UncommonQuests"], _items, user) > 0)
                            {
                                user.User.GainItem(1, 4);
                                string coinUncommon = showEmoji switch { true => _items[1].Emoji, false => $" **{_items[1].Name}**" };
                                text.Add($"<@{user.User.ID}> earned 20{currencyUncommon} + 1{charm1Uncommon} + 4{coinUncommon}!");
                            }
                            else text.Add($"<@{user.User.ID}> earned 20{currencyUncommon} + 1{charm1Uncommon}!");
                            break;
                        case 2:
                            user.User.Add(19, 2);
                            user.User.Increase("earned", 1900);
                            int charmIDRare = Tools.GetCharm(_itemLists, 0, 32, _rand);
                            user.User.GainItem(charmIDRare, 1);
                            string currencyRare = showEmoji switch { true => _currency[2], false => " **sapphires**" };
                            string charm1Rare = showEmoji switch { true => _items[charmIDRare].Emoji, false => $" **{_items[charmIDRare].Name}**" };
                            int charmID2Rare = Tools.GetCharm(_itemLists, 0, 22, _rand);
                            user.User.GainItem(charmID2Rare, 1);
                            string charm2Rare = showEmoji switch { true => _items[charmID2Rare].Emoji, false => $" **{_items[charmID2Rare].Name}**"};
                            if (Tools.CharmEffect(["RareQuests"], _items, user) > 0)
                            {
                                user.User.GainItem(2, 3);
                                string coinRare = showEmoji switch { true => _items[2].Emoji, false => $" **{_items[2].Name}**" };
                                text.Add($"<@{user.User.ID}> earned 19{currencyRare} + 1{charm1Rare} + 1{charm2Rare} + 3{coinRare}!");
                            }
                            else text.Add($"<@{user.User.ID}> earned 19{currencyRare} + 1{charm1Rare} + 1{charm2Rare}!");
                            break;
                        case 3:
                            user.User.Add(5, 3);
                            user.User.Increase("earned", 5000);
                            int charmIDEpic = Tools.GetCharm(_itemLists, 0, 9, _rand);
                            user.User.GainItem(charmIDEpic, 1);
                            string currencyEpic = showEmoji switch { true => _currency[3], false => " **rubies**" };
                            string charm1Epic = showEmoji switch { true => _items[charmIDEpic].Emoji, false => $" **{_items[charmIDEpic].Name}**" };
                            int charmID2Epic = Tools.GetCharm(_itemLists, 1, 17, _rand);
                            user.User.GainItem(charmID2Epic, 1);
                            string charm2Epic = showEmoji switch { true => _items[charmID2Epic].Emoji, false => $" **{_items[charmID2Epic].Name}**"};
                            if (Tools.CharmEffect(["EpikQuests"], _items, user) > 0)
                            {
                                user.User.GainItem(3, 2);
                                string coinEpic = showEmoji switch { true => _items[3].Emoji, false => $" **{_items[3].Name}**" };
                                text.Add($"<@{user.User.ID}> earned 5{currencyEpic} + 1{charm1Epic} + 1{charm2Epic} + 2{coinEpic}!");
                            }
                            else
                            {
                                text.Add($"<@{user.User.ID}> earned 5{currencyEpic} + 1{charm1Epic} + 1{charm2Epic}!");
                            }
                            break;
                        case 4:
                            user.User.Add(1, 4);
                            user.User.Increase("earned", 10000);
                            int charmIDLegendary = Tools.GetCharm(_itemLists, 1, 9, _rand);
                            user.User.GainItem(charmIDLegendary, 1);
                            string currencyLegendary = showEmoji switch { true => _currency[4], false => " **ambers**" };
                            string charm1Legendary = showEmoji switch { true => _items[charmIDLegendary].Emoji, false => $" **{_items[charmIDLegendary].Name}**" };
                            int charmID2Legendary = Tools.GetCharm(_itemLists, 1, 7, _rand);
                            user.User.GainItem(charmID2Legendary, 1);
                            string charm2Legendary = showEmoji switch { true => _items[charmID2Legendary].Emoji, false => $" **{_items[charmID2Legendary].Name}**"};
                            int charmID3Legendary = Tools.GetCharm(_itemLists, 2, 17, _rand);
                            user.User.GainItem(charmID3Legendary, 1);
                            string charm3Legendary = showEmoji switch { true => _items[charmID3Legendary].Emoji, false => $" **{_items[charmID3Legendary].Name}**"};
                            if (Tools.CharmEffect(["LegendaryQuests"], _items, user) > 0)
                            {
                                user.User.GainItem(4, 1);
                                string coinLegendary = showEmoji switch { true => _items[4].Emoji, false => $" **{_items[4].Name}**" };
                                text.Add($"<@{user.User.ID}> earned 1{currencyLegendary} + 1{charm1Legendary} + 1{charm2Legendary} + 1{charm3Legendary} + 1{coinLegendary}!");
                            }
                            else
                            {
                                text.Add($"<@{user.User.ID}> earned 1{currencyLegendary} + 1{charm1Legendary} + 1{charm2Legendary} + 1{charm3Legendary}!");
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
                                       > {Tools.ProgressBar((int)done, (int)amount, pbWidth)}
                                      """;
                }
            }
            embay.AddField(quest.Name, additionalInfo);
        }
        await user.User.Save();
        return new Tuple<EmbedBuilder, List<string>>(embay, text);
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
        if (user.GetData("MineMonth", mineMonth) != mineMonth)
        {
            user.SetData("MineMonth", mineMonth);
            user.SetData("mineY", 0);
            user.SetData("mineX", _rand.Next(_mineData.MineChunks.Count * 30));
            user.SetData("mining", 0);
        }
        string description = user.GetData("mining", 0) switch
        {
            0 => "Click any button to start mining!",
            1 => "You are currently mining a block.",
            _ => "Your data doesn't seem to be saved correctly."
        };
        int mineY = user.GetData("mineY", 0);
        int top = mineY-2;
        if (top <= 0)
        {
            top = 0;
        }
        int mineX = user.GetData("mineX", _rand.Next(_mineData.MineChunks.Count * 30));
        int left = mineX-2;
        if (left <= 0)
        {
            left = 0;
        }

        if (left + 4 >= _mineData.MineChunks.Count * 30)
        {
            left = _mineData.MineChunks.Count * 30 - 5;
        }
        string blocks = "Block ids:";
        if (top + 5 > 150)
        {
            top = 146;
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
                button.WithCustomId($"mine-{x}|{y}|{user.GetData("MineMonth", mineMonth)}");
                blocks += " ";
                blocks += button.CustomId;
                MineBlock block = _mineData.GetBlock(x, y);
                button.WithDisabled(Math.Abs(mineX - x) + Math.Abs(mineY - y) != 1);
                if (x == mineX && y == mineY)
                {
                    button.WithStyle(ButtonStyle.Secondary)
                        .WithEmote(Tools.ParseEmote("<:you:1287157766871580833>"));
                    row.AddComponent(button.Build());
                    continue;
                }
                switch (block.Type)
                {
                    case BlockType.Air:
                        button.WithStyle(ButtonStyle.Secondary)
                            .WithEmote(Tools.ParseEmote("<:air:1287157701905743903>"));
                        break;
                    case BlockType.Stone:
                        button.WithStyle(ButtonStyle.Primary)
                            .WithEmote(Tools.ParseEmote("<:stone:1287086951215796346>"));
                        break;
                    case BlockType.Diamonds:
                        button.WithStyle(ButtonStyle.Success)
                            .WithEmote(Tools.ParseEmote(_currency[0]));
                        break;
                    case BlockType.Emeralds:
                        button.WithStyle(ButtonStyle.Success)
                            .WithEmote(Tools.ParseEmote(_currency[1]));
                        break;
                    case BlockType.Sapphires:
                        button.WithStyle(ButtonStyle.Success)
                            .WithEmote(Tools.ParseEmote(_currency[2]));
                        break;
                    case BlockType.Rubies:
                        button.WithStyle(ButtonStyle.Success)
                            .WithEmote(Tools.ParseEmote(_currency[3]));
                        break;
                    case BlockType.Amber:
                        button.WithStyle(ButtonStyle.Success)
                            .WithEmote(Tools.ParseEmote(_currency[4]));
                        break;
                    case BlockType.DiamondCoin:
                        button.WithStyle(ButtonStyle.Success)
                            .WithEmote(Tools.ParseEmote(_items[0].Emoji));
                        break;
                    case BlockType.EmeraldCoin:
                        button.WithStyle(ButtonStyle.Success)
                            .WithEmote(Tools.ParseEmote(_items[1].Emoji));
                        break;
                    case BlockType.SapphireCoin:
                        button.WithStyle(ButtonStyle.Success)
                            .WithEmote(Tools.ParseEmote(_items[2].Emoji));
                        break;
                    case BlockType.RubyCoin:
                        button.WithStyle(ButtonStyle.Success)
                            .WithEmote(Tools.ParseEmote(_items[3].Emoji));
                        break;
                    case BlockType.AmberCoin:
                        button.WithStyle(ButtonStyle.Success)
                            .WithEmote(Tools.ParseEmote(_items[4].Emoji));
                        break;
                    case BlockType.DiamondKey:
                        button.WithStyle(ButtonStyle.Success)
                            .WithEmote(Tools.ParseEmote(_items[16].Emoji));
                        break;
                    case BlockType.EmeraldKey:
                        button.WithStyle(ButtonStyle.Success)
                            .WithEmote(Tools.ParseEmote(_items[17].Emoji));
                        break;
                    case BlockType.SapphireKey:
                        button.WithStyle(ButtonStyle.Success)
                            .WithEmote(Tools.ParseEmote(_items[18].Emoji));
                        break;
                    case BlockType.RubyKey:
                        button.WithStyle(ButtonStyle.Success)
                            .WithEmote(Tools.ParseEmote(_items[19].Emoji));
                        break;
                    case BlockType.AmberKey:
                        button.WithStyle(ButtonStyle.Success)
                            .WithEmote(Tools.ParseEmote(_items[20].Emoji));
                        break;
                    case BlockType.Gold:
                        button.WithStyle(ButtonStyle.Success)
                            .WithEmote(Tools.ParseEmote(_items[54].Emoji));
                        break;
                    default:
                        button.WithStyle(ButtonStyle.Danger)
                            .WithEmote(Tools.ParseEmote("<:stone:1287086951215796346>"));
                        Console.WriteLine("Block not accounted for in block.Type switch in /mine handler");
                        break;
                }
                if (Tools.AprilFoolsYear() == 2025 && block.Type != BlockType.Air)
                {
                    button.WithStyle(ButtonStyle.Primary)
                        .WithEmote(Emoji.Parse(":question:"));
                }
                if (block.Type != BlockType.Air && block.Left >= 0 && block.MinerID != user.ID)
                {
                    button.WithStyle(ButtonStyle.Danger);
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
        await user.Save();
        return new Tuple<bool, string, Embed, MessageComponent, string>(true, "Mine", embay, buttons.Build(), blocks);
    }
    private async Task<Tuple<string, Embed, MessageComponent>> FurnacesRaw(ulong userId, bool emoj)
    {
        User user = await GetUser(userId);
        int craftSlots = Tools.CharmEffect(["FurnaceSlots"], _items, user) + FurnaceConst;
        user.CheckFurnaces(craftSlots);
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
                    $" > Crafting: {furnace.Amount}{itemText}\n > Progress: {Tools.ProgressBar((int)(furnace.TimeRequired - furnace.TimeLeft), (int)furnace.TimeRequired, (int)user.GetSetting("progressBar", 5))}");
            }
        }

        ComponentBuilder components = new ComponentBuilder();
        ActionRowBuilder refresh = new ActionRowBuilder()
            .WithButton("Refresh", "craft-home", ButtonStyle.Secondary)
            .WithButton("View Cue", "craft-cue|view", ButtonStyle.Secondary);
        ActionRowBuilder favorites = new ActionRowBuilder()
            .WithButton("Favorites", "craft-page|fav", ButtonStyle.Success);
        ActionRowBuilder recents = new ActionRowBuilder()
            .WithButton("Recents", "craft-page|recent", ButtonStyle.Success);
        ActionRowBuilder craftable = new ActionRowBuilder()
            .WithButton("Max Craftable", "craft-page|craftable", ButtonStyle.Success);
        List<int> faveRecipes = user.GetListData("craft_favorites");
        for (int i = 0; i < 4 && i < faveRecipes.Count; i++)
        {
            Item crafted = _items[_craftingRecipes[faveRecipes[i]].ItemCrafted];
            favorites.WithButton(new ButtonBuilder().WithLabel(crafted.Name).WithStyle(ButtonStyle.Primary)
                .WithEmote(Tools.ParseEmote(crafted.Emoji)).WithCustomId($"craft-recipe|{faveRecipes[i]}|f"));
        }
        List<int> recentRecipes = user.GetListData("craft_recents");
        for (int i = 0; i < 4 && i < recentRecipes.Count; i++)
        {
            Item crafted = _items[_craftingRecipes[recentRecipes[i]].ItemCrafted];
            recents.WithButton(new ButtonBuilder()
                .WithLabel(crafted.Name)
                .WithStyle(ButtonStyle.Primary)
                .WithEmote(Tools.ParseEmote(crafted.Emoji))
                .WithCustomId($"craft-recipe|{recentRecipes[i]}|r"));
        }
        List<CraftingRecipe> craftableRecipes = _craftingRecipes.ToArray().ToList();
        craftableRecipes.Sort((recipeA, recipeB) => (recipeB.AmountCraftable(user) - recipeA.AmountCraftable(user)));
        for (int i = 0; i < 4 && i < craftableRecipes.Count; i++)
        {
            Item crafted = _items[craftableRecipes[i].ItemCrafted];
            craftable.WithButton(new ButtonBuilder()
                .WithLabel(crafted.Name)
                .WithStyle(ButtonStyle.Primary)
                .WithEmote(Tools.ParseEmote(crafted.Emoji))
                .WithCustomId($"craft-recipe|{craftableRecipes[i].ID}|c"));
        }

        components.AddRow(refresh);
        components.AddRow(favorites);
        components.AddRow(recents);
        components.AddRow(craftable);
        await user.Save();
        return new Tuple<string, Embed, MessageComponent>("Crafting text:", embay.Build(), components.Build());
    }
    private async Task<Tuple<string, Embed, MessageComponent>> ShopRaw(ulong userID, bool emoj)
    {
        User user = await GetUser(userID);
        EmbedBuilder embay = new EmbedBuilder()
            .WithTitle("Drops")
            .WithDescription("View all the currently active drops\n > ||You're locked to 5 sets due to discord restrictions||")
            .WithColor(new Color((uint) user.GetSetting("uiColor", 3287295)));
        ComponentBuilder components = new ComponentBuilder();
        ActionRowBuilder tokenShop = new ActionRowBuilder();
        int tokens = 1;
        for (int i = 0; i < 5; i++)
        {
            if (_tokenShop.PurchasedReward(user.ID, i)) { tokens *= 10; continue;}
            DailyTokenRewards rewards = _tokenShop.Rewards[i];
            string rewardString = string.Empty;
            foreach (DailyTokenReward reward in rewards.Rewards)
            {
                switch (reward.Type)
                {
                    case DailyTokenRewardType.None:
                        Console.WriteLine("Bugged daily token shop: DailyTokenRewardType.None found");
                        break;
                    case DailyTokenRewardType.Money:
                        if (rewardString != string.Empty) rewardString += "\n";
                        rewardString += $" > {reward.Amount}{_currency[i]}";
                        break;
                    case DailyTokenRewardType.Items:
                        if (reward is DailyTokenRewardItem rewardItem)
                        {
                            if (rewardString != string.Empty) rewardString += "\n";
                            rewardString += $" > {rewardItem.Amount}{_items[rewardItem.Item].Emoji}";
                            break;
                        }
                        Console.WriteLine("Bugged daily token shop: DailyTokenRewardType.Items is not a member of DailyTokenRewardItem");
                        break;
                    default:
                        Console.WriteLine("Bugged daily token shop: reward does not have a valid type");
                        break;
                }
            }
            if (rewardString.Length <= 0) continue;
            embay.AddField($"Exchange {tokens}{_items[21].Emoji} For:", rewardString+"\n > *new rewards daily*");
            tokenShop.WithButton($"x{tokens}", $"exchange-token|{i}", ButtonStyle.Primary, Emoji.Parse(_items[21].Emoji));
            tokens *= 10;
        }
        if (tokenShop.Components.Count > 0) components.AddRow(tokenShop);
        foreach (Drop drop in _drops)
        {
            if (!drop.Published) continue;
            if (drop.Out()) continue;
            ActionRowBuilder row = new ActionRowBuilder();
            for (int i = 0; i <= 4; i++)
            {
                if (drop.Left[i] == 0) continue;
                Item item = _items[drop.Items[i]];
                string currency = emoj ? _currency[drop.Price[i][1]] : _currencyNoEmoji[drop.Price[i][1]];
                string fieldText = $"{drop.Descriptions[i].Replace("$iDescription", item.Description)}\n > *Only {drop.Left[i]} left*.\n > *Price*: {drop.Price[i][0]}{currency}";
                if (!drop.Collectable) fieldText += "\n > **This item may be sold again or available in other parts of the bot.**";
                embay.AddField(item.Name, fieldText);
                IEmote emote;
                if (Emote.TryParse(item.Emoji, out Emote parsedEmote))
                {
                    emote = parsedEmote;
                }
                else
                {
                    emote = Emoji.Parse(item.Emoji);
                }
                row.WithButton($"Buy", $"drop-page|{drop.DropID}|{i}", ButtonStyle.Secondary, emote: emote);
            }
            components.AddRow(row);
            if (components.ActionRows.Count >= 5) break;
        }
        if (embay.Fields.Count == 0)
        {
            embay.WithTitle("Out of stock!")
                .WithDescription("All the drops are out of stock.");
        }
        return new Tuple<string, Embed, MessageComponent>("Shop", embay.Build(), components.Build());
    }
    private async Task<Tuple<bool, string, Embed?, MessageComponent?>> EventMineRaw(ulong userId, ulong serverId)
    {
        User user = await GetUser(userId);
        Server server = await GetServer(serverId);
        if (server.OngoingPortalEvent is null)
        {
            return new Tuple<bool, string, Embed?, MessageComponent?>(true, "No Portal Event in progress", null, null);
        }
        Server.PortalEvent.PlayerMineLocation location = server.OngoingPortalEvent.GetMineLocation(user.ID);
        MineChunk chunk = server.OngoingPortalEvent.Chunk;
        string description = location.Mining switch
        { 
            false => "Click any button to start mining!",
            true => "You are currently mining a block."
        };
        int mineY = location.Y;
        int top = mineY-2;
        if (top <= 0)
        {
            top = 0;
        }
        int mineX = location.X;
        int left = 0;
        if (top + 5 > chunk.Blocks.Length)
        {
            top = chunk.Blocks.Length - 5;
        }
        ComponentBuilder buttons = new ComponentBuilder();
        for (int i = 0; i < 5; i++)
        {
            ActionRowBuilder row = new ActionRowBuilder();
            for (int j = 0; j < 5; j++)
            {
                ButtonBuilder button = new ButtonBuilder();
                int y = top + i;
                int x = left + j;
                button.WithCustomId($"serverEvent-mine|{x}|{y}");
                MineBlock block = chunk.GetBlock(x, y);
                button.WithDisabled(Math.Abs(mineX - x) + Math.Abs(mineY - y) != 1);
                if (x == mineX && y == mineY)
                {
                    button.WithStyle(ButtonStyle.Secondary)
                        .WithEmote(Tools.ParseEmote("<:you:1287157766871580833>"));
                    row.AddComponent(button.Build());
                    continue;
                }
                switch (block.Type)
                {
                    case BlockType.Air:
                        button.WithStyle(ButtonStyle.Secondary)
                            .WithEmote(Tools.ParseEmote("<:air:1287157701905743903>"));
                        break;
                    case BlockType.Stone:
                        button.WithStyle(ButtonStyle.Primary)
                            .WithEmote(Tools.ParseEmote("<:stone:1287086951215796346>"));
                        break;
                    case BlockType.EventScroll:
                        button.WithStyle(ButtonStyle.Primary)
                            .WithEmote(Tools.ParseEmote(_items[52].Emoji));
                        break;
                    case BlockType.Sapphires:
                        button.WithStyle(ButtonStyle.Success)
                            .WithEmote(Tools.ParseEmote(_currency[2]));
                        break;
                    case BlockType.Rubies:
                        button.WithStyle(ButtonStyle.Success)
                            .WithEmote(Tools.ParseEmote(_currency[3]));
                        break;
                    case BlockType.Amber:
                        button.WithStyle(ButtonStyle.Success)
                            .WithEmote(Tools.ParseEmote(_currency[4]));
                        break;
                    case BlockType.RubyCoin:
                        button.WithStyle(ButtonStyle.Success)
                            .WithEmote(Tools.ParseEmote(_items[3].Emoji));
                        break;
                    case BlockType.SapphireKey:
                        button.WithStyle(ButtonStyle.Success)
                            .WithEmote(Tools.ParseEmote(_items[18].Emoji));
                        break;
                    case BlockType.Gold:
                        button.WithStyle(ButtonStyle.Success)
                            .WithEmote(Tools.ParseEmote(_items[54].Emoji));
                        break;
                    case BlockType.Portal:
                        button.WithStyle(ButtonStyle.Success)
                            .WithEmote(Tools.ParseEmote(_items[53].Emoji));
                        break;
                    default:
                        button.WithStyle(ButtonStyle.Danger)
                            .WithEmote(Tools.ParseEmote("<:stone:1287086951215796346>"));
                        Console.WriteLine("Block not accounted for in block.Type switch in /event handler");
                        break;
                }
                if (block.Type != BlockType.Air && block.Left >= 0 && block.MinerID != user.ID)
                {
                    button.WithStyle(ButtonStyle.Danger);
                }
                row.AddComponent(button.Build());
            }
            buttons.AddRow(row);
        }
        Embed embay = new EmbedBuilder()
            .WithTitle("Mine!")
            .WithDescription(description)
            .WithFooter("Someone used a portal to allow this server to access this special realm. Mine it until it all gets destroyed!")
            .Build();
        await user.Save();
        return new Tuple<bool, string, Embed?, MessageComponent?>(false, "See the embed and buttons", embay, buttons.Build());
    }
    
    
    private async Task InventoryButton(SocketMessageComponent component, string settings)
    {
        ulong id = component.User.Id;
        try
        {
            ((User)await GetUser(id)).ItemAmount(_items.Count - 1);
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
        Console.WriteLine(transaction);
        bool multiUpgrade = transaction >= 100;
        if (multiUpgrade) transaction -= 100;
        bool multiUpgradeConfirmed = transaction >= 50;
        if (multiUpgradeConfirmed) transaction -= 50;
        int bottomValue = transaction % 4;
        int type = transaction / 4;
        int upgradeFor = 11;
        int downgradeTo = 9;
        int upgradeForB = 105;
        int downgradeToB = 95;
        if (Tools.CharmEffect(["BetterBankTrades"], _items, user) >= 1)
        {
            upgradeFor = 10;
            downgradeTo = 10;
            upgradeForB = 100;
            downgradeToB = 100;
        }
        //GET CONFIRMATION MESSAGE
        if (multiUpgrade || multiUpgradeConfirmed)
        {
            int transactionsPerStoneCoin = (int)Math.Pow(4, (3 - bottomValue)) * (type switch { 0 => 5, 1 => 15, 2 => 1, 3 => 3, _ => 0 });
            Tuple<int, int> cost = new(bottomValue + (type % 2), type switch { 0 => upgradeFor, 1 => 1, 2 => upgradeForB, 3 => 10, _ => 9999999 });
            Tuple<int, int> reward = new(bottomValue + ((type + 1) % 2), type switch { 0 => 1, 1 => downgradeTo, 2 => 10, 3 => downgradeToB, _ => 0});
            int amountPossible = user.Gems[cost.Item1] / cost.Item2;
            int stoneCoins = (amountPossible + (transactionsPerStoneCoin - 1)) / transactionsPerStoneCoin;
            if (!multiUpgradeConfirmed)
            {
                await component.RespondAsync(
                    $"Would you like to spend {stoneCoins}{_items[46].Emoji} to exchange {amountPossible * cost.Item2}{_currency[cost.Item1]} for {amountPossible * reward.Item2}{_currency[reward.Item1]}?",
                    components: new ComponentBuilder()
                        .WithButton("Confirm", $"bank-{transaction + 50}|{(showEmojis ? "yes" : "no")}")
                        .WithButton("Cancel", "basic-cancel|auto_delete,everyone", ButtonStyle.Secondary).Build(),
                    ephemeral: true);
                return;
            }
            if (user.Inventory[46] < stoneCoins)
            {
                await component.RespondAsync($"You do not have enough stone coins to complete this transaction. You have {user.Inventory[46]}/{stoneCoins} stone coins."
                    + $"\n||This message will auto-delete in <t:{Tools.CurrentTime() + 15}:R>||");
                await Task.Delay(14900);
                await component.DeleteOriginalResponseAsync();
                return;
            }
            user.Add(-1 * amountPossible * cost.Item2, cost.Item1);
            user.Add(amountPossible * reward.Item2, reward.Item1);
            user.GainItem(46, -stoneCoins);
            await component.RespondAsync(
                $"You have spent {stoneCoins}{_items[46].Emoji} to exchange {amountPossible * cost.Item2}{_currency[cost.Item1]} for {amountPossible * reward.Item2}{_currency[reward.Item1]}!",
                ephemeral: true);
            return;
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

                user.Add(-1 * upgradeFor, bottomValue);
                user.Add(1, bottomValue + 1);
                await user.Save();
                break;
            case 1:
                if (user.Gems[bottomValue + 1] < 1)
                {
                    await component.RespondAsync($"You can't afford this trade. This message will auto-delete in <t:{Tools.CurrentTime()+8}:R>", ephemeral: true);
                    await Task.Delay(7800);
                    await component.DeleteOriginalResponseAsync();
                    return;
                }
                user.Add(-1, bottomValue + 1);
                user.Add(downgradeTo, bottomValue);
                await user.Save();
                break;
            case 2:
                if (user.Gems[bottomValue] < upgradeForB)
                {
                    await component.RespondAsync($"You can't afford this trade. This message will auto-delete in <t:{Tools.CurrentTime()+8}:R>", ephemeral: true);
                    await Task.Delay(7800);
                    await component.DeleteOriginalResponseAsync();
                    return;
                }

                user.Add(-1 * upgradeForB, bottomValue);
                user.Add(10, bottomValue + 1);
                await user.Save();
                break;
            case 3:
                if (user.Gems[bottomValue + 1] < 10)
                {
                    await component.RespondAsync($"You can't afford this trade. This message will auto-delete in <t:{Tools.CurrentTime()+8}:R>", ephemeral: true);
                    await Task.Delay(7800);
                    await component.DeleteOriginalResponseAsync();
                    return;
                }
                user.Add(-10, bottomValue + 1);
                user.Add(downgradeToB, bottomValue);
                await user.Save();
                break;
        }
        //step 2: refresh bank if original user, otherwise send message.
        if (!Tools.VerifyOriginalUse(component))
        {
            await component.RespondAsync(await BalanceRaw(showEmojis, component.User.Id, "The transaction was completed successfully!\n**Your balance**:") + $"\n||This message will auto-delete in <t:{Tools.CurrentTime() + 15}:R>||");
            await Task.Delay(14900);
            await component.DeleteOriginalResponseAsync();
        }
        else
        {
            Tuple<EmbedBuilder, ComponentBuilder, string> dat = await BankRaw(showEmojis, user.ID, false);
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
        if (!Tools.VerifyOriginalUse(component))
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
        int x;
        int y;
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
        if (user.GetData("mining", 0) == 1)
        {
            block = _mineData.GetBlock(user.GetData("miningAtX", x), user.GetData("miningAtY", y));
            string progressBar = Tools.ProgressBar((int)block.Durability - (block.Left ?? block.GetLeft()), (int)block.Durability, (int)user.GetSetting("progressBar", 5));
            if ((block.Left ?? block.GetLeft()) == 0)
            {
                user.SetData("mining", 0);
                await user.Save();
            }
            string etl;
            int secondsLeft = (block.Left ?? block.GetLeft())/(5+Tools.CharmEffect(["minePower"], _items, user));
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
                    etl = $"{hours} hours, {minutes} minutes, and {secondsLeft} seconds left";
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
            if (Tools.CharmEffect(["betterAutoRefresh"], _items, user) == 0) 
                return;
            while (secondsLeft >= 0)
            {
                await Task.Delay(20 * secondsLeft + 1000);
                user = await GetUser(component.User.Id);
                block = _mineData.GetBlock(user.GetData("miningAtX", x), user.GetData("miningAtY", y));
                secondsLeft = (block.Left ?? block.GetLeft())/(5+ Tools.CharmEffect(["minePower"], _items, user));
                progressBar = Tools.ProgressBar((int)block.Durability - (block.Left ?? block.GetLeft()), (int)block.Durability, (int)user.GetSetting("progressBar", 5));
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
                        etl = $"{hours} hours, {minutes} minutes, and {secondsLeft} seconds left";
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
                try
                {
                    await component.ModifyOriginalResponseAsync((properties) =>
                    {
                        properties.Content =
                            $"Time mining auto-refresh!\n > **Progress**: {progressBar}\n > **Estimated Time Remaining**: {etl}";
                    });
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    await user.Save();
                    return;
                }
                if (block.Type != BlockType.Air) continue;
                await component.DeleteOriginalResponseAsync();
                await user.Save();
                return;
            }
            await user.Save();
            return;
        }
        if (block.Type == BlockType.Air)
        {
            user.SetData("mineX", x);
            user.SetData("mineY", y);
            Tuple<bool, string, Embed, MessageComponent, string> result = await MineRaw(user.ID, true);
            await component.UpdateAsync((properties) => { 
                properties.Embed = result.Item3;
                properties.Content = result.Item2;
                properties.Components = result.Item4;
            });
            await user.Save();
        }
        else
        {
            try
            {
                block.Mine(component.User.Id, 0); //Mining with power will be determined during MineTick();
                user.SetData("mining", 1);
                user.SetData("miningAtX", x);
                user.SetData("miningAtY", y);
                await _mineData.GetChunk(x / 30).Save(x / 30);
                await user.Save();
                await component.RespondAsync("Successfully started mining this block!", ephemeral: true);
                if (Tools.CharmEffect(["betterAutoRefresh", "mineAutoRefresh"], _items, user) == 0) {return;}
                await Task.Delay(1000*((block.Left ?? block.GetLeft())/(5+Tools.CharmEffect(["minePower"], _items, user))));
                while (block.Type != BlockType.Air)
                {
                    await Task.Delay(300);
                    block = _mineData.GetBlock(x, y);
                }
                Tuple<bool, string, Embed, MessageComponent, string> result = await MineRaw(user.ID, true);
                await component.Message.ModifyAsync((properties) =>
                {
                    properties.Content = result.Item2 + " (auto-refresh)";
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
                await component.RespondAsync($"```\n{e}\n```", ephemeral:true);
            }
        }
    }
    private async Task CraftButton(SocketMessageComponent component, string settings)
    {
        string[] args = settings.Split("|");
        ulong id = component.User.Id;
        CachedUser user = await GetUser(id);
        if (!Tools.VerifyOriginalUse(component))
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
                        List<int> faveRecipes = user.User.GetListData("craft_favorites");
                        MessageComponent faveButtons = PageLogic(faveRecipes);
                        await component.UpdateAsync((properties) =>
                        {
                            properties.Components = faveButtons;
                        });
                        break;
                    case "recent":
                        List<int> recentRecipes = user.User.GetListData("craft_recents");
                        MessageComponent recentButtons = PageLogic(recentRecipes);
                        await component.UpdateAsync((properties) =>
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
                        profitRecipes.Sort((a,b) =>
                        {
                            CraftingRecipe recipeA = _craftingRecipes[a];
                            CraftingRecipe recipeB = _craftingRecipes[b];
                            return recipeA.CompareRecipeProfit(recipeB, _items);
                        });
                        MessageComponent profitButtons = PageLogic(profitRecipes);
                        await component.UpdateAsync((properties) =>
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
                        craftableRecipes.Sort((a, b) => (_craftingRecipes[b].AmountCraftable(user) - _craftingRecipes[a].AmountCraftable(user)));
                        MessageComponent craftableButtons = PageLogic(craftableRecipes);
                        await component.UpdateAsync((properties) =>
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
                IEmote emoj = Tools.ParseEmote(crafted.Emoji);
                int craftable = recipe.AmountCraftable(user);
                embay.WithTitle($"Crafting Recipe {recipe.ID}");
                embay.AddField("Details", recipe.ToString(_items, _currency));
                ActionRowBuilder topRow = new ActionRowBuilder()
                    .WithButton("home", "craft-home", ButtonStyle.Secondary)
                    .WithButton("<--", $"craft-recipe|{recipe.ID - 1}", disabled:recipe.ID <= 0);
                if ((user.User.GetListData("craft_favorites")).Contains(recipe.ID))
                    topRow.WithButton("un-favorite", $"craft-fav|{recipe.ID}|n", ButtonStyle.Danger);
                else topRow.WithButton("favorite", $"craft-fav|{recipe.ID}|y", ButtonStyle.Success);
                topRow.WithButton("-->", $"craft-recipe|{recipe.ID + 1}", disabled: recipe.ID >= (_craftingRecipes.Count-1));
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
                List<int> recent = user.User.GetListData("craft_recents");
                recent.RemoveAll((i) => i == recipeCrafted.ID);
                recent.Insert(0, recipeCrafted.ID);
                await component.RespondAsync($"Successfully started crafting {int.Parse(args[2])*recipeCrafted.AmountCrafted}x{itemCrafted.Emoji}", ephemeral: true);
                break;
            case "fav":
                if (args.Length < 3) throw new ButtonValueError();
                if (!int.TryParse(args[1], out int recipeID)) throw new ButtonValueError();
                List<int> favorites = user.User.GetListData("craft_favorites");
                switch (args[2])
                {
                    case "y":
                        if (favorites.Contains(recipeID))
                        {
                            await component.RespondAsync($"Crafting recipe {recipeID} is already favorited", ephemeral:true);
                            return;
                        }
                        favorites.Add(recipeID);
                        user.User.SetListData("craft_favorites", favorites);
                        await component.RespondAsync($"Crafting recipe {recipeID} successfully favorited!", ephemeral:true);
                        break;
                    case "n":
                        if (!favorites.Contains(recipeID))
                        {
                            await component.RespondAsync($"Crafting recipe {recipeID} is already not favorited", ephemeral:true);
                            return;
                        }
                        favorites.Remove(recipeID);
                        user.User.SetListData("craft_favorites", favorites);
                        await component.RespondAsync($"Crafting recipe {recipeID} successfully un-favorited!", ephemeral:true);
                        break;
                    default:
                        throw new ButtonValueError();
                }
                break;
            case "cue":
                if (args.Length <= 1) throw new ButtonValueError();
                switch (args[1])
                {
                    case "view":
                        Dictionary<int, int> nextCraftingDict = new ();
                        List<int> nextCraftingOrder = new();
                        foreach ((int cueRecipeID, int amount) in user.NextCrafting ?? new List<Tuple<int, int>>())
                        {
                            if (nextCraftingDict.TryAdd(cueRecipeID, amount)) nextCraftingOrder.Add(cueRecipeID);
                            else nextCraftingDict[cueRecipeID] += amount;
                        }
                        List<Tuple<int, int>> nextCrafting = new();
                        foreach (int dictKey in nextCraftingOrder)
                        {
                            nextCrafting.Add(new Tuple<int, int>(dictKey, nextCraftingDict[dictKey]));
                        }
                        user.NextCrafting = nextCrafting;
                        EmbedBuilder cueViewEmbay = new EmbedBuilder()
                            .WithTitle("Crafting Cue")
                            .WithColor(new Color((uint) user.User.GetSetting("uiColor", 3287295)));
                        string cueViewText = "Items waiting to be crafted:";
                        foreach ((int cueRecipeID, int amount) in user.NextCrafting)
                        {
                            CraftingRecipe cueViewRecipe = _craftingRecipes[cueRecipeID];
                            cueViewText += $"\n > {amount*cueViewRecipe.AmountCrafted}{_items[cueViewRecipe.ItemCrafted].Emoji} (Recipe {cueViewRecipe.ID})";
                        }
                        cueViewEmbay.WithDescription(cueViewText);
                        await component.UpdateAsync((properties) =>
                        {
                            properties.Embed = cueViewEmbay.Build();
                            properties.Components = new ComponentBuilder()
                                .WithButton("home", "craft-home", ButtonStyle.Secondary)
                                .WithButton("clear", $"craft-cue|clear|{user.NextCrafting.Count}", ButtonStyle.Danger, disabled:user.NextCrafting.Count <= 0).Build();
                        });
                        break;
                    case "clear":
                        if (args.Length <= 2) throw new ButtonValueError();
                        if (!int.TryParse(args[2], out int cueLength)) throw new ButtonValueError();
                        if ((user.NextCrafting ?? new List<Tuple<int, int>>()).Count != cueLength)
                        {
                            await component.RespondAsync(
                                "This cue page is outdated (you have started crafting something after you pulled this up or a craft has been moved to the furnaces)",
                                ephemeral:true);
                            await Task.Delay(12000);
                            await component.DeleteOriginalResponseAsync();
                            return;
                        }
                        user.NextCrafting = null;
                        await component.RespondAsync("Cleared cue! The next buttons you click to start crafting will begin crafting as soon as the current craft finishes", ephemeral:true);
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
                            .WithEmote(Tools.ParseEmote(itemCrafted.Emoji))
                            .WithStyle(ButtonStyle.Primary)
                            .WithCustomId($"craft-recipe|{t}"));
                    break;
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
        if (!Tools.VerifyOriginalUse(component))
        {
            await component.RespondAsync("This is not your work page. You cannot click any buttons", ephemeral: true);
            return;
        }
        CachedUser user = await GetUser(component.User.Id);
        if (!int.TryParse(args[1], out int workID)) throw new ButtonValueError();
        if (workID != user.LastWork)
        {
            EmbedBuilder wrongIdEmbay = new EmbedBuilder()
                .WithTitle("Old Work")
                .WithDescription("This button is outdated.");
            await component.UpdateAsync((properties) => { properties.Embed = wrongIdEmbay.Build(); properties.Components = null; });
            return;
        }
        if (workID == 0)
        {
            EmbedBuilder hackerEmbay = new EmbedBuilder()
                .WithTitle("Busted!")
                .WithDescription("Unless there is a bug (unlikely) you modified the custom ID of a button. That's cheating!");
            await component.UpdateAsync((properties) => { properties.Embed = hackerEmbay.Build(); properties.Components = null; });
            return;
        }
        user.LastWork = 0;
        int mn = 12 + Tools.CharmEffect(["WorkMin", "Work", "GrindMin", "Grind", "Positive"], _items, user);
        int mx = 20 + Tools.CharmEffect(["WorkMax", "Work", "GrindMax", "Grind", "Positive"], _items, user);
        int amnt = _rand.Next(mn, mx);
        switch (args[0])
        {
            case "success":
                user.User.Add(amnt, 1);
                string text = $"You gained {amnt} Emeralds.";
                if (args[2] == "True")
                {
                    text = $"You gained {amnt}{_currency[1]}!!!";
                }
                user.User.Increase("commands",1);
                user.User.Increase("earned", amnt*10);
                user.User.Increase("workSuccess", 1);
                user.User.Increase("work", 1);
                List<string> workChoices = _dataLists["WorkEffect"];
                EmbedBuilder embay = new EmbedBuilder()
                    .WithTitle(text)
                    .WithDescription(workChoices[_rand.Next(0, workChoices.Count)])
                    .WithColor(new Color(50, 255, 100));
                if (_rand.Next(0, 4) == 3)
                {
                    user.User.GainItem(1, 1);
                    embay.AddField($"You gained 1{_items[1].Emoji}!", " > This happens 25% of the time.");
                }
                await component.UpdateAsync((properties) =>
                {
                    properties.Embed = embay.Build();
                    properties.Components = null;
                });
                break;
            case "failure":
                Console.WriteLine(amnt);
                amnt = _rand.Next((amnt/3), (amnt*3)/4);
                Console.WriteLine(amnt);
                user.User.Add(amnt, 1);
                string textFail = $"You gained {amnt} emeralds.";
                if (args[2] == "True")
                {
                    textFail = $"You gained {amnt}{_currency[1]}!!!";
                }
                user.User.Increase("commands",1);
                user.User.Increase("earned", amnt*10);
                user.User.Increase("workSuccess", 1);
                user.User.Increase("work", 1);
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
    private async Task DropButton(SocketMessageComponent component, string settings)
    {
        Task<CachedUser> userTask = GetUser(component.User.Id);
        string[] args = settings.Split("|");
        if (args.Length < 1) throw new ButtonValueError();
        switch (args[0])
        {
            case "buy":
                if (args.Length < 4) throw new ButtonValueError();
                if (!int.TryParse(args[1], out int dropID)) throw new ButtonValueError();
                if (!int.TryParse(args[2], out int slot)) throw new ButtonValueError();
                if (!int.TryParse(args[3], out int amount)) throw new ButtonValueError();
                Drop drop = _drops[dropID];
                if (drop.Left[slot] < amount)
                {
                    EmbedBuilder embayOutOfStock = new EmbedBuilder()
                        .WithTitle("Out of stock")
                        .WithDescription(drop.Left[slot] <= 0 ? "This drop is now out of stock" : "There is not enough of this drop to buy at this amount")
                        .WithColor(new Color((uint) ((await userTask).User.GetSetting("uiColor",3287295))));
                    await component.RespondAsync(embed:embayOutOfStock.Build(), ephemeral: true);
                    return;
                }
                int[] price = drop.Price[slot];
                User user = await userTask;
                if (user.Gems[price[1]] < price[0]*amount)
                {
                    EmbedBuilder embayTooExpensive = new EmbedBuilder()
                        .WithTitle("You can't afford this")
                        .WithDescription("You don't have enough gems for this drop")
                        .WithColor(new Color((uint) (user.GetSetting("uiColor", 3287295))));
                    await component.RespondAsync(embed:embayTooExpensive.Build(), ephemeral: true);
                    return;
                }
                drop.Left[slot]-= amount;
                user.Add(-1 * price[0] * amount, price[1]);
                user.GainItem(drop.Items[slot], amount);
                string each = amount > 1 ? " (each)" : string.Empty;
                await component.RespondAsync($"You have successfully purchased {amount}{_items[drop.Items[slot]].Emoji} for {price[0]}{_currency[price[1]]}{each}", ephemeral: true);
                break;
            case "page":
                if (args.Length < 3) throw new ButtonValueError();
                if (component.User.Id != component.Message.Interaction.User.Id)
                {
                    await component.RespondAsync("This is not your shop page. You cannot click page buttons", ephemeral: true);
                    return;
                }
                if (!int.TryParse(args[1], out int dropIDPage)) throw new ButtonValueError();
                if (!int.TryParse(args[2], out int slotPage)) throw new ButtonValueError();
                Drop dropPage = _drops[dropIDPage];
                Item item = _items[dropPage.Items[slotPage]];
                int maxAmount = dropPage.Left[slotPage];
                if (maxAmount <= 0)
                {
                    await component.RespondAsync("This drop is out of stock", ephemeral: true);
                    break;
                }
                User usr = await userTask;
                int maxAmountBTemp = usr.Gems[dropPage.Price[slotPage][1]] / dropPage.Price[slotPage][0];
                if (maxAmountBTemp < maxAmount)
                {
                    maxAmount = maxAmountBTemp;
                }
                EmbedBuilder embay = new EmbedBuilder()
                    .WithTitle($"Drop {dropIDPage} item {slotPage} ({item.Name})")
                    .WithDescription(dropPage.Descriptions[slotPage].Replace("$iDescription", item.Description));
                ActionRowBuilder topRow = new ActionRowBuilder()
                    .WithButton("home", "drop-home", ButtonStyle.Secondary);
                IEmote emoj;
                if (Emote.TryParse(item.Emoji, out Emote tempEmote)) emoj = tempEmote;
                else emoj = Emoji.Parse(item.Emoji);
                ActionRowBuilder buyRow = new ActionRowBuilder()
                    .WithButton("x1", $"drop-buy|{dropIDPage}|{slotPage}|1", ButtonStyle.Primary, emoj, disabled: maxAmount < 1)
                    .WithButton("x5", $"drop-buy|{dropIDPage}|{slotPage}|5", ButtonStyle.Primary, emoj, disabled: maxAmount < 5)
                    .WithButton("x10", $"drop-buy|{dropIDPage}|{slotPage}|10", ButtonStyle.Primary, emoj, disabled: maxAmount < 10)
                    .WithButton("x40", $"drop-buy|{dropIDPage}|{slotPage}|40", ButtonStyle.Primary, emoj, disabled: maxAmount < 40)
                    .WithButton($"x{maxAmount}", $"drop-buy|{dropIDPage}|{slotPage}|{maxAmount}|c", ButtonStyle.Primary, emoj, disabled: maxAmount <= 0);
                MessageComponent pageComponent = new ComponentBuilder().AddRow(topRow).AddRow(buyRow).Build();
                Embed embedPage = embay.Build();
                await component.UpdateAsync((properties) =>
                {
                    properties.Embed = embedPage;
                    properties.Components = pageComponent;
                });
                break;
            case "home":
                if (component.User.Id != component.Message.Interaction.User.Id)
                {
                    await component.RespondAsync("This is not your shop page. You cannot click page buttons", ephemeral: true);
                    return;
                }
                Tuple<string, Embed, MessageComponent> shop = await ShopRaw(component.User.Id, true);
                await component.UpdateAsync((properties) =>
                {
                    properties.Content = shop.Item1;
                    properties.Embed = shop.Item2;
                    properties.Components = shop.Item3;
                });
                break;
            default:
                throw new ButtonValueError();
        }

        await (await userTask).User.Save();
    }
    private async Task NotificationButton(SocketMessageComponent component, string settings)
    {
        string[] args = settings.Split("|");
        CachedUser user = await GetUser(component.User.Id);
        if (component.Message.Interaction.User.Id != user.User.ID)
        {
            await component.RespondAsync("These are not your notifications", ephemeral:true);
            return;
        }
        if (args.Length < 3)
            throw new ButtonValueError();
        try
        {
            switch (args[0])
            {
                case "clear":
                    int notificationID = int.Parse(args[1]);
                    int toDelete = int.Parse(args[2]);
                    if (notificationID != user.NotificationsID || toDelete > user.Notifications.Count)
                    {
                        await component.RespondAsync("This is from an old notifications prompt (failure)", ephemeral:true);
                        await component.Message.ModifyAsync((properties) =>
                        {
                            properties.Components = null;
                            properties.Content = "edited";
                        });
                    }
                    user.NotificationsID++;
                    for (int i = 0; i < toDelete; i++)
                    {
                        user.Notifications.RemoveAt(0);
                    }
                    await component.UpdateAsync((properties) => { properties.Components = null;
                        properties.Embed = null;
                        properties.Content = "Cleared notifications";
                    });
                    break;
                default:
                    throw new ButtonValueError();
            }
        }
        catch (InvalidOperationException) { throw new ButtonValueError(); }
    }
    private async Task DefaultButton(SocketMessageComponent component, string settings)
    {
        string[] args = settings.Split("|");
        if (args.Length == 0) throw new ButtonValueError();
        string[] flags = args.Length > 1 ? args[1].Split(',') : [];
        switch (args[0])
        {
            case "cancel":
                if (!(args.Contains("everyone") || Tools.VerifyOriginalUse(component)))
                {
                    await component.RespondAsync("You cannot click this button", ephemeral:true);
                    return;
                }
                Embed embay = new EmbedBuilder().WithTitle("Canceled")
                    .WithDescription("This interaction has been canceled").WithColor(255, 0, 0).Build();
                await component.UpdateAsync((properties) =>
                {
                    if (!args.Contains("keep_text"))
                        properties.Content = string.Empty;
                    if (!args.Contains("keep_embeds"))
                        properties.Embed = embay;
                    else
                    {
                        List<Embed> embeds = properties.Embeds.Value.ToList();
                        embeds.Add(embay);
                        properties.Embeds = embeds.ToArray();
                    }
                    if (!args.Contains("keep_components"))
                        properties.Components = null;
                });
                if (!args.Contains("auto_delete")) break;
                await Task.Delay(8000);
                await component.Message.DeleteAsync();
                break;
            default:
                throw new ButtonValueError();
        }
    }
    private async Task KeyButton(SocketMessageComponent component, string settings)
    {
        string[] args = settings.Split("|");
        if (args.Length < 2) throw new ButtonValueError();
        User user = await GetUser(component.User.Id);
        switch (args[0])
        {
            case "diamond":
                if (!int.TryParse(args[1], out int amountD)) throw new ButtonValueError();
                if (user.Inventory[16] < amountD)
                {
                    await component.RespondAsync($"You don't have {amountD} diamond keys.", ephemeral: true);
                    return;
                }
                user.GainItem(16, -amountD);
                int[] itemsD = new int[_items.Count];
                for (int i = 0; i < amountD; i++)
                {
                    itemsD[Tools.GetCharm(_itemLists, 0, 5)]++;
                    itemsD[Tools.GetCharm(_itemLists, 0, 4)]++;
                }
                string toSendD = $"You used {amountD}{_items[16].Emoji} for:";
                for (int i = 0; i < itemsD.Length; i++)
                {
                    if (itemsD[i] <= 0) continue;
                    user.GainItem(i, itemsD[i]);
                    toSendD += $"\n > {itemsD[i]}{_items[i].Emoji}";
                }
                await component.RespondAsync(toSendD, ephemeral: true);
                await user.Save();
                break;
            case "emerald": 
                if (!int.TryParse(args[1], out int amountE)) throw new ButtonValueError();
                if (user.Inventory[17] < amountE)
                {
                    await component.RespondAsync($"You don't have {amountE} emerald keys.", ephemeral: true);
                    return;
                }
                user.GainItem(17, -amountE);
                int[] itemsE = new int[_items.Count];
                for (int i = 0; i < amountE; i++)
                {
                    itemsE[Tools.GetCharm(_itemLists, 0, 4)]++;
                    itemsE[Tools.GetCharm(_itemLists, 0, 4)]++;
                    itemsE[Tools.GetCharm(_itemLists, 0, 4)]++;
                    itemsE[Tools.GetCharm(_itemLists, 0, 3)]++;
                    itemsE[Tools.GetCharm(_itemLists, 0, 3)]++;
                    itemsE[Tools.GetCharm(_itemLists, 1, 3)]++;
                    itemsE[Tools.GetCharm(_itemLists, 1, 3)]++;
                    itemsE[Tools.GetCharm(_itemLists, 2, 4)]++;
                }
                string toSendE = $"You used {amountE}{_items[17].Emoji} for:";
                for (int i = 0; i < itemsE.Length; i++)
                {
                    if (itemsE[i] <= 0) continue;
                    user.GainItem(i, itemsE[i]);
                    toSendE += $"\n > {itemsE[i]}{_items[i].Emoji}";
                }
                await component.RespondAsync(toSendE, ephemeral: true);
                await user.Save();
                break;
            case "sapphire": 
                if (!int.TryParse(args[1], out int amountS)) throw new ButtonValueError();
                if (user.Inventory[18] < amountS)
                {
                    await component.RespondAsync($"You don't have {amountS} sapphire keys.", ephemeral: true);
                    return;
                }
                user.GainItem(18, -amountS);
                int[] itemsS = new int[_items.Count];
                for (int i = 0; i < amountS; i++)
                {
                    //`5|0`, `4|0`, `4|0`, `4|0`, `3|0`, `3|0`, `3|0`, `3|0`, `4|1`, `4|1`, `3|1`, `3|1`, `3|1`, `3|1`, `5|2`, `4|2`, `4|2`, `3|2`, `6|3`, and `5|3`
                    itemsS[Tools.GetCharm(_itemLists, 0, 5)]++;
                    itemsS[Tools.GetCharm(_itemLists, 0, 4)]++;
                    itemsS[Tools.GetCharm(_itemLists, 0, 4)]++;
                    itemsS[Tools.GetCharm(_itemLists, 0, 4)]++;
                    itemsS[Tools.GetCharm(_itemLists, 0, 3)]++;
                    itemsS[Tools.GetCharm(_itemLists, 0, 3)]++;
                    itemsS[Tools.GetCharm(_itemLists, 0, 3)]++;
                    itemsS[Tools.GetCharm(_itemLists, 0, 3)]++;
                    itemsS[Tools.GetCharm(_itemLists, 1, 4)]++;
                    itemsS[Tools.GetCharm(_itemLists, 1, 4)]++;
                    itemsS[Tools.GetCharm(_itemLists, 1, 3)]++;
                    itemsS[Tools.GetCharm(_itemLists, 1, 3)]++;
                    itemsS[Tools.GetCharm(_itemLists, 1, 3)]++;
                    itemsS[Tools.GetCharm(_itemLists, 1, 3)]++;
                    itemsS[Tools.GetCharm(_itemLists, 2, 5)]++;
                    itemsS[Tools.GetCharm(_itemLists, 2, 4)]++;
                    itemsS[Tools.GetCharm(_itemLists, 2, 4)]++;
                    itemsS[Tools.GetCharm(_itemLists, 2, 3)]++;
                    itemsS[Tools.GetCharm(_itemLists, 3, 6)]++;
                    itemsS[Tools.GetCharm(_itemLists, 3, 5)]++;
                }
                string toSendS = $"You used {amountS}{_items[18].Emoji} for:";
                for (int i = 0; i < itemsS.Length; i++)
                {
                    if (itemsS[i] <= 0) continue;
                    user.GainItem(i, itemsS[i]);
                    toSendS += $"\n > {itemsS[i]}{_items[i].Emoji}";
                }
                await component.RespondAsync(toSendS, ephemeral: true);
                await user.Save();
                break;
            case "ruby":
                string gained;
                if (user.Inventory[19] <= 0)
                {
                    await component.RespondAsync("You cannot afford this!", ephemeral: true);
                    return;
                }
                switch (args[1])
                {
                    case "key":
                        gained = $" > 8{_items[18].Emoji}\n > 64{_items[17].Emoji}\n > 512{_items[16].Emoji}";
                        user.GainItem(18, 8);
                        user.GainItem(17, 64);
                        user.GainItem(16, 512);
                        break;
                    case "coin":
                        gained = $" > 2{_items[3].Emoji}\n > 20{_items[2].Emoji}\n > 200{_items[1].Emoji}\n > 2,000{_items[0].Emoji}";
                        user.GainItem(3, 2);
                        user.GainItem(2, 20);
                        user.GainItem(1, 200);
                        user.GainItem(0, 2000);
                        break;
                    case "money":
                        gained = $" > 1{_currency[4]}\n > 11{_currency[3]}\n > 121{_currency[2]}\n > 1,331{_currency[1]}\n > 14,641{_currency[0]}";
                        user.Add(1, 4);
                        user.Add(11, 3);
                        user.Add(121, 2);
                        user.Add(1331, 1);
                        user.Add(14641, 0);
                        break;
                    default:
                        throw new ButtonValueError();
                }
                user.GainItem(19, -1);
                await component.RespondAsync($"You exchanged 1 {_items[19].Emoji} for:\n{gained}", ephemeral:true);
                break;
            case "amber":
                if (user.Inventory[20] <= 0)
                {
                    await component.RespondAsync("You cannot afford this!", ephemeral: true);
                    return;
                }
                int itemID = args[1] switch {"bolt" => 48, "star" => 49, "refresh" => 50, "coin" => 51, _ => throw new ButtonValueError()};
                user.GainItem(20, -1);
                user.GainItem(itemID, 1);
                await user.Save();
                await component.RespondAsync($"You spent 1{_items[20].Emoji} for 1{_items[itemID].Emoji}", ephemeral:true);
                break;
            case "default":
                throw new ButtonValueError();
        }
    }
    private async Task ExchangeButton(SocketMessageComponent component, string settings)
    {
        string[] args = settings.Split("|");
        User user = await GetUser(component.User.Id);
        switch (args[0])
        {
            case "charm":
                if (!int.TryParse(args[1], out int rarity)) throw new ButtonValueError();
                List<Tuple<int, int>> convert = new List<Tuple<int, int>>();
                int total = 0;
                string rarityString = rarity switch
                { 0 => "CommonCharms", 1 => "UncommonCharms", 2 => "RareCharms", 3 => "EpicCharms", 4 => "LegendaryCharms",
                 _ => throw new ButtonValueError() };
                foreach (int item in _itemLists[rarityString].Where(item => user.Inventory[item] >= 2))
                {
                    convert.Add(new Tuple<int, int>(item, user.Inventory[item] -1));
                    total += user.Inventory[item] - 1;
                }
                if (user.Gems[rarity] < total)
                {
                    await component.RespondAsync("You don't have enough gems to do this", ephemeral:true);
                    return;
                }
                string exchange = $"You bought {total*2}{_items[rarity].Emoji} for \n > {total}{_currency[rarity]}";
                foreach (Tuple<int, int> pair in convert)
                {
                    exchange += $"\n > {pair.Item2}{_items[pair.Item1].Emoji}";
                    user.GainItem(pair.Item1, pair.Item2 * -1);
                }
                user.Add(-total, rarity);
                user.GainItem(rarity, total * 2);
                await component.RespondAsync(embed: new EmbedBuilder().WithTitle("Purchase Successful").WithDescription(exchange).Build(), ephemeral:true);
                break;
            case "token":
                if (!int.TryParse(args[1], out int value)) throw new ButtonValueError();
                if (_tokenShop.PurchasedReward(user.ID, value))
                {
                    await component.RespondAsync(
                        "You have already purchased this today. Check back tomorrow for new offers.", ephemeral: true);
                    return;
                }
                int tokensRequired = (int)Math.Pow(10, value);
                if (user.Inventory[21] < tokensRequired)
                {
                    await component.RespondAsync("You cannot afford this", ephemeral: true);
                    return;
                }
                user.GainItem(21, tokensRequired * -1);
                string tokenRewards = "You gained:";
                foreach (DailyTokenReward reward in _tokenShop.Rewards[value].Rewards)
                {
                    switch (reward.Type)
                    {
                        case DailyTokenRewardType.Money:
                            if (reward is not DailyTokenRewardMoney rewardMoney)
                            {
                                Console.WriteLine("Bugged Daily Token Shop");
                                break;
                            }
                            user.Add(reward.Amount, rewardMoney.Value);
                            tokenRewards += $"\n > {reward.Amount}{_currency[rewardMoney.Value]}";
                            break;
                        case DailyTokenRewardType.Items:
                            if (reward is not DailyTokenRewardItem rewardItem)
                            {
                                Console.WriteLine("Bugged Daily Token Shop");
                                break;
                            }
                            user.GainItem(rewardItem.Item, reward.Amount);
                            tokenRewards += $"\n > {reward.Amount}{_items[rewardItem.Item].Emoji}";
                            break;
                        default:
                            Console.WriteLine("Bugged Daily Token Shop");
                            break;
                    }
                }
                _tokenShop.Users[user.ID][value] = true;
                await component.RespondAsync(
                    embed: new EmbedBuilder().WithTitle("Purchase Successful").WithDescription(tokenRewards)
                        .WithFooter($"This costed {tokensRequired} {_items[21].Name}").Build(), ephemeral: true);
                break;
            default:
                throw new ButtonValueError();
        }
        await user.Save();
    }
    private async Task SettingsButton(SocketMessageComponent component, string settings)
    {
        string[] info = settings.Split("|");
        User user = await GetUser(component.User.Id);
        if (info.Length < 2) throw new ButtonValueError();
        try
        {
            user.SetSetting(info[0], ulong.Parse(info[1]));
            await component.RespondAsync($"Updated setting `{info[0]}` to **{info[1]}**", ephemeral: true);
        }
        catch (FormatException)
        {
            throw new ButtonValueError();
        }
    }
    private async Task ServerEventButton(SocketMessageComponent component, string settings)
    {
        string[] args = settings.Split("|");
        if (args.Length < 2) throw new ButtonValueError();
        User user = await GetUser(component.User.Id);
        if (component.GuildId == null)
        {
            await component.RespondAsync("Events need to happen in a server with gemBOT in it.", ephemeral: true);
            return;
        }
        Server server = await GetServer((ulong)component.GuildId);
        switch (args[0])
        {
            case "trigger":
                if (server.OngoingEvent)
                {
                    await component.RespondAsync("An event is already in progress. You cannot start a new one.", ephemeral: true);
                    return;
                }
                switch (args[1])
                {
                    case "mine":
                        if (user.Inventory[53] <= 0)
                        {
                            await component.RespondAsync("You need a portal to trigger this event. Get one through `/craft`", ephemeral:true);
                            return;
                        }
                        int guildMembers = _client.GetGuild((ulong)component.GuildId).MemberCount;
                        
                        if (user.Inventory[46] < guildMembers)
                        {
                            await component.RespondAsync(
                                "We didn't tell you earlier, but you actually need stone coins to trigger this event as well."
                                + $"\n Craft stone coins until you have as many as server members ({guildMembers}) and then try again."
                                +"\n > Yes, it is more profitable to run this in a larger server", ephemeral:true);
                            return;
                        }
                        await component.RespondAsync($"Spend 1{_items[53].Emoji} and {guildMembers}{_items[46].Emoji} to trigger a server-wide event?",
                            components: new ComponentBuilder()
                                .WithButton("Yes!", "serverEvent-confirmTrigger|mine", ButtonStyle.Success, Tools.ParseEmote(_items[53].Emoji))
                                .WithButton("No", "basic-cancel", ButtonStyle.Danger)
                                .Build());
                        break;
                    default:
                        throw new ButtonValueError();
                }
                break;
            case "confirmTrigger":
                if (server.OngoingEvent)
                {
                    await component.RespondAsync("An event is already in progress. You cannot start a new one.", ephemeral: true);
                    return;
                }
                switch (args[1])
                {
                    case "mine":
                        if (user.Inventory[53] <= 0)
                        {
                            await component.RespondAsync("You need a portal to trigger this event. Get one through `/craft`", ephemeral:true);
                            return;
                        }
                        int guildMembers = _client.GetGuild((ulong)component.GuildId).MemberCount;
                        if (user.Inventory[46] < guildMembers)
                        {
                            await component.RespondAsync(
                                "You need stone coins to trigger this event as well."
                                + $"\n Craft stone coins until you have as many as server members ({guildMembers}) and then try again."
                                +"\n > Yes, it is more profitable to run this in a larger server", ephemeral:true);
                            return;
                        }
                        user.GainItem(53, -1);
                        user.GainItem(46, -guildMembers);
                        server.OngoingPortalEvent = new Server.PortalEvent(guildMembers, user.ID);
                        server.OngoingEvent = true;
                        await user.Save();
                        await server.Save();
                        await component.RespondAsync($"<@{user.ID}> spent 1{_items[53].Emoji} and {guildMembers}{_items[46].Emoji} to trigger a server-wide event!");
                        break;
                    default:
                        throw new ButtonValueError();
                }
                break;
            case "mine":
                if (server.OngoingPortalEvent == null)
                {
                    await component.RespondAsync("A portal mine event is not going on at the moment (it probably ended because everyone mined everything). Run `/event` to see the current event.", ephemeral: true);
                    return;
                }
                if (args.Length < 3) throw new ButtonValueError();
                if (!Tools.VerifyOriginalUse(component))
                {
                    await component.RespondAsync("This is not your mine page for the server event! Use /event to see your options!", ephemeral: true);
                    return;
                }
                int x;
                int y;
                try
                {
                    x = int.Parse(args[1]);
                    y = int.Parse(args[2]);
                }
                catch (FormatException)
                {
                    throw new ButtonValueError();
                }
                Server.PortalEvent portalEvent = server.OngoingPortalEvent;
                MineChunk chunk = portalEvent.Chunk;
                MineBlock block = chunk.GetBlock(x, y);
                Server.PortalEvent.PlayerMineLocation location = portalEvent.GetMineLocation(user.ID);
                if (location.Mining)
                {
                    block = chunk.GetBlock(location.MiningAtX, location.MiningAtY);
                    string progressBar = Tools.ProgressBar((int)block.Durability - (block.Left ?? block.GetLeft()), (int)block.Durability, (int)user.GetSetting("progressBar", 5));
                    if ((block.Left ?? block.GetLeft()) == 0)
                    {
                        location.Mining = false;
                    }
                    string etl;
                    int secondsLeft = (block.Left ?? block.GetLeft())/((user.ID == portalEvent.PlayerID)? 3:2);
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
                            etl = $"{hours} hours, {minutes} minutes, and {secondsLeft} seconds left";
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
                    return;
                }
                if (block.Type == BlockType.Air)
                {
                    location.X = x;
                    location.Y = y;
                    Tuple<bool, string, Embed?, MessageComponent?> result = await EventMineRaw(user.ID, server.ID);
                    await component.Message.ModifyAsync((properties) =>
                    {
                        properties.Content = result.Item2;
                        properties.Embed = result.Item3;
                        properties.Components = result.Item4;
                    });
                    await user.Save();
                }
                else
                {
                    try
                    {
                        block.Mine(component.User.Id, 0); //Mining with power will be determined during server event tick;
                        location.Mining = true;
                        location.MiningAtX = x;
                        location.MiningAtY = y;
                        await server.Save();
                        await user.Save();
                        await component.RespondAsync("Successfully started mining this block!", ephemeral: true);
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
                        await component.RespondAsync($"```\n{e}\n```", ephemeral:true);
                    }
                }
                break;
            default:
                throw new ButtonValueError();
        }
    }
    private Task ButtonHandlerSetup(SocketMessageComponent component)
    {
        _ = ButtonHandler(component);
        return Task.CompletedTask;
    }
    private async Task ButtonHandler(SocketMessageComponent component)
    {
        if (!_running)
        {
            await component.RespondAsync("GemBOT is currently down. Please try again soon.", ephemeral: true);
            return;
        }
        string[] realID = component.Data.CustomId.Split("-");
        if (realID.Length <= 1)
        {
            await component.RespondAsync($"Wow! The button was most certainly written wrong (or you're using a button from python gemBot)\n > `{component.Data.CustomId}`", ephemeral: true);
            return;
        }
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
                case "drop" or "shop":
                    await DropButton(component, realID[1]);
                    break;
                case "notifications":
                    await NotificationButton(component, realID[1]);
                    break;
                case "basic":
                    await DefaultButton(component, realID[1]);
                    break;
                case "key":
                    await KeyButton(component, realID[1]);
                    break;
                case "exchange":
                    await ExchangeButton(component, realID[1]);
                    break;
                case "settings":
                    await SettingsButton(component, realID[1]);
                    break;
                case "event" or "serverEvent":
                    await ServerEventButton(component, realID[1]);
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
            await component.RespondAsync(
                $"An internal error due to button definition prevented this button from being handled. \n > `Button of id {realID[0]} was found, but arguments {realID[1]} were not written correctly`\n**This usually happens when the button you clicked was old. Please run the command that gave you the button again and try again**",
                ephemeral: true);
        }
        catch (UserNotFoundError)
        {
            await component.RespondAsync("Please run a grinding command (like /beg) to get started with gemBOT.",
                ephemeral: true);
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
        _ = TextMessageHandler(message);
        return Task.CompletedTask;
    }
    private async Task TextMessageHandler(SocketMessage socketMessage)
    {
        if (Tools.AprilFoolsYear() == 2025 && !socketMessage.Author.IsBot)
        {
            if (_users.ContainsKey(socketMessage.Author.Id) || File.Exists($"Data/Users/{socketMessage.Author.Id}"))
            {
                User user = await GetUser(socketMessage.Author.Id);
                if (!await user.OnCoolDown("EventScroll", Tools.CurrentTime(), 60, true))
                {
                    user.GainItem(52, 1);
                    if (user.GetSetting("AprilFools2025", 1) == 1){
                        RestUserMessage msg = await socketMessage.Channel.SendMessageAsync($"<@{user.ID}> You got 1{_items[52].Emoji} for chatting during an event\n`April Fools Day 2025`",
                            components: new ComponentBuilder().WithButton("Disable me!", "settings-AprilFools2025|0", ButtonStyle.Danger).Build());
                        await Task.Delay(60000);
                        await msg.DeleteAsync();
                    }
                    
                }
            }
        }
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
                case "add_drop":
                    await CreateDrop(socketMessage);
                    break;
                case "publish_drop":
                    await PublishDrop(socketMessage);
                    break;
                case "edit_drop":
                    await EditDrop(socketMessage);
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
        string[] names = ["item", "balance", "beg", "stats", "inventory", "work", "magik", "hep", "start", "bank",
            "theme", "setting", "quests", "give", "mine", "play", "help", "craft", "shop", "spin", 
            "notifications", "key", "exchange", "event"];
        List<string> existingCommands = new List<string>();
        string[] forceUpdateCommands = message.Content.Split(" ")[1..];
        bool forceClearCommands = forceUpdateCommands.Contains("clear");
        bool forceUpdateAll = forceUpdateCommands.Contains("all") || forceClearCommands;
        IReadOnlyCollection<RestGlobalCommand>? commands = await  _client.Rest.GetGlobalApplicationCommands();
        foreach (RestGlobalCommand command in commands)
        {
            if (!forceClearCommands || names.Contains(command.Name)) {existingCommands.Add(command.Name); continue; }
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
            .WithDescription("Work for emeralds every 30 minutes!");
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
            .WithDescription("Convert your currency to other values")
            .AddOption("quick_upgrade", ApplicationCommandOptionType.Boolean, "Do the upgrade the maximum times instead of just once. Uses stone coins");
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
                .AddOption(new SlashCommandOptionBuilder().WithName("progress_bar_width")
                    .WithDescription("The width of the progress bar such as in crafting, daily quests, and mining")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption(new SlashCommandOptionBuilder()
                        .WithType(ApplicationCommandOptionType.Integer)
                        .WithName("width")
                        .WithDescription("How wide should the progress bar be")
                        .WithMinValue(2)
                        .WithMaxValue(10)
                    )
                )
            )
            .AddOption(new SlashCommandOptionBuilder().WithName("view")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .WithDescription("View all your currently set settings in gemBot")
            );
        SlashCommandBuilder craft = new SlashCommandBuilder()
            .WithName("craft")
            .WithDescription("Craft items from other items")
            .WithIntegrationTypes([ApplicationIntegrationType.GuildInstall, ApplicationIntegrationType.UserInstall]);
        SlashCommandBuilder shop = new SlashCommandBuilder()
            .WithName("shop")
            .WithDescription("View and buy from the global shop.")
            .WithIntegrationTypes([ApplicationIntegrationType.GuildInstall, ApplicationIntegrationType.UserInstall]);
        SlashCommandBuilder spin = new SlashCommandBuilder()
            .WithName("spin")
            .WithDescription("Spin a wheel for rewards.")
            .WithIntegrationTypes(ApplicationIntegrationType.GuildInstall, ApplicationIntegrationType.UserInstall)
            .AddOption(new SlashCommandOptionBuilder()
                .WithType(ApplicationCommandOptionType.Integer)
                .WithName("wheel")
                .WithDescription("Which wheel would you like to spin?")
                .AddChoice("Diamond", 0)
                .AddChoice("Emerald", 1)
                .AddChoice("Sapphire", 2)
                .AddChoice("Ruby", 3)
                .AddChoice("Amber", 4)
                .WithRequired(true)
            )
            .AddOption(new SlashCommandOptionBuilder()
                .WithType(ApplicationCommandOptionType.Integer)
                .WithName("amount")
                .WithDescription("How many wheels would you like to spin?")
                .WithMinValue(1)
                .WithMaxValue(450)
            );
        SlashCommandBuilder notifications = new SlashCommandBuilder()
            .WithName("notifications")
            .WithDescription("View items/gems you gained inactively (like from /mine /craft)")
            .WithIntegrationTypes([ApplicationIntegrationType.GuildInstall, ApplicationIntegrationType.UserInstall]);
        SlashCommandBuilder key = new SlashCommandBuilder()
            .WithName("key")
            .WithDescription("Use a key")
            .WithIntegrationTypes([ApplicationIntegrationType.GuildInstall, ApplicationIntegrationType.UserInstall])
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("diamond")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .WithDescription("Use a diamond key to gain some charms"))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("emerald")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .WithDescription("Use an emerald key to gain several charms, including a rare+ charm guaranteed"))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("sapphire")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .WithDescription("Use a sapphire key to gain a lot of charms, including a 2 epic+ charms guaranteed"))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("ruby")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .WithDescription("Use a ruby key to open a lootbox"))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("amber")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .WithDescription("Use an amber key to gain powerful, amber key exclusive charms"));
        SlashCommandBuilder exchange = new SlashCommandBuilder()
            .WithName("exchange")
            .WithDescription("exchange duplicate charms for coins");
        SlashCommandBuilder eventCmd = new SlashCommandBuilder()
            .WithName("event")
            .WithDescription("View the active server event");
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
            if (forceUpdateAll || forceUpdateCommands.Contains("shop") || !existingCommands.Contains("shop"))
                await _client.CreateGlobalApplicationCommandAsync(shop.Build());
            if (forceUpdateAll || forceUpdateCommands.Contains("spin") || !existingCommands.Contains("spin"))
                await _client.CreateGlobalApplicationCommandAsync(spin.Build());
            if (forceUpdateAll || forceUpdateCommands.Contains("key") || !existingCommands.Contains("key"))
                await _client.CreateGlobalApplicationCommandAsync(key.Build());
            if (forceUpdateAll || forceUpdateCommands.Contains("exchange") || !existingCommands.Contains("exchange"))
                await _client.CreateGlobalApplicationCommandAsync(exchange.Build());
            if (forceUpdateAll || forceUpdateCommands.Contains("event") || !existingCommands.Contains("event"))
                await _client.CreateGlobalApplicationCommandAsync(eventCmd.Build());
            await message.Channel.SendMessageAsync("Pushing info commands (5/6)...");
            if (forceUpdateAll || forceUpdateCommands.Contains("item") || !existingCommands.Contains("item")) 
                await _client.CreateGlobalApplicationCommandAsync(itemInfo.Build());
            if (forceUpdateAll || forceUpdateCommands.Contains("stats") || !existingCommands.Contains("stats")) 
                await _client.CreateGlobalApplicationCommandAsync(stats.Build());
            if (forceUpdateAll || forceUpdateCommands.Contains("help") || !existingCommands.Contains("help")) 
                await _client.CreateGlobalApplicationCommandAsync(help.Build());
            if (forceUpdateAll || forceUpdateCommands.Contains("start") || !existingCommands.Contains("start"))
                await _client.CreateGlobalApplicationCommandAsync(start.Build());
            if (forceUpdateAll || forceUpdateCommands.Contains("notifications") || !existingCommands.Contains("notifications"))
                await _client.CreateGlobalApplicationCommandAsync(notifications.Build());
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
        Console.WriteLine("                                 -- COMMANDS WERE RESET --");
        await message.Channel.SendMessageAsync("Reset commands!");
        await _client.SetGameAsync("Commands recently reset.");
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
                await File.WriteAllTextAsync($"Data/Lists/{listName}.json", JsonConvert.SerializeObject(list, Formatting.Indented));
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
        user.Add(int.Parse(args[3]), int.Parse(args[2]));
        await user.Save();
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
        user.GainItem(int.Parse(args[2]), int.Parse(args[3]));
        await user.Save();
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
        Embed embay5 = new EmbedBuilder()
            .WithTitle("Drops")
            .WithDescription("Commands that modify, add, or delete drops.")
            .AddField("$add_drop", " > **Params**: *none*\n > **Description**: Create a new drop.")
            .AddField("$publish_drop", " > **Params**: *none*\n > **Description**: Publish the last drop.")
            .AddField("$edit_drop", " > **Params**: <property> <value>\n > **Description**: Change the last drop's <property> to <value>")
            .Build();
        await message.Channel.SendMessageAsync("View Embeds for details!", embeds:[embay1, embay2, embay3, embay4, embay5, embay0]);
    }
    private async Task CreateCraftingRecipe(SocketMessage message)
    {
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
                        int requirementID;
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
            case "price":
                int money = int.Parse(args[3]);
                int value = 0;
                while (money % 10 == 0 && value < 4)
                {
                    value++;
                    money /= 10;
                }
                recipe.Price = money;
                recipe.PriceValue = value;
                await recipe.Save();
                break;
            default:
                RestUserMessage msgNone = await message.Channel.SendMessageAsync("Please specify the property you would like to edit:\n> **time**, **amount**, **item**, **price**, or **requirements**.");
                await Task.Delay(5000);
                await msgNone.DeleteAsync();
                return;
        }
        await message.Channel.SendMessageAsync(recipe.ToString(_items, _currency));
    }
    private async Task CreateDrop(SocketMessage message)
    {
        if (_drops.Count > 0 && !_drops[^1].Published)
        {
            RestUserMessage msg = await message.Channel.SendMessageAsync(
                "You already have an unpublished drop. Please edit that one and then publish before making another one.");
            await Task.Delay(5000);
            await msg.DeleteAsync();
            return;
        }
        _drops.Add(new Drop { DropID = _drops.Count });
        EmbedBuilder embay = new EmbedBuilder()
            .WithTitle($"Drop {_drops.Count - 1}")
            .WithDescription(_drops[^1].ToString(_items));
        await message.Channel.SendMessageAsync("Created new drop!", embed:embay.Build());
    }
    private async Task PublishDrop(SocketMessage message)
    {
        if (_drops[^1].Published)
        {
            RestUserMessage msg =
                await message.Channel.SendMessageAsync(
                    "You have already published the last drop. Please use $add_drop to add a drop.");
            await Task.Delay(5000);
            await msg.DeleteAsync();
            return;
        }
        _drops[^1].Published = true;
        await _drops[^1].Save();
    }
    private async Task EditDrop(SocketMessage message)
    {
        if (_drops[^1].Published)
        {
            RestUserMessage msg =
                await message.Channel.SendMessageAsync("The last drop is already published. You cannot edit it.");
            await Task.Delay(5000);
            await msg.DeleteAsync();
            return;
        }
        string[] args = message.Content.Split(" ");
        if (args.Length < 3)
        {
            RestUserMessage msg = await message.Channel.SendMessageAsync("Usage of this command is: $edit_drop <property> <value>");
            await Task.Delay(5000);
            await msg.DeleteAsync();
            return;
        }
        switch (args[1])
        {
            case "name":
                string name = string.Join(' ', args[2..]);
                _drops[^1].Name = name;
                break;
            case "item":
                if (args.Length < 4)
                {
                    RestUserMessage msgItem =
                        await message.Channel.SendMessageAsync("Usage of this command is: $edit_drop item <dropSpot> <itemID>");
                    await Task.Delay(5000);
                    await msgItem.DeleteAsync();
                    return;
                }
                int dropSlotItem = int.Parse(args[2]);
                int itemID = int.Parse(args[3]);
                if (dropSlotItem >= 5)
                {
                    RestUserMessage msgItemSlotMissing =
                        await message.Channel.SendMessageAsync(
                            $"Argument #2 - dropSlot ({dropSlotItem}) must be between 0 and 4 (inclusive)");
                    await Task.Delay(5000);
                    await msgItemSlotMissing.DeleteAsync();
                    return;
                }
                if (itemID >= _items.Count)
                {
                    RestUserMessage msgItemIdTooLarge = await message.Channel.SendMessageAsync($"Argument #3 - itemID ({itemID}) must be between 0 and {_items.Count-1} (inclusive)");
                    await Task.Delay(5000);
                    await msgItemIdTooLarge.DeleteAsync();
                    return;
                }
                _drops[^1].Items[dropSlotItem] = itemID;
                break;
            case "items":
                if (args.Length < 7)
                {
                    RestUserMessage msgItemsNotEnough =
                        await message.Channel.SendMessageAsync("Usage of this command is: $edit_drop items <item1> <item2> <item3> <item4> <item5>");
                    await Task.Delay(5000);
                    await msgItemsNotEnough.DeleteAsync();
                    return;
                }
                try
                {
                    int item1 = int.Parse(args[2]);
                    int item2 = int.Parse(args[3]);
                    int item3 = int.Parse(args[4]);
                    int item4 = int.Parse(args[5]);
                    int item5 = int.Parse(args[6]);
                    _drops[^1].Items = [item1, item2, item3, item4, item5];
                }
                catch (FormatException)
                {
                    RestUserMessage msgItemsFormatException = await message.Channel.SendMessageAsync(
                        $"Parameters <item1> through <item5> (#2-#6) must be integers.");
                    await Task.Delay(5000);
                    await msgItemsFormatException.DeleteAsync();
                    return;
                }
                break;
            case "left":
                if (args.Length < 4)
                {
                    RestUserMessage msgLeft =
                        await message.Channel.SendMessageAsync("Usage of this command is: $edit_drop left <dropSpot> <itemID>");
                    await Task.Delay(5000);
                    await msgLeft.DeleteAsync();
                    return;
                }
                int dropSlotLeft = int.Parse(args[2]);
                int amount = int.Parse(args[3]);
                if (dropSlotLeft >= 5)
                {
                    RestUserMessage msgItemSlotMissing =
                        await message.Channel.SendMessageAsync(
                            $"Argument #2 - dropSlot ({dropSlotLeft}) must be between 0 and 4 (inclusive)");
                    await Task.Delay(5000);
                    await msgItemSlotMissing.DeleteAsync();
                    return;
                }
                _drops[^1].Items[dropSlotLeft] = amount;
                break;
            case "amounts":
                if (args.Length < 7)
                {
                    RestUserMessage msgItemsNotEnough =
                        await message.Channel.SendMessageAsync("Usage of this command is: $edit_drop amounts <amount1> <amount2> <amount3> <amount4> <amount5>");
                    await Task.Delay(5000);
                    await msgItemsNotEnough.DeleteAsync();
                    return;
                }
                try
                {
                    int amount1 = int.Parse(args[2]);
                    int amount2 = int.Parse(args[3]);
                    int amount3 = int.Parse(args[4]);
                    int amount4 = int.Parse(args[5]);
                    int amount5 = int.Parse(args[6]);
                    _drops[^1].Left = [amount1, amount2, amount3, amount4, amount5];
                }
                catch (FormatException)
                {
                    RestUserMessage msgAmountFormatException = await message.Channel.SendMessageAsync(
                        $"Parameters <amount1> through <amount5> (#2-#6) must be integers.");
                    await Task.Delay(5000);
                    await msgAmountFormatException.DeleteAsync();
                    return;
                }
                break;
            case "price":
                if (args.Length < 5)
                {
                    RestUserMessage msgLeft =
                        await message.Channel.SendMessageAsync("Usage of this command is: $edit_drop price <dropSpot> <amount> <currency>");
                    await Task.Delay(5000);
                    await msgLeft.DeleteAsync();
                    return;
                }
                int dropSlotPrice = int.Parse(args[2]);
                int amountMoney = int.Parse(args[3]);
                int currency = int.Parse(args[4]);
                if (dropSlotPrice >= 5)
                {
                    RestUserMessage msgPriceSlotMissing =
                        await message.Channel.SendMessageAsync(
                            $"Argument #2 - dropSlot ({dropSlotPrice}) must be between 0 and 4 (inclusive)");
                    await Task.Delay(5000);
                    await msgPriceSlotMissing.DeleteAsync();
                    return;
                }
                if (currency >= 5)
                {
                    RestUserMessage msgPriceCurrencyOutOfRange =
                        await message.Channel.SendMessageAsync(
                            $"Argument #4 - currency ({currency}) must be between 0 and 4 (inclusive)");
                    await Task.Delay(5000);
                    await msgPriceCurrencyOutOfRange.DeleteAsync();
                    return;
                }
                _drops[^1].Price[dropSlotPrice] = [amountMoney, currency];
                break;
            case "prices":
                try
                {
                    int amountPrices = int.Parse(args[2]);
                    _drops[^1].Price = [[amountPrices, 0],[amountPrices, 1],[amountPrices, 2],[amountPrices, 3],[amountPrices, 4]];
                }
                catch (FormatException)
                {
                    RestUserMessage msgPricesFormatException = await message.Channel.SendMessageAsync(
                        $"Parameter <amount> (#2) must be an integer.");
                    await Task.Delay(5000);
                    await msgPricesFormatException.DeleteAsync();
                    return;
                }
                break;
            case "description":
                if (args.Length < 4)
                {
                    RestUserMessage msgDescriptionTooLittleArgs = await message.Channel.SendMessageAsync(
                        "Usage of this command is: $edit_drop description <id> <value>");
                    await Task.Delay(5000);
                    await msgDescriptionTooLittleArgs.DeleteAsync();
                    return;
                }

                if (!int.TryParse(args[2], out int dropSlot))
                {
                    RestUserMessage msg = await message.Channel.SendMessageAsync(
                        $"Argument #2 must be an integer. You entered: {args[2]}");
                    await Task.Delay(5000);
                    await msg.DeleteAsync();
                    return;
                }
                string description = string.Join(' ', args[3..]);
                _drops[^1].Descriptions[dropSlot] = description;
                break;
            case "collectable":
                switch (args[2].ToLower())
                {
                    case "yes":
                        _drops[^1].Collectable = true;
                        break;
                    case "no":
                        _drops[^1].Collectable = false;
                        break;
                    default:
                        RestUserMessage msgCollectable = await message.Channel.SendMessageAsync(
                            $"For argument #2 (vale) you specified {args[2]}, which was invalid." +
                            $" \n > The valid arguments are \"yes\" and \"no\".");
                        await Task.Delay(5000);
                        await msgCollectable.DeleteAsync();
                        return;
                }
                break;
            default:
                RestUserMessage defaultMsg = await message.Channel.SendMessageAsync(
                    $"For argument #1 (property) you specified {args[1]}, which was invalid." +
                    $" \n > The valid arguments are \"name\", \"item\", \"items\", \"left\", \"amounts\", \"price\", \"prices\", and \"description\".");
                await Task.Delay(5000);
                await defaultMsg.DeleteAsync();
                return;
        }
        await message.Channel.SendMessageAsync(_drops[^1].ToString(_items));
    }

    // ReSharper disable once FunctionNeverReturns
    private async Task RunTicks()
    {
        Console.WriteLine("Starting ticking...");
        Stopwatch stopwatch = new Stopwatch();
        stopwatch.Start();
        uint taskTimes = 0;
        List<Task> tasks = new List<Task>();
        while (true)
        {
            Console.WriteLine($"Tick {taskTimes}...");
            if (true) //every tick
            {
                tasks.Add(MineTick());
                tasks.Add(CraftTick());
                tasks.Add(ServerEventsTick());
            }
            if (taskTimes % 60 == 0) //every minute
            {
                tasks.Add(_mineData.SaveMineData());
                tasks.Add(SaveDropsTick());
            }
            if (taskTimes % 120 == 0) //Every two minutes
            {
                tasks.Add(RemoveInactiveUsersTick());
            }
            if (taskTimes % 3600 == 0) //every hour
            {
                try{_tokenShop = _tokenShop.CheckDate(_itemLists["CharmSets"], Tools.CurrentDay(), _rand);}
                catch{Console.WriteLine("I CAUGHT THE BUG!!!");}
                if (DateTime.Today.Month.ToString() != _mineData.MonthName)
                {
                    await Task.WhenAll(tasks);
                    _mineData = await MineData.LoadMineData();
                }
            }
            await Task.WhenAll(tasks);
            tasks = new List<Task>();
            taskTimes++;
            await Task.Delay((int)(uint)(1000 - (int)stopwatch.ElapsedMilliseconds));
            stopwatch.Restart();
        }
    }
    private async Task MineTick()
    {
        foreach (MineChunk chunk in _mineData.MineChunks)
        {
            foreach (MineBlock[] layer in chunk.Blocks)
            {
                foreach (MineBlock block in layer)
                {
                    if (block is { Type: BlockType.Air, MinerID: not null }) block.MinerID = null;
                    if (block.MinerID is null) continue;
                    try
                    {
                        CachedUser user = await GetUser((ulong)block.MinerID);
                        var (mined, amountDropped, type) = block.Mine((ulong)block.MinerID, Tools.CharmEffect(["minePower"], _items, user)+5);
                        int mineSkip = user.User.GetData("mineSkip", 0);
                        if (Tools.AprilFoolsYear() == 2025) mineSkip++;
                        while (!mined && mineSkip > 0)
                        {
                            int toSkip = (mineSkip > block.Left) switch {true => block.Left ?? block.GetLeft(), false => mineSkip};
                            if (toSkip <= 0) toSkip = 1;
                            (mined, amountDropped, type) = block.Mine((ulong)block.MinerID, toSkip);
                            mineSkip -= toSkip;
                        }
                        user.User.SetData("mineSkip", mineSkip);
                        if (mined)
                        {
                            user.User.SetData("mining", 0);
                            user.User.Increase("mined", 1);
                            _mineData.TimesMined++;
                            bool dropsItem = type switch
                            {
                                BlockType.Air => true,
                                BlockType.Stone => true,
                                BlockType.Diamonds => false,
                                BlockType.Emeralds => false,
                                BlockType.Rubies => false,
                                BlockType.Sapphires => false,
                                BlockType.Amber => false,
                                BlockType.DiamondCoin => true,
                                BlockType.EmeraldCoin => true,
                                BlockType.SapphireCoin => true,
                                BlockType.RubyCoin => true,
                                BlockType.AmberCoin => true,
                                BlockType.DiamondKey => true,
                                BlockType.EmeraldKey => true,
                                BlockType.SapphireKey => true,
                                BlockType.RubyKey => true,
                                BlockType.AmberKey => true,
                                BlockType.Gold => true,
                                BlockType.EventScroll => true,
                                BlockType.Portal => true,
                                _ => true
                            };
                            if (dropsItem)
                            {
                                int itemId = type switch
                                {
                                    BlockType.Air => 39,
                                    BlockType.Stone => 39,
                                    BlockType.DiamondCoin => 0,
                                    BlockType.EmeraldCoin => 1,
                                    BlockType.SapphireCoin => 2,
                                    BlockType.RubyCoin => 3,
                                    BlockType.AmberCoin => 4,
                                    BlockType.DiamondKey => 16,
                                    BlockType.EmeraldKey => 17,
                                    BlockType.SapphireKey => 18,
                                    BlockType.RubyKey => 19,
                                    BlockType.AmberKey => 20,
                                    BlockType.Gold => 54,
                                    BlockType.EventScroll => 52,
                                    BlockType.Portal => 53,
                                    _ => 39
                                };
                                user.User.GainItem(itemId, amountDropped);
                                user.Notifications.Add(new CachedUser.Notification(itemId, amountDropped, "mined"));
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
                                user.User.Add(amountDropped, value);
                                user.Notifications.Add(new CachedUser.Notification((-value)-1, amountDropped, "mined"));
                            }
                            await user.User.Save();
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
            if (user.CraftTick(_items, _craftingRecipes, FurnaceConst)) await user.User.Save();
        }
    }
    private async Task SaveDropsTick()
    {
        foreach (Drop drop in _drops)
        {
            await drop.Save();
        }
    }
    private async Task ServerEventsTick()
    {
        foreach (Server server in _servers.Values)
        {
            if (!server.OngoingEvent) continue;
            if (server.OngoingPortalEvent != null)
            {
                if (await server.OngoingPortalEvent.DoTicks(GetUser))
                {
                    server.OngoingPortalEvent = null;
                    server.OngoingEvent = false;
                }
            }
            else
            {
                Console.WriteLine($"Server {server.ID} event does is not part of the types - Portal Event");
            }
        }
    }
}
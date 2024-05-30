using System.Diagnostics;
using Discord;
using Discord.Net;
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
    public Cooldown(ulong endSeconds): base($"You are on cooldown. Please try again in <t:{endSeconds}:R>"){}
    public Cooldown (int secondsLeft): base($"You are on cooldown. Please try again in <t:{secondsLeft+DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:R>"){}
}
public class ButtonValueError() :Exception("An internal code error due to how the button is defined means that the button does not work."){}

public static class Program
{
    public static async Task Main()
    {
        GemBot gemBot = new GemBot();
        await gemBot.Main();
    }
}

public class GemBot
{
    private Dictionary<string, List<int>> _itemLists = new();
    private readonly Random _rand = new ();
    private readonly DiscordSocketClient _client = new();
    private Dictionary<string, List<string>> _dataLists = new();
    private List<Item> _items = new();
    private List<Tutorial> _tutorials = [];
    private Dictionary<ulong, CachedUser> _users = [];
    private readonly string[] _currency = [
        "<:gem_diamond:1089971521168085072>", 
        "<:gem_emerald:1089971521985970177>", 
        "<:gem_sapphire:1089971528550064128>", 
        "<:gem_ruby:1089971523265237132>", 
        "<:gem_amber:1089971518957699143>"
    ];
    public async Task Main()
    {
        _client.Log += Log;
        _client.SlashCommandExecuted += CommandHandler;
        _client.MessageReceived += TextMessageHandler;
        _client.ButtonExecuted += ButtonHandler;
        string token;
        try
        {
            token = await File.ReadAllTextAsync("../../../token.txt");
        }
        catch {
            throw new NoTokenError();
        }
        await GetItems();
        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();
        await Task.Delay(-1);
    }
    private async Task GetItems()
    {
        _items = new List<Item>();
        _users = new Dictionary<ulong, CachedUser>();
        _tutorials = await Tutorial.LoadAll(2);
        await _client.SetGameAsync("Updating items...");
        foreach (string path in Directory.GetFiles("../../../Data/Items"))
        {
            string itemData = await File.ReadAllTextAsync(path);
            Item? item = JsonConvert.DeserializeObject<Item>(itemData);
            await Task.Delay(1);
            if (item is not null)
            {
                _items.Add(item);
            }
        }
        _itemLists = new Dictionary<string, List<int>>();
        foreach (string path in Directory.GetFiles("../../../Data/ItemLists"))
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
        foreach (string path in Directory.GetFiles("../../../Data/Lists"))
        {
            string name = path.Split('/')[^1].Split('.')[0];
            List<string>? list = JsonConvert.DeserializeObject<List<string>>(await File.ReadAllTextAsync(path));
            await Task.Delay(1);
            if (list is not null)
            {
                _dataLists.Add(name, list);
            }
        }
        _items = _items.OrderBy(o=>o.ID).ToList();
        await _client.SetGameAsync("/start");
    }
    private async Task<User> GetUser(ulong id)
    {
        if (_users.TryGetValue(id, out CachedUser? cachedUser))
        {
            Debug.Assert(cachedUser != null, nameof(cachedUser) + " != null");
            cachedUser.InactiveSince = Tools.CurrentTime();
            return cachedUser.User;
        }
        try
        {
            string baseData = await File.ReadAllTextAsync($"../../../Data/Users/{id}");
            User loadedUser =  JsonConvert.DeserializeObject<User>(baseData) ??
                   throw new Exception("Somehow your save file is bad.");
            _users.Add(id, new CachedUser(loadedUser, Tools.CurrentTime()));
            return loadedUser;
        }
        catch (FileNotFoundException)
        {
            throw new UserNotFoundError();
        }
    }
    private Task Log(LogMessage msg)
    {
        Console.WriteLine(msg.ToString());
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
                        string temp = command.Data.Options.First().Value.ToString() ?? throw new InvalidOperationException();
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
                default:
                    await command.RespondAsync("Command not found", ephemeral: true);
                    break;
            }
            if (_users.TryGetValue(command.User.Id, out CachedUser? value))
            {
                _users[command.User.Id] = await Tools.UpdateTutorial(command.Data.Name, _tutorials, value, command);
            }
        }
        catch (Cooldown cool)
        {
            await command.RespondAsync(cool.Message, ephemeral: true);
        }
        catch (UserNotFoundError)
        {
            try { await UserSetupSlash(command); }
            catch (UserExistsException) { await command.RespondAsync("A user you are trying to interact with doesn't exists. You can only create accounts for yourself."); }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            var embay = new EmbedBuilder()
                .WithTitle("Error")
                .WithAuthor(command.User)
                .WithColor(255, 0, 0)
                .AddField("Your command generated an error", $"**Full Details**: `{e}`");
            await command.RespondAsync(embed:embay.Build());
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
        string text = $"{atStartInfo} {user.Gems[0]}{_currency[0]}, {user.Gems[1]}{_currency[1]}, {user.Gems[2]}{_currency[2]}, {user.Gems[3]}{_currency[3]}, {user.Gems[4]}{_currency[4]}";
        if (!compact)
        {
            text = $"{atStartInfo}\n > **Diamonds**: {user.Gems[0]}\n > **Emeralds**: {user.Gems[1]}\n > **Sapphires**: {user.Gems[2]}\n > **Rubies**: {user.Gems[3]}\n > **Amber**: {user.Gems[4]}";
        }
        return text;
    }
    private async Task Balance(SocketSlashCommand command, string title = "Your balance:", bool? compactArg = null, bool ephemeral = true)
    {
        bool compact = Tools.ShowEmojis(command, settings.BotID(), _client);
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
        await command.RespondAsync(embed: embay.Build(), ephemeral:ephemeral);
    }
    private async Task GetItem(SocketSlashCommand command)
    {
        try
        {
            await command.RespondAsync(_items[int.Parse(command.Data.Options.First().Value.ToString() ?? throw new Exception("Bad parameters - there's probably an error in the code."))].ToString());
        }
        catch (ArgumentOutOfRangeException)
        {
            await command.RespondAsync("This item does not exist");
        }
    }
    private async Task UserSetupSlash(SocketSlashCommand command)
    {
        var id = command.User.Id;
        if (Path.Exists($"../../../Data/OldUsers/{id}"))
        {
            User user = await Tools.UserCreator(id);
            string balanceRaw = await File.ReadAllTextAsync($"../../../Data/OldUsers/{id}/g");
            string[] balanceStrings = balanceRaw.Split(" ");
            for (int i = 0; i < balanceStrings.Length; i++)
            {
                user.Gems[i] = int.Parse(balanceStrings[i]);
            }
            string inventoryRaw = await File.ReadAllTextAsync($"../../../Data/OldUsers/{id}/i");
            string[] inventoryStrings = inventoryRaw.Split(" ");
            user.Inventory = new List<int>();
            for (int i = 0; i < inventoryStrings.Length; i++)
            {
                int x = int.Parse(inventoryStrings[i]);
                if (i < 39) { user.Inventory.Add(x); }
                else if (i == 39) { user.Gems[0] += 30 * x; }
                else if (i == 40) { user.Gems[0] += 450 * x; }
                else if (i == 41) { user.Gems[2] += 20 * x; }
            }
            await File.WriteAllTextAsync($"../../../Data/Users/{id}",JsonConvert.SerializeObject(user));
            await command.RespondAsync("Migrated existing account to new gemBOT!");
        }
        else
        {
            await Tools.UserCreator(id);
            await Balance(command, "Welcome to gemBOT! Here's your starting balance:");
        }
    }
    private async Task Stats(SocketSlashCommand command)
    {
        User user = await GetUser(command.User.Id);
        await user.Increase("commands", 1);
        EmbedBuilder embay = new EmbedBuilder()
            .WithTitle($"{command.User.Username}'s stats")
            .WithDescription($"View all the stats for {command.User.Username}.")
            .WithFooter(new EmbedFooterBuilder().WithText("GemBOT Stats may not be 100% accurate."))
            .AddField("Commands Ran", $"{user.GetStat("commands")} commands")
            .AddField("Gems Earned", $"{user.GetStat("earned")} diamond-equivalents (one diamond is 1, one emerald is 10, one sapphire is 100, one ruby is 1000, and one amber is 10000): earned by grinding only.")
            .WithColor(new Color((uint)await user.GetSetting("uiColor", 3287295)));
        await command.RespondAsync(embed:embay.Build());
    }
    private async Task Beg(SocketSlashCommand command)
    {
        User user = await GetUser(command.User.Id);
        ulong t = Tools.CurrentTime();
        if (await user.OnCoolDown("beg", t, 5)) { throw new Cooldown(user.CoolDowns["beg"]); }
        int mn = 5 + await Tools.CharmEffect(["BegMin", "Beg", "GrindMin", "Grind", "Positive"], _items, user);
        int mx = 9 + await Tools.CharmEffect(["BegMax", "Beg", "GrindMax", "Grind", "Positive"], _items, user);
        int amnt = _rand.Next(mn, mx);
        await user.Add(amnt, 0, false);
        string text = $"You gained {amnt} **Diamonds**.";
        if (Tools.ShowEmojis(command, settings.BotID(), _client))
        {
            text = $"You gained {amnt}{_currency[0]}!";
        }
        await user.Increase("commands",1, false);
        await user.Increase("earned", amnt);
        List<string> begChoices = _dataLists["BegEffects"];
        EmbedBuilder embay = new EmbedBuilder()
            .WithTitle(text)
            .WithDescription(begChoices[_rand.Next(0, begChoices.Count)])
            .WithColor(new Color((uint)(await user.GetSetting("begRandom", 0) switch{0 => await user.GetSetting("begColor", 65525), 1 => (ulong)_rand.Next(16777216), _ => (ulong)3342180})));
        await command.RespondAsync(embed: embay.Build());
    }
    private async Task Work(SocketSlashCommand command)
    {
        User user = await GetUser(command.User.Id);
        ulong t = Tools.CurrentTime();
        if (await user.OnCoolDown("work", t, 300)) { throw new Cooldown(user.CoolDowns["work"]); }
        int mn = 10 + await Tools.CharmEffect(["WorkMin", "Work", "GrindMin", "Grind", "Positive"], _items, user);
        int mx = 16 + await Tools.CharmEffect(["WorkMax", "Work", "GrindMax", "Grind", "Positive"], _items, user);
        int amnt = _rand.Next(mn, mx);
        await user.Add(amnt, 1, false);
        string text = $"You gained {amnt} **Emeralds**.";
        if (Tools.ShowEmojis(command, settings.BotID(), _client))
        {
            text = $"You gained {amnt}{_currency[1]}!!!";
        }
        await user.Increase("commands",1, false);
        await user.Increase("earned", amnt*10);
        List<string> workChoices = _dataLists["WorkEffect"];
        EmbedBuilder embay = new EmbedBuilder()
            .WithTitle(text)
            .WithDescription(workChoices[_rand.Next(0, workChoices.Count)])
            .WithColor(new Color(50, 255, 100));
        await command.RespondAsync(embed: embay.Build());
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
        List<Tuple<int, int>> sortedData = data.OrderBy(o => -o.Item2*_items[o.Item1].Value).ToList();
        string[] optionsSplit = options.Split("|");
        try
        {
            string text = "View all your items:";
            int page = int.Parse(optionsSplit[0]);
            bool showEmojis = optionsSplit[1].StartsWith('y') || optionsSplit[1].StartsWith('t') || optionsSplit[1].StartsWith('T');
            for (int i = page * 8; i < sortedData.Count && i < (page + 1)*8; i++)
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
            return new EmbedBuilder().WithTitle("Inventory").WithDescription(text);
        }
        catch (FormatException)
        {
            throw new ButtonValueError();
        }
    }
    private async Task InventoryCommand(SocketSlashCommand command)
    {
        string emoj = Tools.ShowEmojis(command, settings.BotID(), _client).ToString();
        ComponentBuilder builder = new ComponentBuilder()
            .WithButton("<-- Left", "disabledL", disabled:true)
            .WithButton("Refresh", $"inv-0|{emoj}", ButtonStyle.Secondary)
            .WithButton("Right -->", $"inv-1|{emoj}");
        EmbedBuilder embay = await InventoryRawEmbed(command.User.Id, $"0|{emoj}");
        await command.RespondAsync(embed:embay.Build(), components:builder.Build());
    }
    private async Task Magik(SocketSlashCommand command)
    {
        User user = await GetUser(command.User.Id);
        ulong t = (ulong)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        if (await user.OnCoolDown("magik", t, 12)) { throw new Cooldown(user.CoolDowns["magik"]); }
        int power =  await Tools.CharmEffect(["Magik", "Unlocker", "Positive"], _items, user);
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
        catch (InvalidOperationException) { targetID = await user.GetSetting("magikID", user.ID); }
        catch (InvalidCastException) { targetID = await user.GetSetting("magikID", user.ID); badInput = true;}
        User target = await GetUser(targetID);
        if (targetID != user.ID) { power += 3; }
        power += _rand.Next(0, 2);
        if (targetID != await user.GetSetting("magikID", user.ID)) { power += 1; }
        List<Tuple<string, int, int, int, int, int>> chances = [new Tuple<string, int, int, int, int, int>("You gained 8$diamonds.", 8, 0, 1, 0, 9), new Tuple<string, int, int, int, int, int>("You gained 1$emeralds.", 1, 0, 0, 0, 4), new Tuple<string, int, int, int, int, int>("Nothing happened", 0, 0, 0, 0, 7), new Tuple<string, int, int, int, int, int>("$target gained 10$diamonds", 0, 0, 10, 0, 6)];
        if (power >= 1)
        {
            chances.Add(new Tuple<string, int, int, int, int, int>("$user and $target both gained 7$diamonds", 7, 0, 7, 0, 10));
            chances.Add(new Tuple<string, int, int, int, int, int>("$user and $target both gained 8$diamonds", 8, 0, 8, 0, 4));
        }
        if (power >= 3)
        {
            chances.Add(new Tuple<string, int, int, int, int, int>("$user and $target both gained 11$diamonds!", 11, 0, 11, 0, 12));
            chances.Add(new Tuple<string, int, int, int, int, int>("$user gained 2$emeralds!", 2, 1, 0, 0, 8));
            chances.Add(new Tuple<string, int, int, int, int, int>("$target gained 2$emeralds", 0, 0, 2, 1, 6));
            chances.Add(new Tuple<string, int, int, int, int, int>($"$user gained {power}$emeralds!", power, 1, 0, 0, power));
            chances.Add(new Tuple<string, int, int, int, int, int>($"$target gained {power}$emeralds", 0, 0, power, 1, power));
        }
        if (power >= 4)
        {
            chances.Add(new Tuple<string, int, int, int, int, int>("$user and $target gained 1$emeralds", 1, 1, 1, 1, 15));
            chances.Add(new Tuple<string, int, int, int, int, int>("$user and $target gained 2$emeralds", 2, 1, 2, 1, 5));
            chances.Add(new Tuple<string, int, int, int, int, int>("$user and $target both gained 3$emeralds", 3, 1, 3, 1, 2));
            chances.Add(new Tuple<string, int, int, int, int, int>("$user gained 1$sapphires", 1, 2, 0, 0, 1));
            chances.Add(new Tuple<string, int, int, int, int, int>("$target gained 1$sapphires", 0, 0, 1, 2, 1));
            chances.Add(new Tuple<string, int, int, int, int, int>("$user gained $user_wand", 0, 0, 0, 0, 1));
            chances.Add(new Tuple<string, int, int, int, int, int>("$target gained $target_wand", 0, 0, 0, 0, 1));
            chances.Add(new Tuple<string, int, int, int, int, int>($"$user gained {power}$emeralds!", power, 1, 0, 0, power));
            chances.Add(new Tuple<string, int, int, int, int, int>($"$target gained {power}$emeralds", 0, 0, power, 1, power));
        }
        if (power >= 5)
        {
            chances.Add(new Tuple<string, int, int, int, int, int>("$user gained 1$sapphires", 1, 2, 0, 0, 4));
            chances.Add(new Tuple<string, int, int, int, int, int>("$target gained 1$sapphires", 0, 0, 1, 2, 4));
        }
        if (power >= 6)
        {
            chances.Add(new Tuple<string, int, int, int, int, int>("$user and $target gained 80$diamonds", 80, 0, 80, 0, 12));
            chances.Add(new Tuple<string, int, int, int, int, int>("$user gained 120$diamonds", 120, 0, 0, 0, 6));
            chances.Add(new Tuple<string, int, int, int, int, int>("$target gained 120$diamonds", 0, 0, 120, 0, 6));
            chances.Add(new Tuple<string, int, int, int, int, int>($"$user gained {power}$emeralds!", power, 1, 0, 0, power));
            chances.Add(new Tuple<string, int, int, int, int, int>($"$target gained {power}$emeralds", 0, 0, power, 1, power));
        }
        if (power >= 8)
        {
            chances.Add(new Tuple<string, int, int, int, int, int>("$user gained $user_wand\n$target gained $target_wand", 0, 0, 0, 0, 1));
        }
        if (power >= 9)
        {
            chances.Add(new Tuple<string, int, int, int, int, int>("$user gained $user_charm", 0, 0, 0, 0, 5));
            chances.Add(new Tuple<string, int, int, int, int, int>("$target gained $target_charm", 0, 0, 0, 0, 4));
            chances.Add(new Tuple<string, int, int, int, int, int>("$user gained $user_charm and $target gained $target_charm", 0, 0, 0, 0, 1));
        }
        if (power >= 10)
        {
            chances.Add(new Tuple<string, int, int, int, int, int>("$user gained $user_charm", 0, 0, 0, 0, 5));
            chances.Add(new Tuple<string, int, int, int, int, int>("$target gained $target_charm", 0, 0, 0, 0, 4));
            chances.Add(new Tuple<string, int, int, int, int, int>("$user gained $user_charm, $user_charm2", 0, 0, 0, 0, 2));
            chances.Add(new Tuple<string, int, int, int, int, int>("$target gained $target_charm, $target_charm2", 0, 0, 0, 0, 1));
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
        await user.Add(tuple.Item2, tuple.Item3);
        await target.Add(tuple.Item4, tuple.Item5);
        string diamonds = " **diamonds**";
        string emeralds = " **emeralds**";
        string sapphires = " **sapphires**";
        bool emoji = Tools.ShowEmojis(command, settings.BotID(), _client);
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
                    toRespond = toRespond.Replace("`", emoji ? $"1{_items[itemID].Emoji}" : $"1 **{_items[itemID].Name}**");
                    break;
                }
                case '~':
                {
                    int itemID = Tools.GetCharm(_itemLists, 0, 99);
                    await user.GainItem(itemID, 1);
                    toRespond = toRespond.Replace("~", emoji ? $"1{_items[itemID].Emoji}" : $"1 **{_items[itemID].Name}**");
                    break;
                }
                case '%':
                {
                    int itemID = Tools.GetCharm(_itemLists, 0, 99);
                    await target.GainItem(itemID, 1);
                    toRespond = toRespond.Replace("%", emoji ? $"1{_items[itemID].Emoji}" : $"1 **{_items[itemID].Name}**");
                    break;
                }
                case '¡':
                {
                    int itemID = Tools.GetCharm(_itemLists, 0, 199);
                    await target.GainItem(itemID, 1);
                    toRespond = toRespond.Replace("¡", emoji ? $"1{_items[itemID].Emoji}" : $"1 **{_items[itemID].Name}**");
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
            .WithColor(new Color((uint)(await user.GetSetting("magikRandomColor", 1) switch{0 => await user.GetSetting("magikColor", 13107400), _ => (ulong)_rand.Next(16777216)})));
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
        if (badInput) await command.FollowupAsync("You can't set a target as default if you don't specify a target. Sorry.", ephemeral:true);
    }
    private async Task SetTutorial(SocketSlashCommand command)
    {
        IReadOnlyCollection<SocketSlashCommandDataOption> options = command.Data.Options;
        ushort i = (ushort)options.Count;
        switch (i)
        {
            case 0:
                if (!_users.TryGetValue(command.User.Id, out CachedUser? user1)) { await command.RespondAsync("You have been inactive, and your tutorial progress has been reset."); return; }
                if (user1.TutorialOn == null) { await command.RespondAsync("You do not have an active tutorial"); return; }
                Tutorial tutorial = _tutorials[(int)user1.TutorialOn];
                Step step = tutorial.Steps[user1.TutorialPage];
                EmbedBuilder embay = new EmbedBuilder()
                    .WithTitle($"{tutorial.Name}: {step.Name}")
                    .WithDescription(step.Description);
                await command.RespondAsync("Here is your tutorial progress:", embed: embay.Build());
                break;
            case 1:
                if (_users.TryGetValue(command.User.Id, out CachedUser? user2))
                {
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
                        if (user2.TutorialOn == null) { await command.RespondAsync("You do not have an active tutorial"); return; }
                        Tutorial tutorial2 = _tutorials[(int)user2.TutorialOn];
                        Step step2 = tutorial2.Steps[user2.TutorialPage];
                        EmbedBuilder embay2 = new EmbedBuilder()
                            .WithTitle($"{tutorial2.Name}: {step2.Name}")
                            .WithDescription(step2.Description);
                        await command.RespondAsync("Here is your tutorial progress:", embed: embay2.Build());
                    }
                }
                else
                {
                    await command.RespondAsync("You have been inactive, and have no tutorial progress. Trying to activate you...");
                    await GetUser(command.User.Id);
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
        await command.RespondAsync(embed: new EmbedBuilder().WithTitle("Theme Changed").WithDescription($"Your gemBOT theme has been changed to {themeName}.").WithColor((uint)await user.GetSetting("uiColor", 3287295)).Build());
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
        if (await Tools.CharmEffect(["BetterBankTrades"], _items, user) >= 1) { upgradePrice = 10; downgradeReward = 10; }
        ButtonStyle leftMain = await user.GetSetting("bankLeftStyle", 0) switch { 0 => ButtonStyle.Primary, 1 => ButtonStyle.Success, _ => ButtonStyle.Secondary };
        ButtonStyle rightMain = await user.GetSetting("bankRightStyle", 2) switch { 0 => ButtonStyle.Primary, 1 => ButtonStyle.Success, _ => ButtonStyle.Secondary };
        ButtonStyle leftSecondary = await user.GetSetting("bankShowRed", 1) switch { 1 => ButtonStyle.Danger, _ => leftMain };
        ButtonStyle rightSecondary = await user.GetSetting("bankShowRed", 1) switch { 1 => ButtonStyle.Danger, _ => rightMain };
        ButtonStyle b1 = (user.Gems[0] >= 11) switch { true => leftMain, false => leftSecondary };
        ButtonStyle b2 = (user.Gems[1] >= 11) switch { true => leftMain, false => leftSecondary };
        ButtonStyle b3 = (user.Gems[2] >= 11) switch { true => leftMain, false => leftSecondary };
        ButtonStyle b4 = (user.Gems[3] >= 11) switch { true => leftMain, false => leftSecondary };
        ButtonStyle b5 = (user.Gems[1] >= 1) switch { true => rightMain, false => rightSecondary };
        ButtonStyle b6 = (user.Gems[2] >= 1) switch { true => rightMain, false => rightSecondary };
        ButtonStyle b7 = (user.Gems[3] >= 1) switch { true => rightMain, false => rightSecondary };
        ButtonStyle b8 = (user.Gems[4] >= 1) switch { true => rightMain, false => rightSecondary };
        string emoj = showEmojis switch { true => "yes", false => "no"};
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
        Tuple<EmbedBuilder, ComponentBuilder, string> results = await BankRaw(Tools.ShowEmojis(command, settings.BotID(), _client), command.User.Id);
        await command.RespondAsync(results.Item3, ephemeral:false, embed:results.Item1.Build(), components: results.Item2.Build());
    }
    
    private async Task InventoryButton(SocketMessageComponent component, string settings)
    {
        ulong id = component.User.Id;
        try { await (await GetUser(id)).ItemAmount(_items.Count - 1); }
        catch { await Tools.UserCreator(id); }
        ulong oID = component.Message.Interaction.User.Id;
        if (id != oID)
        {
            await component.RespondAsync("This is not your inventory. You cannot click any buttons", ephemeral:true);
        }
        string[] settings2 = settings.Split("|");
        EmbedBuilder embay = await InventoryRawEmbed(id, settings);
        int page = int.Parse(settings.Split("|")[0]);
        ComponentBuilder builder = new ComponentBuilder();
        if (page > 0) {builder.WithButton("<-- Left", $"inv-{int.Parse(settings2[0])-1}|{settings2[1]}");}
        else {builder.WithButton("<-- Left", "disabledL", disabled:true);}
        builder.WithButton("Refresh", $"inv-{settings}", ButtonStyle.Secondary);
        if (page < (int)Math.Ceiling(_items.Count/8.0)-1){builder.WithButton("Right -->", $"inv-{int.Parse(settings2[0])+1}|{settings2[1]}");}
        else { builder.WithButton("Right -->", "disabledR",  disabled:true); }
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
        bool showEmojis = showEmojisText switch { "no" => false, "yes" => true, _ => false};
        int bottomValue = transaction % 4;
        int type = transaction / 4;
        int upgradeFor = 11;
        int downgradeTo = 9;
        if (await Tools.CharmEffect(["BetterBankTrades"], _items, user) >= 1) { upgradeFor = 10; downgradeTo = 10; }
        switch (type)
        {
            case 0:
                if (user.Gems[bottomValue] < upgradeFor)
                {
                    await component.RespondAsync("You can't afford this trade.", ephemeral:true);
                    return;
                }
                await user.Add(-1*upgradeFor, bottomValue, false);
                await user.Add(1, bottomValue + 1);
                break;
            case 1:
                if (user.Gems[bottomValue + 1] < 1)
                {
                    await component.RespondAsync("You can't afford this trade.", ephemeral:true);
                    return;
                }
                await user.Add(-1, bottomValue+1, false);
                await user.Add(downgradeTo, bottomValue);
                break;
        }
        //step 2: refresh bank if original user, otherwise send message.
        if (component.Message.Interaction.User.Id != user.ID)
        {
            await component.RespondAsync(await BalanceRaw(showEmojis, component.User.Id, "The transaction was completed successfully!\n**Your balance**:"));
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
                $"An internal error due to button definition prevented this button to be handled. \n > `Button of id {realID[0]} was found, but arguments {realID[1]} were not written correctly`");
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            var embay = new EmbedBuilder()
                .WithTitle("Error")
                .WithAuthor(component.User)
                .WithColor(255, 0, 0)
                .AddField("Your command generated an error", $"**Full Details**: `{e}`");
            await component.RespondAsync(embed:embay.Build());
        }
    }
    
    
        
    
    
    private async Task TextMessageHandler(SocketMessage socketMessage)
    {
        if (!settings.OwnerIDs().Contains(socketMessage.Author.Id))
        {
            return;
        }
        string message = socketMessage.ToString();
        if (message.Length == 0)
        {
            return;
        }
        string[] command;
        if (message.StartsWith($"<@{settings.BotID()}>$")) 
        {
            command = message.Split($"<@{settings.BotID()}>$")[1].Split(' ');
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
                default:
                    await socketMessage.Channel.SendMessageAsync("This command was not found");
                    break;
            }
        }
        catch (Exception e)
        {
            await socketMessage.Channel.SendMessageAsync(e.ToString());
        }
    }
    private async Task ResetCommands(SocketMessage message)
    {
        await _client.Rest.DeleteAllGlobalCommandsAsync();
        SlashCommandBuilder itemInfo = new SlashCommandBuilder()
            .WithName("item")
            .WithDescription("Get information about an item.")
            .WithIntegrationTypes([ApplicationIntegrationType.GuildInstall])
            .AddOption("item", ApplicationCommandOptionType.Integer, "The item id of the item you would like to access.", true);
        SlashCommandBuilder balance = new SlashCommandBuilder()
            .WithName("balance")
            .WithDescription("Find out your balance")
            .WithIntegrationTypes([ApplicationIntegrationType.GuildInstall, ApplicationIntegrationType.UserInstall])
            .AddOption(new SlashCommandOptionBuilder()
                .WithType(ApplicationCommandOptionType.Boolean)
                .WithName("private")
                .WithDescription("Whether to keep your balance private (ephemeral message) or show it to everyone (normal message).")
                .WithRequired(false)
            );
        SlashCommandBuilder stats = new SlashCommandBuilder()
            .WithName("stats")
            .WithDescription("Figure out your stats");
        SlashCommandBuilder beg = new SlashCommandBuilder()
            .WithName("beg")
            .WithDescription("Beg for diamonds!");
        SlashCommandBuilder inventory = new SlashCommandBuilder()
            .WithName("inventory")
            .WithDescription("View your inventory!");
        SlashCommandBuilder work = new SlashCommandBuilder()
            .WithName("work")
            .WithDescription("Work for emeralds every 5 minutes!");
        SlashCommandBuilder magik = new SlashCommandBuilder()
            .WithName("magik")
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
            .WithDescription("Convert your currency to other values");
        SlashCommandBuilder theme = new SlashCommandBuilder()
            .WithName("theme")
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
        try
        {
            await _client.CreateGlobalApplicationCommandAsync(itemInfo.Build());
            await _client.CreateGlobalApplicationCommandAsync(balance.Build());
            await _client.CreateGlobalApplicationCommandAsync(stats.Build());
            await _client.CreateGlobalApplicationCommandAsync(beg.Build());
            await _client.CreateGlobalApplicationCommandAsync(inventory.Build());
            await _client.CreateGlobalApplicationCommandAsync(work.Build());
            await _client.CreateGlobalApplicationCommandAsync(magik.Build());
            await _client.CreateGlobalApplicationCommandAsync(help.Build());
            await _client.CreateGlobalApplicationCommandAsync(start.Build());
            await _client.CreateGlobalApplicationCommandAsync(bank.Build());
            await _client.CreateGlobalApplicationCommandAsync(theme.Build());
        }
        catch(HttpException exception)
        {
            var json = JsonConvert.SerializeObject(exception.Errors, Formatting.Indented);
            Console.WriteLine(json);
        }
        await message.Channel.SendMessageAsync("Reset commands!");
    }
    private async Task SetItem(SocketMessage message)
    {
        string[] command = message.ToString().Split(" ");
        if (command.Length < 4)
        {
            await message.Channel.SendMessageAsync("Please enter the command in a proper format");
        }
        if (int.Parse(command[1]) >= _items.Count)
        {
            await message.Channel.SendMessageAsync("Please use $add_item to add another item to the list.");
            return;
        }
        try
        {
            Item item = _items[int.Parse(command[1])];
            switch (command[2])
            {
                case "id":
                    item.ID = int.Parse(command[3]);
                    await File.WriteAllTextAsync(IDString(int.Parse(command[1])),
                        JsonConvert.SerializeObject(item));
                    await message.Channel.SendMessageAsync($"## Item saved! \n {item}");
                    //await GetItems();
                    break;
                case "value":
                    item.Value = int.Parse(command[3]);
                    await File.WriteAllTextAsync(IDString(int.Parse(command[1])),
                        JsonConvert.SerializeObject(item));
                    await message.Channel.SendMessageAsync($"## Item saved! \n {item}");
                    //await GetItems();
                    break;
                case "name":
                    item.Name = String.Join(" ", command[3..^0]);
                    await File.WriteAllTextAsync(IDString(int.Parse(command[1])),
                        JsonConvert.SerializeObject(item));
                    await message.Channel.SendMessageAsync($"## Item saved! \n {item}");
                    //await GetItems();
                    break;
                case "emoji":
                    item.Emoji = command[3];
                    await File.WriteAllTextAsync(IDString(int.Parse(command[1])),
                        JsonConvert.SerializeObject(item));
                    await message.Channel.SendMessageAsync($"## Item saved! \n {item}");
                    //await GetItems();
                    break;
                case "description":
                    item.Description = String.Join(" ", command[3..^0]);
                    await File.WriteAllTextAsync(IDString(int.Parse(command[1])),
                        JsonConvert.SerializeObject(item));
                    await message.Channel.SendMessageAsync($"## Item saved! \n {item}");
                    //await GetItems();
                    break;
                default:
                    await message.Channel.SendMessageAsync($"Could not find option for {command[2]}.");
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
            await message.Channel.SendMessageAsync("Please enter the command in a proper format");
        }
        if (itemID >= _items.Count)
        {
            await message.Channel.SendMessageAsync("Please use $add_item to add another item to the list.");
            return;
        }
        Item item = _items[itemID];
        if (charmID >= item.Charms.Count)
        {
            await message.Channel.SendMessageAsync("Please use $add_charm to add another charm to the list.");
        }
        try
        {
            Charm charm = item.Charms[charmID];
            switch (command[3])
            {
                case "name":
                    charm.Effect = command[4];
                    await File.WriteAllTextAsync(IDString(int.Parse(command[1])),
                        JsonConvert.SerializeObject(item));
                    await message.Channel.SendMessageAsync($"## Charm saved! \n {charm}");
                    //await GetItems();
                    break;
                case "effect":
                    charm.Amount = int.Parse(command[4]);
                    await File.WriteAllTextAsync(IDString(int.Parse(command[1])),
                        JsonConvert.SerializeObject(item));
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
        await File.WriteAllTextAsync(IDString(_items.Count-1), JsonConvert.SerializeObject(_items[^1]));
    }
    private async Task AddCharm(SocketMessage message)
    {
        string[] command = message.ToString().Split(" ");
        if (command.Length < 2)
        {
            await message.Channel.SendMessageAsync("Please enter the command in a proper format");
        }
        if (int.Parse(command[1]) >= _items.Count)
        {
            await message.Channel.SendMessageAsync("Please use $add_item to add another item to the list.");
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
            await message.Channel.SendMessageAsync("Please enter the command correctly.");
            return;
        }
        string listName = dat[1];
        if (!_itemLists.TryGetValue(listName, out List<int>? list)) { list = ([]); _itemLists[listName] = list; await message.Channel.SendMessageAsync("Created list!"); return;}
        string action = dat[2];
        int item = int.Parse(dat[3]);
        switch (action)
        {
            case "add":
                list.Add(item);
                await File.WriteAllTextAsync($"../../../Data/ItemLists/{listName}.json", JsonConvert.SerializeObject(list));
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
                await File.WriteAllTextAsync($"../../../Data/ItemLists/{listName}.json", JsonConvert.SerializeObject(list));
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
            await message.Channel.SendMessageAsync("Please enter the command correctly.");
            return;
        }
        string listName = dat[1];
        if (!_dataLists.TryGetValue(listName, out List<string>? list)) { list = ([]); _dataLists[listName] = list; await message.Channel.SendMessageAsync("Created list!"); return;}
        string action = dat[2];
        string item = string.Join(" ", dat[3..]);
        switch (action)
        {
            case "add":
                list.Add(item);
                await File.WriteAllTextAsync($"../../../Data/Lists/{listName}.json", JsonConvert.SerializeObject(list));
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
                await File.WriteAllTextAsync($"../../../Data/Lists/{listName}.json", JsonConvert.SerializeObject(list));
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
    private async Task ColorToValue(SocketMessage message)
    {
        string[] args = message.Content.Split(" ");
        if (args.Length < 4)
        {
            await message.Channel.SendMessageAsync("Please enter 4 arguments");
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
            await message.Channel.SendMessageAsync("View embed for more details", embed:embay);
        }
        catch (FormatException)
        {
            await message.Channel.SendMessageAsync("Arguments 2-4 must be integers.");
        }
    }
    private String IDString(int id)
    {
        if (id >= 100)
        {
            return $"../../../Data/Items/{id}.json";
        }
        if (id >= 10)
        {
            return $"../../../Data/Items/0{id}.json";
        }
        if (id >= 0)
        {
            return $"../../../Data/Items/00{id}.json";
        }
        throw new InvalidArgumentException();
    }
}
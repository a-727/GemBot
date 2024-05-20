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
    private List<Item> _items = new();
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
        _items = _items.OrderBy(o=>o.ID).ToList();
        await _client.SetGameAsync("/start");
    }
    private async Task<User> GetUser(ulong id)
    {
        try
        {
            string baseData = await File.ReadAllTextAsync($"../../../Data/Users/{id}");
            return JsonConvert.DeserializeObject<User>(baseData) ??
                   throw new Exception("Somehow your save file is bad.");
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
                default:
                    await command.RespondAsync("Command not found", ephemeral: true);
                    break;
            }
        }
        catch (Cooldown cool)
        {
            await command.RespondAsync(cool.Message, ephemeral: true);
        }
        catch (UserNotFoundError)
        {
            await UserSetupSlash(command);
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
    private async Task Balance(SocketSlashCommand command, string atStartInfo = "**Your balance**:", bool? compactArg = null, bool ephemeral = true)
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
        await user.IncreaseStat("commands", 1);
        string text = $"{atStartInfo} {user.Gems[0]}{_currency[0]}, {user.Gems[1]}{_currency[1]}, {user.Gems[2]}{_currency[2]}, {user.Gems[3]}{_currency[3]}, {user.Gems[4]}{_currency[4]}";
        if (!compact)
        {
            text = $"{atStartInfo}\n > **Diamonds**: {user.Gems[0]}\n > **Emeralds**: {user.Gems[1]}\n > **Sapphires**: {user.Gems[2]}\n > **Rubies**: {user.Gems[3]}\n > **Amber**: {user.Gems[4]}";
        }
        await command.RespondAsync(text, ephemeral:ephemeral);
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
        await user.IncreaseStat("commands", 1);
        EmbedBuilder embay = new EmbedBuilder()
            .WithTitle($"{command.User.Username}'s stats")
            .WithDescription($"View all the stats for {command.User.Username}.")
            .WithFooter(new EmbedFooterBuilder().WithText("GemBOT Stats may not be 100% accurate."))
            .AddField("Commands Ran", $"{user.GetStat("commands")} commands")
            .AddField("Gems Earned", $"{user.GetStat("earned")} diamond-equivalents (one diamond is 1, one emerald is 10, one sapphire is 100, one ruby is 1000, and one amber is 10000): earned by grinding only.");
        await command.RespondAsync(embed:embay.Build());
    }
    private async Task Beg(SocketSlashCommand command)
    {
        User user = await GetUser(command.User.Id);
        ulong t = (ulong)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        if (await user.OnCoolDown("beg", t, 5)) { throw new Cooldown(user.CoolDowns["beg"]); }
        int mn = 5 + await Tools.CharmEffect(["BegMin", "Beg", "GrindMin", "Grind", "Positive"], _items, user);
        int mx = 9 + await Tools.CharmEffect(["BegMax", "Beg", "GrindMax", "Grind", "Positive"], _items, user);
        int amnt = _rand.Next(mn, mx);
        await user.Add(amnt, 0, false);
        string text = $"You gained {amnt} **Diamonds**.";
        if (Tools.ShowEmojis(command, settings.BotID(), _client))
        {
            text = $"You gained {amnt}{_currency[0]}!!!";
        }
        await user.IncreaseStat("commands",1, false);
        await user.IncreaseStat("earned", amnt);
        await command.RespondAsync(text);
    }
    private async Task Work(SocketSlashCommand command)
    {
        User user = await GetUser(command.User.Id);
        ulong t = (ulong)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
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
        await user.IncreaseStat("commands",1, false);
        await user.IncreaseStat("earned", amnt*10);
        await command.RespondAsync(text);
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
            .WithButton("<-- Left", $"inv-0|{emoj}|L", disabled:true)
            .WithButton("Refresh", $"inv-0|{emoj}|reset", ButtonStyle.Secondary)
            .WithButton("Right -->", $"inv-1|{emoj}|R");
        EmbedBuilder embay = await InventoryRawEmbed(command.User.Id, $"0|{emoj}");
        await command.RespondAsync(embed:embay.Build(), components:builder.Build());
    }
    private async Task Magik(SocketSlashCommand command)
    {
        User user = await GetUser(command.User.Id);
        ulong t = (ulong)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        if (await user.OnCoolDown("work", t, 12)) { throw new Cooldown(user.CoolDowns["work"]); }
        int power =  await Tools.CharmEffect(["Magik", "Unlocker", "Positive"], _items, user);
        ulong targetID;
        try { targetID = ((SocketGuildUser)command.Data.Options.First().Value).Id; }
        catch { targetID = await user.GetSetting("magikID", user.ID); }
        User target = await GetUser(targetID);
        if (targetID != user.ID) { power += 3; }
        power += _rand.Next(0, 2);
        if (targetID != await user.GetSetting("magikID", user.ID)) { power += 1; }
        List<Tuple<string, int, int, int, int, int>> chances = [new Tuple<string, int, int, int, int, int>("You gained 8$diamonds.", 8, 0, 1, 0, 9), new Tuple<string, int, int, int, int, int>("You gained 1$emeralds.", 1, 0, 0, 0, 4), new Tuple<string, int, int, int, int, int>("Nothing happened", 0, 0, 0, 0, 7), new Tuple<string, int, int, int, int, int>("$target gained 10$diamonds", 0, 0, 10, 0, 6)];
        if (power >= 1)
        {
            chances.Add(new Tuple<string, int, int, int, int, int>("$user and $target both gained 7$diamonds1", 7, 0, 7, 0, 10));
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
            chances.Add(new Tuple<string, int, int, int, int, int>("$user and $target gained 1$emeralds", 1, 1, 1, 1, 25));
            chances.Add(new Tuple<string, int, int, int, int, int>("$user and $target gained 2$emeralds", 2, 1, 2, 1, 15));
            chances.Add(new Tuple<string, int, int, int, int, int>("$user and $target both gained 3$emeralds", 3, 1, 3, 1, 5));
            chances.Add(new Tuple<string, int, int, int, int, int>("$user gained 1$sapphires", 1, 2, 0, 0, 2));
            chances.Add(new Tuple<string, int, int, int, int, int>("$target gained 1$sapphires", 0, 0, 1, 2, 2));
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
        if (Tools.ShowEmojis(command, settings.BotID(), _client))
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
            .Replace("$target_charm", "!")
            .Replace("$target_charm2", "¡")
            .Replace("$user_wand", "*")
            .Replace("$target_ward", "•");
        foreach (char c in toRespond)
        {
            if (c == '`')
            {
                int itemID = Tools.GetCharm(_itemLists, 0, 99);
            }
        }
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
        if (page > 0) {builder.WithButton("<-- Left", $"inv-{int.Parse(settings2[0])-1}|{settings2[1]}|L");}
        else {builder.WithButton("<-- Left", $"inv-0|{settings2[1]}|L", disabled:true);}
        builder.WithButton("Refresh", $"inv-{settings}+r", ButtonStyle.Secondary);
        if (page < (int)Math.Ceiling(_items.Count/8.0)-1){builder.WithButton("Right -->", $"inv-{int.Parse(settings2[0])+1}|{settings2[1]}|R");}
        else { builder.WithButton("Right -->", $"inv-{int.Parse(settings2[0]) + 1}|{settings2[1]}|R",  disabled:true); }
        await component.UpdateAsync(Modify);
        return;

        void Modify(MessageProperties properties)
        {
            properties.Embed = embay.Build();
            properties.Components = builder.Build();
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
                default:
                    await component.RespondAsync($"**Button type not found.**\n > `Button of id {realID[0]} and options {realID[1]} was not able to be executed because id {realID[0]} was not found.`", ephemeral: true);
                    break;
            }
        }
        catch (ButtonValueError)
        {
            await component.RespondAsync($"An internal error due to button definition prevented this button to be handled. \n > `Button of id {realID[0]} was found, but arguments {realID[1]} were not written correctly`");
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
            case "list":
                await AddList(socketMessage);
                break;
            default:
                await socketMessage.Channel.SendMessageAsync("This command was not found");
                break;
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
                .WithName("Target")
                .WithDescription("Who else should get gems?")
                .WithType(ApplicationCommandOptionType.User)
            );
        try
        {
            await _client.CreateGlobalApplicationCommandAsync(itemInfo.Build());
            await _client.CreateGlobalApplicationCommandAsync(balance.Build());
            await _client.CreateGlobalApplicationCommandAsync(stats.Build());
            await _client.CreateGlobalApplicationCommandAsync(beg.Build());
            await _client.CreateGlobalApplicationCommandAsync(inventory.Build());
            await _client.CreateGlobalApplicationCommandAsync(work.Build());
            await _client.CreateGlobalApplicationCommandAsync(magik.Build());
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
    private async Task AddList(SocketMessage message)
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
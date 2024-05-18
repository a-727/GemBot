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
    public Cooldown(long endSeconds): base($"You are on cooldown. Please try again in <t:{endSeconds}:R>"){}
    public Cooldown (int secondsLeft): base($"You are on cooldown. Please try again in <t:{secondsLeft+DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:R>"){}
}

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
    public readonly Random Rand = new ();
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
            if (item is not null)
            {
                _items.Add(item);
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
            await UserSetup(command);
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
        catch (IndexOutOfRangeException)
        {
            await command.RespondAsync("This item does not exist");
        }
    }
    private async Task UserSetup(SocketSlashCommand command)
    {
        var id = command.User.Id;
        if (Path.Exists($"../../../Data/OldUsers/{id}"))
        {
            User user = await Tools.UserCreator(id);
            string inventoryRaw = await File.ReadAllTextAsync($"../../../Data/OldUsers/{id}/i");
            string[] inventoryStrings = inventoryRaw.Split(" ");
            user.Inventory = new List<int>();
            foreach (string i in inventoryStrings)
            {
                user.Inventory.Add(int.Parse(i));
            }
            string balanceRaw = await File.ReadAllTextAsync($"../../../Data/OldUsers/{id}/g");
            string[] balanceStrings = balanceRaw.Split(" ");
            for (int i = 0; i < balanceStrings.Length; i++)
            {
                user.Gems[i] = int.Parse(balanceStrings[i]);
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
        int amnt = Rand.Next(5, 9);
        user.Add(amnt, 0);
        string text = $"You gained {amnt} **Diamonds**.";
        if (Tools.ShowEmojis(command, settings.BotID(), _client))
        {
            text = $"You gained {amnt}{_currency[0]}!!!";
        }
        await user.Save();
        await command.RespondAsync(text);
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
        if (message.StartsWith($"<@{settings.BotID()}>$")) //need to turn ulong properly to string
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
            case "item":
                await SetItem(socketMessage);
                break;
            case "all_items":
                await AllItemsText(socketMessage);
                break;
            case "add_item":
                await AddItem(socketMessage);
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
        try
        {
            await _client.CreateGlobalApplicationCommandAsync(itemInfo.Build());
            await _client.CreateGlobalApplicationCommandAsync(balance.Build());
            await _client.CreateGlobalApplicationCommandAsync(stats.Build());
            await _client.CreateGlobalApplicationCommandAsync(beg.Build());
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
                    await GetItems();
                    break;
                case "value":
                    item.Value = int.Parse(command[3]);
                    await File.WriteAllTextAsync(IDString(int.Parse(command[1])),
                        JsonConvert.SerializeObject(item));
                    await message.Channel.SendMessageAsync($"## Item saved! \n {item}");
                    await GetItems();
                    break;
                case "name":
                    item.Name = String.Join(" ", command[3..^0]);
                    await File.WriteAllTextAsync(IDString(int.Parse(command[1])),
                        JsonConvert.SerializeObject(item));
                    await message.Channel.SendMessageAsync($"## Item saved! \n {item}");
                    await GetItems();
                    break;
                case "emoji":
                    item.Emoji = command[3];
                    await File.WriteAllTextAsync(IDString(int.Parse(command[1])),
                        JsonConvert.SerializeObject(item));
                    await message.Channel.SendMessageAsync($"## Item saved! \n {item}");
                    await GetItems();
                    break;
                case "description":
                    item.Description = String.Join(" ", command[3..^0]);
                    await File.WriteAllTextAsync(IDString(int.Parse(command[1])),
                        JsonConvert.SerializeObject(item));
                    await message.Channel.SendMessageAsync($"## Item saved! \n {item}");
                    await GetItems();
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
    private async Task AddItem(SocketMessage message)
    {
        _items.Add(new Item(_items.Count, description: "To be set"));
        await message.Channel.SendMessageAsync(_items[^1].ToString());
        await File.WriteAllTextAsync(IDString(_items.Count-1), JsonConvert.SerializeObject(_items[^1]));
    }
    private async Task AllItemsText(SocketMessage socketMessage)
    {
        foreach (Item item in _items)
        {
            await socketMessage.Channel.SendMessageAsync(item.ToString());
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
using System.ComponentModel;
using System.Reflection;
using System.Threading.Channels;
using Discord;
using Discord.Commands;
using Discord.Net;
using Discord.WebSocket;
using Newtonsoft.Json;

namespace GemBot;

public class NoTokenError : Exception
{
    
}
public class InvalidArgumentException: Exception{}

public class Cooldown : Exception
{
    public Cooldown() : base($"You are on cooldown.") { }
    public Cooldown(string until) : base($"You are on cooldown. Please try again in {until}."){}
    public Cooldown(long endSeconds): base($"You are on cooldown. Please try again in <t:{endSeconds}:R>"){}
    public Cooldown (int secondsLeft): base($"You are on cooldown. Please try again in <t:{secondsLeft+DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:R>"){}
}

public class Program
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
    private readonly CommandService _commandService = new();
    private List<Item> _items = new();
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
    public async Task GetItems()
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
                case "test":
                    await ReadFileData(command);
                    break;
                case "balance":
                    await Balance(command);
                    break;
                case "item":
                    await GetItem(command);
                    break;
                default:
                    throw new Cooldown(250);
            }
        }
        catch (Cooldown cool)
        {
            command.RespondAsync(cool.Message, ephemeral:true);
        }
        catch (Exception e)
        {
            var embay = new EmbedBuilder()
                .WithTitle("Error")
                .WithAuthor(command.User)
                .WithColor(255, 0, 0)
                .AddField("Your command generated an error", $"**Full Details**: `{e}`");
            command.RespondAsync(embed:embay.Build());
        }
    }
    private async Task ReadFileData(SocketSlashCommand command)
    {
        string ToRespond = "";
        try
        {
            string responce = command.Data.Options.First().Value.ToString() ?? throw new InvalidOperationException();
            if (responce.Contains("../"))
            {
                await command.RespondAsync("# Hacker Warning\n **__The person who ran this command is a suspected hacker__**\n *Here's Why*: \n > They tried to access a file outside where they were supposed to access. Nobody would try this, unless they want to steal information from my computer.");
                return;
            }
            string fileData = await File.ReadAllTextAsync($"../../../{responce}");
            Item item = JsonConvert.DeserializeObject<Item>(fileData) ?? throw new InvalidOperationException();
            await command.RespondAsync(
                $"**Item {item.ID} Data**:\n > Name: *{item.Name}*\n > Emoji: {item.Emoji}\n > Description: {item.Description}");
        }
        catch (InvalidOperationException)
        {
            await command.RespondAsync("The file does not seem to be an item file.");
        }
        catch (JsonReaderException)
        {
            await command.RespondAsync("The file does not seem to be an item file.");
        }
        catch (FileNotFoundException)
        {
            await command.RespondAsync("The file was not found");
        }
        catch (DirectoryNotFoundException)
        {
            await command.RespondAsync("You can't look for a file inside a nonexistent directory");
        }
        catch (UnauthorizedAccessException)
        {
            await command.RespondAsync("This code cannot read the file, so why should you be able to read it?");
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
        string[] command = new string[] { };
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
        var itemInfo = new SlashCommandBuilder()
            .WithName("item")
            .WithDescription("Get information about an item.")
            .WithIntegrationTypes([ApplicationIntegrationType.GuildInstall, ApplicationIntegrationType.UserInstall])
            .AddOption("item", ApplicationCommandOptionType.Integer, "The item id of the item you would like to access.", true);
        var balance = new SlashCommandBuilder()
            .WithName("balance")
            .WithDescription("Find out your balance")
            .WithIntegrationTypes([ApplicationIntegrationType.GuildInstall, ApplicationIntegrationType.UserInstall]);
        try
        {
            await _client.CreateGlobalApplicationCommandAsync(itemInfo.Build());
            await _client.CreateGlobalApplicationCommandAsync(balance.Build());
        }
        catch(HttpException exception)
        {
            var json = JsonConvert.SerializeObject(exception.Errors, Formatting.Indented);
            Console.WriteLine(json);
        }
        await message.Channel.SendMessageAsync("Reset commands!");
    }
    private async Task Balance(SocketSlashCommand command)
    {
        try
        {
            string baseData = await File.ReadAllTextAsync($"../../../Data/DiscordUsers/{command.User.Id}");
            JsonConvert.DeserializeObject<DiscordUserLoader>(baseData);
        }
        catch (FileNotFoundException)
        {
            
        }
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
                    await File.WriteAllTextAsync(await IDString(int.Parse(command[1])),
                        JsonConvert.SerializeObject(item));
                    await message.Channel.SendMessageAsync($"## Item saved! \n {item}");
                    await GetItems();
                    break;
                case "value":
                    item.Value = int.Parse(command[3]);
                    await File.WriteAllTextAsync(await IDString(int.Parse(command[1])),
                        JsonConvert.SerializeObject(item));
                    await message.Channel.SendMessageAsync($"## Item saved! \n {item}");
                    await GetItems();
                    break;
                case "name":
                    item.Name = String.Join(" ", command[3..^0]);
                    await File.WriteAllTextAsync(await IDString(int.Parse(command[1])),
                        JsonConvert.SerializeObject(item));
                    await message.Channel.SendMessageAsync($"## Item saved! \n {item}");
                    await GetItems();
                    break;
                case "emoji":
                    item.Emoji = command[3];
                    await File.WriteAllTextAsync(await IDString(int.Parse(command[1])),
                        JsonConvert.SerializeObject(item));
                    await message.Channel.SendMessageAsync($"## Item saved! \n {item}");
                    await GetItems();
                    break;
                case "description":
                    item.Description = String.Join(" ", command[3..^0]);
                    await File.WriteAllTextAsync(await IDString(int.Parse(command[1])),
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
    private async Task GetItem(SocketSlashCommand command)
    {
        try
        {
            await command.RespondAsync(_items[int.Parse(command.Data.Options.First().Value.ToString())].ToString());
        }
        catch (IndexOutOfRangeException)
        {
            await command.RespondAsync("This item does not exist");
        }
    }
    private async Task AddItem(SocketMessage message)
    {
        _items.Add(new Item(_items.Count, description: "To be set"));
        await message.Channel.SendMessageAsync(_items[^1].ToString());
        await File.WriteAllTextAsync(await IDString(_items.Count-1), JsonConvert.SerializeObject(_items[^1]));
    }
    private async Task AllItemsText(SocketMessage socketMessage)
    {
        foreach (Item item in _items)
        {
            await socketMessage.Channel.SendMessageAsync(item.ToString());
        }
    }
    private async Task<String> IDString(int id)
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
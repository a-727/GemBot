using Discord;
using Discord.Net;
using Discord.WebSocket;
using Newtonsoft.Json;

namespace GemBot;

public class NoTokenError : Exception
{
    
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
    private DiscordSocketClient _client = new();

    public async Task Main()
    {
        _client.Log += Log;
        _client.Ready += ClientReady;
        _client.SlashCommandExecuted += CommandHandler;
        string token;
        try
        {
            token = await File.ReadAllTextAsync("../../../token.txt");
        }
        catch
        {
            throw new NoTokenError();
        }
        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();
        await Task.Delay(-1);
    }
    private Task Log(LogMessage msg)
    {
        Console.WriteLine(msg.ToString());
        return Task.CompletedTask;
    }
    private async Task ClientReady()
    {
        var testCommand = new SlashCommandBuilder()
            .WithName("test")
            .WithDescription("Read the JSON data from a file")
            .AddOption("filename", ApplicationCommandOptionType.String, "The file you would like to read data from", true);
        var testCommand2 = new SlashCommandBuilder();
        testCommand2.WithName("test2");
        testCommand2.WithDescription("For testing purposes only");
        try
        {
            await _client.CreateGlobalApplicationCommandAsync(testCommand.Build());
            await _client.CreateGlobalApplicationCommandAsync(testCommand2.Build());
        }
        catch(HttpException exception)
        {
            var json = JsonConvert.SerializeObject(exception.Errors, Formatting.Indented);
            Console.WriteLine(json);
        }
    }
    private async Task CommandHandler(SocketSlashCommand command)
    {
        switch (command.Data.Name)
        {
            case "test":
                await ReadFileData(command);
                break;
        }
    }

    private async Task ReadFileData(SocketSlashCommand command)
    {
        string ToRespond = "";
        await command.RespondAsync($"Opening file {command.Data.Options.First().Value}...", ephemeral:true);
        try
        {
            String fileData = await File.ReadAllTextAsync($"../../../{command.Data.Options.First().Value}");
            Item item = JsonConvert.DeserializeObject<Item>(fileData) ?? throw new InvalidOperationException();
        }
        catch (InvalidOperationException)
        {
            await command.RespondAsync();
        }
    }
}
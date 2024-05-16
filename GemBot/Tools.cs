using Discord;
using Newtonsoft.Json;

namespace GemBot;

public class UserExistsException(string username = "") : Exception($"Username {username} already exists")
{
    
}
public class Tools
{
    public static string RandomPasswordGenerator()
    {
        var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()+-=";
        var random = new Random();
        string toReturn = "";

        for (int i = 0; i < 10; i++)
        {
            toReturn += chars[random.Next(chars.Length)];
        }

        return toReturn;
    }
    public async static Task<User> UserCreator(string username, int numberOfItems)
    {
        if (File.Exists($"../../../Data/Users/{username}"))
        {
            throw new UserExistsException(username);
        }
        User user = new User();
        user.Username = username;
        user.Inventory = new int[numberOfItems];
        return user;
    }
}
public class DiscordUserLoader
{
    public class UserLoaderException() : Exception("A user could not be loaded");
    public int DiscordID { get; set; }
    public string InternalUsername { get; set; }

    public async Task<User> Load()
    {
        try
        {
            string fileData = await File.ReadAllTextAsync($"../../../Data/Users/{InternalUsername}");
            return JsonConvert.DeserializeObject<User>(fileData) ?? throw new InvalidOperationException();
        }
        catch
        {
            throw new UserLoaderException();
        }
    }
}
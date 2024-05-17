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
    public async static Task<User> UserCreator(ulong id)
    {
        if (File.Exists($"../../../Data/Users/{id}"))
        {
            throw new UserExistsException(id.ToString());
        }
        User user = new User();
        user.ID = id;
        user.Inventory = new List<int>();
        user.Gems = [0, 0, 1, 5, 20];
        user.CurrentPassword = RandomPasswordGenerator();
        user.CoolDowns = new Dictionary<string, long>();
        user.DailyQuestsDay = 0;
        user.DailyQuestsProgress = new Dictionary<string, ulong>();
        user.Stats = new Dictionary<string, UInt128>();
        await File.WriteAllTextAsync($"../../../Data/Users/{id}",JsonConvert.SerializeObject(user));
        return user;
    }
}
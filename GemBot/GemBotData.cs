using Discord.Rest;
using Newtonsoft.Json;

namespace GemBot;

public class Item(int id = 999)
{
    public string Name { get; set; } = "Example Item";
    public int ID { get; set; } = id;
    public string Emoji { get; set; } = ":coin:";
    public string Description { get; set;} = "To Be Set.";
    public int Value { get; set; } = 65;
    public List<Charm> Charms { get; set; } = [];

    public override string ToString()
    {
        string text =  $"**Item {ID}**:\n > *Name*: {Name}\n > *Emoji*: {Emoji}\n > *Description*: {Description}\n > *Value*: {Value} \n > *Charms*: [";
        foreach (Charm charm in Charms)
        {
            text += "(" + charm + "), ";
        }
        text = text.TrimEnd(',', ' ');
        text += "]";
        return text;
    }
}

public class Drop
{
    public string Name { get; set; } = "Example Drop";
    public int[] Items { get; set; } = [11, 12, 13, 14, 15];
    public int[] Left { get; set; } = [50000, 10000, 2000, 400, 80];
    public int[][] Price { get; set; } = [[6,0], [6,1], [6,2], [6,3], [6,4]];
    public string[] Descriptions { get; set; } = ["$iDescription", "$iDescription", "$iDescription", "$iDescription", "$iDescription"];
}

public class User
{
    public int[] Gems { get; set; } = [20, 5, 1, 0, 0];
    public Dictionary<string, ulong> CoolDowns { get; set; } = [];
    public List<int> Inventory { get; set; } = [];
    public uint DailyQuestsDay { get; set; }
    public bool[] DailyQuestsCompleted { get; set; } = new bool[5];
    public Dictionary<string, uint> DailyQuestsProgress { get; set; } = [];
    public Dictionary<string, ulong> Stats { get; set; } = [];
    public Dictionary<string, ulong> Settings { get; set; } = [];
    public Dictionary<string, int> UniqueData { get; set; } = [];
    public ulong ID { get; set; }
    public async Task<int> ItemAmount(int id)
    {
        try
        {
            return Inventory[id];
        }
        catch (IndexOutOfRangeException)
        {
            while (Inventory.Count <= id)
            {
                Inventory.Add(0);
            }
            await Save();
            return 0;
        }
        catch (ArgumentOutOfRangeException)
        {
            while (Inventory.Count <= id)
            {
                Inventory.Add(0);
            }
            await Save();
            return 0;
        }
    }
    public async Task GainItem(int id, int amount, bool save = true)
    {
        int existing = await ItemAmount(id);
        Inventory[id] = existing + amount;
        if (save) { await Save(); }
    }
    public async Task<bool> OnCoolDown(string about, ulong currentTime, uint timeoutFor, bool updateCooldown = true)
    {
        try
        {
            if (currentTime >= CoolDowns[about])
            {
                if (updateCooldown)
                {
                    CoolDowns[about] = currentTime + timeoutFor;
                    await Save();
                }
                return false;
            }
            return true;
        }
        catch (KeyNotFoundException)
        {
            if (updateCooldown)
            {
                CoolDowns[about] = currentTime + timeoutFor;
                await Save();
            }
            return false;
        }
    }
    public async Task Add(int amount, int value, bool save = true)
    {
        Gems[value] += amount;
        if (save) { await Save(); }
    }
    public void UpdateDay(uint day)
    {
        if (DailyQuestsDay >= day) return;
        DailyQuestsDay = day;
        DailyQuestsProgress = new Dictionary<string, UInt32>();
        DailyQuestsCompleted = new bool[5];
    }
    public void Complete(string task, int amount)
    {
        try
        {
            DailyQuestsProgress[task] += (UInt32) amount;
        }
        catch
        {
            DailyQuestsProgress[task] = (UInt32) amount;
        }
    }
    public ulong GetProgress(string task)
    {
        try
        {
            return DailyQuestsProgress[task];
        }
        catch
        {
            DailyQuestsProgress[task] = 0;
            return DailyQuestsProgress[task];
        }
    }
    public void IncreaseStat(string stat, int amount)
    {
        try
        {
            Stats[stat] += (ulong) amount;
        }
        catch
        {
            Stats[stat] = (ulong) amount;
        }
    }
    public async Task Increase(string stat, int amount, bool save = true)
    {
        IncreaseStat(stat, amount);
        Complete(stat, amount);
        if (save) { await Save(); }
        
    }
    public UInt128 GetStat(string stat)
    {
        try
        {
            return Stats[stat];
        }
        catch
        {
            Stats[stat] = 0;
            return Stats[stat];
        }
    }
    public async Task<ulong> GetSetting(string setting, ulong defaultValue)
    {
        try
        {
            return Settings[setting];
        }
        catch
        {
            Settings[setting] = defaultValue;
            await Save();
            return defaultValue;
        }
    }
    public async Task SetSetting(string setting, ulong value)
    {
        Settings[setting] = value;
        await Save();
    }
    public async Task<int> GetData(string data, int defaultValue)
    {
        try
        {
            return UniqueData[data];
        }
        catch
        {
            UniqueData[data] = defaultValue;
            await Save();
            return defaultValue;
        }
    }
    private async Task Save(ulong id = default)
    {
        if (id == default)
        {
            id = ID;
        }
        string dat = JsonConvert.SerializeObject(this, Formatting.None)
            .Insert(1, "\n    ")
            .Replace("],", "],\n    ")
            .Replace("},", "},\n    ")
            .Replace("\":", "\": ")
            .Replace(",", ", ");
        dat = dat.Insert(dat.Length-1, "\n");
        await File.WriteAllTextAsync($"../../../Data/Users/{id}", dat);
    }
}

public class DailyQuest (int id = 99, string rarity = "Common")
{
    public uint Date { get; set; } = 0;
    public string Requirement { get; set; } = "beg";
    public uint Amount { get; set; } = 1;
    public string Description { get; set; } = "To set";
    public string Name { get; set; } = "Quest";
    public int ID { get; set; } = id;
    public string Rarity { get; set; } = rarity;
    public override string ToString()
    {
        return $"""
                **{Rarity} Quest {ID}**:
                 > *Name*: {Name}
                 > *Requirement*: {Amount} x {Requirement}
                 > *Description*: {Description}
                 > *Date*: {Date}
                """;
    }
}

public class Charm(string effect = "Charm", int amount = 0)
{
    public string Effect { get; set; } = effect;
    public int Amount { get; set; } = amount;
    public override string ToString()
    {
        return $"**Effect**: {Effect}, **Multiplier**: {Amount}";
    }
}

public class CachedUser (User user, ulong time)
{
    public User User { get; set; } = user;
    public int? TutorialOn = null;
    public RestInteractionMessage? LastQuestsMessage = null;
    public int TutorialPage = 0;
    public bool[]? TutorialProgress = null;
    public ulong InactiveSince = time;
    public static implicit operator User (CachedUser x)
    {
        return x.User;
    }
}
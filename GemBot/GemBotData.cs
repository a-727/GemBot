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
    public string[] Descriptions { get; set; } = ["$iDescription", "iDescription", "$iDescription", "$iDescription", "$iDescription"];
}

public class User
{
    public int[] Gems { get; set; } = [20, 5, 1, 0, 0];
    public ulong ID { get; set; }
    public Dictionary<string, ulong> CoolDowns { get; set; } = [];
    public List<int> Inventory { get; set; } = [];
    public int DailyQuestsDay { get; set; }
    public Dictionary<string, UInt32> DailyQuestsProgress { get; set; } = [];
    public Dictionary<string, ulong> Stats { get; set; } = [];
    public Dictionary<string, ulong> Settings { get; set; } = [];
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
    public void Complete(string task, int amount, int? day = null)
    {
        if (day is not null)
        {
            if (DailyQuestsDay < day)
            {
                DailyQuestsDay = (int)day;
                DailyQuestsProgress = new Dictionary<string, UInt32>();
            }
        }

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
    public async Task IncreaseStat(string stat, int amount, bool save = true)
    {
        try
        {
            Stats[stat] += (ulong) amount;
        }
        catch
        {
            Stats[stat] = (ulong) amount;
        }
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
    
    private async Task Save(ulong id = default)
    {
        if (id == default)
        {
            id = ID;
        }
        await File.WriteAllTextAsync($"../../../Data/Users/{id}", JsonConvert.SerializeObject(this));
    }
}

public class DailyQuest
{
    public int Date { get; set; }
    public string Requirement { get; set; }
    public int Amount { get; set; }
    public string Description { get; set; }
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
using System.Text.Json.Nodes;
using Newtonsoft.Json;

namespace GemBot;

public class Item
{
    public string Name { get; set; }
    public int ID { get; set; }
    public string Emoji { get; set; }
    public string Description { get; set;}
    public int Value { get; set; }

    public Item(int id = 999, string name = "Example Item", string emoji = ":coin:", string description = "This is an example item, you probably should not be seeing this.", int value = 65)
    {
        ID = id;
        Name = name;
        Emoji = emoji;
        Description = description;
        Value = value;
    }
    public override string ToString()
    {
        return $"**Item {ID}**:\n > *Name*: {Name}\n > *Emoji*: {Emoji}\n > *Description*: {Description}\n > *Value*: {Value}";
    }
}

public class Drop
{
    public string Name { get; set; }
    public int[] Items { get; set; }
    public int[] Left { get; set; }
    public int Price { get; set; }
    public string[] Descriptions { get; set; }
}

public class User
{
    public int[] Gems { get; set; }
    public ulong ID { get; set; }
    public string CurrentPassword { get; set; }
    public Dictionary<string, long> CoolDowns { get; set; }
    public List<int> Inventory { get; set; }
    public int DailyQuestsDay { get; set; }
    public Dictionary<string, ulong> DailyQuestsProgress { get; set; }
    public Dictionary<string, UInt128> Stats { get; set; }
    public bool OnCoolDown(string about, long currentTime, int timeoutFor, bool updateCooldown = true)
    {
        try
        {
            if (currentTime >= CoolDowns[about])
            {
                if (updateCooldown)
                {
                    CoolDowns[about] = currentTime + timeoutFor;
                }
                return true;
            }
            return false;
        }
        catch (KeyNotFoundException)
        {
            if (updateCooldown)
            {
                CoolDowns[about] = currentTime + timeoutFor;
            }
            return true;
        }
    }
    public void Add(int amount, int value)
    {
        Gems[value] += amount;
    }
    public void Complete(string task, int amount, int? day = null)
    {
        if (day is not null)
        {
            if (DailyQuestsDay < day)
            {
                DailyQuestsDay = (int)day;
                DailyQuestsProgress = new Dictionary<string, UInt64>();
            }
        }

        try
        {
            DailyQuestsProgress[task] += (UInt64) amount;
        }
        catch
        {
            DailyQuestsProgress[task] = (UInt64) amount;
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
            Stats[stat] += (UInt128) amount;
        }
        catch
        {
            Stats[stat] = (UInt128) amount;
        }
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
    public async Task Save(ulong id = default)
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
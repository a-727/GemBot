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
    public int[] Money { get; set; }
    public string ExternalType { get; set; }
    public string Username { get; set; }
    public string CurrentPassword { get; set; }
    public Dictionary<string, ulong> CoolDowns { get; set; }
    public int[] Inventory { get; set; }
    public int[] DailyQuests { get; set; }
}

public class DailyQuest
{
    public int Date { get; set; }
    public string Requirement { get; set; }
    public int Amount { get; set; }
    public string Description { get; set; }
}
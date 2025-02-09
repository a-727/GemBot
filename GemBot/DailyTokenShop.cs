using Discord;

namespace GemBot;

public class DailyTokenShop
{
    public DailyTokenRewards[] Rewards = new DailyTokenRewards[5];
    public Dictionary<ulong, bool[]> Users = new Dictionary<ulong, bool[]>();
    public uint Date = 0;
    public DailyTokenShop(DailyTokenRewards[] rewards, uint date)
    {
        Rewards = rewards;
    }
    public static DailyTokenShop Generate(List<int> charmSets, uint date, Random? randomParam = null)
    {
        Random random = randomParam ?? new Random();
        int money = 0; List<byte> items = new List<byte>(); int left = 15;
        while (left > 0)
        {
            int max = left switch { >= 13 => 20, >= 3 => 19, >= 2 => 15, _ => 10};
            switch (random.Next(max))
            {
                case >= 19:
                    items.Add(16);
                    left -= 13;
                    break;
                case >= 15:
                    items.Add((byte)charmSets[random.Next(charmSets.Count)]);
                    left -= 3;
                    break;
                case >= 10:
                    items.Add(0);
                    left -= 2;
                    break;
                default:
                    money += 5;
                    left--;
                    break;
            }
        }
        List<DailyTokenRewards> rewards = new List<DailyTokenRewards>(); 
        for (int i = 0; i < 5; i++)
        {
            rewards.Add(DailyTokenRewards.FromData(money, items.ToArray(), i));
        }
        return new DailyTokenShop(rewards.ToArray(), date);
    }

    public DailyTokenShop CheckDate(List<int> charmSets, uint date, Random? randomParam = null)
    {
        return date != Date ? Generate(charmSets, date, randomParam) : this;
    }
    public bool PurchasedReward(ulong userID, int level)
    {
        if (Users.TryGetValue(userID, out bool[]? value)) return value[level];
        value = ([false, false, false, false, false]);
        Users[userID] = value;
        return false;
    }
    public static implicit operator DailyTokenRewards[](DailyTokenShop shop)
    {
        return shop.Rewards;
    }
}

public class DailyTokenRewards
{
    public DailyTokenReward[] Rewards;
    public int Value = 0;
    public DailyTokenRewards(List<DailyTokenReward> rewards, int value = 0)
    {
        Rewards = rewards.ToArray();
        Value = value;
    }
    public static DailyTokenRewards FromData(int money, byte[] items, int value)
    {
        List<DailyTokenReward> rewards = new();
        rewards.Add(new DailyTokenRewardMoney(value) {Amount = money});
        foreach (byte id in items)
        {
            rewards.Add(new DailyTokenRewardItem(id + value));
        }
        return new DailyTokenRewards(rewards, value);
    }
}
public class DailyTokenReward
{
    public DailyTokenRewardType Type = DailyTokenRewardType.None;
    public int Amount = 1;
}
public class DailyTokenRewardMoney: DailyTokenReward
{
    public int Value = 0;
    public DailyTokenRewardMoney(int value = 0)
    {
        Type = DailyTokenRewardType.Money;
        Amount = 5;
        Value = value;
    }
}
public class DailyTokenRewardItem: DailyTokenReward
{
    public int Item = 0;
    public DailyTokenRewardItem(int item = 0)
    {
        Type = DailyTokenRewardType.Items;
        Item = item;
    }
}
public enum DailyTokenRewardType
{
    None,
    Money,
    Items
}
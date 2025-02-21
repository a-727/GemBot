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
    public bool Published = false;
    public bool Collectable = true;
    public int DropID = 0;
    public string Name { get; set; } = "Example Drop";
    public int[] Items { get; set; } = [0, 1, 2, 3, 4];
    public int[] Left { get; set; } = [50000, 10000, 2000, 400, 80];
    public int[][] Price { get; set; } = [[7,0], [7,1], [7,2], [7,3], [7,4]];
    public string[] Descriptions { get; set; } = ["$iDescription", "$iDescription", "$iDescription", "$iDescription", "$iDescription"];

    public async Task Save()
    {
        await File.WriteAllTextAsync($"Data/Drops/{DropID}.json", JsonConvert.SerializeObject(this));
    }
    public bool Out()
    {
        return Left[0] <= 0 && Left[1] <= 0 && Left[2] <= 0 && Left[3] <= 0 && Left[4] <= 0;
    }
    public override string ToString()
    {
        return $"""
                **{Name}** (Drop)
                **Collectable**: {Collectable}
                **Items:**
                > {Left[0]} of {Items[0]} for {Price[0][0]} {Price[0][1] switch {0 => "diamonds", 1 => "emeralds", 2=> "sapphires", 3 => "rubies", 4 => "ambers", _ => "error"}}
                >  -- `{Descriptions[0]}`
                > {Left[1]} of {Items[1]} for {Price[1][0]} {Price[1][1] switch {0 => "diamonds", 1 => "emeralds", 2=> "sapphires", 3 => "rubies", 4 => "ambers", _ => "error"}}
                >  -- `{Descriptions[1]}`
                > {Left[2]} of {Items[2]} for {Price[2][0]} {Price[2][1] switch {0 => "diamonds", 1 => "emeralds", 2=> "sapphires", 3 => "rubies", 4 => "ambers", _ => "error"}}
                >  -- `{Descriptions[2]}`
                > {Left[3]} of {Items[3]} for {Price[3][0]} {Price[3][1] switch {0 => "diamonds", 1 => "emeralds", 2=> "sapphires", 3 => "rubies", 4 => "ambers", _ => "error"}}
                >  -- `{Descriptions[3]}`
                > {Left[4]} of {Items[4]} for {Price[4][0]} {Price[4][1] switch {0 => "diamonds", 1 => "emeralds", 2=> "sapphires", 3 => "rubies", 4 => "ambers", _ => "error"}}
                >  -- `{Descriptions[4]}`
                """;
    }
    public string ToString(List<Item> items)
    {
        return $"""
                **{Name}** (Drop)
                **Collectable**: {Collectable}
                **Items:**
                > {Left[0]} of {items[Items[0]].Name} for {Price[0][0]} {Price[0][1] switch {0 => "diamonds", 1 => "emeralds", 2=> "sapphires", 3 => "rubies", 4 => "ambers", _ => "error"}}
                >  -- `{Descriptions[0]}`
                > {Left[1]} of {items[Items[1]].Name} for {Price[1][0]} {Price[1][1] switch {0 => "diamonds", 1 => "emeralds", 2=> "sapphires", 3 => "rubies", 4 => "ambers", _ => "error"}}
                >  -- `{Descriptions[1]}`
                > {Left[2]} of {items[Items[2]].Name} for {Price[2][0]} {Price[2][1] switch {0 => "diamonds", 1 => "emeralds", 2=> "sapphires", 3 => "rubies", 4 => "ambers", _ => "error"}}
                >  -- `{Descriptions[2]}`
                > {Left[3]} of {items[Items[3]].Name} for {Price[3][0]} {Price[3][1] switch {0 => "diamonds", 1 => "emeralds", 2=> "sapphires", 3 => "rubies", 4 => "ambers", _ => "error"}}
                >  -- `{Descriptions[3]}`
                > {Left[4]} of {items[Items[4]].Name} for {Price[4][0]} {Price[4][1] switch {0 => "diamonds", 1 => "emeralds", 2=> "sapphires", 3 => "rubies", 4 => "ambers", _ => "error"}}
                >  -- `{Descriptions[4]}`
                """;
    }
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
    public Dictionary<string, List<int>> DataLists { get; set; } = [];
    public List<CraftingRecipe.Furnace> Furnaces { get; set; } = [new()];
    public ulong ID { get; set; }
    public int ItemAmount(int id)
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
            return 0;
        }
        catch (ArgumentOutOfRangeException)
        {
            while (Inventory.Count <= id)
            {
                Inventory.Add(0);
            }
            return 0;
        }
    }
    public void GainItem(int id, int amount)
    {
        int existing = ItemAmount(id);
        Inventory[id] = existing + amount;
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
    public void Add(int amount, int value)
    {
        Gems[value] += amount;
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
    public void Increase(string stat, int amount)
    {
        IncreaseStat(stat, amount);
        Complete(stat, amount);
    }
    public ulong GetStat(string stat)
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
    public ulong GetSetting(string setting, ulong defaultValue)
    {
        try
        {
            return Settings[setting];
        }
        catch
        {
            Settings[setting] = defaultValue;
            return defaultValue;
        }
    }
    public void SetSetting(string setting, ulong value)
    {
        Settings[setting] = value;
    }
    public int GetData(string data, int defaultValue)
    {
        try
        {
            return UniqueData[data];
        }
        catch
        {
            UniqueData[data] = defaultValue;
            return defaultValue;
        }
    }
    public void SetData(string data, int value)
    {
        UniqueData[data] = value;
    }
    public List<int> GetListData(string data, List<int> defaultValue)
    {
        try
        {
            return DataLists[data];
        }
        catch
        {
            DataLists[data] = defaultValue;
            return defaultValue;
        }
    }
    public List<int> GetListData(string data)
    {
        return GetListData(data, []);
    }
    public void SetListData(string data, List<int> value)
    {
        DataLists[data] = value;
    }
    public void CheckFurnaces(int furnaceCount)
    {
        int extraFurnaces = Furnaces.Count - furnaceCount;
        while (extraFurnaces < 0)
        {
            Furnaces.Add(new CraftingRecipe.Furnace());
            extraFurnaces++;
        }
        if (extraFurnaces == 0) return;
        while (extraFurnaces > 0)
        {
            try
            {
                if (Furnaces.Remove(Furnaces.First(furnace => furnace.Crafting == false))) extraFurnaces--;
                else break;
            }
            catch (InvalidOperationException)
            {
                break;
            }
        }
    }
    public async Task Save(ulong id = default)
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
        await File.WriteAllTextAsync($"Data/Users/{id}", dat);
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
    public List<Tuple<int, int>>? NextCrafting = null;
    public ulong InactiveSince = time;
    public byte LastWork = 0;
    public List<Notification> Notifications = [];
    public byte NotificationsID = 0;
    public static implicit operator User (CachedUser x)
    {
        return x.User;
    }
    public void ConsolidateNotifications()
    {
        Notifications = Notification.ConsolidateNotifications(Notifications);
    }
    public bool CraftTick(List<Item> items, List<CraftingRecipe> craftingRecipes, int furnaceConst)
    {
        bool toReturn = false;
        User.CheckFurnaces(Tools.CharmEffect(["FurnaceSlots"], items, user) + furnaceConst);
        foreach (CraftingRecipe.Furnace furnace in User.Furnaces)
        {
            if (furnace.Crafting)
            {
                toReturn = true;
                if (furnace.TimeLeft > 1) furnace.TimeLeft--;
                else
                {
                    User.GainItem(furnace.NextItem, furnace.Amount);
                    //$"You gained {furnace.Amount}{_items[furnace.NextItem].Emoji}"
                    Notifications.Add(new Notification(furnace.NextItem, furnace.Amount, "crafted"));
                    furnace.Crafting = false;
                }
            }
            if (furnace.Crafting) continue; //Code below gets recipe to craft, so if you're already crafting you don't need to do it
            if (NextCrafting == null)
            {
                // ReSharper disable once UseCollectionExpression
                NextCrafting = new List<Tuple<int, int>>();
                continue;
            }
            if (NextCrafting.Count <= 0) continue;
            if (NextCrafting[0].Item2 <= 0)
            {
                NextCrafting.RemoveAt(0);
                continue;
            }
            CraftingRecipe recipe = craftingRecipes[NextCrafting[0].Item1];
            bool failedRecipe = User.Gems[recipe.PriceValue] < recipe.Price;
            if (failedRecipe)
            {
                Tuple<int, int> failedCraft = NextCrafting[0];
                NextCrafting.RemoveAt(0);
                NextCrafting.Add(failedCraft);
                continue;
            }
            foreach (CraftingRecipe.RecipeRequirements requirement in recipe.Requirements)
            {
                if (failedRecipe) break;
                if (User.Inventory[requirement.Item] >= requirement.Amount) continue;
                Tuple<int, int> failedCraft = NextCrafting[0];
                NextCrafting.RemoveAt(0);
                NextCrafting.Add(failedCraft);
                failedRecipe = true;
                break;
            }
            if (failedRecipe) continue;
            NextCrafting[0] = new Tuple<int, int>(NextCrafting[0].Item1, NextCrafting[0].Item2 - 1);
            if (NextCrafting[0].Item2 <= 0)
            {
                NextCrafting.RemoveAt(0);
            }
            User.Add(recipe.Price * -1, recipe.PriceValue);
            foreach (CraftingRecipe.RecipeRequirements requirement in recipe.Requirements)
            {
                User.GainItem(requirement.Item, -1 * requirement.Amount);
            }
            furnace.UpdateFromCraftingRecipe(recipe);
            toReturn = true;
        }
        return toReturn;
    }
    public class Notification
    {
        public int Reward { get; set; } //0+ for items, -1 diamond, -2 emerald, -3 sapphire, -4 ruby, -5 amber
        public int Amount { get; set; }
        public string Source { get; set; }
        public static List<Notification> ConsolidateNotifications(List<Notification> notifications)
        {
            List<Notification> newNotifications = new List<Notification>();
            foreach (Notification notification in notifications)
            {
                int alreadyCoverd = newNotifications.FindIndex(n =>
                    n.Source == notification.Source && n.Reward == notification.Reward);
                if (alreadyCoverd == -1)
                {
                    newNotifications.Add(notification);
                    continue;
                }
                newNotifications[alreadyCoverd].Amount += notification.Amount;
            }
            return newNotifications;
        }
        public Notification(int reward, int amount, string source)
        {
            Reward = reward;
            Amount = amount;
            Source = source;
        }
    }
}

public class CraftingRecipe
{
    public int ID = 999;
    public int ItemCrafted = 21; //token
    public int AmountCrafted = 1;
    public List<RecipeRequirements> Requirements = [];
    public uint TimeRequired = 3600; //in seconds
    public int Price = 1;
    public int PriceValue = 0;
    public async Task Save(int id = -1)
    {
        if (id == -1)
        {
            id = ID;
        }
        await File.WriteAllTextAsync($"Data/CraftingRecipes/{id}", JsonConvert.SerializeObject(this, Formatting.None));
    }
    public override string ToString()
    {
        int secondsLeft = (int)TimeRequired;
        string etl;
        switch (secondsLeft)
        {
            case > 86400:
            {
                int days = secondsLeft / 86400;
                secondsLeft -= days * 86400;
                int hours = secondsLeft / 3600;
                secondsLeft -= hours * 3600;
                int minutes = secondsLeft / 60;
                secondsLeft -= minutes * 60;
                etl = $"{days} days, {hours} hr, {minutes} min, {secondsLeft} sec";
                break;
            }
            case > 3600:
            {
                int hours = secondsLeft / 3600;
                secondsLeft -= hours * 3600;
                int minutes = secondsLeft / 60;
                secondsLeft -= minutes * 60;
                etl = $"{hours} hr, {minutes} min, and {secondsLeft} sec";
                break;
            }
            case >= 60:
            {
                int minutes = secondsLeft / 60;
                secondsLeft -= minutes * 60;
                string secondsProgress = secondsLeft < 10 ? '0' + secondsLeft.ToString() : secondsLeft.ToString();
                etl = $"{minutes}:{secondsProgress} (min:sec)";
                break;
            }
            case >= 1:
                etl = $"{secondsLeft} seconds";
                break;
            default:
                etl = "Almost none!";
                break;
        }
        string toReturn = $"**Crafting Recipe {ID}**"
                + (Price > 0 ? $"\n > Price: {Price} {PriceValue switch {0 => "diamonds", 1 => "emeralds", 2 => "sapphires", 3 => "rubies", 4 => "ambers", _ => "**error errors**"}}" : "")
                + $"\n > Crafts {AmountCrafted} copies of Item **{ItemCrafted}**"
                + $"\n > Time: {etl}"
                + $"\n > Requirements:";
        foreach (RecipeRequirements req in Requirements)
        {
            toReturn += $"\n> -- {req.Amount} of {req.Item}";
        }
        return toReturn;
    }
    public string ToString(List<Item> items, string[] currency)
    {
        int secondsLeft = (int)TimeRequired;
        string etl;
        switch (secondsLeft)
        {
            case > 86400:
            {
                int days = secondsLeft / 86400;
                secondsLeft -= days * 86400;
                int hours = secondsLeft / 3600;
                secondsLeft -= hours * 3600;
                int minutes = secondsLeft / 60;
                secondsLeft -= minutes * 60;
                etl = $"{days} days, {hours} hr, {minutes} min, {secondsLeft} sec";
                break;
            }
            case > 3600:
            {
                int hours = secondsLeft / 3600;
                secondsLeft -= hours * 3600;
                int minutes = secondsLeft / 60;
                secondsLeft -= minutes * 60;
                etl = $"{hours} hr, {minutes} min, and {secondsLeft} sec";
                break;
            }
            case >= 60:
            {
                int minutes = secondsLeft / 60;
                secondsLeft -= minutes * 60;
                string secondsProgress = secondsLeft < 10 ? '0' + secondsLeft.ToString() : secondsLeft.ToString();
                etl = $"{minutes}:{secondsProgress} (min:sec)";
                break;
            }
            case >= 1:
                etl = $"{secondsLeft} seconds";
                break;
            default:
                etl = "Almost none!";
                break;
        }
        Item itemCrafted = items[ItemCrafted];
        string toReturn = $"**Crafting Recipe {ID}**"
                + (Price > 0 ? $"\n > Price: {Price}{currency[PriceValue]}" : "")
                + $"\n > Crafts {AmountCrafted} copies of **{itemCrafted.Name}** (Item {ItemCrafted})"
                + $"\n > Time: {etl}"
                + $"\n > Requirements:";
        foreach (RecipeRequirements req in Requirements)
        {
            Item itemRequired = items[req.Item];
            toReturn += $"\n> -- {req.Amount} of {itemRequired.Name} (Item {req.Item})";
        }
        return toReturn;
    }
    public int AmountCraftable(User user)
    {
        int toReturn = Price > 0 ? user.Gems[PriceValue] / Price : int.MaxValue;
        foreach (RecipeRequirements req in Requirements)
        {
            int thisMax = user.Inventory[req.Item] / req.Amount;
            if (thisMax < toReturn)
            {
                toReturn = thisMax;
            }
        }
        return toReturn;
    }
    public int CompareRecipeProfit(CraftingRecipe with, List<Item> items)
    {
        int thisProfit = -1 * Price * (int)Math.Pow(10, PriceValue);
        int thatProfit = -1 * with.Price * (int)Math.Pow(10, with.PriceValue);
        foreach (RecipeRequirements requirement in Requirements)
        {
            thisProfit -= requirement.Amount * items[requirement.Item].Value;
        }
        foreach (RecipeRequirements requirement in with.Requirements)
        {
            thatProfit -= requirement.Amount * items[requirement.Item].Value;
        }
        thisProfit += AmountCrafted * items[ItemCrafted].Value;
        thatProfit += with.AmountCrafted * items[with.ItemCrafted].Value;
        return thisProfit - thatProfit;
    }
    
    public class RecipeRequirements
    {
        public int Item { get; set; } = 39; //stone coin.
        public int Amount { get; set; } = 1;
    }

    public class Furnace
    {
        public int NextItem = 21; //token
        public int Amount = 0;
        public bool Crafting = false;
        public uint TimeRequired = 360;
        public uint TimeLeft = 0;
        public static Furnace FromCraftingRecipe(CraftingRecipe recipe)
        {
            return new Furnace
            {
                Amount = recipe.AmountCrafted,
                Crafting = true,
                NextItem = recipe.ItemCrafted,
                TimeLeft = recipe.TimeRequired,
                TimeRequired = recipe.TimeRequired
            };
        }

        public void UpdateFromCraftingRecipe(CraftingRecipe recipe)
        {
            Amount = recipe.AmountCrafted;
            Crafting = true;
            NextItem = recipe.ItemCrafted;
            TimeLeft = recipe.TimeRequired;
            TimeRequired = recipe.TimeRequired;
        }
        public bool Tick()
        {
            TimeLeft--;
            return TimeLeft <= 0;
        }
    }
}

public class MagikReward
{ 
    public int Chance;
    public Func<CachedUser, CachedUser, string> DoMagik;
    public MagikReward(int chance, Func<CachedUser, CachedUser, string> action)
    {
        Chance = chance; 
        DoMagik = action;
    }
} 
public class StandardMagikReward : MagikReward
{
    private string _text;
    private int _userGems;
    private int _userGemValue; 
    private int _targetGems; 
    private int _targetGemValue; 
    public StandardMagikReward(string text, int userGems, int userGemValue, int targetGems, int targetGemValue, int chance) : base(chance, MagikLogicStatic) 
    { 
        _text = text; 
        _userGems = userGems; 
        _userGemValue = userGemValue; 
        _targetGems = targetGems; 
        _targetGemValue = targetGemValue; 
        DoMagik = MagikLogic;
    } 
    public string MagikLogic(CachedUser user, CachedUser target) 
    { 
        user.User.Increase("earned", (int)(_userGems*Math.Pow(10, _userGemValue))); 
        user.User.Add(_userGems, _userGemValue); 
        target.User.Add(_targetGems, _targetGemValue); 
        return _text;
    }
    public static string MagikLogicStatic(CachedUser user, CachedUser target) { return "error error MagikLogicStatic: StandardMagikReward"; } 
}
public class ItemMagikReward : MagikReward
{
    private string _text; 
    private int _userItems; 
    private int _userItemID; 
    private int _targetItems; 
    private int _targetItemID; 
    private List<Item> _items;
    public ItemMagikReward(string text, int userItems, int userItemID, int targetItems, int targetItemID, int chance, List<Item> items) : base(chance, MagikLogicStatic) 
    { 
        _text = text; 
        _userItems = userItems; 
        _userItemID = userItemID; 
        _targetItems = targetItems; 
        _targetItemID = targetItemID; 
        _items = items;
        DoMagik = MagikLogic;
    } 
    public string MagikLogic(CachedUser user, CachedUser target) 
    { 
        user.User.GainItem(_userItemID, _userItems); 
        target.User.GainItem(_targetItemID, _targetItems); 
        string toReturn = _text
            .Replace("$user_item", $"{_userItems}{_items[_userItemID].Emoji}")
            .Replace("$target_item", $"{_targetItems}{_items[_targetItemID].Emoji}");
        return toReturn;
    }
    public static string MagikLogicStatic(CachedUser user, CachedUser target)
    {
        return "error error MagikLogicStatic: ItemMagikReward";
    }
}
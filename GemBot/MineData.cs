using System.Data.Common;
using System.Globalization;
using System.Reflection.Metadata.Ecma335;
using Newtonsoft.Json;

namespace GemBot;

public class MineData
{
    public List<MineChunk> MineChunks;
    public uint TimesMined;
    public string MonthName;
    private MineData(List<MineChunk> mineChunks, uint timesMined, string monthName)
    {
        MineChunks = mineChunks;
        TimesMined = timesMined;
        MonthName = monthName;
    }
    public static async Task<MineData> LoadMineData()
    {
        if (!File.Exists("Data/Mine/general.txt"))
        {
            return await ResetMineData(0);
        }
        string[] lines = await File.ReadAllLinesAsync("Data/Mine/general.txt");
        if (lines[0] != DateTime.Today.Month.ToString())
        {
            return await ResetMineData(uint.Parse(lines[1]));
        }
        string[] chunkPaths = Directory.GetFiles("Data/Mine/Chunks").Order().ToArray();
        List<MineChunk> chunks = [];
        foreach (string chunkPath in chunkPaths)
        {
            chunks.Add(await LoadMineChunk(chunkPath));
        }
        return new MineData(chunks, uint.Parse(lines[1]), lines[0]);

        async Task<MineChunk> LoadMineChunk(string chunkPath)
        {
            return JsonConvert.DeserializeObject<MineChunk>(await File.ReadAllTextAsync(chunkPath)) ?? MineChunk.GenerateMineChunk();
        }
    }
    public async Task SaveMineData()
    {
        await File.WriteAllTextAsync("Data/Mine/general.txt", $"{(await File.ReadAllLinesAsync("Data/Mine/general.txt"))[0]}\n{TimesMined}");
        for (int i = 0; i < MineChunks.Count; i++)
        {
            await MineChunks[i].Save(i);
        }
    }
    private static async Task<MineData> ResetMineData(uint mineCount)
    {
        List<MineChunk> chunks = [];
        ushort numChunks = mineCount switch
        {
            < 100 => 1,
            < 600 => 2,
            < 1400 => 3,
            < 2200 => 4,
            < 3000 => 5,
            <= 50000 => (ushort)(mineCount / 500),
            <= 500000 => (ushort)((mineCount - 50000)/1000 + 100),
            _ => (ushort)((mineCount - 500000)/2000 + 550)
        };
        if (numChunks > 1000)
        {
            numChunks = 1000;
        }
        await File.AppendAllTextAsync("Data/Mine/history.txt", $"\n{DateTime.Today.ToString(CultureInfo.CurrentCulture)}: A total of {mineCount} blocks were mined last month, so {numChunks} chunks were generated.");
        await File.WriteAllTextAsync("Data/Mine/general.txt", $"{ DateTime.Today.Month.ToString()}\n0");
        for (int i = 0; i < numChunks; i++)
        {
            chunks.Add(MineChunk.GenerateMineChunk());
        }
        return new MineData(chunks, 0,  DateTime.Today.Month.ToString());
    }
    public MineChunk GetChunk(int x)
    {
        return MineChunks[x];
    }
    public MineBlock GetBlock(int x, int y)
    {
        return GetChunk(x/20).GetBlock(x%20, y);
    }
    public Tuple<bool, int, BlockType> Mine(int x, int y, ulong playerId, int power)
    {
        return GetChunk(x/20).Mine(x%20, y, playerId, power);
    }
}

public class MineChunk
{
    public List<List<MineBlock>> Blocks {get; set;}
    public static MineChunk GenerateMineChunk()
    {
        Random random = new Random();
        MineBlock air = new MineBlock()
        {
            Durability = 1,
            Left = 0,
            Type = BlockType.Air,
        };
        List<List<MineBlock>> layers = [[air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy()]];
        for (int layer = 1; layer < 251; layer++)
        {
            MineBlock stone = new MineBlock
            {
                Durability = 30*(uint)layer+300, //1 min + 6 sec per layer (on power 5)
                DropAmount = layer + random.Next(6)
            };
            MineBlock diamond = new MineBlock
            {
                Durability = 6000,
                DropAmount = layer / 3 + random.Next(5),
                Type = BlockType.Diamonds
            };
            MineBlock emerald = new MineBlock
            {
                Durability = 20000,
                DropAmount = layer / 9 + random.Next(4),
                Type = BlockType.Emeralds
            };
            MineBlock sapphire = new MineBlock()
            {
                Durability = 155200,
                DropAmount = layer / 27 + random.Next(3),
                Type = BlockType.Sapphires
            };
            MineBlock ruby = new MineBlock()
            {
                Durability = 640000,
                DropAmount = layer / 81 + random.Next(2),
                Type = BlockType.Rubies
            };
            MineBlock amber = new MineBlock()
            {
                Durability = 6400000,
                DropAmount = 1,
                Type = BlockType.Amber
            };
            List<MineBlock> thisLayer = [];
            for (int i = 0; i < 20; i++)
            {
                thisLayer.Add((random.Next(layer*2+750) switch
                {
                    <= 650 => stone,
                    <= 750 => diamond,
                    <= 840 => emerald,
                    <= 920 => sapphire,
                    <= 990 => ruby,
                    <= 1000 => amber,
                    _ => new MineBlock(){Left = 0, Type = BlockType.Air}
                }).Copy());
            }
            layers.Add(thisLayer);
        }
        MineChunk mineChunk = new MineChunk
        {
            Blocks = layers
        };
        return mineChunk;
    }
    public MineBlock GetBlock(int x, int y)
    {
        return Blocks[y][x];
    }
    public async Task Save(int chunkId)
    {
        await File.WriteAllTextAsync(Tools.IDString(chunkId, "Data/Mine/Chunks"), JsonConvert.SerializeObject(this).Replace("],", "],\n"));
    }
    public Tuple<bool, int, BlockType> Mine(int x, int y, ulong playerId, int power)
    {
        return GetBlock(x, y).Mine(playerId, power);
    }
}

public class MineBlock(uint durabilityOnStart = 100, BlockType typeOnStart = BlockType.Stone, int dropAmountOnStart = 5, ulong? minerIdOnStart = null, int damageOnStart = 0)
{
    public uint Durability { get; set; } = durabilityOnStart;
    public int? Left { get; set; } = null;
    public ulong? MinerID { get; set; } = minerIdOnStart;
    public BlockType Type { get; set; } = typeOnStart;
    public int DropAmount { get; set; } = dropAmountOnStart;
    public int GetLeft()
    {
        if (Left != null)
        {
            return Left.Value;
        }
        Left = (int)Durability - damageOnStart;
        return Left.Value;
    }
    public MineBlock Copy()
    {
        return new MineBlock(Durability, Type, DropAmount, MinerID, (int)Durability-(Left ?? GetLeft()));
    }
    public Tuple<bool, int, BlockType> Mine(ulong playerID, int power)
    {
        if (MinerID is null)
        {
            MinerID = playerID;
        }
        else if (playerID != MinerID)
        {
            throw new SomeoneElseIsMiningError();
        }
        if (Type == BlockType.Air)
        {
            throw new BlockIsAirError();
        }
        if (Left is null)
        {
            GetLeft();
        }
        Left -= power;
        if (Left <= 0)
        {
            Left = 0;
            Tuple<bool, int, BlockType> tuple = new(true, DropAmount, Type);
            Type = BlockType.Air;
            return tuple;
        }
        return new Tuple<bool, int, BlockType>(false, DropAmount, Type);
    }
}

public enum BlockType
{
    Air,
    Stone,
    Diamonds,
    Emeralds,
    Sapphires,
    Rubies,
    Amber
}

public class SomeoneElseIsMiningError() : Exception("Someone else is already mining this block.") { }

public class BlockIsAirError() : Exception("This block cannot be mined as it is already mined."){ }
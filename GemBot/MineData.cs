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
        await File.WriteAllTextAsync("Data/Mine/general.txt", $"{DateTime.Today.Month.ToString()}\n0");
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
                DropAmount = (byte)(layer + random.Next(6))
            };
            MineBlock diamond = new MineBlock
            {
                Durability = 1200, //4 min (on power 5)
                DropAmount = (byte)(layer / 3 + random.Next(5)),
                Type = BlockType.Diamonds
            };
            MineBlock diamondCoin = new MineBlock
            {
                Durability = 3000, //10 min (on power 5)
                DropAmount = (byte)(layer / 9 + random.Next(5)),
                Type = BlockType.DiamondCoin
            };
            MineBlock diamondKey = new MineBlock
            {
                Durability = 6000, //20 min (on power 5)
                DropAmount = (byte)(layer / 30 + random.Next(1)),
                Type = BlockType.DiamondKey
            };
            MineBlock emerald = new MineBlock
            {
                Durability = 3000, //10 min (on power 5)
                DropAmount = (byte)(layer / 9 + random.Next(4)),
                Type = BlockType.Emeralds
            };
            MineBlock emeraldCoin = new MineBlock
            {
                Durability = 6300, //21 min (on power 5)
                DropAmount = (byte)(layer / 20 + random.Next(2)),
                Type = BlockType.EmeraldCoin
            };
            MineBlock emeraldKey = new MineBlock
            {
                Durability = 18000, //1 hour (on power 5)
                DropAmount = 1,
                Type = BlockType.EmeraldKey
            };
            MineBlock sapphire = new MineBlock()
            {
                Durability = 7500, //25 min (on power 5)
                DropAmount = (byte)(layer / 27 + random.Next(3)),
                Type = BlockType.Sapphires
            };
            MineBlock sapphireCoin = new MineBlock()
            {
                Durability = 21000, //1 hour 10 min (on power 5)
                DropAmount = 1,
                Type = BlockType.SapphireCoin
            };
            MineBlock sapphireKey = new MineBlock
            {
                Durability = 45000, //2 hour 30 min (on power 5)
                DropAmount = 1,
                Type = BlockType.SapphireKey
            };
            MineBlock ruby = new MineBlock()
            {
                Durability = 42000, //2 hour 20 min (on power 5)
                DropAmount = (byte)(layer / 81 + random.Next(2)),
                Type = BlockType.Rubies
            };
            MineBlock rubyCoin = new MineBlock
            {
                Durability = 63000, //3 hour 30 min (on power 5)
                DropAmount = 1,
                Type = BlockType.RubyCoin
            };
            MineBlock rubyKey = new MineBlock
            {
                Durability = 126000, //7 hour (on power 5)
                DropAmount = 1,
                Type = BlockType.RubyKey
            };
            MineBlock amber = new MineBlock()
            {
                Durability = 108000, //6 hour (on power 5)
                DropAmount = 1,
                Type = BlockType.Amber
            };
            MineBlock amberCoin = new MineBlock
            {
                Durability = 360000, //20 hour (on power 5)
                DropAmount = 1,
                Type = BlockType.AmberCoin
            };
            MineBlock amberKey = new MineBlock
            {
                Durability = 864000, //48 hour (on power 5)
                DropAmount = 1,
                Type = BlockType.AmberKey
            };
            List<MineBlock> thisLayer = [];
            for (int i = 0; i < 20; i++)
            {
                thisLayer.Add((random.Next(layer, layer*3+777) switch
                {
                    <= 200 => air, //200
                    <= 850 => stone, //650
                    <= 950 => diamond, //100
                    <= 1040 => emerald, //90
                    <= 1100 => diamondCoin, //60
                    <= 1180 => sapphire, //80
                    <= 1234 => emeraldCoin, //54
                    <= 1244 => diamondKey, //10
                    <= 1314 => ruby, //70
                    <= 1362 => sapphireCoin, //48
                    <= 1371 => emeraldKey, //9
                    <= 1431 => amber, //60
                    <= 1473 => rubyCoin, //42
                    <= 1481 => sapphireKey, //8
                    <= 1517 => amberCoin, //36
                    <= 1524 => rubyKey, //7
                    <= 1530 => amberKey, //6
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

public class MineBlock(uint durabilityOnStart = 100, BlockType typeOnStart = BlockType.Stone, byte dropAmountOnStart = 5, ulong? minerIdOnStart = null)
{
    public uint Durability { get; set; } = durabilityOnStart;
    public int? Left { get; set; } = null;
    public ulong? MinerID { get; set; } = minerIdOnStart;
    public BlockType Type { get; set; } = typeOnStart;
    public byte DropAmount { get; set; } = dropAmountOnStart;
    public int GetLeft()
    {
        if (Left != null)
        {
            return Left.Value;
        }
        Left = (int)Durability;
        return Left.Value;
    }
    public MineBlock Copy()
    {
        return new MineBlock(Durability, Type, DropAmount, MinerID) { Left = Left };
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
            Left = null;
            MinerID = null;
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
    Amber,
    DiamondKey,
    EmeraldKey,
    SapphireKey,
    RubyKey,
    AmberKey,
    DiamondCoin,
    EmeraldCoin,
    SapphireCoin,
    RubyCoin,
    AmberCoin
}

public class SomeoneElseIsMiningError() : Exception("Someone else is already mining this block.") { }

public class BlockIsAirError() : Exception("This block cannot be mined as it is already mined."){ }
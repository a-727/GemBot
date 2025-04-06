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
        foreach (string path in Directory.GetFiles("Data/Mine/Chunks").Order())
        {
            if (int.Parse(path.Split("/").Last().Split(".").First()) >= MineChunks.Count)
            {
                File.Delete(path);
            }
        }
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
            _ => (ushort)(((mineCount - 500000)/2000) + 550)
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
        return GetChunk(x/30).GetBlock(x%30, y);
    }
    public Tuple<bool, int, BlockType> Mine(int x, int y, ulong playerId, int power)
    {
        return GetChunk(x/30).Mine(x%30, y, playerId, power);
    }
}

public class MineChunk
{
    public MineBlock[][] Blocks {get; set;}
    public static MineChunk GenerateMineChunk()
    {
        Random random = new Random();
        MineBlock air = new MineBlock()
        {
            Durability = 1,
            Left = 0,
            Type = BlockType.Air,
        };
        List<MineBlock[]> layers = [[air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy(), air.Copy()]];
        for (int layer = 1; layer < 151; layer++)
        {
            MineBlock stone = new MineBlock
            {
                Durability = 60*(uint)layer+50, //1 min + 2 sec per layer (on power 5)
                DropAmount = (byte)(6 + random.Next(layer))
            };
            MineBlock diamond = new MineBlock
            {
                Durability = 1200, //4 min (on power 5)
                DropAmount = (byte)(layer / 2 + random.Next(5)),
                Type = BlockType.Diamonds
            };
            MineBlock diamondCoin = new MineBlock
            {
                Durability = 3000, //10 min (on power 5)
                DropAmount = (byte)(layer / 6 + random.Next(5)),
                Type = BlockType.DiamondCoin
            };
            MineBlock diamondKey = new MineBlock
            {
                Durability = 6000, //20 min (on power 5)
                DropAmount = (byte)(layer / 20 + random.Next(1)),
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
                DropAmount = (byte)(layer / 90),
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
            MineBlock amberKey = new MineBlock
            {
                Durability = 864000, //48 hour (on power 5)
                DropAmount = 1,
                Type = BlockType.AmberKey
            };
            MineBlock gold = new MineBlock()
            {
                Durability = 18000, //1 hour (on power 5)
                DropAmount = 2,
                Type = BlockType.Gold
            };
            List<MineBlock> thisLayer = [];
            for (int i = 0; i < 30; i++)
            {
                thisLayer.Add((random.Next(layer, layer*3+900) switch
                {
                    <= 200 => air, //200
                    <= 840 => stone, //640
                    <= 850 => gold, //10
                    <= 950 => diamond, //100
                    <= 960 => gold, //10
                    <= 1040 => emerald, //80
                    <= 1090 => diamondCoin, //50
                    <= 1100 => gold, //10
                    <= 1160 => sapphire, //60
                    <= 1200 => emeraldCoin, //40
                    <= 1220 => diamondKey, //20
                    <= 1230 => gold, //10
                    <= 1260 => sapphireCoin, //30
                    <= 1276 => emeraldKey, //16
                    <= 1286 => gold, //10
                    <= 1306 => rubyCoin, //20
                    <= 1318 => sapphireKey, //12
                    <= 1328 => gold, //10
                    <= 1336 => rubyKey, //8
                    <= 1346 => gold, //10
                    <= 1350 => amberKey, //4
                    _ => new MineBlock(){Left = 0, Type = BlockType.Air}
                }).Copy());
            }
            layers.Add(thisLayer.ToArray());
        }
        MineChunk mineChunk = new MineChunk
        {
            Blocks = layers.ToArray()
        };
        return mineChunk;
    }
    public static MineChunk GeneratePortalMineChunk(int height, int spawnX, int spawnY)
    {
        Random random = new Random();
        MineBlock air = new MineBlock()
        {
            Durability = 1,
            Left = 0,
            Type = BlockType.Air
        };
        MineBlock eventScroll = new MineBlock()
        {
            Durability = 120, //1 min (on power 2)
            DropAmount = 1,
            Type = BlockType.EventScroll
        };
        MineBlock stone = new MineBlock()
        {
            Durability = 30, //15 sec (on power 2)
            DropAmount = 30,
            Type = BlockType.Stone
        };
        MineBlock gold = new MineBlock()
        {
            Durability = 360, //3 min (on power 2)
            DropAmount = 1,
            Type = BlockType.Gold
        };
        MineBlock sapphire = new MineBlock()
        {
            Durability = 600, //5 min (on power 2)
            DropAmount = 1,
            Type = BlockType.Sapphires
        };
        MineBlock ruby = new MineBlock()
        {
            Durability = 3000, //25 min (on power 2)
            DropAmount = 1,
            Type = BlockType.Rubies
        };
        MineBlock treasure = random.Next(4) switch
        {
            //All durabilities 1 hour on power 2
            //Each event hase one type of treasure. The treasure is guaranteed to spawn at least once
            0 => new MineBlock() { Durability = 7200, DropAmount = 1, Type = BlockType.Portal },
            1 => new MineBlock() { Durability = 7200, DropAmount = 1, Type = BlockType.RubyCoin },
            2 => new MineBlock() { Durability = 7200, DropAmount = 1, Type = BlockType.SapphireKey },
            3 => new MineBlock() {Durability = 7200, DropAmount = 1, Type = BlockType.Amber},
            _ => new MineBlock() {Durability = 1, DropAmount = 3, Type = BlockType.Gold}
        };
        List<MineBlock[]> layers = [];
        for (int layer = 0; layer < height; layer++)
        {
            List<MineBlock> thisLayer = [];
            //Insert base block
            for (int i = 0; i < 5; i++)
            {
                thisLayer.Add(((random.Next(40) +1) switch
                {
                    <= 20 => eventScroll, //20
                    <= 30 => stone, //10
                    <= 33 => gold, //3
                    <= 37 => sapphire, //4
                    <= 39 => ruby, //2
                    <= 40 => treasure, //1
                    _ => air
                }).Copy());
            }
            //Generate other blocks
            layers.Add(thisLayer.ToArray());
        }
        layers[random.Next(height)][random.Next(5)] = treasure.Copy();
        while (layers[spawnY][spawnX].Type == treasure.Type && layers[spawnY][spawnX].DropAmount == treasure.DropAmount)
        {
            layers[spawnY][spawnX] = air.Copy();
            layers[random.Next(height)][random.Next(5)] = treasure.Copy();
        }
        layers[spawnY][spawnX] = air.Copy();
        MineChunk mineChunk = new MineChunk
        {
            Blocks = layers.ToArray()
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

public enum BlockType : byte
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
    AmberCoin,
    Gold,
    EventScroll,
    Portal,
}

public class SomeoneElseIsMiningError() : Exception("Someone else is already mining this block.") { }

public class BlockIsAirError() : Exception("This block cannot be mined as it is already mined."){ }
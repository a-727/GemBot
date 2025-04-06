using Discord;
using Discord.Net;
using Discord.Rest;
using Discord.WebSocket;

namespace GemBot;

public partial class GemBot
{
    private Task CommandHandlerStartup(SocketSlashCommand command)
    {
        _ = CommandHandler(command);
        return Task.CompletedTask;
    }
    //Slash Command Handler Below
    private async Task CommandHandler(SocketSlashCommand command)
    {
        if (!_running)
        {
            await command.RespondAsync("GemBOT is currently down. Please try again soon.", ephemeral: true);
        }
        try
        {
            switch (command.Data.Name)
            {
                case "balance":
                    try
                    {
                        string temp = command.Data.Options.First().Value.ToString() ??
                                      throw new InvalidOperationException();
                        if (temp == "False")
                        {
                            await Balance(command, ephemeral: false);
                            break;
                        }

                        await Balance(command);
                    }
                    catch
                    {
                        await Balance(command);
                    }

                    break;
                case "item":
                    await GetItem(command);
                    break;
                case "stats":
                    await Stats(command);
                    break;
                case "beg":
                    await Beg(command);
                    break;
                case "magik":
                    await Magik(command);
                    break;
                case "work":
                    await Work(command);
                    break;
                case "inventory":
                    await InventoryCommand(command);
                    break;
                case "help":
                    await HelpCommand(command);
                    break;
                case "start":
                    await SetTutorial(command);
                    break;
                case "bank":
                    await Bank(command);
                    break;
                case "theme":
                    await SetTheme(command);
                    break;
                case "quests":
                    await DailyQuests(command);
                    break;
                case "give":
                    await Give(command);
                    break;
                case "play":
                    await Play(command);
                    break;
                case "mine":
                    await Mine(command);
                    break;
                case "craft":
                    await Craft(command);
                    break;
                case "shop":
                    await Shop(command);
                    break;
                case "spin" or "wheel":
                    await Spin(command);
                    break;
                case "notifications":
                    await Notifications(command);
                    break;
                case "key":
                    await Key(command);
                    break;
                case "exchange":
                    await CharmsToCoins(command);
                    break;
                case "setting":
                    await SettingsCommand(command);
                    break;
                case "event":
                    await EventCommand(command);
                    break;
                default:
                    await command.RespondAsync($"Command {command.Data.Name} not found", ephemeral: true);
                    break;
            }

            if (_users.TryGetValue(command.User.Id, out CachedUser? value))
            {
                _users[command.User.Id] = await Tools.UpdateTutorial(command.Data.Name, _tutorials, value, command);
            }
            if (Tools.AprilFoolsYear() == 2025)
            {
                User user = await GetUser(command.User.Id);
                if (!await user.OnCoolDown("EventScroll", Tools.CurrentTime(), 60, true))
                {
                    user.GainItem(52, 1);
                    if (user.GetSetting("AprilFools2025", 1) == 1){
                        RestUserMessage msg = await command.Channel.SendMessageAsync($"<@{user.ID}> You got 1{_items[52].Emoji} for using gemBot during an event\n`April Fools Day 2025`",
                            components: new ComponentBuilder().WithButton("Disable me!", "settings-AprilFools2025|0", ButtonStyle.Danger).Build());
                        await Task.Delay(60000);
                        await msg.DeleteAsync();
                    }
                }
            }
        }
        catch (Cooldown cool)
        {
            await command.RespondAsync(cool.Message, ephemeral: true);
        }
        catch (UserNotFoundError)
        {
            try
            {
                await UserSetupSlash(command);
            }
            catch (UserExistsException)
            {
                await command.RespondAsync(
                    "A user you are trying to interact with doesn't exists. You can only create accounts for yourself.");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            var embay = new EmbedBuilder()
                .WithTitle("Error")
                .WithAuthor(command.User)
                .WithColor(255, 0, 0)
                .AddField("Your command generated an error", $"**Full Details**: `{e}`");
            await command.RespondAsync(embed: embay.Build());
        }

    }
    //Slash Command Handler Above
    private async Task HelpCommand(SocketSlashCommand command)
    {
        EmbedBuilder embay = new EmbedBuilder()
            .WithTitle("All commands")
            .WithDescription("The main commands of gemBOT will be listed here, organized into groups.")
            .AddField("Grind commands",
                " > **/beg**: Beg for gems every 20 seconds! You can get 5-8 diamonds. \n > **/work**: Work for gems every 30 minutes! You can get 12-15 emeralds! \n > **/magik**: Technically a grind command, you can magik up gems every 60 seconds. Better with a wand!\n > **/play**: Play videogames. Rewards increase the more you run this command. Requires a controller\n > **/mine**: Mine stone, gems & coins! You don't NEED pickaxes to use this command.")
            .AddField("Balance Commands",
                " > **/balance**: view your balance in gems!\n > **/inventory**: view your inventory, split up into multiple pages\n > **/bank**: Convert your currency to other values")
            .AddField("Spend/Use Commands",
                " > **/shop**: View and buy limited-time drops and the daily token shop\n **/spin**: Use your coins to gain rewards, including charms!\n > **/key**: Use your keys\n > **/exchange**: Exchange duplicate chrams for coins")
            .AddField("Other economy commands", 
                " > **/quests**: View daily quests & gain rewards from daily quests\n > **/give**: Give gems and items to other users\n > **/craft**: Craft items from other items")
            .AddField("Info commands",
                " > **/help**: List (almost) all gemBOT commands, use *table* option to see loot tables \n > **/start**: View any of gemBOT's many tutorials. \n > **/item**: View details about an item\n > **/stats**: View your stats\n > **/notifications**: View your notifications")
            .AddField("Customization commands",
                " > **/theme**: Change your theme!\n > **/setting**: Change your settings")
            .WithColor((uint)(await GetUser(command.User.Id)).User.GetSetting("uiColor", 3287295))
            .WithFooter($"GemBot v{MainVersion}.{SmallVersion}.{BugfixVersion}");
        ComponentBuilder components = new ComponentBuilder()
            .WithButton("Support server", style: ButtonStyle.Link, url: "https://discord.gg/bMcWqPAaB7");
        await command.RespondAsync(embed: embay.Build(), components: components.Build());
    }
    private async Task Balance(SocketSlashCommand command, string title = "Your balance:", bool? compactArg = null, bool ephemeral = true)
    {
        bool compact = Tools.ShowEmojis(command, Settings.BotID(), _client);
        if (compactArg == true)
        {
            compact = true;
        }
        else if (compactArg == false)
        {
            compact = false;
        }
        User user = await GetUser(command.User.Id);
        user.Increase("commands", 1);
        EmbedBuilder embay = new EmbedBuilder()
            .WithTitle(title)
            .WithDescription(await BalanceRaw(compact, command.User.Id, ""))
            .WithColor((uint)user.GetSetting("uiColor", 3287295));
        await command.RespondAsync(embed: embay.Build(), ephemeral: ephemeral);
        await user.Save();
        if (Tools.CharmEffect(["betterAutoRefresh"], _items, user) == 0) return;
        ulong upperTime = Tools.CurrentTime() + 1800;
        while (Tools.CurrentTime() < upperTime)
        {
            user = await GetUser(command.User.Id);
            embay = new EmbedBuilder()
                .WithTitle(title)
                .WithDescription(await BalanceRaw(compact, command.User.Id, ""))
                .WithColor((uint)user.GetSetting("uiColor", 3287295));
            await command.ModifyOriginalResponseAsync(p =>
            {
                p.Embed = embay.Build();
                p.Content = "View your balance! This message auto-updates for an hour.";
            });
            await Task.Delay(2000);
        }
    }
    private async Task GetItem(SocketSlashCommand command)
    {
        try
        {
            await command.RespondAsync(
                _items[
                        int.Parse(command.Data.Options.First().Value.ToString() ??
                                  throw new Exception("Bad parameters - there's probably an error in the code."))]
                    .ToString(), ephemeral:true);
        }
        catch (ArgumentOutOfRangeException)
        {
            await command.RespondAsync("This item does not exist", ephemeral:true);
        }
    }
    private async Task UserSetupSlash(SocketSlashCommand command)
    {
        var id = command.User.Id;
        await Tools.UserCreator(id);
        await Balance(command, "Welcome to gemBOT! Here's your starting balance:");
    }
    private async Task Stats(SocketSlashCommand command)
    {
        User user = await GetUser(command.User.Id);
        user.Increase("commands", 1);
        EmbedBuilder embay = new EmbedBuilder()
            .WithTitle($"{command.User.Username}'s stats")
            .WithDescription($"View all the stats for {command.User.Username}.")
            .WithFooter(new EmbedFooterBuilder().WithText("GemBOT Stats may not be 100% accurate."))
            .AddField("Commands Ran", $"""
                                       {user.GetStat("commands")} commands in total
                                        > {user.GetStat("beg")} begs!
                                        > {user.GetStat("work")} works!
                                        > {user.GetStat("magik")} magiks!
                                        > {user.GetStat("play")} plays!
                                       """)
            .AddField("Gems Earned/Gifted",
                $"Earned {user.GetStat("earned")} diamond-equivalents by grinding only."
                + $"\n > Gifted {user.GetStat("gifted")} diamond-equivelents to other users only."
                + "\n > Diamond Equivalents: one diamond is 1, one emerald is 10, one sapphire is 100, one ruby is 1,000, and one amber is 10,000")
            .WithColor(new Color((uint)user.GetSetting("uiColor", 3287295)));
        await command.RespondAsync(embed: embay.Build());
    }
    private async Task Beg(SocketSlashCommand command)
    {
        User user = await GetUser(command.User.Id);
        if (command.Data.Options.Count >= 1)
        {
            user.SetSetting("hideBeg", (ulong)(long)command.Data.Options.First().Value);
        }
        ulong t = Tools.CurrentTime();
        uint timeoutFor = (Tools.CharmEffect(["fasterCooldown", "positive"], _items, user)) switch
        {
            < 1 => 20,
            < 8 => 19,
            < 27 => 18,
            < 64 => 17,
            < 125 => 16,
            < 216 => 15,
            < 343 => 14,
            < 512 => 13,
            < 729 => 12,
            < 1000 => 11,
            < 1331 => 10,
            < 1728 => 9,
            < 2197 => 8,
            < 2744 => 7,
            < 3375 => 6,
            < 4096 => 5,
            < 4913 => 4,
            < 5832 => 3,
            < 6859 => 2,
            < 8000 => 1,
            _ => 0
        };
        if (await user.OnCoolDown("beg", t, timeoutFor))
        {
            throw new Cooldown(user.CoolDowns["beg"]);
        }

        int mn = 7 + Tools.CharmEffect(["BegMin", "Beg", "GrindMin", "Grind", "Positive"], _items, user);
        int mx = 10 + Tools.CharmEffect(["BegMax", "Beg", "GrindMax", "Grind", "Positive"], _items, user);
        int amnt = _rand.Next(mn, mx);
        int chanceRoll = _rand.Next(0, 200) + 1; //random number from 1 to 200
        user.Increase("commands", 1);
        user.Increase("beg", 1);
        int sucsessChance = 120 - (int) user.GetProgress("begSuccess") + (Tools.CharmEffect(["BegChance", "Beg"], _items, user))*2;
        if (sucsessChance < 50) sucsessChance = 50;
        if (chanceRoll > sucsessChance)
        {
            uint color = (uint)(user.GetSetting("begRandom", 0) switch
            {
                0 => user.GetSetting("begColor", 65525),
                1 => (ulong)_rand.Next(16777216), 
                _ => (ulong)3342180
            });
            if (user.GetSetting("begFailRed", 1) == 1) color = 16711680;
            user.Increase("begFail", 1);
            EmbedBuilder embayFail = new EmbedBuilder()
                .WithTitle("Beg failure!")
                .WithDescription($"You failed and didn't get any gems. \n > You had a {sucsessChance/2}.{(sucsessChance%2) * 5}% chance to succeed.")
                .WithColor(new Color(color));
            await command.RespondAsync(embed: embayFail.Build(), ephemeral:user.GetSetting("hideBeg", 0) == 1);
            await user.Save();
            return;
        }
        user.Add(amnt, 0);
        string text = $"You gained {amnt} **Diamonds**.";
        if (Tools.ShowEmojis(command, Settings.BotID(), _client))
        {
            text = $"You gained {amnt}{_currency[0]}!";
        }
        user.Increase("earned", amnt);
        user.Increase("begSuccess", 1);
        List<string> begChoices = _dataLists["BegEffects"];
        EmbedBuilder embay = new EmbedBuilder()
            .WithTitle(text)
            .WithDescription(begChoices[_rand.Next(0, begChoices.Count)])
            .WithColor(new Color((uint)(user.GetSetting("begRandom", 0) switch
            {
                0 => user.GetSetting("begColor", 65525), 1 => (ulong)_rand.Next(16777216), _ => (ulong)3342180
            })));
        if (_rand.Next(0, 5) == 4)
        {
            embay.AddField($"You also gained 1{_items[0].Emoji}!", " > This happens 20% of the time");
            user.GainItem(0, 1);
        }
        await command.RespondAsync(embed: embay.Build(), ephemeral:user.GetSetting("hideBeg", 0) switch {1 => true, _ => false});
        await user.Save();
    }
    private async Task Work(SocketSlashCommand command)
    {
        CachedUser cached = await GetUser(command.User.Id);
        User user = cached.User;
        user.UpdateDay(Tools.CurrentDay());
        ulong t = Tools.CurrentTime();
        int effect = Tools.CharmEffect(["fasterCooldown", "positive"], _items, user);
        uint timeoutFor = effect switch
        {
            <= 0 => 1800,
            <= 150 => 1800-(uint)effect*2,
            <= 650 => 1500-((uint)effect-150),
            <= 1650 => 1000-((uint)effect-650)/2,
            <= 3150 => 500-((uint)effect-1650)/3,
            _ => 0
        };
        if (await user.OnCoolDown("work", t, timeoutFor))
        {
            throw new Cooldown(user.CoolDowns["work"]);
        }
        int workNum = _rand.Next(255) + 1;
        cached.LastWork = (byte) workNum;
        int jobRandom = _rand.Next(2);
        if (Tools.AprilFoolsYear() == 2025)
            jobRandom = -1;
        EmbedBuilder embay = new EmbedBuilder().WithTitle("Work!").WithColor((uint)user.GetSetting("uiColor", 3287295));
        ComponentBuilder components = new ComponentBuilder();
        string text = "View embed for more information.";
        switch (jobRandom)
        {
            case -1:
                embay.WithDescription("One of these is correct. The other is wrong. Good luck!");
                text = "April fools!!!";
                int correctButton = _rand.Next(2);
                components.WithButton("Click Me!", "work-" + (correctButton == 0) switch { true => "success", false => "failure" } + $"|{workNum}|{Tools.ShowEmojis(command, Settings.BotID(), _client)}")
                    .WithButton("Click Me!", "work-" + (correctButton == 0) switch { false => "success", true => "failure" } + $"|{workNum}|{Tools.ShowEmojis(command, Settings.BotID(), _client)}");
                await command.RespondAsync(text, embed: embay.Build(), components: components.Build());
                break;
            case 0:
                List<string> hearts = [":heart:", ":orange_heart:", ":yellow_heart:", ":green_heart:", ":blue_heart:", ":purple_heart:"];
                List<string> squares = [":red_square:", ":orange_square:", ":yellow_square:", ":green_square:", ":blue_square:", ":purple_square:"];
                List<string> circles = [":red_circle:", ":orange_circle:", ":yellow_circle:", ":green_circle:", ":blue_circle:", ":purple_circle:"];
                List<string> types = ["heart", "square", "circle"];
                int choiceShape = _rand.Next(3);
                int heartID = _rand.Next(hearts.Count);
                int squareID = _rand.Next(squares.Count);
                int circleID = _rand.Next(circles.Count);
                embay.WithDescription($"Remember the color of the following shapes\n > {hearts[heartID]} {squares[squareID]} {circles[circleID]}");
                await command.RespondAsync(text, embed: embay.Build());
                await Task.Delay(6000);
                embay.WithDescription($"What was the color of the {types[choiceShape]}?");
                int chosenID = choiceShape switch { 0 => heartID, 1 => squareID, _ => circleID };
                List<string> chosenList = choiceShape switch { 0 => hearts, 1=> squares, _ => circles };
                int excludedShape = _rand.Next(5);
                if (excludedShape >= chosenID) excludedShape++;
                ActionRowBuilder rowShapes = new ActionRowBuilder();
                for (int i = 0; i < chosenList.Count; i++)
                {
                    if (i == excludedShape) continue;
                    string customIDShapes = "work-" + (i == chosenID) switch { true => "success", false => "failure" } + $"|{workNum}|{Tools.ShowEmojis(command, Settings.BotID(), _client)}|{i}";
                    //{i} is added to prevent custom ID duplication.
                    rowShapes.WithButton(customId: customIDShapes, emote: Emoji.Parse(chosenList[i]), style: ButtonStyle.Primary);
                }
                components.AddRow(rowShapes);
                await command.ModifyOriginalResponseAsync((properties) =>
                {
                    properties.Embed = embay.Build();
                    properties.Components = components.Build();
                });
                break;
            case 1:
                int checkX = _rand.Next(5);
                int checkY = _rand.Next(5);
                for (int i = 0; i < 5; i++)
                {
                    ActionRowBuilder rowBuilder = new ActionRowBuilder();
                    for (int j = 0; j < 5; j++)
                    {
                        if (i != checkY || j != checkX) rowBuilder.WithButton(new ButtonBuilder()
                            .WithEmote(Emoji.Parse(":heavy_multiplication_x:"))
                            .WithStyle(ButtonStyle.Danger)
                            .WithCustomId($"basic-disabled|{i*5+j}")
                            .WithDisabled(true)
                        );
                        else rowBuilder.WithButton(
                            new ButtonBuilder()
                                .WithEmote(Emoji.Parse(":heavy_check_mark:"))
                                .WithStyle(ButtonStyle.Success)
                                .WithCustomId($"basic-disabled|{i*5+j}")
                                .WithDisabled(true)
                            );
                    }
                    components.AddRow(rowBuilder);
                }
                embay.WithDescription("Memorize the location of the checkmark");
                await command.RespondAsync(embed: embay.Build(), components: components.Build());
                await Task.Delay(6000);
                embay.WithDescription("Click the button where the checkmark was");
                components = new ComponentBuilder();
                for (int i = 0; i < 5; i++)
                {
                    ActionRowBuilder rowBuilder = new ActionRowBuilder();
                    for (int j = 0; j < 5; j++)
                    {
                        if (i != checkY || j != checkX) rowBuilder.WithButton(
                            new ButtonBuilder()
                                .WithEmote(Emoji.Parse(":question:"))
                                .WithStyle(ButtonStyle.Secondary)
                                .WithCustomId($"work-failure|{workNum}|{Tools.ShowEmojis(command, Settings.BotID(), _client)}|{5*i+j}")
                            );
                        else rowBuilder.WithButton(
                            new ButtonBuilder()
                                .WithEmote(Emoji.Parse(":question:"))
                                .WithStyle(ButtonStyle.Secondary)
                                .WithCustomId($"work-success|{workNum}|{Tools.ShowEmojis(command, Settings.BotID(), _client)}|{5*i+j}")
                        );
                    }
                    components.AddRow(rowBuilder);
                }
                await command.ModifyOriginalResponseAsync((properties) =>
                {
                    properties.Embed = embay.Build();
                    properties.Components = components.Build();
                });
                break;
            default:
                await user.OnCoolDown("work", t - 1, 0);
                await command.RespondAsync("This work was bugged, so we reset the cooldown. Please /work again.", ephemeral: true);
                break;
        }
        /*
        await user.Add(amnt, 1, false);
        string text = $"You gained {amnt} **Emeralds**.";
        if (Tools.ShowEmojis(command, Settings.BotID(), _client))
        {
            text = $"You gained {amnt}{_currency[1]}!!!";
        }
        await user.Increase("commands",1, false);
        await user.Increase("earned", amnt*10, false);
        await user.Increase("work", 1);
        List<string> workChoices = _dataLists["WorkEffect"];
        EmbedBuilder embay = new EmbedBuilder()
            .WithTitle(text)
            .WithDescription(workChoices[_rand.Next(0, workChoices.Count)])
            .WithColor(new Color(50, 255, 100));
        await command.RespondAsync(embed: embay.Build());
        */
    }
    private async Task InventoryCommand(SocketSlashCommand command)
    {
        string emoj = Tools.ShowEmojis(command, Settings.BotID(), _client).ToString();
        ComponentBuilder builder = new ComponentBuilder()
            .WithButton("<-- Left", "disabledL", disabled: true)
            .WithButton("Refresh", $"inv-0|{emoj}", ButtonStyle.Secondary)
            .WithButton("Right -->", $"inv-1|{emoj}");
        EmbedBuilder embay = await InventoryRawEmbed(command.User.Id, $"0|{emoj}");
        await command.RespondAsync(embed: embay.Build(), components: builder.Build());
    }
    private async Task Magik(SocketSlashCommand command)
    {
        CachedUser user = await GetUser(command.User.Id);
        ulong t = (ulong)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        uint timeoutFor = (Tools.CharmEffect(["fasterCooldown", "positive"], _items, user)) switch
        {
            < 1 => 60, < 4 => 59, < 9 => 58, < 16 => 57, < 25 => 56, < 36 => 55, < 49 => 54, < 64 => 53, < 81 => 52,
            < 100 => 51,
            < 121 => 50, < 144 => 49, < 169 => 48, < 196 => 47, < 225 => 46, < 256 => 45, < 300 => 44, < 350 => 43,
            < 400 => 42, < 450 => 41,
            < 500 => 40, < 550 => 39, < 600 => 38, < 650 => 37, < 700 => 36, < 750 => 35, < 800 => 34, < 850 => 33,
            < 900 => 32, < 950 => 31,
            < 1000 => 30, < 1070 => 29, < 1140 => 28, < 1210 => 27, < 1280 => 26, < 1350 => 25, < 1420 => 24,
            < 1490 => 23, < 1560 => 22, < 1630 => 21,
            < 1700 => 20, < 1800 => 19, < 1900 => 18, < 2000 => 17, < 2100 => 16, < 2200 => 15, < 2300 => 14,
            < 2400 => 13, < 2500 => 12, < 2600 => 11,
            < 2700 => 10, < 2845 => 9, < 2990 => 8, < 3135 => 7, < 3280 => 6, < 3425 => 5, < 3570 => 4, < 3715 => 3,
            < 3860 => 2, < 4005 => 1,
            _ => 0
        };
        if (await user.User.OnCoolDown("magik", t, timeoutFor))
        {
            throw new Cooldown(user.User.CoolDowns["magik"]);
        }
        int power = Tools.CharmEffect(["Magik", "Unlocker", "Positive"], _items, user);
        bool badInput = false;
        ulong targetID;
        try
        {
            targetID = ((SocketGuildUser)command.Data.Options.First().Value).Id;
            if (command.Data.Options.Last().Value.ToString() == "yes")
            {
                if (targetID != user.User.GetSetting("magikID", user.User.ID)) power += 1;

                user.User.SetSetting("magikID", targetID);
            }
        }
        catch (InvalidOperationException)
        {
            targetID = user.User.GetSetting("magikID", user.User.ID);
        }
        catch (InvalidCastException)
        {
            targetID = user.User.GetSetting("magikID", user.User.ID);
            badInput = true;
        }
        CachedUser target = await GetUser(targetID);
        bool otherTarget = targetID != user.User.ID;
        if (otherTarget) power += 3;
        power += _rand.Next(0, 2);
        if (targetID != user.User.GetSetting("magikID", user.User.ID))
        {
            power += 1;
        }
        if (Tools.AprilFoolsYear() == 2025) power += 1;
        List<MagikReward> chances =
        [
            //total chance = 10
            new StandardMagikReward("$user gained 12$diamonds", 12, 0, 1, 0, 3),
            new StandardMagikReward("$user gained 1$emeralds.", 1, 0, 0, 0, 2),
            new MagikReward(3 + power, (_, _) => "Nothing happened"),
            new StandardMagikReward("$target gained 10$diamonds", 0, 0, 10, 0, 2)
        ];
        if (power >= 1)
        {
            //total chance = 15, new chance = 5
            chances.Add(new StandardMagikReward("$user and $target both gained 7$diamonds", 7, 0, 7, 0, 2));
            chances.Add(new StandardMagikReward("$user and $target both gained 8$diamonds", 8, 0, 8, 0, 2));
        }
        if (power >= 2)
        {
            //total chance = 20, new chance = 5
            chances.Add(new StandardMagikReward("$user gained 20$diamonds", 20, 0, 0, 0, 2));
            chances.Add(new StandardMagikReward("$target gained 20$diamonds", 3, 0, 20, 0, 2));
        }
        if (power >= 3)
        {
            //total chance = 25, new chance = 5
            chances.Add(new StandardMagikReward($"$user and $target both gained {10 + power * 4}$diamonds!", 10 + power * 4, 0, 10 + power * 4, 0, 2));
            chances.Add(new StandardMagikReward("$user gained 2$emeralds!", 2, 1, 0, 0, 1));
            chances.Add(new StandardMagikReward("$target gained 2$emeralds", 0, 0, 2, 1, 1));
        }
        if (power >= 4)
        {
            //total chance = 30, new chance = 5
            chances.Add(new StandardMagikReward("$user and $target both gained 2$emeralds", 2, 1, 2, 1, 2));
            chances.Add(new ItemMagikReward("$user gained $user_item", 1, 10, 0, 0, 1, _items));
            chances.Add(new ItemMagikReward("$target gained $target_item", 0, 0, 1, 10, 1, _items));
        }
        if (power >= 5)
        {
            //total chance = 35, new chance = 5
            chances.Add(new StandardMagikReward("$user gained 1$sapphires", 1, 2, 0, 0, 2));
            chances.Add(new StandardMagikReward("$target gained 1$sapphires", 0, 0, 1, 2, 2));
        }
        if (power >= 6)
        {
            //total chance = 40, new chance = 5
            chances.Add(new StandardMagikReward($"$user and $target gained {30 + 3 * power}$diamonds", 30 + 3 * power, 0, 30 + 3 * power, 0, 2));
            chances.Add(new StandardMagikReward($"$user gained {65 + 5 * power}$diamonds", 65 + 5 * power, 0, 0, 0, 1));
            chances.Add(new StandardMagikReward($"$target gained {50 + 5 * power}$diamonds", 0, 0, 50 + 5 * power, 0, 1));
        }
        if (power >= 7)
        {
            //total chance == 45, new chance = 5
            //NOW HAS TWO POWER-SCALING CHANCES
            chances.Add(new MagikReward(power - 4, (mUser, mTarget) =>
            {
                bool didSomethingUser = false;
                for (int i = 0; i < 1800 + (power * 300); i++)
                {
                    if (mUser.CraftTick(_items, _craftingRecipes, FurnaceConst)) didSomethingUser = true;
                }
                if (!didSomethingUser)
                {
                    mUser.User.Add(70 + 6 * power, 0);
                }
                if (mUser.User.ID != mTarget.User.ID)
                {
                    bool didSomethingTarget = false;
                    for (int i = 0; i < 600 + (power * 120); i++)
                    {
                        if (mTarget.CraftTick(_items, _craftingRecipes, FurnaceConst)) didSomethingTarget = true;
                    }

                    if (!didSomethingTarget)
                    {
                        mTarget.User.Add(40 + 2 * power, 0);
                    }

                    string toReturnT = didSomethingUser switch
                    {
                        true => $"$user's crafting was sped up by {30 + power*5} minutes",
                        false => $"$user gained {70 + power * 6}$diamonds"
                    };
                    toReturnT += didSomethingTarget switch
                    {
                        true => $"\n > And, $target's crafting was sped up by {10+power*2} minutes",
                        false => $"\n > And, $target gained {40 + 2 * power}$diamonds"
                    };
                    return toReturnT;
                }
                string toReturn = didSomethingUser switch
                {
                    true => $"$user's crafting was sped up by {30 + power *5} minutes",
                    false => $"$user gained {70 + power * 6}$diamonds"
                } + "\n > The target could've gained extra rewards if you targeted somebody.";
                return toReturn;
            }));
            chances.Add(new ItemMagikReward($"$user gained $user_item and $target gained $target_item", 3, 0, 3, 0, 1, _items));
        }
        if (power >= 8)
        {
            //total chance = 50, new chance = 5
            chances.Add(new StandardMagikReward("$user gained 1$sapphires", 1, 2, 0, 0, 2));
            chances.Add(new StandardMagikReward("$target gained 1$sapphires", 0, 0, 1, 2, 1));
        }
        if (power >= 9)
        {
            //total chance = 55, new chance = 5
            chances.Add(new ItemMagikReward($"$user gained $user_item and $target gained $target_item", 1, 10, 1, 10, 1, _items));
            chances.Add(new ItemMagikReward($"$user gained $user_item and $target gained $target_item", 5, 0, 5, 0, 1, _items));
            chances.Add(new ItemMagikReward($"$user gained $user_item and $target gained $target_item", 1, Tools.GetCharm(_itemLists, 0, 5), 1, 0, 1, _items));
        }
        if (power >= 10)
        {
            //total chance = 60, new chance = 5
            //NOW HAS THREE POWER-SCALING CHANCES
            chances.Add(otherTarget
                ? new ItemMagikReward($"Where alchemy fails, ~~science~~ Magik! prevails ($user got $user_item, $target got $target_item).", 1, 54, 1, 54, power - 8, _items)
                : new ItemMagikReward($"Where alchemy fails.... **oh wait you targeted yourself you idiot** ($user got $user_item).", 1, 54, 0, 54, power - 8, _items));
            chances.Add(new ItemMagikReward($"$user gained $user_item and $target gained $target_item", 3, 0, 1, Tools.GetCharm(_itemLists, 0, 6), 1, _items));
        }
        if (power >= 11)
        {
            //total chance = 65, new chance = 5
            //NOW HAS FOUR POWER-SCALING CHANCES
            chances.Add(new MagikReward(power-10, (userMine, targetMine) =>
            {
                userMine.User.SetData("mineSkip", userMine.User.GetData("mineSkip", 0)+3000);
                if (userMine.User.ID != targetMine.User.ID)
                {
                    userMine.User.SetData("mineSkip", userMine.User.GetData("mineSkip", 0)+600);
                    targetMine.User.SetData("mineSkip", targetMine.User.GetData("mineSkip", 0)+1800);
                    return "$user and $target got their mining skipped by 60 and 30 minutes, respectively";
                }

                return
                    "$user got their mining sped up by 50 minutes. They missed out on an additional 10 minutes and 30 minutes to target by targeting themselves";
            })); 
            chances.Add(new ItemMagikReward("$user gained $user_item and $target gained $target_item", 1, Tools.GetCharm(_itemLists, 0, 5), 2, 0, 1, _items));
        }
        if (power >= 12)
        {
            //total chance = 70, new chance = 5
            chances.Add(otherTarget
                ? new StandardMagikReward("$user gained 2$sapphires", 2, 2, 0, 0, 1)
                : new StandardMagikReward("Why did you target yourself??? You lost so much money. You only get 12$diamonds", 12, 0, 0, 0, 1));
        }
        if (power >= 13)
        {
            //total chance = 75, new chance = 5
            chances.Add(otherTarget
                ? new StandardMagikReward("$target gained 2$sapphires and $user gained 4$emeralds", 4, 1, 2, 2, 1)
                : new StandardMagikReward("Why did you target yourself??? You lost so much money. You only get 12$diamonds", 12, 0, 0, 0, 1));
        }
        if (power >= 14)
        {
            //total chance = 80, new chance = 5
            chances.Add(otherTarget
                ? new ItemMagikReward("$user gained $user_item", 1, Tools.GetCharm(_itemLists, 0, 3), 0, 0, 1, _items)
                : new StandardMagikReward("Why did you target yourself??? You lost so much. You only get 12$diamonds", 12, 0, 0, 0, 1));
        }
        if (power >= 15)
        {
            //total chance = 85, new chance = 5
            chances.Add(otherTarget
                ? new ItemMagikReward("$target gained $target_item and $user gained $user_item", 6, 0, 1, Tools.GetCharm(_itemLists, 0, 4), 1, _items)
                : new StandardMagikReward("Why did you target yourself??? You lost so much. You only get 12$diamonds", 12, 0, 0, 0, 1));
        }
        if (power >= 16)
        {
            //total chance = 90, new chance = 5
            chances.Add(otherTarget
                ? new ItemMagikReward("$user gained $user_item and $target gained $target_item", 1, 1, 3, 0, 1, _items)
                : new StandardMagikReward("Why did you target yourself??? You lost so much. You only get 12$diamonds", 12, 0, 0, 0, 1));
        }
        if (power >= 17)
        {
            //total chance = 95, new chance = 5
            chances.Add(otherTarget
                ? new ItemMagikReward("$user gained $user_item and $target gained $target_item", 5, 0, 1, 1, 1, _items)
                : new StandardMagikReward("Why did you target yourself??? You lost so much. You only get 12$diamonds", 12, 0, 0, 0, 1));
        }
        if (power >= 18)
        {
            //total chance = 100, new chance = 5
            chances.Add(otherTarget
                ? new ItemMagikReward("$user gained $user_item and $target gained $target_item", 2, 1, 1, 0, 1, _items)
                : new StandardMagikReward("Why did you target yourself??? You lost so much. You only get 12$diamonds", 12, 0, 0, 0, 1));
        }
        if (power >= 19)
        {
            //total chance = 105, new chance = 5
            chances.Add(otherTarget
                ? new ItemMagikReward("$user gained $user_item and $target gained $target_item", 12, 0, 1, 1, 1, _items)
                : new StandardMagikReward("Why did you target yourself??? You lost so much. You only get 12$diamonds", 12, 0, 0, 0, 1));
        }
        if (power >= 20)
        {
            //total chance = 109, new chance = 4
        }
        if (power >= 21)
        {
            //total chance = 113, new chance =4
        }
        List<int> pickFrom = [];
        for (int i = 0; i < chances.Count; i++)
        {
            for (int j = 0; j < chances[i].Chance; j++)
            {
                pickFrom.Add(i);
            }
        }
        MagikReward reward = chances[pickFrom[_rand.Next(pickFrom.Count)]];
        string diamonds = " **diamonds**";
        string emeralds = " **emeralds**";
        string sapphires = " **sapphires**";
        bool emoji = Tools.ShowEmojis(command, Settings.BotID(), _client);
        if (emoji)
        {
            diamonds = _currency[0];
            emeralds = _currency[1];
            sapphires = _currency[2];
        }
        user.User.Increase("commands", 1);
        user.User.Increase("magik", 1);
        string toRespond = reward.DoMagik(user, target)
            .Replace("$diamonds", diamonds)
            .Replace("$emeralds", emeralds)
            .Replace("$sapphires", sapphires)
            .Replace("$user", $"<@{user.User.ID}>")
            .Replace("$target", $"<@{target.User.ID}>");
        if (Tools.AprilFoolsYear() == 2025)
            toRespond = "Get April Fooled! I won't tell you what you got! (But in return, I gave you an extra power! Enjoy)";
        EmbedBuilder embay = new EmbedBuilder()
            .WithTitle("Magik Time!")
            .WithDescription(toRespond)
            .WithFooter($"Magik by gemBOT: {power} power!")
            .WithColor(new Color((uint)(user.User.GetSetting("magikRandomColor", 1) switch
            {
                0 => user.User.GetSetting("magikColor", 13107400), _ => (ulong)_rand.Next(16777216)
            })));
        string topText = power switch
        {
            0 => "Good Magik!",
            1 => "Thin air Magiked!",
            2 => "Magik Power!",
            3 => "Double thin air Magiked!",
            4 => "Magik! Magik! Magik!",
            5 => "Sparks shoot out of your wand, and... (my challenge: can you get this text WITHOUT a wand?)",
            6 => "Magik sparks shoot out of your wand, and...",
            7 => "Okay, I know you did an elaborate setup to get this text.",
            8 => "A Magik ball shoots out of your wand, and...",
            9 => "A large Magik Ball shoots ouf of your Magik wand, and Magik happened...",
            10 => "**Sparks shoot out of your wand**, and... ||this text is tied to your power||",
            11 => "Do people even read this text? With the strat you clearly have, probably not",
            12 => "Magik done successfully!",
            13 => "Excellent Magik!",
            14 => "Thin air Magiked into sparks, and...",
            15 => "**Sparks shoot out of your wand**, *and*... ||Can you please recommend my bot to your friends?||",
            16 => "Magik! Magik! Magik! Magik! Magik!",
            17 => "Seventeen-uple thin air Magiked!",
            18 => "18 magik balls shoot out of your wand, merging together to become...",
            19 => "Imagine if nothing happens at this power LOL. It's over a 1/5 change always!",
            20 => "***Sparks shoot out of your wand***, *and*... ||I feel like I'll be giving magik power inflation forever||",
            _ => "You're probably cheating at this point..."
        };
        await command.RespondAsync(topText, embed: embay.Build());
        if (badInput)
            await command.FollowupAsync("You can't set a target as default if you don't specify a target. Sorry.",
                ephemeral: true);
    }
    private async Task SetTutorial(SocketSlashCommand command)
    {
        IReadOnlyCollection<SocketSlashCommandDataOption> options = command.Data.Options;
        ushort i = (ushort)options.Count;
        switch (i)
        {
            case 0:
                CachedUser user1 = await GetUser(command.User.Id);
                if (user1.TutorialOn == null)
                {
                    await command.RespondAsync("You do not have an active tutorial");
                    return;
                }

                Tutorial tutorial = _tutorials[(int)user1.TutorialOn];
                Step step = tutorial.Steps[user1.TutorialPage];
                EmbedBuilder embay = new EmbedBuilder()
                    .WithTitle($"{tutorial.Name}: {step.Name}")
                    .WithDescription(step.Description);
                await command.RespondAsync("Here is your tutorial progress:", embed: embay.Build());
                break;
            case 1:
                CachedUser user2 = await GetUser(command.User.Id);
                long value = (long)options.First().Value;
                if (value == -1)
                {
                    user2.TutorialOn = null;
                    user2.TutorialPage = 0;
                    user2.TutorialProgress = null;
                    await command.RespondAsync("Deleted current tutorial!");
                }
                else
                {
                    user2.TutorialOn = Convert.ToInt32(value);
                    user2.TutorialPage = 0;
                    user2.TutorialProgress = null;
                    if (user2.TutorialOn == null)
                    {
                        await command.RespondAsync("You do not have an active tutorial");
                        return;
                    }

                    Tutorial tutorial2 = _tutorials[(int)user2.TutorialOn];
                    Step step2 = tutorial2.Steps[user2.TutorialPage];
                    EmbedBuilder embay2 = new EmbedBuilder()
                        .WithTitle($"{tutorial2.Name}: {step2.Name}")
                        .WithDescription(step2.Description);
                    await command.RespondAsync("Here is your tutorial progress:", embed: embay2.Build());
                }
                break;
        }
    }
    private async Task SetTheme(SocketSlashCommand command)
    {
        User user = await GetUser(command.User.Id);
        string themeName = "Theme not found";
        switch ((long)command.Data.Options.First().Value)
        {
            //default = 0
            case 0:
                user.SetSetting("bankLeftStyle", 0);
                user.SetSetting("bankRightStyle", 2);
                user.SetSetting("bankShowRed", 1); 
                user.SetSetting("begRandom", 0);
                user.SetSetting("begColor", 65525);
                user.SetSetting("magikRandomColor", 1);
                user.SetSetting("uiColor", 3287295);
                user.SetSetting("begFailRed", 1);
                user.SetSetting("progressBar", 5);
                await user.Save();
                themeName = "default";
                break;
            //discord = 1
            case 1:
                user.SetSetting("bankLeftStyle", 0);
                user.SetSetting("bankRightStyle", 0);
                user.SetSetting("bankShowRed", 0);
                user.SetSetting("begRandom", 0);
                user.SetSetting("begColor", 5793266);
                user.SetSetting("magikRandomColor", 0);
                user.SetSetting("magikColor", 6584831);
                user.SetSetting("uiColor", 6566600);
                user.SetSetting("begFailRed", 1);
                user.SetSetting("progressBar", 4);
                await user.Save();
                themeName = "Discord";
                break;
            //green = 2
            case 2: 
                user.SetSetting("bankLeftStyle", 1);
                user.SetSetting("bankRightStyle", 1);
                user.SetSetting("bankShowRed", 1);
                user.SetSetting("begRandom", 0);
                user.SetSetting("begColor", 3342180);
                user.SetSetting("magikRandomColor", 1);
                user.SetSetting("uiColor", 57640);
                user.SetSetting("begFailRed", 1);
                user.SetSetting("progressBar", 5);
                themeName = "Green";
                break;
            //grey = 3
            case 3:
                user.SetSetting("bankLeftStyle", 2);
                user.SetSetting("bankRightStyle", 2);
                user.SetSetting("bankShowRed", 1);
                user.SetSetting("begRandom", 0);
                user.SetSetting("begColor", 8224125);
                user.SetSetting("magikRandomColor", 0);
                user.SetSetting("magikColor", 0);
                user.SetSetting("uiColor", 5260890);
                user.SetSetting("begFailRed", 1);
                user.SetSetting("progressBar", 6);
                await user.Save();
                themeName = "Grey";
                break;
            //random = 4
            case 4:
                user.SetSetting("bankLeftStyle", (ulong)_rand.Next(0, 3));
                user.SetSetting("bankShowRed", (ulong)_rand.Next(0, 2));
                user.SetSetting("begRandom", 1);
                user.SetSetting("magikRandomColor", 1);
                user.SetSetting("uiColor", (ulong)_rand.Next(16777216));
                user.SetSetting("bankRightStyle", (ulong)_rand.Next(0, 3));
                user.SetSetting("begFailRed", (ulong)_rand.Next(2));
                user.SetSetting("progressBar", (ulong)_rand.Next(2, 8));
                await user.Save();
                themeName = "Random (the uiColor is randomized just once: now)";
                break;
            //OG = 5
            case 5:
                user.SetSetting("bankLeftStyle", 0);
                user.SetSetting("bankRightStyle", 0);
                user.SetSetting("bankShowRed", 0);
                user.SetSetting("begRandom", 0);
                user.SetSetting("begColor", 65535);
                user.SetSetting("magikRandomColor", 1);
                user.SetSetting("uiColor", 65535);
                user.SetSetting("begFailRed", 0);
                user.SetSetting("progressBar", 3);
                await user.Save();
                themeName = "OG Gembot";
                break;
        }

        await command.RespondAsync(embed: new EmbedBuilder().WithTitle("Theme Changed")
            .WithDescription($"Your gemBOT theme has been changed to {themeName}.")
            .WithColor((uint)user.GetSetting("uiColor", 3287295)).Build());
    }
    private async Task Bank(SocketSlashCommand command)
    {
        bool fastUpgrade = command.Data.Options.Count > 0 && (bool)command.Data.Options.First().Value;
        Tuple<EmbedBuilder, ComponentBuilder, string> results =
            await BankRaw(Tools.ShowEmojis(command, Settings.BotID(), _client), command.User.Id, fastUpgrade);
        await command.RespondAsync(results.Item3, ephemeral: false, embed: results.Item1.Build(),
            components: results.Item2.Build());
        await (await GetUser(command.User.Id)).User.Save();
    }
    private async Task DailyQuests(SocketSlashCommand command)
    {
        for (int i= 0; i < (Tools.CharmEffect(["betterAutoRefresh"], _items, await GetUser(command.User.Id))) switch {0 => 16, _ => (60*12)+1}; i++)
        {
            CachedUser cUser = await GetUser(command.User.Id);
            User user = cUser;
            Tuple<EmbedBuilder, List<string>> main = await QuestsEmbed(cUser,
                Tools.ShowEmojis(command, Settings.BotID(), _client));
            try{await command.RespondAsync("View your daily quests!", embed: main.Item1.Build());}
            catch (Exception ex) when (ex is  HttpException or TimeoutException or InvalidOperationException)
            {
                try
                {
                    await command.ModifyOriginalResponseAsync(properties =>
                    {
                        properties.Embed = main.Item1.Build();
                        properties.Content = "View your daily quests (auto-refresh)!";
                    });
                }
                catch (HttpException)
                {
                    return;
                }
            }
            foreach (string toSend in main.Item2)
            {
                await Task.Delay(500);
                await command.FollowupAsync(toSend);
            }
            await Task.Delay((Tools.CharmEffect(["betterAutoRefresh"], _items, user)) switch {0 => 60000, _ => 4800});
        }
    }
    private async Task Give(SocketSlashCommand command)
    {
        bool gem = command.Data.Options.First().Name == "gems";
        if (gem)
        {
            List<SocketSlashCommandDataOption> temp = command.Data.Options.First().Options.ToList();
            IUser iUser = (IUser)temp[0].Value;
            ushort index = (ushort)(uint)(ulong)(long)temp[1].Value;
            int amount = (int)(long)temp[2].Value;
            User main = await GetUser(command.User.Id);
            User target = await GetUser(iUser.Id);
            if (main.Gems[index] < amount)
            {
                await command.RespondAsync("You can't afford this.", ephemeral: true);
                return;
            }
            if (main.ID == target.ID)
            {
                await command.RespondAsync("You can't gift gems to yourself", ephemeral: true);
                return;
            }
            main.Add(-1*amount, index);
            main.Increase("gifted", (int)(amount * Math.Pow(10, index)));
            main.Increase("commands", 1);
            target.Add(amount, index);
            bool emoj = Tools.ShowEmojis(command, Settings.BotID(), _client);
            EmbedBuilder embay = new EmbedBuilder()
                .WithTitle("Transaction successful")
                .WithFooter("GemBOT economy!")
                .WithColor((uint)main.GetSetting("uiColor", 3287295))
                .WithDescription(
                    $"You have successfully transferred {amount}{(emoj switch { true => _currency, false => _currencyNoEmoji })[index]} from <@{main.ID}> to <@{target.ID}>");
            await command.RespondAsync($"Thank you for giving gems to <@{target.ID}>", embed: embay.Build());
        }
        else
        {
            List<SocketSlashCommandDataOption> temp = command.Data.Options.First().Options.ToList();
            IUser iUser = (IUser)temp[0].Value;
            int index = (int)(ulong)(long)temp[1].Value;
            int amount = (int)(long)temp[2].Value;
            User main = await GetUser(command.User.Id);
            User target = await GetUser(iUser.Id);
            if (main.Inventory[index] < amount)
            {
                await command.RespondAsync("You can't afford this.", ephemeral: true);
                return;
            }
            if (main.ID == target.ID)
            {
                await command.RespondAsync("You can't gift items to yourself", ephemeral: true);
                return;
            }
            main.GainItem(index, -amount);
            main.Increase("gifted", amount * _items[index].Value);
            main.Increase("commands", 1);
            target.GainItem(index, amount);
            bool emoj = Tools.ShowEmojis(command, Settings.BotID(), _client);
            EmbedBuilder embay = new EmbedBuilder()
                .WithTitle("Item Gifting successful")
                .WithFooter("GemBOT economy!")
                .WithColor((uint) target.GetSetting("uiColor", 3287295))
                .WithDescription(
                    $"You have successfully gifted {amount}{emoj switch { true => _items[index].Emoji, false => $"  **{_items[index].Name}**" }} from <@{main.ID}> to <@{target.ID}>");
            await command.RespondAsync($"Thank you for giving items to <@{target.ID}>", embed: embay.Build());
        }
    }
    private async Task Play(SocketSlashCommand command)
    {
        User user = await GetUser(command.User.Id);
        if (Tools.CharmEffect(["unlockPlay"], _items, user) <= 0)
        {
            await command.RespondAsync("You need to have a controller to play video games in gemBOT.", ephemeral: true);
            return;
        }
        int effect = Tools.CharmEffect(["fasterCooldown", "positive"], _items, user);
        uint timeoutFor = effect switch
        {
            <= 0 => 600,
            <= 20 => 600-(uint)effect*3,
            <= 50 => 540-((uint)effect-20)*2,
            <= 110 => 480-((uint)effect-50),
            <= 230 => 420-((uint)effect-110)/2,
            <= 410 => 360-((uint)effect-230)/3,
            <= 710 => 300-((uint)effect-410)/5,
            <= 1190 => 240-((uint)effect-710)/8,
            <= 1910 => 180-((uint)effect-1190)/12,
            <= 3110 => 120-((uint)effect-1910)/20,
            <= 5210 => 60-((uint)effect-3110)/35,
            _ => 0
        } + (uint)user.GetStat("play");
        if (await user.OnCoolDown("play", Tools.CurrentTime(), timeoutFor))
        {
            throw new Cooldown(user.CoolDowns["play"]);
        }
        user.IncreaseStat("play", 1);
        uint power = (uint)user.GetStat("play");
        int roll = _rand.Next(20)+1;
        string text = $"gemBOT could not resolve text for power {power} and d20 roll {roll}";
        string header = "Play Video Games!";
        bool emoji = Tools.ShowEmojis(command, Settings.BotID(), _client);
        // ReSharper disable once StringLiteralTypo
        string diamonds = emoji switch { true => _currency[0], false => " DIAMONDZ" };
        // ReSharper disable once StringLiteralTypo
        string emeralds = emoji switch { true => _currency[1], false => " EMERALDZ" };
        // ReSharper disable once StringLiteralTypo
        string sapphires = emoji switch { true => _currency[2], false => " SAPPHIERS" };
        // ReSharper disable once StringLiteralTypo
        string rubies = emoji switch { true => _currency[3], false => " RUBEES" };
        // ReSharper disable once StringLiteralTypo
        string ambers = emoji switch { true => _currency[0], false => " AMBURRRRRZ" };
        string[] cur = [diamonds, emeralds, sapphires, rubies, ambers];
        string footer = roll switch
        {
            1 => "TIP: Run grinding commands for gems",
            2 => "TIP: Charms with positive effects have positive effects",
            3 => "TIP: Daily quests contain rewards",
            4 => "TIP: `/magik` is better with a wand",
            5 => "TIP: Play more, get better at the game, earn more",
            6 => "TIP: These tips can randomly appear!",
            7 => $"You have played video games {power} times.",
            8 => "TIP: limited-time drops are limited in amount, not time",
            9 => $"You are too addicted to video games! You played a whopping {power} times",
            10 => "Did you know?: GemBOT was coded with code",
            11 => "TIP: At the bottom of `/play` useless tips appear, make sure to read them instead of grinding.",
            12 => "TIP: You can give items gems to friends with `/give`",
            13 => "TIP: Try using `/play` to play video games. It can get you some nifty rewards!",
            14 => "TIP: You can't run gemBOT commands when it is offline",
            15 => "Did you know?: You rolled a 15 on your d20 to randomly determine the effect.",
            16 => "Did you know?: a727 has a discord account",
            17 => "Did you know?: These represent a loading screen tip",
            18 => "TIP: The more gems you have, the more gems you have.",
            19 => "TIP: Items can be obtained",
            20 => $"You rolled a 20 and you have played video games {power} times.",
            _ => "TIP: Did you know? This should never happen. If this does happen, please report this to a727 and you might get half a diamond\n(okay, fine, he'll have to give you one diamond)"
        };
        if (power <= 0)
        {
            await command.RespondAsync("glitched gemBOT", ephemeral: true);
            return;
        }
        if (power <= 1)
        {
            header = "Welcome to play";
            text = "This is your first time playing, so you had to do the tutorial. You didn't earn anything";
        }
        else if (power <= 2)
        {
            text = "You're still learning, but you got a single diamond";
            user.Add(1, 0);
            user.Increase("earned", 1);
        }
        else if (power <= 4)
        {
            text = "You're still learning, but you got two diamonds";
            user.Add(2, 0);
            user.Increase("earned", 2);
        }
        else if (power <= 8)
        {
            switch (roll)
            {
                case 1 or 2 or 3 or 4:
                    text = "You forgot to stream, so you gained nothing";
                    break;
                case 5 or 6 or 7 or 8 or 9:
                    text = "Your stream got 0 viewers because a very popular stream was going on in the moment.";
                    break;
                case 10 or 11 or 12:
                    text = "You got 3 viewers, but you weren't able to make any bank from your ad break."
                        + "\nAnd, you lost all your viewers because it was a crypto scam ad.";
                    break;
                case 13 or 14 or 15 or 16:
                    text = "You got 3 viewers, and were able to make 1 diamond from the ad break."
                       + "\nHowever, the ad was another hero wars fake ad, and you lost all your viewers.";
                    user.Add(1, 0);
                    user.Increase("earned", 1);
                    break;
                case 17 or 18 or 19:
                    text = "You got 5 viewers! You did an ad break! +2 diamonds!";
                    user.Add(2, 0);
                    user.Increase("earned", 2);
                    break;
                case 20:
                    text = "You got 12 viewers! Some guy donated 3 diamonds!";
                    user.Add(3, 0);
                    user.Increase("earned", 3);
                    break;
            }
        }
        else if (power <= 16)
        {
            switch (roll)
            {
                case 1 or 2 or 3:
                    text = "You forgot to stream, so you gained nothing";
                    break;
                case 4 or 5 or 6:
                    text = "Your stream got 0 viewers because a very popular stream was going on in the moment.";
                    break;
                case 7 or 8:
                    text = "You got 3 viewers, but you weren't able to make any bank from your ad break."
                           + "\nAnd, you lost all your viewers because it was a crypto scam ad.";
                    break;
                case 9 or 10 or 11 or 12 or 13 or 14:
                    text = "You got 3 viewers, and were able to make 1 diamond from the ad break."
                           + "\nHowever, the ad was another hero wars fake ad, and you lost all your viewers.";
                    user.Add(1, 0);
                    user.Increase("earned", 1);
                    break;
                case 15 or 16 or 17:
                    text = "You got 5 viewers! You did an ad break! +2 diamonds!";
                    user.Add(2, 0);
                    user.Increase("earned", 2);
                    break;
                case 18 or 19:
                    text = "You got 12 viewers! Some guy donated 3 diamonds!";
                    user.Add(3, 0);
                    user.Increase("earned", 3);
                    break;
                case 20:
                    text = $"You somehow got 2{emeralds}";
                    user.Add(2, 1);
                    user.Increase("earned", 20);
                    break;
            }
        }
        else if (power <= 32)
        {
             switch (roll)
            {
                case 1 or 2 or 3:
                    text = "You forgot to stream, so you gained nothing";
                    break;
                case 4 or 5:
                    text = "Your stream got 0 viewers because a very popular stream was going on in the moment.";
                    break;
                case 6:
                    text = "You got 3 viewers, but you weren't able to make any bank from your ad break."
                        + "\nAnd, you lost all your viewers because it was a crypto scam ad.";
                    break;
                case 7 or 8 or 9:
                    text = "You got 3 viewers, and were able to make 1 diamond from the ad break."
                       + "\nHowever, the ad was another hero wars fake ad, and you lost all your viewers.";
                    user.Add(1, 0);
                    user.Increase("earned", 1);
                    break;
                case 10 or 11:
                    text = "You got 5 viewers! You did an ad break! +2 diamonds!";
                    user.Add(2, 0);
                    user.Increase("earned", 2);
                    break;
                case 12:
                    text = "You got 12 viewers! Some guy donated 3 diamonds!";
                    user.Add(3, 0);
                    user.Increase("earned", 3);
                    break;
                case 13:
                    text = "You got 12 viewers! two guys donated 4 diamonds each!";
                    user.Add(8, 0);
                    user.Increase("earned", 8);
                    break;
                case 14:
                    text = $"You got 12 viewers! Somebody donated 20{diamonds}";
                    user.Add(20, 0);
                    user.Increase("earned", 20);
                    break;
                case 15 or 16:
                    text = $"You got 25 viewers! You ran an ad break for 2{emeralds}";
                    user.Add(2, 1);
                    user.Increase("earned", 20);
                    break;
                case 17 or 18 or 19:
                    text = $"You got 12 viewers! You ran 3 ad breaks, with one emerald each. Plus, somebody donated 2{diamonds}!"
                        + $"\nIn total:\n> 3{emeralds}\n> 2{diamonds}";
                    user.Add(3, 1);
                    user.Add(2, 0);
                    user.Increase("earned", 32);
                    break;
                case 20:
                    text = $"You didn't live stream, but you got something rare in the game and sold it for 4{emeralds}";
                    user.Add(4, 1);
                    user.Increase("earned", 40);
                    break;
            }
        }
        else if (power <= 64)
        {
            switch (roll)
            {
                case 0:
                    text = "NOTHING!!! NADA!";
                    break;
                case 1:
                    text = $"You got only ONE viewer, so logic implies you get 000{diamonds}";
                    break;
                case 2:
                    text = "**Stream Stats**:" +
                           "\n> 0 viewers" +
                           "\n> 2 rng" +
                           $"\n> 0 ad breaks (0 diamonds each): 0{diamonds}" +
                           $"\n> 0 viewers donated a total of 0{diamonds}";
                    break;
                default:
                    int viewers = (int)power + 2*roll;
                    int adBreaks = 3;
                    int adbBeakMoney = viewers/25;
                    int adBreakValue = 0;
                    int donaters = 0;
                    int donated = 0;
                    for (int i = 0; i < viewers; i++)
                    {
                        if (_rand.Next(20) < 19) continue;
                        donaters += 1;
                        donated += _rand.Next(roll) + 1;
                    }
                    text = "**Stream Stats**:" +
                           $"\n> {viewers} viewers" +
                           $"\n> {roll} rng" +
                           $"\n> {adBreaks} ad breaks ({adbBeakMoney} diamonds each): {adBreaks*adbBeakMoney}{diamonds}" +
                           $"\n> {donaters} viewers donated a total of {donated}{diamonds}";
                    user.Add(adBreaks * adbBeakMoney, adBreakValue);
                    user.Increase("earned", adBreaks * adbBeakMoney * (int)Math.Pow(10, adBreakValue));
                    user.Add(donated, 0);
                    user.Increase("earned", donated);
                    break;
            }
        }
        else if (power <= 128)
        {
            int viewers = (int)power + 3*roll;
            int adBreaks = 4;
            int adBreakMoney = viewers/20;
            int adBreakValue = 0;
            int donaters = 0;
            int donated = 0;
            for (int i = 0; i < viewers; i++)
            {
                if (_rand.Next(25) < 24) continue;
                donaters += 1;
                donated += _rand.Next(roll) + 1;
            }
            text = "**Stream Stats**:" +
                   $"\n> {viewers} viewers" +
                   $"\n> {roll} rng" +
                   $"\n> {adBreaks} ad breaks ({adBreakMoney} diamonds each): {adBreaks*adBreakMoney}{diamonds}" +
                   $"\n> {donaters} viewers donated a total of {donated}{diamonds}";
            user.Add(adBreaks * adBreakMoney, adBreakValue);
            user.Increase("earned", adBreaks * adBreakMoney * (int)Math.Pow(10, adBreakValue));
            user.Add(donated, 0);
            user.Increase("earned", donated);
        }
        else if (power <= 256)
        {
            int viewers = (int)power + 6*roll;
            const int adBreaks = 4;
            int adBeakMoney = viewers/19;
            const int adBreakValue = 0;
            int donaters = 0;
            int donated = 0;
            for (int i = 0; i < viewers; i++)
            {
                if (_rand.Next(30) < 29) continue;
                donaters += 1;
                donated += _rand.Next(roll) + 1;
            }
            text = "**Stream Stats**:" +
                   $"\n> {viewers} viewers" +
                   $"\n> {roll} rng" +
                   $"\n> {adBreaks} ad breaks ({adBeakMoney} diamonds each): {adBreaks*adBeakMoney}{cur[adBreakValue]}" +
                   $"\n> {donaters} viewers donated a total of {donated}{diamonds}";
            user.Add(adBreaks * adBeakMoney, adBreakValue);
            user.Increase("earned", adBreaks * adBeakMoney * (int)Math.Pow(10, adBreakValue));
            user.Add(donated, 0);
            user.Increase("earned", donated);
        }
        else if (power <= 512)
        {
            int viewers = (int)power + 10*roll;
            const int adBreaks = 4;
            int adbBeakMoney = viewers/190;
            const int adBreakValue = 1;
            int donaters = 0;
            int donated = 0;
            for (int i = 0; i < viewers; i++)
            {
                if (_rand.Next(32) < 31) continue;
                donaters += 1;
                donated += _rand.Next(roll) + 1;
            }
            text = "**Stream Stats**:" +
                   $"\n> {viewers} viewers" +
                   $"\n> {roll} rng" +
                   $"\n> {adBreaks} ad breaks ({adbBeakMoney} emeralds each): {adBreaks*adbBeakMoney}{cur[adBreakValue]}" +
                   $"\n> {donaters} viewers donated a total of {donated}{diamonds}";
            user.Add(adBreaks * adbBeakMoney, adBreakValue);
            user.Increase("earned", adBreaks * adbBeakMoney * (int)Math.Pow(10, adBreakValue));
            user.Add(donated, 0);
            user.Increase("earned", donated);
        }
        else if (power <= 1024)
        {
            int viewers = (int)power + 100*roll;
            const int adBreaks = 4;
            int adbBeakMoney = viewers/190;
            const int adBreakValue = 1;
            int donaters = 0;
            int donated = 0;
            for (int i = 0; i < viewers; i++)
            {
                if (_rand.Next(32) < 31) continue;
                donaters += 1;
                donated += _rand.Next(roll) + 1;
            }
            text = "**Stream Stats**:" +
                   $"\n> {viewers} viewers" +
                   $"\n> 2{roll} rng" +
                   $"\n> {adBreaks} ad breaks ({adbBeakMoney} emeralds each): {adBreaks*adbBeakMoney}{cur[adBreakValue]}" +
                   $"\n> {donaters} viewers donated a total of {donated}{diamonds}";
            user.Add(adBreaks * adbBeakMoney, adBreakValue);
            user.Increase("earned", adBreaks * adbBeakMoney * (int)Math.Pow(10, adBreakValue));
            user.Add(donated, 0);
            user.Increase("earned", donated);
        }
        else if (power <= 2048)
        {
            int viewers = (int)power + 150*roll;
            const int adBreaks = 5;
            int adbBeakMoney = viewers/165;
            const int adBreakValue = 1;
            int donaters = 0;
            int donated = 0;
            for (int i = 0; i < viewers; i++)
            {
                if (_rand.Next(32) < 31) continue;
                donaters += 1;
                donated += _rand.Next(roll) + 1;
            }
            text = "**Stream Stats**:" +
                   $"\n> {viewers} viewers" +
                   $"\n> {roll} rng" +
                   $"\n> {adBreaks} ad breaks ({adbBeakMoney} sapphires each): {adBreaks*adbBeakMoney}{cur[adBreakValue]}" +
                   $"\n> {donaters} viewers donated a total of {donated}{diamonds}";
            user.Add(adBreaks * adbBeakMoney, adBreakValue);
            user.Increase("earned", adBreaks * adbBeakMoney * (int)Math.Pow(10, adBreakValue));
            user.Add(donated, 0);
            user.Increase("earned", donated);
        }
        else if (power <= 4096)
        {
            int viewers = (int)power + 250*roll;
            const int adBreaks = 5;
            int adbBeakMoney = viewers/1200;
            const int adBreakValue = 2;
            int donaters = 0;
            int donated = 0;
            for (int i = 0; i < viewers; i++)
            {
                if (_rand.Next(35) < 34) continue;
                donaters += 1;
                donated += _rand.Next(roll) + 1;
            }
            text = "**Stream Stats**:" +
                   $"\n> {viewers} viewers" +
                   $"\n> {roll} rng" +
                   $"\n> {adBreaks} ad breaks ({adbBeakMoney} sapphires each): {adBreaks*adbBeakMoney}{cur[adBreakValue]}" +
                   $"\n> {donaters} viewers donated a total of {donated}{diamonds}";
            user.Add(adBreaks * adbBeakMoney, adBreakValue);
            user.Increase("earned", adBreaks * adbBeakMoney * (int)Math.Pow(10, adBreakValue));
            user.Add(donated, 0);
            user.Increase("earned", donated);
        }
        else
        {
            int viewers = (int)power + 350*roll;
            const int adBreaks = 5;
            int adbBeakMoney = viewers/1000;
            const int adBreakValue = 2;
            int donaters = 0;
            int donated = 0;
            for (int i = 0; i < viewers; i++)
            {
                if (_rand.Next(35) < 34) continue;
                donaters += 1;
                donated += _rand.Next(roll) + 1;
            }
            text = "**Stream Stats**:" +
                   $"\n> {viewers} viewers" +
                   $"\n> {roll} rng" +
                   $"\n> {adBreaks} ad breaks ({adbBeakMoney} sapphires each): {adBreaks*adbBeakMoney}{cur[adBreakValue]}" +
                   $"\n> {donaters} viewers donated a total of {donated}{diamonds}";
            user.Add(adBreaks * adbBeakMoney, adBreakValue);
            user.Increase("earned", adBreaks * adbBeakMoney * (int)Math.Pow(10, adBreakValue));
            user.Add(donated, 0);
            user.Increase("earned", donated);
        }
        EmbedBuilder embay = new EmbedBuilder()
            .WithTitle(header)
            .WithDescription(text)
            .WithFooter(footer)
            .WithColor((uint) user.GetSetting("uiColor", 3287295));
        await command.RespondAsync(embed: embay.Build());
        await user.Save();
    }
    private async Task Mine(SocketSlashCommand command)
    {
        Tuple<bool, string, Embed, MessageComponent, string> result = await MineRaw(command.User.Id, Tools.ShowEmojis(command, Settings.BotID(), _client));
        if (!result.Item1)
        {
            await command.RespondAsync(result.Item2, embed: result.Item3);
            return;
        }
        await command.RespondAsync(result.Item2, embed: result.Item3, components: result.Item4);
    }
    private async Task Craft(SocketSlashCommand command)
    {
        Tuple<string, Embed, MessageComponent> furnaces = await FurnacesRaw(command.User.Id, Tools.ShowEmojis(command, Settings.BotID(), _client));
        await command.RespondAsync(furnaces.Item1, embed: furnaces.Item2, components: furnaces.Item3);
    }
    private async Task Shop(SocketSlashCommand command)
    {
        Task<Tuple<string, Embed, MessageComponent>> shopDetailsGetter = ShopRaw(command.User.Id, Tools.ShowEmojis(command, Settings.BotID(), _client));
        Tuple<string, Embed, MessageComponent> shop = await shopDetailsGetter;
        _ = command.RespondAsync(shop.Item1, embed: shop.Item2, components: shop.Item3);
    }
    private async Task Spin(SocketSlashCommand command)
    {
        int spinID = (int)(long)command.Data.Options.First().Value;
        int amount = command.Data.Options.Count < 2 ? 1: (int)(long)command.Data.Options.ToArray()[1].Value;
        User user = await GetUser(command.User.Id);
        EmbedBuilder embay = new EmbedBuilder().WithTitle("Spin Results:");
        if (user.Inventory[spinID] < amount)
        {
            await command.RespondAsync("You don't have the required item(s) for this.", ephemeral: true);
            return;
        }
        int coinsGained = 0;
        int[] itemsGained = new int[user.Inventory.Count];
        for (int i = 0; i < amount; i++)
        {
            int roll = _rand.Next(100) + 1; //From 1 to 100
            switch (spinID)
            {
                case < 5:
                    user.GainItem(spinID, -1);
                    switch (roll)
                    {
                        case <= 13:
                            //Gain 3 of currency (13%)
                            coinsGained += 3;
                            break;
                        case <= 43:
                            //Gain 4 of currency (30%)
                            coinsGained += 4;
                            break;
                        case <= 66:
                            //Gain 6 of currency (23%)
                            coinsGained += 6;
                            break;
                        case <= 74:
                            //Gain 10 of currency (8%)
                            coinsGained += 10;
                            break;
                        case <= 88:
                            //Gain a charm (1 gem if ruby, 2 gems if amber) (14%)
                            itemsGained[Tools.GetCharm(_itemLists, spinID, 7, _rand)]++;
                            if (spinID >= 3)
                            {
                                coinsGained += spinID - 2;
                            }
                            break;
                        case <= 92:
                            //Gain 11^rarity tickets (4%)
                            itemsGained[21] += (int)Math.Pow(11, spinID);
                            break;
                        case <= 95:
                            //Gain 65 of currency (3%)
                            coinsGained += 65;
                            break;
                        case <= 97:
                            //Gain 5 x current coin (2%)
                            itemsGained[spinID] += 5;
                            break;
                        case <= 99:
                            //Gain 1x key (2%)
                            itemsGained[spinID + 16] += 1;
                            break;
                        case >= 100:
                            //Gain next level coin (1%)
                            //Amber has separate logic
                            if (spinID == 4)
                            {
                                itemsGained[4] += 5;
                                itemsGained[3] += 45;
                                itemsGained[2] += 400;
                                itemsGained[1] += 3750;
                                itemsGained[0] += 35000;
                                break;
                            }
                            itemsGained[spinID + 1] += 1;
                            break;
                    }
                    break;
            }
            
        }
        user.Add(coinsGained, spinID);
        user.Increase("earned", coinsGained * (int)Math.Pow(10, spinID));
        string rewards = coinsGained > 0 ? $"You earned:\n > {coinsGained}{_currency[spinID]}": "You earned:";
        for (int i = 0; i < itemsGained.Length; i++)
        {
            if (itemsGained[i] <= 0) continue;
            user.GainItem(i, itemsGained[i]);
            rewards += $"\n > {itemsGained[i]}{_items[i].Emoji}";
        }
        embay.WithDescription(rewards);
        await command.RespondAsync(embed: embay.Build());
        await user.Save();
    }
    private async Task Notifications(SocketSlashCommand command)
    {
        CachedUser user = await GetUser(command.User.Id);
        user.NotificationsID++;
        int notificationsAmount = 0;
        EmbedBuilder embay = new EmbedBuilder().WithTitle("Notifications").WithColor(new Color((uint) user.User.GetSetting("uiColor", 3287295)));
        Dictionary<string, List<CachedUser.Notification>> notificationsDict = new();
        user.ConsolidateNotifications();
        foreach (CachedUser.Notification notification in user.Notifications)
        {
            if (!notificationsDict.ContainsKey(notification.Source)) notificationsDict[notification.Source] = [notification];
            else notificationsDict[notification.Source].Add(notification);
        }
        List<CachedUser.Notification> otherNotifications= new();
        foreach (KeyValuePair<string, List<CachedUser.Notification>> kvp in notificationsDict)
        {
            if (kvp.Value.Count <= 0) continue;
            if (kvp.Value.Count == 1)
            {
                otherNotifications.Add(kvp.Value[0]);
                continue;
            }
            string curField = "";
            foreach (CachedUser.Notification notif in kvp.Value)
            {
                if (curField != string.Empty) curField += "\n";
                string reward = (notif.Reward >= 0) switch {true => _items[notif.Reward].Emoji, false => _currency[(notif.Reward * -1) - 1]};
                curField += $" > {notif.Amount}{reward}";
                notificationsAmount++;
            }
            embay.AddField($"You {kvp.Value[0].Source}:", curField);
        }
        if (otherNotifications.Count > 0)
        {
            string curField = "";
            foreach (CachedUser.Notification notif in otherNotifications)
            {
                if (curField != string.Empty) curField += "\n";
                string reward = (notif.Reward >= 0) switch {true => _items[notif.Reward].Emoji, false => _currency[(notif.Reward * -1) - 1]};
                curField += $" > You {notif.Source} {notif.Amount}{reward}";
                notificationsAmount++;
            }
            embay.AddField("Other Notifications", curField);
        }
        string text = "View all your notifications";
        Embed embed = embay.Build();
        await command.RespondAsync(text, embed: embed, 
            components: new ComponentBuilder()
                .WithButton("Delete Notifications",
                    $"notifications-clear|{user.NotificationsID}|{notificationsAmount}",
                    ButtonStyle.Secondary,
                    Emoji.Parse(":x:"))
                .Build());
    }
    private async Task Key(SocketSlashCommand command)
    {
        string key = command.Data.Options.First().Name;
        User user = await GetUser(command.User.Id);
        switch (key)
        {
            case "diamond":
                int amountD = user.Inventory[16];
                if (amountD <= 0)
                {
                    await command.RespondAsync("You don't have a diamond key", ephemeral: true);
                    return;
                }
                IEmote emojiD = Tools.ParseEmote(_items[16].Emoji);
                await command.RespondAsync($"<@{command.User.Id}>, would you how many diamond keys would you like to use?\n > Diamond keys give you 2 charms, at rates `5|0` and `4|0`",
                    components: new ComponentBuilder()
                        .WithButton("x1", "key-diamond|1", ButtonStyle.Primary, emojiD)
                        .WithButton("x5", "key-diamond|5", ButtonStyle.Primary, emojiD, disabled: amountD < 5)
                        .WithButton("x10", "key-diamond|10", ButtonStyle.Primary, emojiD, disabled: amountD < 10)
                        .WithButton("x40", "key-diamond|40", ButtonStyle.Primary, emojiD, disabled: amountD < 40)
                        .WithButton($"x{amountD}", $"key-diamond|{amountD}", ButtonStyle.Primary, emojiD)
                        .WithButton("Cancel", "basic-cancel|auto_delete", ButtonStyle.Secondary)
                        .Build(),
                    ephemeral:true);
                break;
            case "emerald":
                int amountE = user.Inventory[17];
                if (amountE <= 0)
                {
                    await command.RespondAsync("You don't have an emerald key", ephemeral: true);
                    return;
                }
                IEmote emojiE = Tools.ParseEmote(_items[17].Emoji);
                await command.RespondAsync($"<@{command.User.Id}>, would you how many emerald keys would you like to use?\n > Emerald keys give you 2 charms, at rates `4|0`, `4|0`, `4|0`, `3|0`, `3|0`, `3|1`, `3|1`, and `4|2`",
                    components: new ComponentBuilder()
                        .WithButton("x1", "key-emerald|1", ButtonStyle.Primary, emojiE)
                        .WithButton("x5", "key-emerald|5", ButtonStyle.Primary, emojiE, disabled: amountE < 5)
                        .WithButton("x10", "key-emerald|10", ButtonStyle.Primary, emojiE, disabled: amountE < 10)
                        .WithButton("x40", "key-emerald|40", ButtonStyle.Primary, emojiE, disabled: amountE < 40)
                        .WithButton($"x{amountE}", $"key-emerald|{amountE}", ButtonStyle.Primary, emojiE)
                        .WithButton("Cancel", "basic-cancel|auto_delete", ButtonStyle.Secondary)
                        .Build(),
                    ephemeral:true);
                break;
            case "sapphire":
                int amountS = user.Inventory[18];
                if (amountS <= 0)
                {
                    await command.RespondAsync("You don't have a sapphire key", ephemeral: true);
                    return;
                }
                IEmote emojiS = Tools.ParseEmote(_items[18].Emoji);
                await command.RespondAsync($"<@{command.User.Id}>, would you how many sapphire keys would you like to use?\n > Sapphire keys give you 20 charms, at rates `5|0`, `4|0`, `4|0`, `4|0`, `3|0`, `3|0`, `3|0`, `3|0`, `4|1`, `4|1`, `3|1`, `3|1`, `3|1`, `3|1`, `5|2`, `4|2`, `4|2`, `3|2`, `6|3`, and `5|3`",
                    components: new ComponentBuilder()
                        .WithButton("x1", "key-sapphire|1", ButtonStyle.Primary, emojiS)
                        .WithButton("x5", "key-sapphire|5", ButtonStyle.Primary, emojiS, disabled: amountS < 5)
                        .WithButton("x10", "key-sapphire|10", ButtonStyle.Primary, emojiS, disabled: amountS < 10)
                        .WithButton("x40", "key-sapphire|40", ButtonStyle.Primary, emojiS, disabled: amountS < 40)
                        .WithButton($"x{amountS}", $"key-sapphire|{amountS}", ButtonStyle.Primary, emojiS)
                        .WithButton("Cancel", "basic-cancel|auto_delete", ButtonStyle.Secondary)
                        .Build(),
                    ephemeral:true);
                break;
            case "ruby":
                if (user.Inventory[19] <= 0)
                {
                    await command.RespondAsync("You don't have a ruby key.", ephemeral: true);
                    return;
                }
                EmbedBuilder embayR = new EmbedBuilder()
                    .WithTitle("Ruby key")
                    .WithDescription("Spend your ruby key on a lootbox.")
                    .AddField("Key Box", $" > 8{_items[18].Emoji}\n > 64{_items[17].Emoji}\n > 512{_items[16].Emoji}")
                    .AddField("Coin Box", $" > 2{_items[3].Emoji}\n > 20{_items[2].Emoji}\n > 200{_items[1].Emoji}\n > 2,000{_items[0].Emoji}")
                    .AddField("Money Box", $" > 1{_currency[4]}\n > 11{_currency[3]}\n > 121{_currency[2]}\n > 1,331{_currency[1]}\n > 14,641{_currency[0]}");
                IEmote emojiR = Tools.ParseEmote(_items[19].Emoji);
                ComponentBuilder buttonsR = new ComponentBuilder()
                    .WithButton("Buy Key Box", "key-ruby|key", emote:emojiR)
                    .WithButton("Buy Coin Box", "key-ruby|coin", emote:emojiR)
                    .WithButton("Buy Money Box", "key-ruby|money", emote:emojiR)
                    .WithButton("Cancel", "basic-cancel|auto_delete", ButtonStyle.Secondary);
                await command.RespondAsync(embed:embayR.Build(), components:buttonsR.Build(), ephemeral: true);
                break;
            case "amber":
                if (user.Inventory[20] <= 0)
                {
                    await command.RespondAsync("You don't have an amber key.", ephemeral: true);
                    return;
                }
                EmbedBuilder embayA = new EmbedBuilder()
                    .WithTitle("Amber key")
                    .WithDescription("Spend your amber key on a rare, amber-key exclusive charm.")
                    .AddField($"{_items[48].Emoji} ({_items[48].Name})", $"Massive cooldown reduction on all grinding commands.")
                    .AddField($"{_items[49].Emoji} ({_items[49].Name})", $"The combined effects of most legendary grinding charms (stacks with said charms).\n > Use `/item 49` to get more info")
                    .AddField($"{_items[50].Emoji} ({_items[50].Name})", $"Automatically refresh many commands (and of the ones already available, quicker auto-refresh). \n > Also lesser cooldown reduction.")
                    .AddField($"{_items[51].Emoji} ({_items[51].Name})", $"Fair bank trades\n > And a medium cooldown reduction")
                    ;
                ComponentBuilder buttonsA = new ComponentBuilder()
                    .WithButton($"Buy {_items[48].Name}", "key-amber|bolt", emote:Emoji.Parse(_items[48].Emoji))
                    .WithButton($"Buy {_items[49].Name}", "key-amber|star", emote:Emoji.Parse(_items[49].Emoji))
                    .WithButton($"Buy {_items[50].Name}", "key-amber|refresh", emote:Emoji.Parse(_items[50].Emoji))
                    .WithButton($"Buy {_items[51].Name}", "key-amber|coin", emote:Emoji.Parse(_items[51].Emoji))
                    .WithButton("Cancel", "basic-cancel|auto_delete", ButtonStyle.Secondary);
                await command.RespondAsync(embed:embayA.Build(), components:buttonsA.Build(), ephemeral: true);
                break;
            default:
                await command.RespondAsync($"Key {key} not found.", ephemeral: true);
                break;
        }
    }
    private async Task CharmsToCoins(SocketSlashCommand command)
    {
        User user = await GetUser(command.User.Id);
        
        List<Tuple<int, int>> diamondConvert = new List<Tuple<int, int>>();
        int diamondTotal = 0;
        foreach (int item in _itemLists["CommonCharms"].Where(item => user.Inventory[item] >= 2))
        {
            diamondConvert.Add(new Tuple<int, int>(item, user.Inventory[item] -1));
            diamondTotal += user.Inventory[item] - 1;
        }
        List<Tuple<int, int>> emeraldConvert = new List<Tuple<int, int>>();
        int emeraldTotal = 0;
        foreach (int item in _itemLists["UncommonCharms"].Where(item => user.Inventory[item] >= 2))
        {
            emeraldConvert.Add(new Tuple<int, int>(item, user.Inventory[item] -1));
            emeraldTotal += user.Inventory[item] - 1;
        }
        List<Tuple<int, int>> sapphireConvert = new List<Tuple<int, int>>();
        int sapphireTotal = 0;
        foreach (int item in _itemLists["RareCharms"].Where(item => user.Inventory[item] >= 2))
        {
            sapphireConvert.Add(new Tuple<int, int>(item, user.Inventory[item] -1));
            sapphireTotal += user.Inventory[item] - 1;
        }
        List<Tuple<int, int>> rubyConvert = new List<Tuple<int, int>>();
        int rubyTotal = 0;
        foreach (int item in _itemLists["EpicCharms"].Where(item => user.Inventory[item] >= 2))
        {
            rubyConvert.Add(new Tuple<int, int>(item, user.Inventory[item] -1));
            rubyTotal += user.Inventory[item] - 1;
        }
        List<Tuple<int, int>> amberConvert = new List<Tuple<int, int>>();
        int amberTotal = 0;
        foreach (int item in _itemLists["LegendaryCharms"].Where(item => user.Inventory[item] >= 2))
        {
            amberConvert.Add(new Tuple<int, int>(item, user.Inventory[item] -1));
            amberTotal += user.Inventory[item] - 1;
        }
        
        EmbedBuilder embay = new EmbedBuilder()
            .WithTitle("Exchange")
            .WithDescription("Use the buttons below to exchange duplicate charms for coins.\n 1 charm + 1 gem of same rarity = 2 coins of same rarity");
        ActionRowBuilder mainRow = new ActionRowBuilder();
        
        string diamondExchange = $"Gain {diamondTotal*2}{_items[0].Emoji} for \n > {diamondTotal}{_currency[0]}";
        foreach (Tuple<int, int> pair in diamondConvert)
        {
            diamondExchange += $"\n > {pair.Item2}{_items[pair.Item1].Emoji}";
        }
        string emeraldExchange = $"Gain {emeraldTotal*2}{_items[1].Emoji} for \n > {emeraldTotal}{_currency[1]}";
        foreach (Tuple<int, int> pair in emeraldConvert)
        {
            emeraldExchange += $"\n > {pair.Item2}{_items[pair.Item1].Emoji}";
        }
        string sapphireExchange = $"Gain {sapphireTotal*2}{_items[2].Emoji} for \n > {sapphireTotal}{_currency[2]}";
        foreach (Tuple<int, int> pair in sapphireConvert)
        {
            sapphireExchange += $"\n > {pair.Item2}{_items[pair.Item1].Emoji}";
        }
        string rubyExchange = $"Gain {rubyTotal*2}{_items[3].Emoji} for \n > {rubyTotal}{_currency[3]}";
        foreach (Tuple<int, int> pair in rubyConvert)
        {
            rubyExchange += $"\n > {pair.Item2}{_items[pair.Item1].Emoji}";
        }
        string amberExchange = $"Gain {amberTotal*2}{_items[4].Emoji} for \n > {amberTotal}{_currency[4]}";
        foreach (Tuple<int, int> pair in amberConvert)
        {
            amberExchange += $"\n > {pair.Item2}{_items[pair.Item1].Emoji}";
        }

        if (diamondTotal > 0)
        {
            embay.AddField("Diamond Exchange", diamondExchange);
            mainRow.WithButton($"x{diamondTotal*2}", $"exchange-charm|0", ButtonStyle.Primary, Tools.ParseEmote(_items[0].Emoji));
        }
        if (emeraldTotal > 0)
        {
            embay.AddField("Emerald Exchange", emeraldExchange);
            mainRow.WithButton($"x{emeraldTotal*2}", $"exchange-charm|1", ButtonStyle.Primary, Tools.ParseEmote(_items[1].Emoji));
        }
        if (sapphireTotal > 0)
        {
            embay.AddField("Sapphire Exchange", sapphireExchange);
            mainRow.WithButton($"x{sapphireTotal*2}", $"exchange-charm|2", ButtonStyle.Primary, Tools.ParseEmote(_items[2].Emoji));
        }
        if (rubyTotal > 0)
        {
            embay.AddField("Ruby Exchange", rubyExchange);
            mainRow.WithButton($"x{rubyTotal*2}", $"exchange-charm|3", ButtonStyle.Primary, Tools.ParseEmote(_items[3].Emoji));
        }
        if (amberTotal > 0)
        {
            embay.AddField("Amber Exchange", amberExchange);
            mainRow.WithButton($"x{amberTotal*2}", $"exchange-charm|4", ButtonStyle.Primary, Tools.ParseEmote(_items[4].Emoji));
        }

        ComponentBuilder components = new ComponentBuilder()
            .AddRow(mainRow)
            .AddRow(new ActionRowBuilder().WithButton("cancel", "basic-cancel", ButtonStyle.Secondary));
        await command.RespondAsync(embed: embay.Build(), components:components.Build(), ephemeral:true);
        while (true)
        {
            await Task.Delay(2500);
            bool changed = false;
            foreach (Tuple<int, int> pair in diamondConvert)
            {
                if (user.Inventory[pair.Item1] == pair.Item2 + 1) continue;
                changed = true;
                break;
            }
            foreach (Tuple<int, int> pair in emeraldConvert)
            {
                if (user.Inventory[pair.Item1] == pair.Item2 + 1) continue;
                changed = true;
                break;
            }
            foreach (Tuple<int, int> pair in sapphireConvert)
            {
                if (user.Inventory[pair.Item1] == pair.Item2 + 1) continue;
                changed = true;
                break;
            }
            foreach (Tuple<int, int> pair in rubyConvert)
            {
                if (user.Inventory[pair.Item1] == pair.Item2 + 1) continue;
                changed = true;
                break;
            }
            foreach (Tuple<int, int> pair in amberConvert)
            {
                if (user.Inventory[pair.Item1] == pair.Item2 + 1) continue;
                changed = true;
                break;
            }
            if (diamondConvert.Count == 0 && emeraldConvert.Count == 0 &&
                sapphireConvert.Count == 0 && rubyConvert.Count == 0 && amberConvert.Count == 0)
            {
                await Task.Delay(30000);
                break;
            }
            if (changed) break;
        }
        await (await command.GetOriginalResponseAsync()).DeleteAsync();
    }
    private async Task SettingsCommand(SocketSlashCommand command)
    {
        string group = command.Data.Options.First().Name;
        User user = await GetUser(command.User.Id);
        if (group == "view")
        {
            string viewText = "";
            foreach (KeyValuePair<string, ulong> viewSetting in user.Settings)
            {
                if (viewText != "") viewText += "\n";
                viewText += $"> {viewSetting.Key}: {viewSetting.Value}";
            }
            await command.RespondAsync(
                embed: new EmbedBuilder().WithTitle("Your settings").WithDescription(viewText).Build(),
                ephemeral: true);
            return;
        }
        string subGroup = command.Data.Options.First().Options.First().Name;
        IReadOnlyCollection<SocketSlashCommandDataOption>? dat = command.Data.Options.First().Options.First().Options;
        string setting = "error_error";
        ulong value = 0;
        switch (group)
        {
            case "theme":
                switch (subGroup)
                {
                    case "bank_left":
                        setting = "bankLeftStyle";
                        value = (ulong)(long)dat.First().Value;
                        break;
                    case "bank_right":
                        setting = "bankRightStyle";
                        value = (ulong)(long)dat.First().Value;
                        break;
                    case "bank_show_red":
                        setting = "bankShowRed";
                        value = ((bool)dat.First().Value) switch { true => 1, false => 0 };
                        break;
                    case "beg_randomize_color":
                        setting = "begRandom";
                        value = ((bool)dat.First().Value) switch { true => 1, false => 0 };
                        break;
                    case "beg_color":
                        setting = "begColor";
                        SocketSlashCommandDataOption[] begRGB = dat.ToArray();
                        value = new Color((int)(long)begRGB[0].Value, (int)(long)begRGB[1].Value, (int)(long)begRGB[2].Value).RawValue;
                        break;
                    case "magik_randomize_color":
                        setting = "magikRandom";
                        value = ((bool)dat.First().Value) switch { true => 1, false => 0 };
                        break;
                    case "magik_color":
                        setting = "magikColor";
                        SocketSlashCommandDataOption[] magikRGB = dat.ToArray();
                        value = new Color((int)(long)magikRGB[0].Value, (int)(long)magikRGB[1].Value, (int)(long)magikRGB[2].Value)
                            .RawValue;
                        break;
                    case "color":
                        setting = "uiColor";
                        SocketSlashCommandDataOption[] uiRGB = dat.ToArray();
                        value = new Color((int)((long)uiRGB[0].Value), (int)((long)uiRGB[1].Value), (int)((long)uiRGB[2].Value)).RawValue;
                        break;
                    case "progress_bar_width":
                        setting = "progressBar";
                        value = (ulong)(long)dat.First().Value;
                        break;
                    default:
                        await command.RespondAsync($"Setting {group} - {subGroup} not found", ephemeral: true);
                        return;
                }
                break;
            case "main":
                switch (subGroup)
                {
                    case "auto_delete":
                        setting = "delayBeforeDelete";
                        value = (ulong)(long)dat.First().Value;
                        break;
                    default:
                        await command.RespondAsync($"Setting {group} - {subGroup} not found", ephemeral: true);
                        return;
                }
                break;
            default:
                await command.RespondAsync($"Setting group {group} not found", ephemeral: true);
                return;
        }
        user.SetSetting(setting, value);
        await user.Save();
        await command.RespondAsync($"Set {setting} to {value}!");
    }
    private async Task EventCommand(SocketSlashCommand command)
    {
        if (command.GuildId is null)
        {
            await command.RespondAsync($"You need to run this in a server.\n||This message will auto-delete in <t:{Tools.CurrentTime() + 15}:R>||", ephemeral:true);
            await Task.Delay(14950);
            await command.DeleteOriginalResponseAsync();
            return;
        }
        Server server = await GetServer((ulong)command.GuildId);
        if (!server.OngoingEvent)
        {
            EmbedBuilder embay = new EmbedBuilder()
                .WithTitle("Start an event!")
                .WithDescription("You can start a server-wide event by using specific items and then paying 1 stone coin per person in the server")
                .AddField("Alternate Mine World event", "Visit an alternate mine world with rewards to claim quickly. Look out for the rare treasure!");
            ComponentBuilder components = new ComponentBuilder()
                .WithButton("Portal", "serverEvent-trigger|mine", ButtonStyle.Primary, Tools.ParseEmote(_items[53].Emoji));
            await command.RespondAsync("There is no ongoing event", components:components.Build());
        }
        else if (server.OngoingPortalEvent is not null)
        {
            (bool hidden, string text, Embed? embay, MessageComponent? buttons) = await EventMineRaw(command.User.Id, server.ID);
            await command.RespondAsync(text, embed:embay, components:buttons, ephemeral:hidden);
        }
        else
        {
            await command.RespondAsync("Your server events are broken. Please contact a727");
        }
        await server.Save();
    }
}
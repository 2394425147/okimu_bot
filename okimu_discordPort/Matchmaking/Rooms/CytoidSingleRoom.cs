using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Interactivity.Extensions;
using okimu_discordPort.Apis.CytoidApi.LevelInfo;
using okimu_discordPort.Apis.CytoidApi.ProfileDetails;
using okimu_discordPort.Communication;
using okimu_discordPort.Helpers;
using okimu_discordPort.Properties;
using okimu_discordPort.Structures;

namespace okimu_discordPort.Matchmaking.Rooms
{
    public class CytoidSingleRoom : BaseRoom
    {
        private readonly LevelInfo _level;

        private readonly int _diffIndex;
        private readonly string _displayDifficulty;

        public static async Task CreateAsync(MessageCreateEventArgs e)
        {
            await e.Message.RespondAsync("Please give me a level id to start a match with!");
            var result = await e.Message.GetNextMessageAsync();

            var song = await CytoidClient.GetLevelAsync(result.Result.Content);

            if (song.StatusCode.HasValue)
            {
                await e.Channel.SendMessageAsync($"{DiscordEmoji.FromUnicode("🚫")} Invalid level id!");
                return;
            }

            var diffBuilder = new DiscordMessageBuilder()
                .WithContent($"{song.Title}: Please select a difficulty!");

            var diffSelectionDict = new Dictionary<string, int>();

            for (var index = 0; index < song.Charts.Count; index++)
            {
                var chart = song.Charts[index];
                diffSelectionDict.Add($"match_difficulty_{chart.Type}_{chart.Difficulty}", index);

                diffBuilder.AddComponents(
                    new DiscordButtonComponent(ButtonStyle.Secondary,
                        $"match_difficulty_{chart.Type}_{chart.Difficulty}",
                        $"{chart.GetName()} {chart.Difficulty}"));
            }

            var diffChoiceMessage = await diffBuilder.SendAsync(e.Channel);

            var diffSelection = await diffChoiceMessage.WaitForButtonAsync(e.Author, CancellationToken.None);

            if (diffSelection.TimedOut)
                return;

            await diffSelection.Result.Interaction.CreateResponseAsync(
                InteractionResponseType.UpdateMessage,
                new DiscordInteractionResponseBuilder(
                    new DiscordMessageBuilder().WithContent(
                        $"Chosen difficulty {song.Charts[diffSelectionDict[diffSelection.Result.Id]].GetName()}!")));

            await e.Message.RespondAsync("Lastly, give me a room name!");
            var roomTitle = await e.Message.GetNextMessageAsync();

            var room = new CytoidSingleRoom(roomTitle.Result.Content, e.Author, e.Channel,
                song,
                diffSelectionDict[diffSelection.Result.Id]);

            MultiHost.Lobby.Add(room);

            await e.Message.RespondAsync(
                $"Room created! Its room id is `{room.UniqueId}`. Ask some friends to join your play!");
        }

        public override async Task Start()
        {
            Started = true;
            var secondsWaitTime = (int)System.Math.Floor(_level.Duration + 120);

            await MultiHost.MessageAll(AnnouncementChannel, Channels,
                new DiscordMessageBuilder().WithEmbed(new DiscordEmbedBuilder().WithTitle(RoomName)
                        .WithDescription("Match has started!")
                        .AddField("Title", _level.Title)
                        .AddField("Difficulty", $"{_displayDifficulty} {_level.Charts[_diffIndex].Difficulty}")
                        .WithImageUrl(_level.Cover.Thumbnail)
                        .WithFooter($"Please finish this in {secondsWaitTime / 60} minutes!")
                        .WithColor(DiscordColor.MidnightBlue))
                    .AddComponents(new DiscordLinkButtonComponent("https://cytoid.io/levels/" + _level.Uid,
                        $"Download {_level.Uid}")));

            await Task.Delay(secondsWaitTime * 1000);

            await MultiHost.MessageAll(AnnouncementChannel, Channels,
                new DiscordMessageBuilder().WithContent($"{RoomName} - Time's up!"));

            var ranking = new Dictionary<DiscordUser, RecentRecord>();
            foreach (var d in Players)
            {
                var user = await CytoidClient.GetProfileDetailsAsync(Configuration.GetUser(d).CytoidId);
                var a = user.RecentRecords[0];

                if (a.Date.TimeUntilNow().TotalMinutes < secondsWaitTime && a.Chart.Level.Uid == _level.Uid &&
                    a.Chart.Type == _level.Charts[_diffIndex].Type)
                {
                    ranking.Add(d, a);
                }

                await Task.Delay(500);
            }

            var ordered = ranking.OrderByDescending(r => r.Value.Score).ToList();

            var verified = new DiscordEmbedBuilder().WithTitle($"{RoomName} ({UniqueId}) - Results")
                .WithDescription(
                    "Players who aren't in this list are automatically disqualified due to incorrect score.")
                .WithFooter("This room will now be disposed.").WithColor(DiscordColor.SpringGreen);

            for (var i = 0; i < ordered.Count(); i++)
                verified.AddField($"**#{i + 1}** {ordered.ElementAt(i).Key.Username}",
                    ordered.ElementAt(i).Value.Score +
                    $" ({float.Parse(ordered.ElementAt(i).Value.Accuracy) * 100:0.000000}%)");

            await MultiHost.MessageAll(AnnouncementChannel, Channels, new DiscordMessageBuilder().WithEmbed(verified));

            await Dispose();
        }

        public override async Task SendInformation(MessageCreateEventArgs e)
        {
            var embed = new DiscordEmbedBuilder()
                .WithAuthor($"Artist: {_level.Metadata.Artist.Name} | Charter: {_level.Metadata.Charter.Name}")
                .WithTitle(_level.Title)
                .AddField("Difficulty", $"{_displayDifficulty} {_level.Charts[_diffIndex].Difficulty}")
                .WithImageUrl(_level.Cover.Thumbnail)
                .WithFooter(
                    $"Players: {Players.Count} ({MinPlayers})/{MaxPlayers}");

            var components = new DiscordMessageBuilder()
                .WithEmbed(embed)
                .AddComponents(new DiscordLinkButtonComponent(
                    "https://cytoid.io/levels/" + _level.Uid,
                    $"Download {_level.Uid}"));

            await e.Message.RespondAsync(components);
        }

        public override async Task TryJoin(MessageCreateEventArgs e)
        {
            if (Players.Count >= MaxPlayers)
            {
                await e.Channel.SendMessageAsync(Resources.ERR_MATCH_ROOM_FULL);
                return;
            }

            if (Configuration.GetUser(e.Author).CytoidId.IsNullOrEmpty())
            {
                await e.Channel.SendMessageAsync(Resources.ERR_CYTOID_NO_ID);
                return;
            }

            if (MultiHost.Lobby.Any(r => r.Players.Contains(e.Author)))
            {
                await e.Channel.SendMessageAsync(Resources.ERR_MATCH_ALREADY_IN_ROOM);
                return;
            }

            Players.Add(e.Author);
            if (!Channels.Contains(e.Channel))
                Channels.Add(e.Channel);
            await e.Channel.SendMessageAsync("Successfully joined room!");

            await base.TryJoin(e);
        }

        public override async Task RequestConfigure(MessageCreateEventArgs e)
        {
            var builder = new DiscordMessageBuilder()
                .WithContent("Please select an option you'd like to configure:")
                .AddComponents(
                    new DiscordButtonComponent(ButtonStyle.Secondary, "match_config_name",
                        "Room name"),
                    new DiscordButtonComponent(ButtonStyle.Secondary, "match_config_desc",
                        "Description"),
                    new DiscordButtonComponent(ButtonStyle.Secondary, "match_config_max",
                        "Max players"),
                    new DiscordButtonComponent(ButtonStyle.Secondary, "match_config_min",
                        "Min players")
                );

            var buttonMessage = await builder.SendAsync(e.Channel);

            var selection = await buttonMessage.WaitForButtonAsync(Host, CancellationToken.None);

            if (selection.TimedOut)
                return;

            switch (selection.Result.Id)
            {
                case "match_config_name":
                {
                    await selection.Result.Interaction.CreateResponseAsync(
                        InteractionResponseType.UpdateMessage,
                        new DiscordInteractionResponseBuilder(
                            new DiscordMessageBuilder().WithContent(
                                "Please enter a new name for this room:")));

                    var result = await e.Message.GetNextMessageAsync();

                    if (result.TimedOut)
                        return;

                    RoomName = result.Result.Content;
                    await e.Message.RespondAsync(
                        $"Successfully changed room name to {RoomName}!");
                }
                    break;
                case "match_config_desc":
                {
                    await selection.Result.Interaction.CreateResponseAsync(
                        InteractionResponseType.UpdateMessage,
                        new DiscordInteractionResponseBuilder(
                            new DiscordMessageBuilder().WithContent(
                                "Please enter a new description for this room:")));

                    var result = await e.Message.GetNextMessageAsync();

                    if (result.TimedOut)
                        return;

                    RoomDescription = result.Result.Content;
                    await e.Message.RespondAsync(
                        $"Successfully changed room description to {RoomDescription}!");
                }
                    break;
                case "match_config_max":
                {
                    await selection.Result.Interaction.CreateResponseAsync(
                        InteractionResponseType.UpdateMessage,
                        new DiscordInteractionResponseBuilder(
                            new DiscordMessageBuilder().WithContent(
                                "Please enter a new maximum threshold for this room:")));

                    var result = await e.Message.GetNextMessageAsync();

                    if (result.TimedOut)
                        return;

                    if (!int.TryParse(result.Result.Content, out var newMax))
                    {
                        await e.Message.RespondAsync(
                            $"{result.Result.Content} is not a valid number!");
                    }
                    else if (newMax >= MinPlayers)
                    {
                        MaxPlayers = newMax;
                        await e.Message.RespondAsync(
                            $"Successfully changed max player count to {MaxPlayers}!");
                    }
                    else
                    {
                        await e.Message.RespondAsync(
                            $"Max player count must be greater than (or equal to) the minimum ({MinPlayers})!");
                    }
                }
                    break;
                case "match_config_min":
                {
                    await selection.Result.Interaction.CreateResponseAsync(
                        InteractionResponseType.UpdateMessage,
                        new DiscordInteractionResponseBuilder(
                            new DiscordMessageBuilder().WithContent(
                                "Please enter a new minimum threshold for this room:")));

                    var result = await e.Message.GetNextMessageAsync();

                    if (result.TimedOut)
                        return;

                    if (!int.TryParse(result.Result.Content, out var newMin))
                    {
                        await e.Message.RespondAsync(
                            $"{result.Result.Content} is not a valid number!");
                    }
                    else if (newMin <= MaxPlayers)
                    {
                        MinPlayers = newMin;
                        await e.Message.RespondAsync(
                            $"Successfully changed min player count to {MinPlayers}!");
                    }
                    else
                    {
                        await e.Message.RespondAsync(
                            $"Min player count must be less than (or equal to) the maximum ({MaxPlayers})!");
                    }
                }
                    break;
            }
        }

        private CytoidSingleRoom(string name, DiscordUser host, DiscordChannel hostPrivateChannel,
            LevelInfo song,
            int diffIndex) : base(name, host, hostPrivateChannel)
        {
            RoomType = (RoomBehaviour.Single, "Singular");

            _level = song;
            _diffIndex = diffIndex;

            _displayDifficulty = _level.Charts[diffIndex].GetName();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
using okimu_discordPort.Structures;

namespace okimu_discordPort.Matchmaking.Rooms
{
    public class CytoidPlaylistRoom : MultiHost.PlaylistRoom
    {
        private readonly Queue<(LevelInfo level, int diffIndex)> _songQueue = new();

        public override async Task<bool> Enqueue(MessageCreateEventArgs e, List<string> cmd)
        {
            if (cmd.Count < 1)
            {
                await e.Channel.SendMessageAsync("[Error] LevelID is not specified");
                return false;
            }

            if (!FreeEnqueue && e.Author != Host)
            {
                await e.Channel.SendMessageAsync(
                    "[Error] This room is not free to enqueue! (Only the host can enqueue)");
                return false;
            }

            LevelInfo song;

            try
            {
                song = await CytoidClient.GetLevelAsync(cmd[0]);
            }
            catch (HttpRequestException ex)
            {
                await e.Channel.SendMessageAsync($"{DiscordEmoji.FromUnicode("🚫")} {ex.Message}");
                return false;
            }

            if (song.StatusCode.HasValue)
            {
                await e.Channel.SendMessageAsync($"{DiscordEmoji.FromUnicode("🚫")} Invalid level id!");
                return false;
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
                return false;

            var selectedDifficulty = diffSelectionDict[diffSelection.Result.Id];

            _songQueue.Enqueue((song, selectedDifficulty));

            await diffSelection.Result.Interaction.CreateResponseAsync(
                InteractionResponseType.UpdateMessage,
                new DiscordInteractionResponseBuilder(
                    new DiscordMessageBuilder().WithContent(
                        $"{DiscordEmoji.FromUnicode("🎵")} {song.Title} ({song.Charts[selectedDifficulty].GetName()}) has been added to the queue!")));

            return true;
        }

        public override async Task Start()
        {
            Started = true;

            await MultiHost.MessageAll(AnnouncementChannel, Channels,
                new DiscordMessageBuilder().WithEmbed(new DiscordEmbedBuilder().WithTitle(RoomName)
                    .WithFooter("Prepare yourselves! The first round will begin in a few seconds.")
                    .WithColor(DiscordColor.MidnightBlue)));

            await Task.Delay(3000);

            var round = 0;

            while (_songQueue.Count > 0)
            {
                round++;

                var (level, diffIndex) = _songQueue.Dequeue();
                var itemDuration = level.Duration + 120;

                var displayDifficultyName = level.Charts[diffIndex].GetName();
                await MultiHost.MessageAll(AnnouncementChannel, Channels,
                    new DiscordMessageBuilder().WithEmbed(new DiscordEmbedBuilder()
                            .WithTitle($"{RoomName} - Round {round}")
                            .WithDescription("Match has started!")
                            .AddField("Title", level.Title)
                            .AddField("Difficulty", $"{displayDifficultyName} {level.Charts[diffIndex].Difficulty}")
                            .WithImageUrl(level.Cover.Thumbnail)
                            .WithFooter($"Please finish this in {(int)itemDuration / 60} minutes!")
                            .WithColor(DiscordColor.MidnightBlue))
                        .AddComponents(new DiscordLinkButtonComponent("https://cytoid.io/levels/" + level.Uid,
                            $"Download {level.Uid}")));

                await Task.Delay((int)(itemDuration * 1000));

                await MultiHost.MessageAll(AnnouncementChannel, Channels,
                    new DiscordMessageBuilder().WithContent($"{RoomName} - Time's up!"));

                var ranking = new Dictionary<DiscordUser, RecentRecord>();
                foreach (var d in Players)
                {
                    try
                    {
                        var user = await CytoidClient.GetProfileDetailsAsync(Configuration.GetUser(d).CytoidId);
                        var a    = user.RecentRecords[0];

                        if (a.Date.TimeUntilNow().TotalMinutes < itemDuration &&
                            a.Chart.Level.Uid == level.Uid)
                        {
                            ranking.Add(d, a);
                        }

                        await Task.Delay(500);
                    }
                    catch (Exception)
                    {
                        // ignored
                    }
                }

                var ordered = ranking.OrderByDescending(r => r.Value.Score).ToList();

                var verified = new DiscordEmbedBuilder().WithTitle($"{RoomName} ({UniqueId}) - Results")
                    .WithDescription(
                        "Players who aren't in this list are either not in top 10, or disqualified in **this round only**.")
                    .WithFooter("Next round will begin automatically").WithColor(DiscordColor.SpringGreen);

                for (var j = 0; j < Math.Min(ordered.Count, 10); j++)
                    verified.AddField($"**#{j + 1}** {ordered.ElementAt(j).Key.Username}",
                        ordered.ElementAt(j).Value.Score +
                        $" ({float.Parse(ordered.ElementAt(j).Value.Accuracy) * 100:0.000000}%)");

                if (_songQueue.Count > 0)
                    verified.WithFooter($"Next up - {_songQueue.Peek().level.Title}");

                await MultiHost.MessageAll(AnnouncementChannel, Channels,
                    new DiscordMessageBuilder().WithEmbed(verified));

                if (_songQueue.Count > 0)
                {
                    await MultiHost.MessageAll(AnnouncementChannel, Channels,
                        new DiscordMessageBuilder().WithContent(
                            $"Good job everyone! {RoomName} - Round {round} is over!\n" +
                            $"You have {_songQueue.Count} more rounds to go! Feel free to enqueue new songs.\n" +
                            $"The next round will begin in {BreakDuration} minutes."));

                    await Task.Delay(BreakDuration * 60 * 1000);
                }
                else
                    break;
            }

            await MultiHost.MessageAll(AnnouncementChannel, Channels,
                new DiscordMessageBuilder().WithEmbed(new DiscordEmbedBuilder().WithTitle(RoomName)
                    .WithDescription("All rounds finished!")
                    .WithFooter("This room won't be disposed automatically, feel free to enqueue new songs!")));

            Started = false;
        }

        public override async Task SendInformation(MessageCreateEventArgs e)
        {
            if (_songQueue.Count > 0)
            {
                var level = _songQueue.Peek().level;
                var chart = level.Charts[_songQueue.Peek().diffIndex];
                var embed = new DiscordEmbedBuilder()
                    .WithTitle(RoomName)
                    .WithDescription(RoomDescription)
                    .AddField("Free to enqueue?", FreeEnqueue ? "Yes" : "No")
                    .AddField("Next up", level.Title)
                    .AddField("Difficulty", $"{chart.GetName()} {chart.Difficulty}")
                    .AddField("Song remaining", _songQueue.Count.ToString())
                    .WithImageUrl(level.Cover.Thumbnail)
                    .WithFooter(
                        $"Players: {Players.Count} ({MinPlayers})/{MaxPlayers}");

                var components = new DiscordMessageBuilder()
                    .WithEmbed(embed)
                    .AddComponents(new DiscordLinkButtonComponent(
                        "https://cytoid.io/levels/" + level.Uid,
                        $"Download {level.Uid}"));

                await e.Message.RespondAsync(components);
            }
            else
            {
                var embed = new DiscordEmbedBuilder()
                    .WithTitle(RoomName)
                    .WithDescription(RoomDescription)
                    .AddField("No songs in queue!", $"Join and have fun!")
                    .WithFooter(
                        $"Players: {Players.Count} ({MinPlayers})/{MaxPlayers}");

                var components = new DiscordMessageBuilder()
                    .WithEmbed(embed);

                await e.Message.RespondAsync(components);
            }
        }

        public override async Task TryJoin(MessageCreateEventArgs e)
        {
            if (Players.Count >= MaxPlayers)
            {
                await e.Channel.SendMessageAsync("This room is already full!");
                return;
            }

            if (Configuration.GetUser(e.Author).CytoidId.IsNullOrEmpty())
            {
                await e.Channel.SendMessageAsync("You'll have to bind your id first!");
                return;
            }

            if (MultiHost.Lobby.Any(r => r.Players.Contains(e.Author)))
            {
                await e.Channel.SendMessageAsync("You're already in another room!");
                return;
            }

            Players.Add(e.Author);
            if (!Channels.Contains(e.Channel))
                Channels.Add(e.Channel);
            await e.Channel.SendMessageAsync("Successfully joined room!");

            await base.TryJoin(e);
        }

        private CytoidPlaylistRoom(string name, DiscordUser host, DiscordChannel hostPrivateChannel) : base(name, host, hostPrivateChannel)
        {
            RoomType = (RoomBehaviour.Playlist, "Playlist");
        }

        public static async Task CreateAsync(MessageCreateEventArgs e)
        {
            await e.Message.RespondAsync("Give me a room name!");
            var roomName = await e.Message.GetNextMessageAsync();

            var room = new CytoidPlaylistRoom(roomName.Result.Content, e.Author, e.Channel);

            var questionBuilder = new DiscordMessageBuilder()
                .WithContent("Would you like to make this room free to enqueue?")
                .AddComponents(
                    new DiscordButtonComponent(ButtonStyle.Secondary, "match_config_yes",
                        "Yes, anyone can enqueue songs"),
                    new DiscordButtonComponent(ButtonStyle.Secondary, "match_config_no",
                        "No, I've planned this match already")
                );

            var buttonMessage = await questionBuilder.SendAsync(e.Channel);

            var questionChoice = await buttonMessage.WaitForButtonAsync(e.Author, CancellationToken.None);

            if (questionChoice.TimedOut)
                return;

            switch (questionChoice.Result.Id)
            {
                case "match_config_yes":
                {
                    room.FreeEnqueue = true;
                    await questionChoice.Result.Interaction.CreateResponseAsync(
                        InteractionResponseType.UpdateMessage,
                        new DiscordInteractionResponseBuilder(
                            new DiscordMessageBuilder().WithContent(
                                "Enabled free enqueue! (Anyone can enqueue songs)")));
                }
                    break;
                case "match_config_no":
                {
                    room.FreeEnqueue = false;
                    await questionChoice.Result.Interaction.CreateResponseAsync(
                        InteractionResponseType.UpdateMessage,
                        new DiscordInteractionResponseBuilder(
                            new DiscordMessageBuilder().WithContent(
                                "Disabled free enqueue! (Only the host can enqueue songs)")));
                }
                    break;
            }
            
            MultiHost.Lobby.Add(room);
            await roomName.Result.RespondAsync(
                $"Room created! Its room id is `{room.UniqueId}`. Ask some friends to join your play!");
        }


        public override async Task RequestConfigure(MessageCreateEventArgs e)
        {
            var builder = new DiscordMessageBuilder()
                .WithContent("Please select an option you'd like to configure:")
                .AddComponents(new DiscordButtonComponent(ButtonStyle.Secondary, "match_config_name",
                        "Room name"),
                    new DiscordButtonComponent(ButtonStyle.Secondary, "match_config_desc",
                        "Description"))
                .AddComponents(new DiscordButtonComponent(ButtonStyle.Secondary, "match_config_max",
                        "Max players"),
                    new DiscordButtonComponent(ButtonStyle.Secondary, "match_config_min",
                        "Min players"))
                .AddComponents(new DiscordButtonComponent(ButtonStyle.Secondary, "match_config_free_enqueue",
                        "Free enqueue"),
                    new DiscordButtonComponent(ButtonStyle.Secondary, "match_config_break_duration",
                        "Break duration"));

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
                case "match_config_free_enqueue":
                {
                    var questionBuilder = new DiscordMessageBuilder()
                        .WithContent("Would you like to make this room free to enqueue?")
                        .AddComponents(
                            new DiscordButtonComponent(ButtonStyle.Secondary, "match_config_yes",
                                "Yes, anyone can enqueue songs"),
                            new DiscordButtonComponent(ButtonStyle.Secondary, "match_config_no",
                                "No, I've planned this match already")
                        );

                    await selection.Result.Interaction.CreateResponseAsync(
                        InteractionResponseType.UpdateMessage,
                        new DiscordInteractionResponseBuilder(questionBuilder));

                    var questionChoice = await buttonMessage.WaitForButtonAsync(Host, CancellationToken.None);

                    if (questionChoice.TimedOut)
                        return;

                    switch (questionChoice.Result.Id)
                    {
                        case "match_config_yes":
                        {
                            FreeEnqueue = true;
                            await questionChoice.Result.Interaction.CreateResponseAsync(
                                InteractionResponseType.UpdateMessage,
                                new DiscordInteractionResponseBuilder(
                                    new DiscordMessageBuilder().WithContent(
                                        "Enabled free enqueue! (Anyone can enqueue songs)")));
                        }
                            break;
                        case "match_config_no":
                        {
                            FreeEnqueue = false;
                            await questionChoice.Result.Interaction.CreateResponseAsync(
                                InteractionResponseType.UpdateMessage,
                                new DiscordInteractionResponseBuilder(
                                    new DiscordMessageBuilder().WithContent(
                                        "Disabled free enqueue! (Only the host can enqueue songs)")));
                        }
                            break;
                    }
                }
                    break;
                case "match_config_break_duration":
                {
                    await selection.Result.Interaction.CreateResponseAsync(
                        InteractionResponseType.UpdateMessage,
                        new DiscordInteractionResponseBuilder(
                            new DiscordMessageBuilder().WithContent(
                                "Please enter a new break duration between two songs:")));

                    var result = await e.Message.GetNextMessageAsync();

                    if (result.TimedOut)
                        return;

                    if (!int.TryParse(result.Result.Content, out var newDuration))
                    {
                        await e.Message.RespondAsync(
                            $"{result.Result.Content} is not a valid integer!");
                    }
                    else if (newDuration <= 0)
                    {
                        await e.Message.RespondAsync(
                            $"Break duration must be at least 1 minute!");
                    }
                    else
                    {
                        BreakDuration = newDuration;
                        await e.Message.RespondAsync(
                            $"Successfully changed break duration to {BreakDuration}!");
                    }
                }
                    break;
            }
        }
    }
}

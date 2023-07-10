using System;
using System.Collections.Generic;
using System.Globalization;
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
using okimu_discordPort.Structures;

namespace okimu_discordPort.Matchmaking.Rooms
{
    public class CytoidGauntletRoom : MultiHost.PlaylistRoom
    {
        private readonly Queue<Challenge> _songQueue = new();

        private class Challenge
        {
            public Challenge(LevelInfo level, int diffIndex)
            {
                Level = level;
                DiffIndex = diffIndex;

                DisplayDifficulty = Level.Charts[DiffIndex].GetName();
            }

            public readonly LevelInfo Level;

            public readonly int DiffIndex;
            public readonly string DisplayDifficulty;

            internal (string name, Func<RecentRecord, float> get) Condition;
            internal (string name, Func<float, bool> verify) Operation;
            internal float ConditionValue;
        }

        public override async Task<bool> Enqueue(MessageCreateEventArgs e, List<string> cmd)
        {
            if (cmd.Count < 1)
            {
                await e.Channel.SendMessageAsync($"{DiscordEmoji.FromUnicode("🚫")} level id is not specified");
                return false;
            }

            if (!FreeEnqueue && e.Author != Host)
            {
                await e.Channel.SendMessageAsync(
                    "[Error] This room is not free to enqueue! (Only the host can enqueue)");
                return false;
            }

            var song = await CytoidClient.GetLevelAsync(cmd[0]);

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

            await diffSelection.Result.Interaction.CreateResponseAsync(
                InteractionResponseType.UpdateMessage,
                new DiscordInteractionResponseBuilder(
                    new DiscordMessageBuilder().WithContent(
                        $"Chosen difficulty {song.Charts[selectedDifficulty].GetName()}!")));

            var challenge = new Challenge(song, selectedDifficulty);

            var conditions = new Dictionary<string, (string name, Func<RecentRecord, float> parse)>
            {
                { "match_ctd_challenge_score", ("Score", record => record.Score) },
                { "match_ctd_challenge_max_combo", ("Max Combo", record => record.Details.MaxCombo) },
                { "match_ctd_challenge_accuracy", ("Accuracy", record => float.Parse(record.Accuracy) * 100) }
            };

            var targetConditionBuilder = new DiscordMessageBuilder()
                .WithContent("Constructing criteria...\n" +
                             "Please select a condition type!")
                .AddComponents(
                    conditions.Select(c =>
                        new DiscordButtonComponent(ButtonStyle.Secondary,
                            c.Key,
                            c.Value.name
                        )));

            var buttonMessage = await targetConditionBuilder.SendAsync(e.Channel);

            var selection = await buttonMessage.WaitForButtonAsync(e.Author, CancellationToken.None);

            if (selection.TimedOut)
                return false;


            challenge.Condition = conditions[selection.Result.Id];

            var operations = new Dictionary<string, (string name, Func<float, bool> determine)>
            {
                { "match_ctd_challenge_more_than", (">", f => f > challenge.ConditionValue) },
                { "match_ctd_challenge_less_than", ("<", f => f < challenge.ConditionValue) },
                { "match_ctd_challenge_equal_to", ("=", f => Math.Abs(f - challenge.ConditionValue) < 0.001f) }
            };

            await selection.Result.Interaction.CreateResponseAsync(
                InteractionResponseType.UpdateMessage,
                new DiscordInteractionResponseBuilder(
                    new DiscordMessageBuilder()
                        .WithContent($"Constructing criteria... {conditions[selection.Result.Id].name}\n" +
                                     "Please select an operation!")
                        .AddComponents(
                            operations.Select(c =>
                                new DiscordButtonComponent(ButtonStyle.Secondary,
                                    c.Key,
                                    c.Value.name
                                ))
                        )));


            var operationSelection = await buttonMessage.WaitForButtonAsync(e.Author, CancellationToken.None);

            if (operationSelection.TimedOut)
                return false;

            challenge.Operation = operations[operationSelection.Result.Id];

            await operationSelection.Result.Interaction.CreateResponseAsync(
                InteractionResponseType.UpdateMessage,
                new DiscordInteractionResponseBuilder(
                    new DiscordMessageBuilder()
                        .WithContent(
                            $"Constructing criteria... {conditions[selection.Result.Id].name} {operations[operationSelection.Result.Id].name}\n" +
                            "Finally, please give me a value to compare to!")));

            var valueInput = await e.Channel.GetNextMessageAsync();

            if (!float.TryParse(valueInput.Result.Content, out var conditionValue))
            {
                await e.Channel.SendMessageAsync("[Error] Value isn't a number!");
                return false;
            }

            await buttonMessage.ModifyAsync(
                $"Criteria: {conditions[selection.Result.Id].name} {operations[operationSelection.Result.Id].name} {conditionValue}");

            challenge.ConditionValue = conditionValue;

            _songQueue.Enqueue(challenge);

            await valueInput.Result.RespondAsync(new DiscordMessageBuilder().WithContent(
                $"{DiscordEmoji.FromUnicode("🎵")} {song.Title} ({song.Charts[selectedDifficulty].GetName()}) has been added to the queue!"));

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

                var challenge = _songQueue.Dequeue();
                var secondsWaitTime = (int)Math.Floor(challenge.Level.Duration + 120);

                await MultiHost.MessageAll(AnnouncementChannel, Channels,
                    new DiscordMessageBuilder().WithEmbed(new DiscordEmbedBuilder().WithTitle(RoomName)
                            .WithDescription("Match has started!")
                            .AddField("Title", challenge.Level.Title)
                            .AddField("Difficulty",
                                $"{challenge.DisplayDifficulty} {challenge.Level.Charts[challenge.DiffIndex].Difficulty}")
                            .AddField("Condition", GetConditionString(challenge))
                            .WithImageUrl(challenge.Level.Cover.Thumbnail)
                            .WithFooter($"Please finish this in {secondsWaitTime / 60} minutes!")
                            .WithColor(DiscordColor.MidnightBlue))
                        .AddComponents(new DiscordLinkButtonComponent("https://cytoid.io/levels/" + challenge.Level.Uid,
                            $"Download {challenge.Level.Uid}")));

                await Task.Delay(secondsWaitTime * 1000);

                await MultiHost.MessageAll(AnnouncementChannel, Channels,
                    new DiscordMessageBuilder().WithContent($"{RoomName} - Time's up!"));

                var passed = new Dictionary<DiscordUser, RecentRecord>();
                var failed = new Dictionary<DiscordUser, RecentRecord>();
                foreach (var player in Players)
                {
                    var user = await CytoidClient.GetProfileDetailsAsync(Configuration.GetUser(player).CytoidId);
                    var latestPlay = user.RecentRecords[0];

                    if (latestPlay.Date.TimeUntilNow().TotalMinutes < secondsWaitTime
                        && latestPlay.Chart.Level.Uid == challenge.Level.Uid
                        && latestPlay.Chart.Type == challenge.Level.Charts[challenge.DiffIndex].Type)
                        if (challenge.Operation.verify(challenge.Condition.get(latestPlay)))
                            passed.Add(player, latestPlay);
                        else
                            failed.Add(player, latestPlay);

                    await Task.Delay(500);
                }

                {
                    var ordered = passed.OrderByDescending(r => challenge.Condition.get(r.Value)).ToList();

                    var verified = new DiscordEmbedBuilder()
                        .WithTitle($"{RoomName} ({UniqueId}) - Passed ({challenge.Condition.name})")
                        .WithDescription(
                            "Players who aren't in either of the lists are automatically disqualified due to incorrect score.")
                        .WithFooter("This room won't be disposed automatically, feel free to enqueue new songs!")
                        .WithColor(DiscordColor.SpringGreen);

                    for (var i = 0; i < ordered.Count; i++)
                        verified.AddField($"**#{i + 1}** {ordered.ElementAt(i).Key.Username}",
                            challenge.Condition.get(ordered.ElementAt(i).Value).ToString(CultureInfo.InvariantCulture));

                    await MultiHost.MessageAll(AnnouncementChannel, Channels,
                        new DiscordMessageBuilder().WithEmbed(verified));
                }

                {
                    var ordered = failed.OrderByDescending(r => challenge.Condition.get(r.Value)).ToList();

                    var verified = new DiscordEmbedBuilder()
                        .WithTitle($"{RoomName} ({UniqueId}) - Failed ({challenge.Condition.name})")
                        .WithDescription(
                            "Players who aren't in either of the lists are automatically disqualified due to incorrect score.")
                        .WithFooter("This room won't be disposed automatically, feel free to enqueue new songs!")
                        .WithColor(DiscordColor.Gray);

                    for (var i = 0; i < ordered.Count; i++)
                        verified.AddField($"**#{i + 1}** {ordered.ElementAt(i).Key.Username}",
                            challenge.Condition.get(ordered.ElementAt(i).Value).ToString(CultureInfo.InvariantCulture));

                    await MultiHost.MessageAll(AnnouncementChannel, Channels,
                        new DiscordMessageBuilder().WithEmbed(verified));
                }

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

        private static string GetConditionString(Challenge c) =>
            $"{c.Condition.name} {c.Operation.name} {c.ConditionValue}";

        public override async Task SendInformation(MessageCreateEventArgs e)
        {
            if (_songQueue.Count > 0)
            {
                var level = _songQueue.Peek().Level;
                var chart = level.Charts[_songQueue.Peek().DiffIndex];
                var embed = new DiscordEmbedBuilder()
                    .WithTitle(RoomName)
                    .WithDescription(RoomDescription)
                    .AddField("Free to enqueue?", FreeEnqueue ? "Yes" : "No")
                    .AddField("Next up", level.Title)
                    .AddField("Difficulty", $"{chart.GetName()} {chart.Difficulty}")
                    .AddField("Condition", GetConditionString(_songQueue.Peek()))
                    .AddField("Trials remaining", _songQueue.Count.ToString())
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
                    .AddField("No trials in queue!", $"Join and have fun!")
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

        private CytoidGauntletRoom(string name, DiscordUser host, DiscordChannel hostPrivateChannel) : base(name, host,
            hostPrivateChannel)
        {
            RoomType = (RoomBehaviour.Playlist, "Gauntlet");
        }

        public static async Task CreateAsync(MessageCreateEventArgs e)
        {
            await e.Message.RespondAsync("Give me a room name!");
            var roomName = await e.Message.GetNextMessageAsync();

            var room = new CytoidGauntletRoom(roomName.Result.Content, e.Author, e.Channel);

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

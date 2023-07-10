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

namespace okimu_discordPort.Matchmaking.Rooms;

public class CytoidChallengeRoom : BaseRoom
{
	private readonly LevelInfo _level;

	private readonly int    _diffIndex;
	private readonly string _displayDifficulty;

	private (string name, Func<RecentRecord, float> extract) _requirement;
	private (string name, Func<float, bool> verify)          _operation;
	private float                                            _conditionValue;

	private CytoidChallengeRoom(string    name, DiscordUser host,      DiscordChannel hostPrivateChannel,
	                            LevelInfo song, int         diffIndex) :
		base(name, host, hostPrivateChannel)
	{
		RoomType = (RoomBehaviour.Single, "Challenge");

		_level     = song;
		_diffIndex = diffIndex;

		_displayDifficulty = _level.Charts[diffIndex].GetName();
	}

	public override async Task Start()
	{
		Started = true;
		var secondsWaitTime = (int)Math.Floor(_level.Duration + 120);

		await MultiHost.MessageAll(AnnouncementChannel, Channels,
		                           new DiscordMessageBuilder().WithEmbed(new DiscordEmbedBuilder().WithTitle(RoomName)
			                                                                 .WithDescription("Match has started!")
			                                                                 .AddField("Title", _level.Title)
			                                                                 .AddField("Difficulty",
				                                                                 $"{_displayDifficulty} {_level.Charts[_diffIndex].Difficulty}")
			                                                                 .AddField("Condition",
				                                                                 GetConditionString())
			                                                                 .WithImageUrl(_level.Cover.Thumbnail)
			                                                                 .WithFooter(
				                                                                 $"Please finish this in {secondsWaitTime / 60} minutes!")
			                                                                 .WithColor(DiscordColor.MidnightBlue))
		                                                      .AddComponents(
			                                                      new DiscordLinkButtonComponent(
				                                                      "https://cytoid.io/levels/" + _level.Uid,
				                                                      $"Download {_level.Uid}")));

		await Task.Delay(secondsWaitTime * 1000);

		await MultiHost.MessageAll(AnnouncementChannel, Channels,
		                           new DiscordMessageBuilder().WithContent($"{RoomName} - Time's up!"));

		var passed = new Dictionary<DiscordUser, RecentRecord>();
		var failed = new Dictionary<DiscordUser, RecentRecord>();

		foreach (var player in Players)
		{
			var user       = await CytoidClient.GetProfileDetailsAsync(Configuration.GetUser(player).CytoidId);
			var latestPlay = user.RecentRecords[0];

			if (latestPlay.Date.TimeUntilNow().TotalMinutes < secondsWaitTime
			    && latestPlay.Chart.Level.Uid                == _level.Uid
			    && latestPlay.Chart.Type                     == _level.Charts[_diffIndex].Type)
				if (VerifyPlay(latestPlay))
					passed.Add(player, latestPlay);
				else
					failed.Add(player, latestPlay);

			await Task.Delay(500);
		}

		{
			var ordered = passed.OrderByDescending(r => _requirement.extract(r.Value)).ToList();

			var verified = new DiscordEmbedBuilder()
			               .WithTitle($"{RoomName} ({UniqueId}) - Passed ({_requirement.name})")
			               .WithDescription(
				               "Players who aren't in either of the lists are automatically disqualified due to incorrect score.")
			               .WithFooter("This room will now be disposed.").WithColor(DiscordColor.SpringGreen);

			for (var i = 0; i < ordered.Count; i++)
				verified.AddField($"**#{i + 1}** {ordered.ElementAt(i).Key.Username}",
				                  _requirement.extract(ordered.ElementAt(i).Value)
				                              .ToString(CultureInfo.InvariantCulture));

			await MultiHost.MessageAll(AnnouncementChannel, Channels,
			                           new DiscordMessageBuilder().WithEmbed(verified));
		}

		{
			var ordered = failed.OrderByDescending(r => _requirement.extract(r.Value)).ToList();

			var verified = new DiscordEmbedBuilder()
			               .WithTitle($"{RoomName} ({UniqueId}) - Failed ({_requirement.name})")
			               .WithDescription(
				               "Players who aren't in either of the lists are automatically disqualified due to incorrect score.")
			               .WithFooter("This room will now be disposed.").WithColor(DiscordColor.Gray);

			for (var i = 0; i < ordered.Count; i++)
				verified.AddField($"**#{i + 1}** {ordered.ElementAt(i).Key.Username}",
				                  _requirement.extract(ordered.ElementAt(i).Value)
				                              .ToString(CultureInfo.InvariantCulture));

			await MultiHost.MessageAll(AnnouncementChannel, Channels,
			                           new DiscordMessageBuilder().WithEmbed(verified));
		}

		await Dispose();
	}

	private bool VerifyPlay(RecentRecord latestPlay)
	{
		return _operation.verify(_requirement.extract(latestPlay));
	}

	private string GetConditionString()
	{
		return $"{_requirement.name} {_operation.name} {_conditionValue}";
	}

	public override async Task SendInformation(MessageCreateEventArgs e)
	{
		var embed = new DiscordEmbedBuilder()
		            .WithAuthor($"Artist: {_level.Metadata.Artist.Name} | Charter: {_level.Metadata.Charter.Name}")
		            .WithTitle(_level.Title)
		            .AddField("Difficulty", $"{_displayDifficulty} {_level.Charts[_diffIndex].Difficulty}")
		            .AddField("Condition",  GetConditionString())
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

		var selectedDifficulty = diffSelectionDict[diffSelection.Result.Id];

		await diffSelection.Result.Interaction.CreateResponseAsync(
			InteractionResponseType.UpdateMessage,
			new DiscordInteractionResponseBuilder(
				new DiscordMessageBuilder().WithContent(
					$"Chosen difficulty {song.Charts[selectedDifficulty].GetName()}!")));

		await e.Message.RespondAsync(
			"Before we move on to specifying the challenge criteria, give me a room name!");

		var roomTitle = await e.Message.GetNextMessageAsync();

		var room = new CytoidChallengeRoom(roomTitle.Result.Content, e.Author, e.Channel,
		                                   song, selectedDifficulty);

		var conditions = new Dictionary<string, (string name, Func<RecentRecord, float> parse)>()
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
			return;

		room._requirement = conditions[selection.Result.Id];

		var operations = new Dictionary<string, (string name, Func<float, bool> determine)>
		{
			{ "match_ctd_challenge_more_than", (">", f => f                                 > room._conditionValue) },
			{ "match_ctd_challenge_less_than", ("<", f => f                                 < room._conditionValue) },
			{ "match_ctd_challenge_equal_to", ("=", f => Math.Abs(f - room._conditionValue) < 0.001f) }
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
			return;

		room._operation = operations[operationSelection.Result.Id];

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
			return;
		}

		await buttonMessage.ModifyAsync(
			$"Criteria: {conditions[selection.Result.Id].name} {operations[operationSelection.Result.Id].name} {conditionValue}");

		room._conditionValue = conditionValue;

		MultiHost.Lobby.Add(room);

		await e.Message.RespondAsync(
			$"Room created! Its room id is `{room.UniqueId}`. Ask some friends to join your play!");
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
}

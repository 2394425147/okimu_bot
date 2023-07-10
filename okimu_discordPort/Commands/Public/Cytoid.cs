using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Enums;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.SlashCommands;
using okimu_discordPort.Communication;
using okimu_discordPort.Helpers;
using okimu_discordPort.Properties;
using okimu_discordPort.Structures;

namespace okimu_discordPort.Commands.Public;

[SlashCommandGroup("cytoid", "Various commands for the game Cytoid")]
public sealed class Cytoid : ApplicationCommandModule
{
	[SlashCommand("probe", "Gets your most recent Cytoid play")]
	public async Task Probe(InteractionContext ctx)
	{
		var config   = Configuration.GetUser(ctx.User);
		var cytoidId = config.CytoidId;

		if (cytoidId.IsNullOrEmpty())
		{
			await ctx.CreateResponseAsync(InteractionResponseType.Modal,
			                              new DiscordInteractionResponseBuilder()
				                              .WithTitle($"Cytoid ID not saved")
				                              .WithContent(Resources.ERR_CYTOID_NO_ID));
			return;
		}

		await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource,
		                              new DiscordInteractionResponseBuilder()
			                              .WithContent("Probing cytoid.io..."));

		await ctx.DeferAsync();

		var user   = await CytoidClient.GetProfileDetailsAsync(cytoidId);
		var recent = user.RecentRecords[0];

		var isNewProbe = recent.Date.TimeUntilNow() < TimeSpan.FromMinutes(10) &&
		                 recent.Date                 != config.LastQueryDate;

		await ctx.DeleteResponseAsync();

		var embed = new DiscordEmbedBuilder()
		            .WithTitle($"Recent ranking for **{user.User.Uid}**:")
		            .AddField("Song", recent.Chart.Level.Title, true)
		            .AddField("Difficulty",
		                      recent.Chart.Name + $" ({recent.Chart.Difficulty})",
		                      true)
		            .AddField("Score", $"{recent.Score}")
		            .AddField("Accuracy",
		                      $"{float.Parse(recent.Accuracy) * 100:0.00000}%")
		            .AddField("Perfect", $"{recent.Details.Perfect}", true)
		            .AddField("Great",   $"{recent.Details.Great}",   true)
		            .AddField("Good",    $"{recent.Details.Good}",    true)
		            .AddField("Bad",     $"{recent.Details.Bad}",     true)
		            .AddField("Miss",    $"{recent.Details.Miss}",    true)
		            .WithImageUrl(recent.Chart.Level.Cover.Thumbnail);

		if (isNewProbe)
		{
			config.LastQueryDate = recent.Date;

			var reward = config.AddTokens(recent.CalculateReward());
			embed.WithFooter($"+ {reward} tokens");
		}

		await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
		                              new DiscordInteractionResponseBuilder()
			                              .AddEmbed(embed));
	}

	[SlashCommand("bind", "Binds your Cytoid account with okimu")]
	public async Task Bind(InteractionContext ctx,
	                       [Option("uid", "The UID of your Cytoid account")]
	                       string uid)
	{
		var user = Configuration.GetUser(ctx.User);

		var response = new DiscordInteractionResponseBuilder();

		response.WithContent(!user.CytoidId.IsNullOrEmpty()
			                     ? $"Rebinding, your original uid was {user.CytoidId}"
			                     : "Binding...");

		await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource,
		                              response);

		var profileInfo = await CytoidClient.GetProfileAsync(uid);

		if (profileInfo != null)
		{
			user.CytoidId = uid;
			await ctx.EditResponseAsync(new DiscordWebhookBuilder()
				                            .WithContent("Binding was successful!"));
		}
		else
		{
			await ctx.EditResponseAsync(new DiscordWebhookBuilder()
				                            .WithContent("Binding failed...\n"                    +
				                                         "Try double-checking the uid for typos " +
				                                         "and try again in a while"));
		}
	}

	[SlashCommand("info", "Check out the info of a level")]
	public async Task Info(InteractionContext ctx,
	                       [Option("level id", "The ID of the level you want to view")]
	                       string levelId)
	{
		await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource,
		                              new DiscordInteractionResponseBuilder()
			                              .WithContent("Searching cytoid.io..."));
		await ctx.DeferAsync();

		var levelData = await CytoidClient.GetLevelAsync(levelId);
		var embed = new DiscordEmbedBuilder()
		            .WithAuthor(
		                        $"Artist: {levelData.Metadata.Artist.Name} | Charter: {levelData.Metadata.Charter.Name}")
		            .WithTitle(levelData.Title)
		            .WithTimestamp(levelData.CreationDate)
		            .WithDescription(levelData.Description)
		            .WithImageUrl(levelData.Cover.Thumbnail);

		foreach (var chart in levelData.Charts)
			embed.AddField($"{chart.GetName()} {chart.Difficulty}",
			               $"Note Count: {chart.NotesCount}");

		var components = new DiscordInteractionResponseBuilder()
		                 .AddEmbed(embed)
		                 .AddComponents(new DiscordLinkButtonComponent(
		                                                               "https://cytoid.io/levels/" + levelData.Uid,
		                                                               "Download"));

		await ctx.DeleteResponseAsync();
		await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
		                              components);
	}

	[SlashCommand("ranking", "View the ranking for the specified level")]
	public async Task Ranking(InteractionContext ctx,
	                          [Option("level id", "The ID of the level you want to view")]
	                          string levelId)
	{
		await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource,
		                              new DiscordInteractionResponseBuilder()
			                              .WithContent($"Searching for {levelId}..."));
		await ctx.DeferAsync();

		var details = await CytoidClient.GetLevelAsync(levelId);

		var info     = await CytoidClient.GetLeaderboardAsync(details.Uid, details.Charts[0].Type);
		var diffInfo = details.Charts[0].Type;

		var pages = new List<Page>();

		// Populate the pages
		var list = info.SplitBySize(5);
		for (var pageIndex = 0; pageIndex < list.Count; pageIndex++)
		{
			var deb = new DiscordEmbedBuilder()
			          .WithTitle($"{details.Title} [{diffInfo}]")
			          .WithDescription($"{5 * pageIndex + 1} ~ {5 * pageIndex + list[pageIndex].Count}:");

			for (var index = 0; index < list[pageIndex].Count; index++)
			{
				var i = list[pageIndex][index];
				deb.AddField($"{5 * pageIndex + index + 1} - {i.Owner.Uid}",
				             $"{i.Score} / {i.Accuracy * 100:0.000000}");
			}

			pages.Add(new Page
			          {
				          Embed = deb
			          });
		}

		await ctx.DeleteResponseAsync();
		await ctx.Channel.SendPaginatedMessageAsync(ctx.User, pages, PaginationBehaviour.WrapAround,
		                                            ButtonPaginationBehavior.DeleteButtons, CancellationToken.None);

		var config = Configuration.GetUser(ctx.User);
		if (config.CytoidId.IsNullOrEmpty())
			return;

		var myScore = info.FirstOrDefault(i =>
			                                  i.Owner.Uid == config.CytoidId);
		if (myScore != null)
			await ctx.CreateResponseAsync(new DiscordInteractionResponseBuilder().AddEmbed(
				                               new DiscordEmbedBuilder()
					                               .WithTitle($"My ranking (Placed {(info.IndexOf(myScore) + 1).ToPlacement()}):")
					                               .AddField("Score",
					                                         $"{myScore.Score}")
					                               .AddField("Perfect",
					                                         $"{myScore.Details.Perfect}")
					                               .AddField("Great",
					                                         $"{myScore.Details.Great}")
					                               .AddField("Good",
					                                         $"{myScore.Details.Good}")
					                               .AddField("Bad",
					                                         $"{myScore.Details.Bad}")
					                               .AddField("Miss",
					                                         $"{myScore.Details.Miss}")
					                               .AddField("Accuracy",
					                                         $"{myScore.Accuracy * 100:0.000000}%")
				                              ));
	}
}

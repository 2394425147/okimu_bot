using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using okimu_discordPort.Structures;

namespace okimu_discordPort.Commands.Public;

[SlashCommandGroup("mod", "Administrative tools")]
public sealed class Moderation : ApplicationCommandModule
{
	// ReSharper disable once MemberCanBePrivate.Global
	// ReSharper disable once UnusedAutoPropertyAccessor.Global
	public Configuration Configuration { private get; set; }
	
	[SlashCommand("save", "Backups user/guild configurations")]
	public async Task Save(InteractionContext ctx)
	{
		await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource,
		                              new DiscordInteractionResponseBuilder()
			                              .WithContent("Backing up files..."));
		
		Configuration.Save();
		
		await ctx.EditResponseAsync(new DiscordWebhookBuilder()
			                            .WithContent("👏 Configurations all backed up!"));
	}
	
	[SlashCommand("rollback", "Rollback config changes to the latest backup")]
	public async Task Rollback(InteractionContext ctx)
	{
		await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource,
		                              new DiscordInteractionResponseBuilder()
			                              .WithContent("Rolling back changes..."));
		
		Configuration.Load();
		
		await ctx.EditResponseAsync(new DiscordWebhookBuilder()
			                            .WithContent("👏 Configurations restored!"));
	}
}
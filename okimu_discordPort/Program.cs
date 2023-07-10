using System.Collections.Generic;
using System.Threading.Tasks;
using dotenv.net;
using DSharpPlus;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.EventHandling;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.SlashCommands;
using okimu_discordPort.Commands.Public;

namespace okimu_discordPort
{
	internal static class Program
	{
		public static  DiscordClient               DiscordClient;
		public static IDictionary<string, string> EnvironmentVariables { get; private set; }

		private static async Task Main()
		{
			DotEnv.Load();
			EnvironmentVariables = DotEnv.Read();

			var configuration = new DiscordConfiguration
			                    {
				                    AutoReconnect = true,
				                    Token         = EnvironmentVariables["DISCORD_TOKEN"],
				                    TokenType     = TokenType.Bot,
				                    Intents       = DiscordIntents.AllUnprivileged
			                    };

			var interactivityConfiguration = new InteractivityConfiguration
			                                 {
				                                 PaginationButtons = new PaginationButtons()
			                                 };
			
			DiscordClient = new DiscordClient(configuration);
			DiscordClient.UseInteractivity(interactivityConfiguration);
			
			var slash = DiscordClient.UseSlashCommands();

			slash.RegisterCommands<Moderation>();
			slash.RegisterCommands<Cytoid>();

			await DiscordClient.ConnectAsync();
			await Task.Delay(-1);
		}
	}
}

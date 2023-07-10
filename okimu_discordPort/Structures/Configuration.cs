using System;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using Nito.AsyncEx;
using okimu_discordPort.Helpers;

namespace okimu_discordPort.Structures
{
	public class Configuration
	{
		public const string Prefix  = "%";
		public const string Version = "v2.3";

		private readonly UserBank  _userBank;
		private readonly GuildBank _guildBank;

		private static Configuration Instance { get; } = new();

		private Configuration()
		{
			_userBank  = new UserBank();
			_guildBank = new GuildBank();

			Load();

			Task.Run(RefreshRoutine).ContinueWith(t => Log.Error("Error while performing refresh", t.Exception),
			                                      TaskContinuationOptions.OnlyOnFaulted);
		}

		public static void Save()
		{
			Instance._userBank.Save();
			Instance._guildBank.Save();
		}

		public static void Load()
		{
			Instance._userBank.Load();
			Instance._guildBank.Load();
		}
		
		private static void RefreshRoutine()
		{
			Task.Factory.StartNew(async () =>
			{
				var retryCount = 0;

				while (true)
					try
					{
						await Task.Delay(new TimeSpan(0, 10, 0));
						Save();
					}
					catch
					{
						retryCount++;
						if (retryCount > 3)
							throw;
					}
			}).ContinueWith(t =>
			    {
				    if (t?.Exception != null)
					    throw t.Exception;
			    }, TaskContinuationOptions.OnlyOnFaulted)
			    .ConfigureAwait(false);
		}

		public static UserConfig GetUser(DiscordUser user)
		{
			return Instance._userBank.GetUserOrCreate(user.Id.ToString());
		}
		
		public static GuildConfig GetGuild(DiscordGuild guild)
		{
			return Instance._guildBank.GetGuildOrCreate(guild.Id.ToString());
		}
	}
}

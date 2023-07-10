using System;

namespace okimu_discordPort.Structures
{
	public class UserConfig
	{
		public UserStatus Status = UserStatus.User;
		public ulong       Tokens;

		public string         CytoidId;
		public DateTimeOffset LastQueryDate;

		public ulong AddTokens(ulong amount)
		{
			var originalTokensCount = Tokens;
			Tokens = checked(Tokens + amount);

			return Tokens - originalTokensCount;
		}
	}

	public enum UserStatus
	{
		Owner,
		Administrator,
		User,
		Banned
	}
}
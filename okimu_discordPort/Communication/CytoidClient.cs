using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using DSharpPlus.SlashCommands;
using okimu_discordPort.Apis.CytoidApi.LeaderboardRecord;
using okimu_discordPort.Apis.CytoidApi.LevelInfo;
using okimu_discordPort.Apis.CytoidApi.LevelQuery;
using okimu_discordPort.Apis.CytoidApi.ProfileDetails;
using okimu_discordPort.Apis.CytoidApi.ProfileInfo;
using okimu_discordPort.Helpers;
using okimu_discordPort.Structures.Cytoid;

namespace okimu_discordPort.Communication;

public static class CytoidClient
{
	private static HttpClient Client { get; } = new()
	{
		DefaultRequestHeaders =
		{
			{ "UserAgent", Program.EnvironmentVariables["CYTOID_UA"] }
		}
	};

	private const string ApiAddress = "https://services.cytoid.io";

	public static async Task<List<LeaderboardRecord>> GetLeaderboardAsync(string uid,
	                                                          DifficultyType difficulty)
	{
		var body = await Client.GetUtf8StringAsync($"{ApiAddress}/levels/{uid}/charts/{difficulty.GetName()}/records?limit=255");
		return LeaderboardRecord.FromJson(body);
	}

	public static async Task<ProfileInfo> GetProfileAsync(string uid)
	{
		var body = await Client.GetUtf8StringAsync($"{ApiAddress}/profile/{uid}");
		return ProfileInfo.FromJson(body);
	}

	public static async Task<ProfileDetails> GetProfileDetailsAsync(string uid)
	{
		var body = await Client.GetUtf8StringAsync($"{ApiAddress}/profile/{uid}/details");
		return ProfileDetails.FromJson(body);
	}

	public static async Task<List<LevelQuery>> SearchLevel(string     query,
	                                                       SearchSort sorting      = SearchSort.UploadDate,
	                                                       int        pageNumber   = 0,
	                                                       int        limit        = 24,
	                                                       bool       isDescending = false)
	{
		var sort = sorting switch
		{
			SearchSort.UploadDate   => "creation_date",
			SearchSort.ModifiedDate => "modification_date",
			SearchSort.Difficulty   => "difficulty",
			SearchSort.Duration     => "duration",
			SearchSort.Downloads    => "downloads",
			SearchSort.Rating       => "rating",
			_                       => throw new ArgumentOutOfRangeException(nameof(sorting), sorting, null)
		};

		var order = isDescending ? "desc" : "asc";

		var body = await Client.GetUtf8StringAsync($"{ApiAddress}/search/levels?" +
		                                           $"search={query}"              +
		                                           $"&page={pageNumber}"          +
		                                           $"&sort={sort}"                +
		                                           $"&order={order}"              +
		                                           $"&limit={limit}");
		return LevelQuery.FromJson(body);
	}

	public static async Task<LevelInfo> GetLevelAsync(string levelId)
	{
		var body = await Client.GetUtf8StringAsync($"{ApiAddress}/levels/{levelId}");
		return LevelInfo.FromJson(body);
	}
}

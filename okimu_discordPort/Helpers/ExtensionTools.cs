using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using ImageMagick;
using MoreLinq.Extensions;

namespace okimu_discordPort.Helpers;

public static class ExtensionTools
{
	/// <summary>
	///     Checks if this string is a number
	/// </summary>
	/// <returns>Boolean value indicating the string's numeric status</returns>
	public static bool IsNumber(this string str)
	{
		return str.All(char.IsDigit);
	}

	public static bool IsNullOrEmpty(this string str)
	{
		return string.IsNullOrEmpty(str);
	}

	/// <summary>
	///     Gets the last index in a list
	/// </summary>
	public static int LastIndex<T>(this List<T> obj)
	{
		return obj.Count - 1;
	}

	public static List<List<T>> SplitBySize<T>(this IEnumerable<T> obj, int size)
	{
		return obj.Batch(size).Select(b => b.ToList()).ToList();
	}

	public static async Task<string> GetUtf8StringAsync(this HttpClient client, string url)
	{
		return Encoding.UTF8.GetString(await client.GetByteArrayAsync(url));
	}

	public static TimeSpan TimeUntilNow(this DateTimeOffset time)
	{
		return DateTimeOffset.Now - time;
	}

	public static string ToPlacement(this int i)
	{
		return (i % 10) switch
		{
			1 => $"{i}st",
			2 => $"{i}nd",
			3 => $"{i}rd",
			_ => $"{i}th"
		};
	}

	public static string JoinArgs(this IEnumerable<string> args)
	{
		return string.Join(' ', args);
	}

	public static async Task SendAsResponse(this MagickImage image, DiscordMessage message)
	{
		var file = File.Create(Path.Combine(Path.GetTempPath(), $"OKIMU_IMG_{GenerateGuid()}.jpg"));
		await image.WriteAsync(file, MagickFormat.Jpg);

		file.Position = 0;
		await message.RespondAsync(new DiscordMessageBuilder().AddFile(file));

		file.Close();
		File.Delete(file.Name);
	}

	private static string GenerateGuid()
	{
		return Guid.NewGuid().ToString("N");
	}
}

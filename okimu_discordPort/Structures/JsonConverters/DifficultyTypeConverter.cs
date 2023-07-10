using System;
using Newtonsoft.Json;
using okimu_discordPort.Structures.Cytoid;

namespace okimu_discordPort.Structures.JsonConverters;

public class DifficultyTypeConverter : JsonConverter<DifficultyType>
{
	private const string Easy    = "easy";
	private const string Hard    = "hard";
	private const string Extreme = "extreme";

	public override void WriteJson(JsonWriter     writer,
	                               DifficultyType value,
	                               JsonSerializer serializer)
	{
		switch (value)
		{
			case DifficultyType.Easy:
				writer.WriteValue(Easy);
				break;
			case DifficultyType.Hard:
				writer.WriteValue(Hard);
				break;
			case DifficultyType.Extreme:
				writer.WriteValue(Extreme);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(value), value, null);
		}
	}

	public override DifficultyType ReadJson(JsonReader     reader,
	                                        Type           objectType,
	                                        DifficultyType existingValue,
	                                        bool           hasExistingValue,
	                                        JsonSerializer serializer)
	{
		var difficultyString = (string)reader.Value;
		return difficultyString switch
		{
			Easy    => DifficultyType.Easy,
			Hard    => DifficultyType.Hard,
			Extreme => DifficultyType.Extreme,
			_ => throw new JsonReaderException(
				     $"The value {difficultyString} should be either {Easy}, {Hard}, or {Extreme}.")
		};
	}
}

﻿// <auto-generated />
//
// To parse this JSON data, add NuGet 'Newtonsoft.Json' then do:
//
//    using okimu_discordPort.Apis.CytoidApi.LeaderboardRecord;
//
//    var leaderboardRecord = LeaderboardRecord.FromJson(jsonString);

using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace okimu_discordPort.Apis.CytoidApi.LeaderboardRecord
{
    public partial class LeaderboardRecord
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("date")]
        public DateTimeOffset Date { get; set; }

        [JsonProperty("score")]
        public long Score { get; set; }

        [JsonProperty("accuracy")]
        public double Accuracy { get; set; }

        [JsonProperty("details")]
        public Details Details { get; set; }

        [JsonProperty("mods")]
        public List<Mod> Mods { get; set; }

        [JsonProperty("owner")]
        public Owner Owner { get; set; }

        [JsonProperty("rank")]
        public long Rank { get; set; }
    }

    public partial class Details
    {
        [JsonProperty("bad")]
        public long Bad { get; set; }

        [JsonProperty("good")]
        public long Good { get; set; }

        [JsonProperty("miss")]
        public long Miss { get; set; }

        [JsonProperty("great")]
        public long Great { get; set; }

        [JsonProperty("perfect")]
        public long Perfect { get; set; }

        [JsonProperty("maxCombo")]
        public long MaxCombo { get; set; }

        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public string Data { get; set; }
    }

    public partial class Owner
    {
        [JsonProperty("id")]
        public Guid Id { get; set; }

        [JsonProperty("uid")]
        public string Uid { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("avatar")]
        public Avatar Avatar { get; set; }
    }

    public partial class Avatar
    {
        [JsonProperty("original")]
        public Uri Original { get; set; }

        [JsonProperty("small")]
        public Uri Small { get; set; }

        [JsonProperty("medium")]
        public Uri Medium { get; set; }

        [JsonProperty("large")]
        public Uri Large { get; set; }
    }

    public enum Mod { 
        Empty,
        Fast, Slow,
        FlipX, FlipY, FlipAll,
        Hard,
        FullCombo, AllPerfect,
        Hyper, Another,
        HideNotes, HideScanline,
    };

    public partial class LeaderboardRecord
    {
        public static List<LeaderboardRecord> FromJson(string json) => JsonConvert.DeserializeObject<List<LeaderboardRecord>>(json, Converter.Settings);
    }

    public static class Serialize
    {
        public static string ToJson(this List<LeaderboardRecord> self) => JsonConvert.SerializeObject(self, Converter.Settings);
    }

    internal static class Converter
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            DateParseHandling = DateParseHandling.None,
            Converters =
            {
                ModConverter.Singleton,
                new IsoDateTimeConverter { DateTimeStyles = DateTimeStyles.AssumeUniversal }
            },
        };
    }

    internal class ModConverter : JsonConverter
    {
        public override bool CanConvert(Type t) => t == typeof(Mod) || t == typeof(Mod?);

        public override object ReadJson(JsonReader reader, Type t, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var value = serializer.Deserialize<string>(reader);
            switch (value)
            {
                case "":
                    return Mod.Empty;
                case "FC":
                    return Mod.FullCombo;
                case "AP":
                    return Mod.AllPerfect;
                case "FlipX":
                    return Mod.FlipX;
                case "FlipY":
                    return Mod.FlipY;
                case "FlipAll":
                    return Mod.FlipAll;
                case "Fast":
                    return Mod.Fast;
                case "Slow":
                    return Mod.Slow;
                case "Hard":
                    return Mod.Hyper;
                case "ExHard":
                    return Mod.Another;
                case "HideScanline":
                    return Mod.HideScanline;
                case "HideNotes":
                    return Mod.HideNotes;
            }
            throw new Exception("Cannot unmarshal type Mod");
        }

        public override void WriteJson(JsonWriter writer, object untypedValue, JsonSerializer serializer)
        {
            if (untypedValue == null)
            {
                serializer.Serialize(writer, null);
                return;
            }
            var value = (Mod)untypedValue;
            switch (value)
            {
                case Mod.Empty:
                    serializer.Serialize(writer, "");
                    return;
                case Mod.Fast:
                    serializer.Serialize(writer, "Fast");
                    return;
                case Mod.FlipY:
                    serializer.Serialize(writer, "FlipY");
                    return;
                case Mod.HideScanline:
                    serializer.Serialize(writer, "HideScanline");
                    return;
            }
            throw new Exception("Cannot marshal type Mod");
        }

        public static readonly ModConverter Singleton = new ModConverter();
    }
}
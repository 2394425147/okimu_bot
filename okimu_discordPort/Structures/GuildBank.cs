using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using okimu_discordPort.Properties;

namespace okimu_discordPort.Structures;

public class GuildBank
{
	public Dictionary<string, GuildConfig> Guilds = new();

	public void Load()
	{
		if (!File.Exists(Resources.CONFIG_GUILD))
			File.Create(Resources.CONFIG_GUILD).Close();

		var backupUserData = JsonConvert.DeserializeObject<Dictionary<string, GuildConfig>>(
		 File.ReadAllText(Resources.CONFIG_GUILD));

		if (backupUserData == null)
			return;

		Guilds = backupUserData;
	}

	public void Save()
	{
		File.WriteAllText(Resources.CONFIG_GUILD,
		                  JsonConvert.SerializeObject(Guilds));
	}

	public GuildConfig GetGuildOrCreate(string id)
	{
		if (!Guilds.ContainsKey(id)) 
			Guilds.Add(id, new GuildConfig());
		
		return Guilds[id];
	}
}
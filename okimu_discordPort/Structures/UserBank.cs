using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using okimu_discordPort.Properties;

namespace okimu_discordPort.Structures;

public sealed class UserBank
{
	public Dictionary<string, UserConfig> Users = new();

	public void Load()
	{
		if (!File.Exists(Resources.CONFIG_USER))
			File.Create(Resources.CONFIG_USER).Close();

		var backupUserData = JsonConvert.DeserializeObject<Dictionary<string, UserConfig>>(
		 Encoding.UTF8.GetString(File.ReadAllBytes(Resources.CONFIG_USER)));

		if (backupUserData == null)
			return;

		Users = backupUserData;
	}

	public void Save()
	{
		File.WriteAllText(Resources.CONFIG_USER,
		                  JsonConvert.SerializeObject(Users));
	}

	public UserConfig GetUserOrCreate(string id)
	{
		Users.TryAdd(id, new UserConfig());
		return Users[id];
	}
}

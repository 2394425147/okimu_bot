using System.Collections.Generic;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace okimu_discordPort.Matchmaking
{
    public static class MultiHost
    {
        public static readonly List<BaseRoom> Lobby = new();

        internal static async Task MessageAll(DiscordChannel hostChannel, List<DiscordChannel> channels,
            DiscordMessageBuilder dmb)
        {
            if (hostChannel != null)
                await hostChannel.SendMessageAsync(dmb);
            else
            {
                async void Action(DiscordChannel channel) => await channel.SendMessageAsync(dmb);

                channels.ForEach(Action);
            }
        }

        public abstract class PlaylistRoom : BaseRoom
        {
            public abstract Task<bool> Enqueue(MessageCreateEventArgs e, List<string> cmd);
            protected bool FreeEnqueue = true;
            protected int BreakDuration = 3;
            
            protected PlaylistRoom(string name, DiscordUser host, DiscordChannel hostPrivateChannel) : base(name, host, hostPrivateChannel)
            {
            }
        }
    }

    public enum RoomBehaviour
    {
        Single = 0,
        Playlist
    }
}
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Interactivity.Extensions;

namespace okimu_discordPort.Matchmaking
{
    public abstract class BaseRoom
    {
        public (RoomBehaviour Type, string alias) RoomType { get; protected init; }
            
        public readonly string UniqueId;

        public           string       RoomName;
        public           string       RoomDescription = "Join my game!";

        public    DiscordUser    Host                { get; }
        private   DiscordChannel HostPrivateChannel  { get; }
        public    DiscordChannel AnnouncementChannel { get; set; }

        public readonly List<DiscordUser> Players = new();
        protected readonly List<DiscordChannel> Channels = new();

        protected int MaxPlayers = 8;
        public int MinPlayers = 2;

        public bool Started;

        protected BaseRoom(string name, DiscordUser host, DiscordChannel hostPrivateChannel)
        {
            UniqueId     = GenerateUid();
            RoomName     = name;

            Host = host;
            HostPrivateChannel = hostPrivateChannel;
        }

        public abstract Task Start();

        public async Task Dispose()
        {
            // Announce disposal message to all players
            var dmb = new DiscordMessageBuilder();
            dmb.WithContent($"{RoomName} - Thank you for playing! \n" +
                            $"This room will now be removed from the lobby, players will be removed from the room automatically.");
                
            await MultiHost.MessageAll(AnnouncementChannel, Channels, dmb);
                
            Players.Clear();
            Channels.Clear();

            MultiHost.Lobby.Remove(this);
        }

        public abstract Task SendInformation(MessageCreateEventArgs e);

        public virtual async Task TryJoin(MessageCreateEventArgs e)
        {
            if (Players.Count != MinPlayers) return;

            var builder = new DiscordMessageBuilder()
                .WithContent("Minimum requirement has been reached! You may start the match at any moment.")
                .AddComponents(
                    new DiscordButtonComponent(ButtonStyle.Primary, "match_begin",
                        "Begin match!"),
                    new DiscordButtonComponent(ButtonStyle.Danger, "match_dispose",
                        "Dispose"));

            var buttonMessage = await builder.SendAsync(HostPrivateChannel);

            var selection = await buttonMessage.WaitForButtonAsync(Host, CancellationToken.None);

            if (selection.TimedOut)
                return;

            switch (selection.Result.Id)
            {
                case "match_begin":
                    await selection.Result.Interaction.CreateResponseAsync(
                        InteractionResponseType.UpdateMessage,
                        new DiscordInteractionResponseBuilder(
                            new DiscordMessageBuilder().WithContent(
                                "Starting match...")));
                    await Start();
                    break;
                case "match_dispose":
                    await Dispose();
                    await selection.Result.Interaction.CreateResponseAsync(
                        InteractionResponseType.UpdateMessage,
                        new DiscordInteractionResponseBuilder(
                            new DiscordMessageBuilder().WithContent(
                                "Match has been disposed!")));
                    break;
            }
        }

        public abstract Task RequestConfigure(MessageCreateEventArgs e);

        public async Task ViewPlayers(MessageCreateEventArgs e)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"Players in this match ({Players.Count}/{MaxPlayers}):");
            Players.ForEach(p => builder.AppendLine($"{p.Username}#{p.Discriminator}"));

            var interactivity = Program.DiscordClient.GetInteractivity();
            var pages = interactivity.GeneratePagesInEmbed(builder.ToString());
                
            await e.Channel.SendPaginatedMessageAsync(e.Author, pages);
        }

        private static string GenerateUid()
        {
            var dt = DateTime.Now;
            return (dt.Second + dt.Minute * 100 + dt.Hour * 10000 +
                    dt.Day * 1000000).ToString();
        }
    }
}

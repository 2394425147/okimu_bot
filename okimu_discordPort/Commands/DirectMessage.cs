using System.Linq;
using System.Threading;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity.Extensions;
using okimu_discordPort.Helpers;
using okimu_discordPort.Matchmaking;
using okimu_discordPort.Matchmaking.Rooms;
using okimu_discordPort.Structures;
using Math = System.Math;

namespace okimu_discordPort.Commands
{
    public static class DirectMessage
    {
        public static readonly CommandsList Commands = new()
        {
            new Command("help", "Check out the help pointer")
            {
                Action = async (e, cmd) => { await Commands.SendHelp(e, cmd); }
            },
            new Command("match", "Create / Set online multiplayer rooms for Cytoid!")
            {
                Action = async (e, cmd) =>
                {
                    var dmb = new DiscordMessageBuilder();
                    var deb = new DiscordEmbedBuilder();
                    var rooms = MultiHost.Lobby;

                    var pageIndex = 0;

                    if (cmd.Count > 0)
                        if (cmd.Last().IsNumber())
                        {
                            pageIndex = int.Parse(cmd.Last()) - 1;
                            cmd.RemoveAt(cmd.LastIndex());
                        }

                    deb.Title = "Multiplayer Lobby:";
                    deb.Description = $"{rooms.Count} active rooms.";

                    if (rooms.Any())
                    {
                        var pages = rooms.SplitBySize(5);

                        pageIndex = Math.Min(pages.Count - 1, pageIndex);

                        var thisPage = pages[pageIndex];

                        thisPage.ForEach(c => { deb.AddField($"{c.RoomName} [{c.UniqueId}]", c.RoomDescription); });

                        deb.WithFooter($"Page {pageIndex + 1} of {pages.Count}");
                    }

                    await dmb.WithEmbed(deb.Build()).SendAsync(e.Channel);
                },
                Children = new CommandsList
                {
                    new("info", "Views the detailed info of a room")
                    {
                        Action = async (e, cmd) =>
                        {
                            if (cmd.Any())
                            {
                                var results = MultiHost.Lobby.Where(r => r.UniqueId == cmd[0]).ToList();

                                if (results.Any())
                                    await results[0].SendInformation(e);
                                else
                                    await e.Message.RespondAsync("Returned no results! :(");
                            }
                            else if (MultiHost.Lobby.Any(r => r.Players.Contains(e.Author)))
                            {
                                var room = MultiHost.Lobby.First(r => r.Players.Contains(e.Author));
                                await room.SendInformation(e);
                            }
                            else
                            {
                                await e.Message.RespondAsync(
                                    "You must either enter a room id or be in a room to view the info!");
                            }
                        }
                    },
                    new("players", "Views a list of players of a room")
                    {
                        Action = async (e, cmd) =>
                        {
                            if (cmd.Any())
                            {
                                var results = MultiHost.Lobby.Where(r => r.UniqueId == cmd[0]).ToList();

                                if (results.Any())
                                    await results[0].ViewPlayers(e);
                                else
                                    await e.Message.RespondAsync("Returned no results! :(");
                            }
                            else if (MultiHost.Lobby.Any(r => r.Players.Contains(e.Author)))
                            {
                                var room = MultiHost.Lobby.First(r => r.Players.Contains(e.Author));
                                await room.ViewPlayers(e);
                            }
                            else
                            {
                                await e.Message.RespondAsync(
                                    "You must either enter a room id or be in a room to view the players!");
                            }
                        }
                    },
                    new("create", "Creates a new room in the multiplayer lobby")
                    {
                        Action = async (e, _) =>
                        {
                            if (MultiHost.Lobby.Any(r => r.Host == e.Author))
                            {
                                await e.Channel.SendMessageAsync("You've already created a room before!");
                                return;
                            }

                            var builder = new DiscordMessageBuilder()
                                .WithContent("Please select a room type that best suits your target!")
                                .AddComponents(
                                    new DiscordButtonComponent(ButtonStyle.Primary,
                                        "match_ctd_single",
                                        "Singular"),
                                    new DiscordButtonComponent(ButtonStyle.Primary,
                                        "match_ctd_playlist",
                                        "Playlist"),
                                    new DiscordButtonComponent(ButtonStyle.Primary,
                                        "match_ctd_challenge",
                                        "Challenge"),
                                    new DiscordButtonComponent(ButtonStyle.Primary,
                                        "match_ctd_gauntlet",
                                        "Gauntlet")
                                );

                            var buttonMessage = await builder.SendAsync(e.Channel);

                            var selection = await buttonMessage.WaitForButtonAsync(e.Author, CancellationToken.None);

                            if (selection.TimedOut)
                                return;

                            await selection.Result.Interaction.CreateResponseAsync(
                                InteractionResponseType.UpdateMessage,
                                new DiscordInteractionResponseBuilder(
                                    new DiscordMessageBuilder().WithContent(
                                        "Booking a room...")));

                            switch (selection.Result.Id)
                            {
                                case "match_ctd_single":
                                    await CytoidSingleRoom.CreateAsync(e);
                                    break;
                                case "match_ctd_playlist":
                                    await CytoidPlaylistRoom.CreateAsync(e);
                                    break;
                                case "match_ctd_challenge":
                                    await CytoidChallengeRoom.CreateAsync(e);
                                    break;
                                case "match_ctd_gauntlet":
                                    await CytoidGauntletRoom.CreateAsync(e);
                                    break;
                            }
                        }
                    },
                    new("config", "View and edit room settings")
                    {
                        Action = async (e, _) =>
                        {
                            if (MultiHost.Lobby.All(r => r.Host != e.Author))
                            {
                                await e.Channel.SendMessageAsync("You haven't created a room yet!");
                                return;
                            }

                            var room = MultiHost.Lobby.First(r => r.Host == e.Author);
                            await room.RequestConfigure(e);
                        }
                    },
                    new("start", "Starts a match created by you")
                    {
                        Action = async (e, _) =>
                        {
                            var matches = MultiHost.Lobby.Where(r => r.Host == e.Author).ToList();

                            if (matches.Any())
                            {
                                if (matches[0].Players.Count >= matches[0].MinPlayers)
                                {
                                    await e.Channel.SendMessageAsync("Match has started!");
                                    await matches[0].Start();
                                }
                                else
                                {
                                    await e.Channel.SendMessageAsync(
                                        "Too few players to start the game! Consider inviting friends over to join you!");
                                }
                            }
                            else
                            {
                                await e.Channel.SendMessageAsync("You haven't created any rooms yet!");
                            }
                        }
                    },
                    new("enqueue", "Adds a song to your room's queue (**playlist mode** only)")
                    {
                        Action = async (e, cmd) =>
                        {
                            var hostRoom = MultiHost.Lobby.Where(r => r.Host.Id == e.Author.Id).ToList();

                            if (hostRoom.Any())
                                if (hostRoom[0].RoomType.Type == RoomBehaviour.Playlist)
                                    await ((MultiHost.PlaylistRoom)hostRoom[0]).Enqueue(e, cmd);
                        }
                    },
                    new("dispose", "Disposes a room you've created")
                    {
                        Action = async (e, _) =>
                        {
                            var matches = MultiHost.Lobby.Where(r => r.Host == e.Author).ToList();

                            if (matches.Any())
                            {
                                if (matches[0].Started)
                                {
                                    await e.Channel.SendMessageAsync(
                                        "Match has already started! For the sake of others' experience, you'll be unable to dispose this room at this time.");
                                }
                                else
                                {
                                    await matches[0].Dispose();
                                    await e.Channel.SendMessageAsync("Match has been disposed!");
                                }
                            }
                            else
                            {
                                await e.Channel.SendMessageAsync("You haven't created any rooms yet!");
                            }
                        }
                    }
                }
            },
            new Command("config", "Edit your preferences")
            {
                Action = async (e, _) => { await e.Author.ToUser().ChangeConfig(e); }
            }
        };
    }
}
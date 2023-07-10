using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Enums;
using DSharpPlus.Interactivity.Extensions;
using ImageMagick;
using okimu_discordPort.Apis.CytoidApi.LeaderboardRecord;
using okimu_discordPort.Communication;
using okimu_discordPort.Events.Challenge;
using okimu_discordPort.Events.Challenge.Data;
using okimu_discordPort.Helpers;
using okimu_discordPort.Matchmaking;
using okimu_discordPort.Properties;
using okimu_discordPort.Structures;
using UserStatus = okimu_discordPort.Structures.UserStatus;

namespace okimu_discordPort.Commands
{
    public static class GuildMessage
    {
        public static readonly CommandsList Commands = new()
        {
            new Command("help", Resources.DESC_HELP)
            {
                Action = async (e, cmd) => { await Commands.SendHelp(e, cmd); }
            },
            new Command("ctd", "Probes your most recent Cytoid score", "Various commands for the game Cytoid")
            {
                Children = new CommandsList
                           {
                               new("ranking", "Gets the ranking data for the specified level id (optional: difficulty)")
                               {
                                   Action = async (e, cmd) =>
                                            {
                                                var details = await CytoidClient.Instance.GetLevelInfo(cmd[0]);

                                                List<LeaderboardRecord> info;

                                                string diffInfo;

                                                if (cmd.Count > 1 && details.Charts.Any(c => c.Type == cmd[1]))
                                                {
                                                    info = await CytoidClient.Instance.GetLeaderboard(details.Uid, cmd[1]);
                                                    diffInfo = cmd[1];
                                                }
                                                else
                                                {
                                                    info = await CytoidClient.Instance.GetLeaderboard(details.Uid, details.Charts[0].Type);
                                                    diffInfo = details.Charts[0].Type;
                                                }

                                                var pages = new List<Page>();

                                                // Populate the pages
                                                var list = info.SplitBySize(5);
                                                for (var pageIndex = 0; pageIndex < list.Count; pageIndex++)
                                                {
                                                    var deb = new DiscordEmbedBuilder()
                                                              .WithTitle(details.Title + $" [{diffInfo}]")
                                                              .WithDescription($"{5 * pageIndex + 1} ~ {5 * pageIndex + list[pageIndex].Count}:");

                                                    for (var index = 0; index < list[pageIndex].Count; index++)
                                                    {
                                                        var i = list[pageIndex][index];
                                                        deb.AddField($"{5 * pageIndex + index + 1} - {i.Owner.Uid}",
                                                                     $"{i.Score} / {i.Accuracy * 100:0.000000}");
                                                    }

                                                    pages.Add(new Page
                                                              {
                                                                  Embed = deb
                                                              });
                                                }

                                                await e.Channel.SendPaginatedMessageAsync(e.Author, pages, PaginationBehaviour.WrapAround,
                                                    ButtonPaginationBehavior.DeleteButtons, CancellationToken.None);

                                                if (e.Author.ToUser().CytoidId
                                                     .IsNullOrEmpty()) return;

                                                var myScore = info.FirstOrDefault(i =>
                                                    i.Owner.Uid == e.Author.ToUser()
                                                                    .CytoidId);
                                                if (myScore != null)
                                                    await e.Message.RespondAsync(new DiscordMessageBuilder().WithEmbed(
                                                         new DiscordEmbedBuilder()
                                                             .WithTitle($"My ranking (Placed {(info.IndexOf(myScore) + 1).ToPlacement()}):")
                                                             .AddField("Score", $"{myScore.Score}")
                                                             .AddField("Perfect", $"{myScore.Details.Perfect}")
                                                             .AddField("Great", $"{myScore.Details.Great}")
                                                             .AddField("Good", $"{myScore.Details.Good}")
                                                             .AddField("Bad", $"{myScore.Details.Bad}")
                                                             .AddField("Miss", $"{myScore.Details.Miss}")
                                                             .AddField("Accuracy",
                                                                       $"{(float)myScore.Accuracy * 100:0.00000}%")
                                                        ));
                                            }
                               },
                               new("search", "Query for a level with your search")
                               {
                                   Action = async (e, cmd) =>
                                            {
                                                var results = await CytoidClient.Instance.Search(cmd.JoinArgs());
                                                var topResults = results.Take(Math.Min(5, results.Count)).ToList();

                                                var deb = new DiscordEmbedBuilder()
                                                          .WithTitle($"Search results for {cmd.JoinArgs()}:")
                                                          .WithDescription(results.Count == 0
                                                                               ? "No results found"
                                                                               : $"Top {topResults.Count}:");

                                                for (var index = 0; index < topResults.Count; index++)
                                                {
                                                    deb.AddField($"{index + 1}: {topResults[index].Title}",
                                                                 $"{topResults[index].Uid}");
                                                }

                                                await e.Message.RespondAsync(new DiscordMessageBuilder().WithEmbed(deb));
                                            }
                               }
                           }
            },
            new Command("match", "View a list of ongoing matches",
                "View and join online multiplayer rooms for rhythm games!")
            {
                Action = async (e, cmd) =>
                {
                    try
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
                    }
                    catch (Exception ex)
                    {
                        await CoreLogic.LogError(e, ex);
                    }
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
                    new("join", "Joins a multiplayer room based on its room id!")
                    {
                        Action = async (e, cmd) =>
                        {
                            if (cmd.Any())
                            {
                                var results = MultiHost.Lobby.Where(r => r.UniqueId == cmd[0]).ToList();

                                if (results.Any())
                                    await results[0].TryJoin(e);
                                else
                                    await e.Message.RespondAsync("This room doesn't exist! :(");
                            }
                            else
                            {
                                await e.Message.RespondAsync("Please enter a UID to enter!");
                            }
                        }
                    },
                    new("leave", "Exits a room you've previously joined")
                    {
                        Action = async (e, _) =>
                        {
                            var results = MultiHost.Lobby.Where(r => r.Players.Contains(e.Author)).ToList();

                            if (results.Any())
                            {
                                results[0].Players.Remove(e.Author);
                                await e.Message.RespondAsync("You left the room!");
                            }
                            else
                            {
                                await e.Message.RespondAsync("You're currently outside of all the rooms!");
                            }
                        }
                    },
                    new("host", "Makes the bot only send result data here.")
                    {
                        Action = async (e, _) =>
                        {
                            var matches = MultiHost.Lobby.Where(r => r.Host == e.Author).ToList();

                            if (matches.Any())
                            {
                                matches[0].AnnouncementChannel = e.Channel;
                                await e.Message.RespondAsync(
                                    $"This channel will now be the announcement channel for the game {matches[0].RoomName}");
                            }
                            else
                            {
                                await e.Message.RespondAsync("You haven't created any rooms yet!");
                            }
                        }
                    },
                    new("enqueue", "Adds a song to your room's queue (**playlist mode** only)")
                    {
                        Action = async (e, cmd) =>
                        {
                            var roomMatches = MultiHost.Lobby.Where(r => r.Players.Any(p => p.Id == e.Author.Id))
                                .ToList();

                            if (roomMatches.Any())
                            {
                                var hostRoom = roomMatches[0];
                                if (hostRoom.RoomType.Type == RoomBehaviour.Playlist)
                                    await ((MultiHost.PlaylistRoom)hostRoom).Enqueue(e, cmd);
                            }
                        }
                    }
                }
            },
            new Command("challenger", "View the current challenge event", "View ongoing challenge events!")
            {
                Action = async (e, _) =>
                {
                    if (!ChallengeHost.IsBossAlive)
                    {
                        if (ChallengeHost.CurrentChallenger == null)
                        {
                            await e.Message.RespondAsync("Hmm... It doesn't seem like I've found any bosses since I last woke up. Would you mind summoning one right now?");
                            await Challenger.CreateAsync(e);
                        }
                        else if (ChallengeHost.CurrentChallenger.Attempts[^1].Trier.Id == e.Author.Id)
                            await Challenger.CreateAsync(e);
                        else
                            await e.Message.RespondAsync("There aren't any bosses around uwu");
                    }
                    else
                        await e.Message.RespondAsync(ChallengeHost.CurrentChallenger.GetDescriptionEmbed());
                }
            },
            new Command("set", null, "Settings for this server / channel")
            {
                Children = new CommandsList
                {
                    new("active",
                        "Determines whether the bot should respond to messages in this channel. [true / false]")
                    {
                        Condition = e => e.Guild.Permissions == Permissions.Administrator,
                        Action = async (e, cmd) =>
                        {
                            if (cmd.Any())
                            {
                                switch (cmd[0])
                                {
                                    case "true":
                                        e.Guild.ToGuild().BannedChannels.Remove(e.Channel.Id);
                                        await e.Message.RespondAsync(
                                            "Got it! I'll process messages in this channel now.");
                                        break;
                                    case "false":
                                        e.Guild.ToGuild().BannedChannels.Add(e.Channel.Id);
                                        await e.Message.RespondAsync(
                                            "Got it! I'll stop processing messages in this channel now.");
                                        break;
                                }
                            }
                        }
                    }
                }
            },
            new Command("profile", "Check your own profile")
            {
                Action = async (e, _) =>
                {
                    var user = e.Author.ToUser();

                    using var image = new MagickImage(new MagickColor(255, 255, 255), 675, 4 * 156);

                    void DrawField(string title, string content, double y)
                    {
                        new Drawables()
                            .FillColor(new MagickColor(24, 24, 24))
                            .StrokeWidth(0)
                            .RoundRectangle(20, y + 20, image.Width - 20, y + 136,
                                20, 20)
                            .Draw(image);

                        new Drawables()
                            .Font("./cal-sans.otf")
                            .TextAlignment(TextAlignment.Left)
                            .FontPointSize(24)
                            .FillColor(new MagickColor("#888"))
                            .Text(40, y + 64, title)
                            .Draw(image);

                        new Drawables()
                            .Font("./cal-sans.otf")
                            .TextAlignment(TextAlignment.Left)
                            .FontPointSize(42)
                            .FillColor(new MagickColor("#fff"))
                            .Text(40, y + 106, content)
                            .Draw(image);
                    }

                    string PreventOverflow(string text, double ptSize)
                    {
                        var availableWidth = image.Width - 40f;
                        return text[..Math.Min(text.Length, (int)Math.Floor(availableWidth / (ptSize * 0.48f)))];
                    }

                    DrawField("Username", PreventOverflow(e.Author.Username, 42), 0);
                    DrawField("Cytoid ID", PreventOverflow(user.CytoidId, 42), 156);
                    DrawField("Status", PreventOverflow(user.Status.ToString(), 42), 156 * 2);
                    DrawField("Tokens", PreventOverflow(user.Tokens.ToString(), 42), 156 * 3);

                    await image.SendAsResponse(e.Message);
                }
            },
            new Command("tip", "Tips a player 30 tokens")
            {
                Action = async (e, _) =>
                {
                    switch (e.MentionedUsers.Count)
                    {
                        case < 1:
                            await e.Message.RespondAsync("You need to mention a user!");
                            return;
                        case > 1:
                            await e.Message.RespondAsync("You can only tip one user at a time!");
                            return;
                    }

                    if (e.MentionedUsers[0].Id == e.Author.Id)
                    {
                        await e.Message.RespondAsync("You can't tip yourself!");
                        return;
                    }

                    var user = e.MentionedUsers[0].ToUser();

                    if (e.Author.ToUser().Tokens < 30)
                    {
                        await e.Message.RespondAsync("You don't have enough tokens to tip (30)!");
                        return;
                    }

                    user.Tokens += 30;
                    e.Author.ToUser().Tokens -= 30;

                    await e.Message.RespondAsync($"You tipped {e.MentionedUsers[0].Username} 30 tokens!");
                }
            },
            new Command("info", "Get runtime info")
            {
                Action = async (e, _) =>
                {
                    var deb = new DiscordEmbedBuilder();
                    deb.AddField("Version", Configuration.Version)
                        .AddField("Users", UserBank.Users.Count.ToString())
                        .AddField("Servers", GuildBank.Guilds.Count.ToString());

                    await e.Message.RespondAsync(deb);
                }
            },
            new Command("paint", "Procedural image generation commands")
            {
                Children =
                {
                    new Command("perfectify", "Pictures ASCII text as perfect (Provide text)")
                    {
                        Action = async (e, cmd) =>
                        {
                            const int cellSize = 64;
                            var colors = new MagickColor[]
                            {
                                new(0, 0, 0),
                                new(255, 255, 255),
                                new(255, 0, 255)
                            };
                            
                            var text = string.Join(" ", cmd).Replace(' ', '_').ToLower();
                            
                            // validate string
                            var regex = new Regex("^[_a-z]*$");
                            if (!regex.IsMatch(text))
                            {
                                await e.Message.RespondAsync("Please ensure that your input consists of only spaces, underscores, and a-z!");
                                return;
                            }

                            using var image = new MagickImage(new MagickColor(0, 0, 0), text.Length * cellSize, cellSize * 3);

                            for (var index = 0; index < text.Length; index++)
                            {
                                var character = text[index];
                                
                                if (character == '_')
                                    continue;

                                var charValue = character - 'a' + 1;

                                for (var i = 2; i >= 0; i--)
                                {
                                    var currentPower = (int)Math.Pow(3, i);
                                    if (charValue < currentPower)
                                        continue;

                                    var count = charValue / currentPower;
                                    charValue %= currentPower;
                                    
                                    var drawable = new Drawables()
                                        .FillColor(colors[count])
                                        .StrokeWidth(0)
                                        .Rectangle(index * cellSize, i * cellSize, (index + 1) * cellSize, (i + 1) * cellSize);

                                    drawable.Draw(image);
                                }
                            }

                            await image.SendAsResponse(e.Message);
                        }
                    },
                    
                    new Command("perfectifyblue", "Pictures ASCII text as perfect (Provide text)")
                    {
                        Action = async (e, cmd) =>
                        {
                            const int cellSize = 64;
                            var colors = new MagickColor[]
                            {
                                new(0, 0, 0),
                                new(255, 255, 255),
                                new(6, 103, 255)
                            };
                            
                            var text = string.Join(" ", cmd).Replace(' ', '_').ToLower();
                            
                            // validate string
                            var regex = new Regex("^[_a-z]*$");
                            if (!regex.IsMatch(text))
                            {
                                await e.Message.RespondAsync("Please ensure that your input consists of only spaces, underscores, and a-z!");
                                return;
                            }

                            using var image = new MagickImage(new MagickColor(0, 0, 0), text.Length * cellSize, cellSize * 3);

                            for (var index = 0; index < text.Length; index++)
                            {
                                var character = text[index];
                                
                                if (character == '_')
                                    continue;

                                var charValue = character - 'a' + 1;

                                for (var i = 2; i >= 0; i--)
                                {
                                    var currentPower = (int)Math.Pow(3, i);
                                    if (charValue < currentPower)
                                        continue;

                                    var count = charValue / currentPower;
                                    charValue %= currentPower;
                                    
                                    var drawable = new Drawables()
                                        .FillColor(colors[count])
                                        .StrokeWidth(0)
                                        .Rectangle(index * cellSize, i * cellSize, (index + 1) * cellSize, (i + 1) * cellSize);

                                    drawable.Draw(image);
                                }
                            }

                            await image.SendAsResponse(e.Message);
                        }
                    }
                }
            }
        };
    }
}
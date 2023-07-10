using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using okimu_discordPort.Helpers;

namespace okimu_discordPort.Structures
{
    public class CommandsList : List<Command>
    {
        public async Task SendHelp(MessageCreateEventArgs e, List<string> parameters)
        {
            var dmb = new DiscordMessageBuilder();
            var deb = new DiscordEmbedBuilder();
            var children = this;

            var pageIndex = 0;

            if (parameters.Count > 0 && parameters.Last().IsNumber())
            {
                pageIndex = int.Parse(parameters.Last()) - 1;
                parameters.RemoveAt(parameters.LastIndex());
            }

            if (TryFind(parameters, out var command))
            {
                var name = $"{command.Accessor}";
                deb.Title = name;

                children = command.Children;

                if (!string.IsNullOrEmpty(command.Help))
                    deb.Description = command.Help;
            }
            else
            {
                deb.Title = "Command index:";
                deb.Description = $"To start using okimu, prepend your message with \"{Configuration.Prefix}\"";
            }

            if (children.Any())
            {
                var pages = children.SplitBySize(5);

                pageIndex = Math.Min(pages.Count - 1, pageIndex);

                var thisPage = pages[pageIndex];

                thisPage.ForEach(c =>
                {
                    var name = c.Action != Command.DefaultAction ? $"**{c.Accessor}**" : $"{c.Accessor}";

                    if (c.Children.Any())
                        name += "[...]";

                    deb.AddField(name,
                        string.IsNullOrEmpty(c.ContainerHelp)
                            ? string.IsNullOrEmpty(c.Help) ? "No help available" : c.Help
                            : c.ContainerHelp);
                });

                deb.WithFooter($"Page {pageIndex + 1} of {pages.Count}");
            }

            await dmb.WithEmbed(deb.Build()).SendAsync(e.Channel);
        }

        public Command GetCommandByPath(List<string> parameters)
        {
            if (parameters.Count < 1)
                return null;

            var result = this.Where(c => c.Accessor == parameters[0]).ToList();

            if (result.Count < 1)
                return null;

            var command = result[0];
            parameters.RemoveAt(0);

            while (parameters.Count > 0 && command.Children.Exists(c => c.Accessor == parameters[0]))
            {
                command = command.Children.First(c => c.Accessor == parameters[0]);
                parameters.RemoveAt(0);
            }

            return command;
        }

        public bool TryFind(List<string> parameters, out Command command)
        {
            command = GetCommandByPath(parameters);
            return command != null;
        }
    }
}
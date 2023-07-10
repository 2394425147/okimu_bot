using System;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.EventArgs;
using DSharpPlus.Interactivity;

namespace okimu_discordPort.Helpers.DiscordExtensions
{
    public class DiscordInteractiveButton
    {
        public readonly ButtonStyle Style;
        public readonly string Label;
        public readonly string Guid;

        public readonly Func<InteractivityResult<ComponentInteractionCreateEventArgs>, Task> Action;

        public DiscordInteractiveButton(ButtonStyle style, string label,
            Func<InteractivityResult<ComponentInteractionCreateEventArgs>, Task> action)
        {
            Style = style;
            Label = label;
            Action = action;

            Guid = System.Guid.NewGuid().ToString("N");
        }
    }
}
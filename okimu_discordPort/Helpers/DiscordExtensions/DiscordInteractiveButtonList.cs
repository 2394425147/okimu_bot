using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.EventArgs;
using DSharpPlus.Interactivity;

namespace okimu_discordPort.Helpers.DiscordExtensions
{
    public class DiscordInteractiveButtonList
    {
        public readonly List<DiscordInteractiveButton> Buttons = new();
        
        public DiscordInteractiveButtonList WithButton(ButtonStyle style, string label,
            Func<InteractivityResult<ComponentInteractionCreateEventArgs>, Task> action)
        {
            Buttons.Add(new DiscordInteractiveButton(style, label, action));
            return this;
        }
    }
}
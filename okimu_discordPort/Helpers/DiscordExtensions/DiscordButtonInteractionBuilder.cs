using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity.Extensions;
using MoreLinq;

namespace okimu_discordPort.Helpers.DiscordExtensions
{
    public class DiscordButtonInteractionBuilder
    {
        private readonly List<List<DiscordInteractiveButton>> _buttonRows;
        private DiscordMessageBuilder _builder;

        public DiscordButtonInteractionBuilder()
        {
            _buttonRows = new List<List<DiscordInteractiveButton>>();
            _builder = new DiscordMessageBuilder();
        }

        public DiscordButtonInteractionBuilder BuildOn(DiscordMessageBuilder existingBuilder)
        {
            _builder = existingBuilder;
            return this;
        }

        public DiscordButtonInteractionBuilder WithRow(Action<DiscordInteractiveButtonList> edit)
        {
            var newRow = new DiscordInteractiveButtonList();
            edit(newRow);
            _buttonRows.Add(newRow.Buttons);
            return this;
        }

        public async Task SendAsync(DiscordChannel channel, DiscordUser user)
        {
            Compile();
            await WaitForButtonAsync(await _builder.SendAsync(channel), user, CancellationToken.None);
        }

        public DiscordMessageBuilder Compile()
        {
            foreach (var buttonRow in _buttonRows)
            {
                _builder.AddComponents(buttonRow.Select(button => new DiscordButtonComponent(button.Style, button.Guid,
                    button.Label)));
            }
            
            return _builder;
        }

        public async Task WaitForButtonAsync(DiscordMessage sentMessage, DiscordUser user, CancellationToken cancellationToken)
        {
            if (sentMessage == null)
                throw new InvalidOperationException("You must compile the message before waiting for a button.");
            
            var selection = await sentMessage.WaitForButtonAsync(user, cancellationToken);

            if (selection.TimedOut)
                return;
            
            var first = _buttonRows.Flatten().Select(button => (DiscordInteractiveButton)button).First(button => button.Guid == selection.Result.Id);
            
            first.Action?.Invoke(selection);
        }
    }
}
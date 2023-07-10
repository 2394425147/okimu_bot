using System.Collections.Generic;
using System.Threading.Tasks;
using DSharpPlus.EventArgs;

namespace okimu_discordPort.Structures
{
    public class Command
    {
        public static readonly CommandAction DefaultAction = (_, _) => Task.CompletedTask;
        
        public readonly string Accessor;
        public readonly string Help;
        public readonly string ContainerHelp;

        public CommandAction Action = DefaultAction;
        public Criteria Condition = _ => true;
        public CommandsList Children = new();

        public Command(string accessor, string help, string containerHelp = "")
        {
            Accessor = accessor;
            Help = help;
            ContainerHelp = containerHelp;
        }

        public async Task Invoke(MessageCreateEventArgs e, List<string> parameters)
        {
            if (Condition(e))   
                await Action.Invoke(e, parameters);
        }

        public delegate Task CommandAction(MessageCreateEventArgs e, List<string> cmd);
        public delegate bool Criteria(MessageCreateEventArgs e);
    }
}
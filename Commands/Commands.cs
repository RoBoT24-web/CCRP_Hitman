using System.Collections.Generic;
using System.Globalization;
using Rocket.API;
using Rocket.Unturned.Player;

namespace Hitman
{
    public class CommandHit : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "hit";
        public string Help => "Place a bounty on a player.";
        public string Syntax => "<player> <bounty>";
        public List<string> Aliases => new List<string>();
        public List<string> Permissions => new List<string> { "hit" };

        public void Execute(IRocketPlayer caller, string[] command)
        {
            var plugin = HitmanPlugin.Instance;
            var config = plugin.Configuration.Instance.HitsConfiguration;
            var hirer = (UnturnedPlayer)caller;

            if (command.Length != 2)
            {
                plugin.Tell(caller, plugin.Format("HitSyntax"));
                return;
            }

            if (!decimal.TryParse(command[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var bounty))
            {
                plugin.Tell(caller, plugin.Format("HitSyntax"));
                return;
            }

            if (bounty < config.MinimumBounty || bounty <= 0)
            {
                plugin.Tell(caller, plugin.Format("InvalidBounty", "MinimumBounty", config.MinimumBounty));
                return;
            }

            var target = UnturnedPlayer.FromName(command[0]);
            if (target == null)
            {
                plugin.Tell(caller, plugin.Format("InvalidTarget", "TargetPlayerName", command[0]));
                return;
            }

            if (!config.CanPlaceOnSelf && target.CSteamID == hirer.CSteamID)
            {
                plugin.Tell(caller, plugin.Format("CannotSelfPlace"));
                return;
            }

            if (HitmanPlugin.Has(target, HitmanPlugin.PermissionExempt))
            {
                plugin.Tell(caller, plugin.Format("TargetHitExempt", "TargetPlayerName", target.CharacterName));
                return;
            }

            plugin.PlaceHit(hirer, target, decimal.Round(bounty, 2));
        }
    }

    public class CommandHitUI : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "hitui";
        public string Help => "Toggle the hit list UI.";
        public string Syntax => "";
        public List<string> Aliases => new List<string>();
        public List<string> Permissions => new List<string> { "hitman.ui" };

        public void Execute(IRocketPlayer caller, string[] command)
        {
            var plugin = HitmanPlugin.Instance;
            var player = (UnturnedPlayer)caller;

            if (!HitmanPlugin.Has(player, HitmanPlugin.PermissionUI))
            {
                plugin.Tell(caller, plugin.Format("UINoPermission"));
                return;
            }

            if (plugin.UI.IsViewing(player)) plugin.UI.Hide(player);
            else plugin.UI.Show(player);
        }
    }
}

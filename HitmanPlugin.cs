using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Rocket.API;
using Rocket.API.Collections;
using Rocket.Core;
using Rocket.Core.Logging;
using Rocket.Core.Plugins;
using Rocket.Core.Utils;
using Rocket.Unturned;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Events;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace Hitman
{
    public class HitmanPlugin : RocketPlugin<HitmanPluginConfiguration>
    {
        public const string PermissionHitman = "hitman";
        public const string PermissionUI = "hitman.ui";
        public const string PermissionExempt = "hit.exempt";

        private static readonly Color MessageColor = Color.yellow;
        private static readonly Regex TokenRegex = new Regex(@"\{(\w+)(?::([^}]*))?\}", RegexOptions.Compiled);

        public static HitmanPlugin Instance { get; private set; }

        public HitUIService UI { get; private set; }

        private IHitRepository repository;
        private readonly object sync = new object();
        private readonly List<Hit> hits = new List<Hit>();
        private readonly HashSet<ulong> pendingTargets = new HashSet<ulong>();

        public override TranslationList DefaultTranslations => new TranslationList
        {
            { "HitPlacedHirer", "You have placed a hit on {TargetPlayerName} for ${Bounty:0.00}" },
            { "HitPlacedTarget", "{HirerPlayerName} has placed a hit on you for ${Bounty:0.00}" },
            { "HitPlacedAnnounce", "{HirerPlayerName} has placed a hit on {TargetPlayerName} for ${Bounty:0.00}" },
            { "HitCompletedTarget", "{KillerPlayerName} has claimed the bounty of ${Bounty:0.00} on your head." },
            { "HitCompletedAnnounce", "{KillerPlayerName} has claimed the bounty of ${Bounty:0.00} on {TargetPlayerName}'s head." },
            { "HitCompletedKiller", "You have successfully claimed the bounty of ${Bounty:0.00} on {TargetPlayerName}'s head." },
            { "HitExpiredTarget", "A hit placed on your head for ${Bounty:0.00} has expired." },
            { "HitExpiredHirer", "A hit you placed for ${Bounty:0.00} has expired." },
            { "UIHeader", "Current Hits:" },
            { "UIPlayerName", "{PlayerName}" },
            { "UIBounty", "${Bounty:0.00}" },
            { "InvalidBounty", "The bounty must be greater than {MinimumBounty}." },
            { "CannotSelfPlace", "You cannot place a bounty on yourself." },
            { "TargetHitExempt", "{TargetPlayerName} cannot have hits placed on them." },
            { "InvalidTarget", "{TargetPlayerName} was not found." },
            { "HitSyntax", "Correct usage: /hit <player> <bounty (number)>" },
            { "HitAlreadyExists", "{TargetPlayerName} already has a hit on their head." },
            { "InvalidBalance", "You do not have enough balance to place this bounty." },
            { "UINoPermission", "You do not have permission to use the UI" }
        };

        protected override void Load()
        {
            Instance = this;
            try
            {
                repository = Configuration.Instance.DatabaseType == DatabaseType.MySQL
                    ? (IHitRepository)new MySqlHitRepository(Configuration.Instance.MySqlConfiguration)
                    : new LiteDbHitRepository(Directory);
                repository.Initialize();
                lock (sync)
                {
                    hits.Clear();
                    hits.AddRange(repository.GetAll());
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed to initialize the hit database, unloading plugin.");
                repository?.Dispose();
                repository = null;
                UnloadPlugin();
                return;
            }

            UI = new HitUIService(this);

            U.Events.OnPlayerConnected += OnPlayerConnected;
            U.Events.OnPlayerDisconnected += OnPlayerDisconnected;
            UnturnedPlayerEvents.OnPlayerDeath += OnPlayerDeath;
            UnturnedPlayerEvents.OnPlayerRevive += OnPlayerRevive;

            var interval = Math.Max(1, Configuration.Instance.HitsConfiguration.CheckIntervalInSeconds);
            InvokeRepeating(nameof(CheckExpiredHits), interval, interval);

            Logger.Log($"Hitman loaded with {hits.Count} active hit(s) using {Configuration.Instance.DatabaseType}.");
        }

        protected override void Unload()
        {
            CancelInvoke(nameof(CheckExpiredHits));
            U.Events.OnPlayerConnected -= OnPlayerConnected;
            U.Events.OnPlayerDisconnected -= OnPlayerDisconnected;
            UnturnedPlayerEvents.OnPlayerDeath -= OnPlayerDeath;
            UnturnedPlayerEvents.OnPlayerRevive -= OnPlayerRevive;
            UI?.Dispose();
            UI = null;
            repository?.Dispose();
            repository = null;
            Instance = null;
        }

        // ---------- Helpers ----------

        public string Format(string key, params object[] pairs)
        {
            var raw = Translations.Instance[key] ?? key;
            return TokenRegex.Replace(raw, m =>
            {
                for (var i = 0; i + 1 < pairs.Length; i += 2)
                {
                    if (!string.Equals((string)pairs[i], m.Groups[1].Value, StringComparison.Ordinal)) continue;
                    var value = pairs[i + 1];
                    return m.Groups[2].Success
                        ? string.Format(CultureInfo.InvariantCulture, "{0:" + m.Groups[2].Value + "}", value)
                        : Convert.ToString(value, CultureInfo.InvariantCulture);
                }
                return m.Value;
            });
        }

        public void Tell(IRocketPlayer player, string message)
        {
            if (player != null && !string.IsNullOrEmpty(message))
                UnturnedChat.Say(player, message, MessageColor);
        }

        private static UnturnedPlayer Online(ulong id) => UnturnedPlayer.FromCSteamID(new CSteamID(id));

        // Rocket's HasPermission returns true for admins; look up the granted permissions directly so admins get no implicit access.
        public static bool Has(IRocketPlayer player, string permission) =>
            R.Permissions.GetPermissions(player, new List<string> { permission }).Count > 0;

        public List<Hit> GetHitSnapshot()
        {
            lock (sync) return hits.ToList();
        }

        // ---------- Placing ----------

        public void PlaceHit(UnturnedPlayer hirer, UnturnedPlayer target, decimal bounty)
        {
            var hirerId = hirer.CSteamID.m_SteamID;
            var targetId = target.CSteamID.m_SteamID;
            var hirerName = hirer.CharacterName;
            var targetName = target.CharacterName;
            var hitsConfig = Configuration.Instance.HitsConfiguration;

            // Reserve the target so only one hit exists (or is being placed) per player.
            lock (sync)
            {
                if (hits.Any(h => h.TargetId == targetId) || !pendingTargets.Add(targetId))
                {
                    Tell(hirer, Format("HitAlreadyExists", "TargetPlayerName", targetName));
                    return;
                }
            }

            Task.Run(() =>
            {
                try
                {
                    var economy = EconomyResolver.Resolve();
                    if (economy == null)
                    {
                        Logger.LogError("No economy plugin (AdvancedEconomy or Uconomy) is loaded.");
                        return;
                    }

                    if (economy.GetBalance(hirer.Id) < bounty)
                    {
                        TaskDispatcher.QueueOnMainThread(() => Tell(hirer, Format("InvalidBalance")));
                        return;
                    }

                    economy.Add(hirer.Id, -bounty);

                    var now = DateTime.UtcNow;
                    var hit = new Hit
                    {
                        TargetId = targetId,
                        TargetName = targetName,
                        HirerId = hirerId,
                        HirerName = hirerName,
                        Bounty = bounty,
                        CreatedAt = now,
                        ExpiresAt = now.AddDays(hitsConfig.DurationInDays)
                    };

                    try
                    {
                        repository.Add(hit);
                    }
                    catch
                    {
                        economy.Add(hirer.Id, bounty);
                        throw;
                    }

                    lock (sync) hits.Add(hit);

                    TaskDispatcher.QueueOnMainThread(() =>
                    {
                        var args = new object[] { "HirerPlayerName", hirerName, "TargetPlayerName", targetName, "Bounty", bounty };
                        Tell(hirer, Format("HitPlacedHirer", args));
                        if (hitsConfig.Placed.TellTarget)
                            Tell(Online(targetId), Format("HitPlacedTarget", args));
                        if (hitsConfig.Placed.Announce)
                            UnturnedChat.Say(Format("HitPlacedAnnounce", args), MessageColor);
                        UI?.RefreshAll();
                    });
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, "Failed to place hit.");
                }
                finally
                {
                    lock (sync) pendingTargets.Remove(targetId);
                }
            });
        }

        // ---------- Claiming ----------

        private void OnPlayerDeath(UnturnedPlayer player, EDeathCause cause, ELimb limb, CSteamID murderer)
        {
            if (murderer == CSteamID.Nil || murderer == player.CSteamID) return;

            var killer = UnturnedPlayer.FromCSteamID(murderer);
            if (killer == null) return;

            var hitsConfig = Configuration.Instance.HitsConfiguration;
            if (!Has(killer, PermissionHitman)) return;

            var targetId = player.CSteamID.m_SteamID;
            var killerId = murderer.m_SteamID;
            List<Hit> claimed;
            lock (sync)
            {
                claimed = hits.Where(h => h.TargetId == targetId && h.HirerId != killerId).ToList();
                if (claimed.Count == 0) return;
                hits.RemoveAll(h => claimed.Contains(h));
            }

            var total = claimed.Sum(h => h.Bounty);
            var targetName = player.CharacterName;
            var killerName = killer.CharacterName;

            var args = new object[] { "KillerPlayerName", killerName, "TargetPlayerName", targetName, "Bounty", total };
            Tell(killer, Format("HitCompletedKiller", args));
            if (hitsConfig.Completed.TellTarget)
                Tell(player, Format("HitCompletedTarget", args));
            if (hitsConfig.Completed.Announce)
                UnturnedChat.Say(Format("HitCompletedAnnounce", args), MessageColor);
            UI?.RefreshAll();

            var killerIdString = killer.Id;
            Task.Run(() =>
            {
                try
                {
                    repository.Remove(claimed.Select(h => h.Id));
                    var economy = EconomyResolver.Resolve();
                    if (economy == null)
                        Logger.LogError("No economy plugin loaded; could not pay bounty of " + total + " to " + killerIdString);
                    else
                        economy.Add(killerIdString, total);
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, "Failed to complete hit payout.");
                }
            });
        }

        // ---------- Expiry ----------

        private void CheckExpiredHits()
        {
            List<Hit> expired;
            lock (sync)
            {
                var now = DateTime.UtcNow;
                expired = hits.Where(h => h.ExpiresAt <= now).ToList();
                if (expired.Count == 0) return;
                hits.RemoveAll(h => expired.Contains(h));
            }

            var hitsConfig = Configuration.Instance.HitsConfiguration;
            foreach (var hit in expired)
            {
                var args = new object[] { "HirerPlayerName", hit.HirerName, "TargetPlayerName", hit.TargetName, "Bounty", hit.Bounty };
                if (hitsConfig.Expired.TellTarget)
                    Tell(Online(hit.TargetId), Format("HitExpiredTarget", args));
                if (hitsConfig.Expired.TellHirer)
                    Tell(Online(hit.HirerId), Format("HitExpiredHirer", args));
            }
            UI?.RefreshAll();

            Task.Run(() =>
            {
                try
                {
                    repository.Remove(expired.Select(h => h.Id));
                    if (!hitsConfig.RefundExpired) return;
                    var economy = EconomyResolver.Resolve();
                    if (economy == null)
                    {
                        Logger.LogError("No economy plugin loaded; could not refund expired hits.");
                        return;
                    }
                    foreach (var hit in expired)
                        economy.Add(hit.HirerId.ToString(), hit.Bounty);
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, "Failed to process expired hits.");
                }
            });
        }

        // ---------- Player events ----------

        private void OnPlayerConnected(UnturnedPlayer player)
        {
            if (Configuration.Instance.UIConfiguration.AutoDisplay && Has(player, PermissionUI))
                UI?.Show(player);
        }

        private void OnPlayerDisconnected(UnturnedPlayer player) => UI?.Forget(player);

        private void OnPlayerRevive(UnturnedPlayer player, Vector3 position, byte angle) => UI?.Reshow(player);
    }
}

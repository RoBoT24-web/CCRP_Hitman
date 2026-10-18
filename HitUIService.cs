using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Rocket.Core.Logging;
using Rocket.Core.Steam;
using Rocket.Core.Utils;
using Rocket.Unturned.Player;
using SDG.NetTransport;
using SDG.Unturned;
using Steamworks;

namespace Hitman
{
    public class HitUIService : IDisposable
    {
        private const short EffectKey = 29200;
        private const int MaxRows = 10;
        private const string TitleElement = "HitListTitle";

        private readonly HitmanPlugin plugin;
        private readonly HashSet<ulong> viewers = new HashSet<ulong>();
        private readonly Dictionary<ulong, string> avatars = new Dictionary<ulong, string>();
        private readonly HashSet<ulong> avatarRequests = new HashSet<ulong>();
        private bool warnedMissingEffect;

        public HitUIService(HitmanPlugin plugin)
        {
            this.plugin = plugin;
        }

        private static string Row(int i) => $"Hit ({i})";
        private static string NameElement(int i) => $"HitName ({i})";
        private static string PriceElement(int i) => $"HitPrice ({i})";
        private static string IconElement(int i) => $"HitIcon ({i})";

        private bool TryGetEffectId(out ushort id)
        {
            id = 0;
            if (Guid.TryParse(plugin.Configuration.Instance.UIConfiguration.EffectGuid, out var guid))
            {
                var asset = Assets.find<EffectAsset>(guid);
                if (asset != null)
                {
                    id = asset.id;
                    return true;
                }
            }
            if (!warnedMissingEffect)
            {
                warnedMissingEffect = true;
                Logger.LogWarning("Hit UI effect was not found. Check UIConfiguration.EffectGuid and that the HitUI mod is installed on the server.");
            }
            return false;
        }

        private static ITransportConnection Connection(UnturnedPlayer player) =>
            player.Player.channel.owner.transportConnection;

        public bool IsViewing(UnturnedPlayer player)
        {
            lock (viewers) return viewers.Contains(player.CSteamID.m_SteamID);
        }

        public void Show(UnturnedPlayer player)
        {
            if (!HitmanPlugin.Has(player, HitmanPlugin.PermissionUI)) return;
            if (!TryGetEffectId(out var id)) return;

            var conn = Connection(player);
            EffectManager.askEffectClearByID(id, conn);
            EffectManager.sendUIEffect(id, EffectKey, conn, true);
            lock (viewers) viewers.Add(player.CSteamID.m_SteamID);
            Update(player);
        }

        public void Hide(UnturnedPlayer player)
        {
            lock (viewers) viewers.Remove(player.CSteamID.m_SteamID);
            if (TryGetEffectId(out var id))
                EffectManager.askEffectClearByID(id, Connection(player));
        }

        public void Reshow(UnturnedPlayer player)
        {
            if (IsViewing(player)) Show(player);
        }

        public void Forget(UnturnedPlayer player)
        {
            lock (viewers) viewers.Remove(player.CSteamID.m_SteamID);
        }

        public void RefreshAll()
        {
            ulong[] ids;
            lock (viewers) ids = viewers.ToArray();
            foreach (var steamId in ids)
            {
                var player = UnturnedPlayer.FromCSteamID(new CSteamID(steamId));
                if (player == null) { lock (viewers) viewers.Remove(steamId); continue; }
                if (!HitmanPlugin.Has(player, HitmanPlugin.PermissionUI)) { Hide(player); continue; }
                Update(player);
            }
        }

        private void Update(UnturnedPlayer player)
        {
            var conn = Connection(player);
            var max = Math.Max(0, Math.Min(MaxRows, plugin.Configuration.Instance.UIConfiguration.MaxHitsShown));

            var rows = plugin.GetHitSnapshot()
                .GroupBy(h => h.TargetId)
                .Select(g => new
                {
                    TargetId = g.Key,
                    Name = g.OrderByDescending(h => h.CreatedAt).First().TargetName,
                    Bounty = g.Sum(h => h.Bounty)
                })
                .OrderByDescending(r => r.Bounty)
                .Take(max)
                .ToList();

            EffectManager.sendUIEffectText(EffectKey, conn, true, TitleElement, plugin.Format("UIHeader"));

            for (var i = 0; i < MaxRows; i++)
            {
                if (i >= rows.Count)
                {
                    EffectManager.sendUIEffectVisibility(EffectKey, conn, true, Row(i), false);
                    continue;
                }

                var row = rows[i];
                EffectManager.sendUIEffectVisibility(EffectKey, conn, true, Row(i), true);
                EffectManager.sendUIEffectText(EffectKey, conn, true, NameElement(i), plugin.Format("UIPlayerName", "PlayerName", row.Name));
                EffectManager.sendUIEffectText(EffectKey, conn, true, PriceElement(i), plugin.Format("UIBounty", "Bounty", row.Bounty));
                SetIcon(conn, i, row.TargetId, player.CSteamID.m_SteamID);
            }
        }

        private void SetIcon(ITransportConnection conn, int index, ulong targetId, ulong viewerId)
        {
            string url;
            lock (avatars) avatars.TryGetValue(targetId, out url);

            if (!string.IsNullOrEmpty(url))
            {
                EffectManager.sendUIEffectImageURL(EffectKey, conn, true, IconElement(index), url, true, false);
                return;
            }

            lock (avatars)
            {
                if (!avatarRequests.Add(targetId)) return;
            }

            // Steam profile lookups are web requests, so resolve off the game thread then redraw.
            Task.Run(() =>
            {
                string resolved = string.Empty;
                try
                {
                    resolved = new Profile(targetId).AvatarFull?.ToString() ?? string.Empty;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning("Could not load Steam avatar for " + targetId + ": " + ex.Message);
                }

                lock (avatars)
                {
                    avatars[targetId] = resolved;
                    avatarRequests.Remove(targetId);
                }

                if (!string.IsNullOrEmpty(resolved))
                    TaskDispatcher.QueueOnMainThread(RefreshAll);
            });
        }

        public void Dispose()
        {
            ulong[] ids;
            lock (viewers) { ids = viewers.ToArray(); viewers.Clear(); }
            if (!TryGetEffectId(out var id)) return;
            foreach (var steamId in ids)
            {
                var player = UnturnedPlayer.FromCSteamID(new CSteamID(steamId));
                if (player != null) EffectManager.askEffectClearByID(id, Connection(player));
            }
        }
    }
}

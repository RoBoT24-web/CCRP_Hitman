using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Rocket.Core.Logging;

namespace Hitman
{
    public class DiscordField
    {
        public string Name;
        public string Value;
        public bool Inline = true;
    }

    public class DiscordWebhook
    {
        private const int ColorPlaced = 0xF1C40F;
        private const int ColorCompleted = 0x2ECC71;
        private const int ColorExpired = 0x95A5A6;

        private readonly DiscordConfiguration config;

        public DiscordWebhook(DiscordConfiguration config)
        {
            this.config = config;
            // .NET Framework 4.8 may not negotiate TLS 1.2 by default on older runtimes.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        private bool Active => config != null && config.Enabled && !string.IsNullOrWhiteSpace(config.WebhookUrl);

        public void HitPlaced(Hit hit, int activeHits, int serverPlayers)
        {
            if (!Active || !config.LogPlaced) return;
            var fields = new List<DiscordField>
            {
                new DiscordField { Name = "Placed By", Value = Player(hit.HirerName, hit.HirerId) },
                new DiscordField { Name = "Target", Value = Player(hit.TargetName, hit.TargetId) },
                new DiscordField { Name = "Bounty", Value = Money(hit.Bounty) },
                new DiscordField { Name = "Placed At", Value = Stamp(hit.CreatedAt) },
                new DiscordField { Name = "Expires", Value = Stamp(hit.ExpiresAt) + " (" + Stamp(hit.ExpiresAt, 'R') + ")" },
                new DiscordField { Name = "Hit ID", Value = hit.Id.ToString(CultureInfo.InvariantCulture) },
                new DiscordField { Name = "Active Hits", Value = activeHits.ToString(CultureInfo.InvariantCulture) },
                new DiscordField { Name = "Players Online", Value = serverPlayers.ToString(CultureInfo.InvariantCulture) }
            };
            Send("Hit Placed", ColorPlaced, fields, hit.CreatedAt);
        }

        public void HitCompleted(string killerName, ulong killerId, string targetName, ulong targetId,
            IList<Hit> claimed, decimal total, string cause, string limb, double? distance, int activeHits, bool paid, DateTime? protectedUntil)
        {
            if (!Active || !config.LogCompleted) return;
            var now = DateTime.UtcNow;
            var hirers = new StringBuilder();
            foreach (var h in claimed)
            {
                if (hirers.Length > 0) hirers.Append('\n');
                hirers.Append(Player(h.HirerName, h.HirerId)).Append(" - ").Append(Money(h.Bounty));
            }

            var fields = new List<DiscordField>
            {
                new DiscordField { Name = "Hitman", Value = Player(killerName, killerId) },
                new DiscordField { Name = "Target", Value = Player(targetName, targetId) },
                new DiscordField { Name = "Bounty Paid", Value = Money(total) + (paid ? string.Empty : " (payout pending/failed)") },
                new DiscordField { Name = "Completed At", Value = Stamp(now) },
                new DiscordField { Name = "Cause", Value = cause },
                new DiscordField { Name = "Limb", Value = limb },
            };
            if (distance.HasValue)
                fields.Add(new DiscordField { Name = "Distance", Value = distance.Value.ToString("0.0", CultureInfo.InvariantCulture) + "m" });
            fields.Add(new DiscordField { Name = "Hit Age", Value = Duration(now - claimed[0].CreatedAt) });
            fields.Add(new DiscordField { Name = "Active Hits Left", Value = activeHits.ToString(CultureInfo.InvariantCulture) });
            if (protectedUntil.HasValue)
                fields.Add(new DiscordField { Name = "Target Protected Until", Value = Stamp(protectedUntil.Value) + " (" + Stamp(protectedUntil.Value, 'R') + ")" });
            fields.Add(new DiscordField { Name = claimed.Count > 1 ? "Placed By (" + claimed.Count + " hits)" : "Placed By", Value = hirers.ToString(), Inline = false });
            Send("Hit Completed", ColorCompleted, fields, now);
        }

        public void HitExpired(Hit hit, bool refunded)
        {
            if (!Active || !config.LogExpired) return;
            var now = DateTime.UtcNow;
            var fields = new List<DiscordField>
            {
                new DiscordField { Name = "Placed By", Value = Player(hit.HirerName, hit.HirerId) },
                new DiscordField { Name = "Target", Value = Player(hit.TargetName, hit.TargetId) },
                new DiscordField { Name = "Bounty", Value = Money(hit.Bounty) },
                new DiscordField { Name = "Expired At", Value = Stamp(now) },
                new DiscordField { Name = "Placed At", Value = Stamp(hit.CreatedAt) },
                new DiscordField { Name = "Lasted", Value = Duration(now - hit.CreatedAt) },
                new DiscordField { Name = "Hit ID", Value = hit.Id.ToString(CultureInfo.InvariantCulture) },
                new DiscordField { Name = "Refunded", Value = refunded ? "Yes" : "No" }
            };
            Send("Hit Expired", ColorExpired, fields, now);
        }

        private static string Player(string name, ulong id) =>
            "[" + Escape(name) + "](https://steamcommunity.com/profiles/" + id.ToString(CultureInfo.InvariantCulture) + ")\n`" + id.ToString(CultureInfo.InvariantCulture) + "`";

        private static string Money(decimal amount) => "$" + amount.ToString("N2", CultureInfo.InvariantCulture);

        private static string Stamp(DateTime utc, char style = 'F')
        {
            var seconds = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
            return "<t:" + seconds.ToString(CultureInfo.InvariantCulture) + ":" + style + ">";
        }

        private static string Duration(TimeSpan span)
        {
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            if (span.TotalDays >= 1) return string.Format(CultureInfo.InvariantCulture, "{0}d {1}h {2}m", (int)span.TotalDays, span.Hours, span.Minutes);
            if (span.TotalHours >= 1) return string.Format(CultureInfo.InvariantCulture, "{0}h {1}m", (int)span.TotalHours, span.Minutes);
            return string.Format(CultureInfo.InvariantCulture, "{0}m {1}s", span.Minutes, span.Seconds);
        }

        // Neutralize markdown and mentions in player-controlled names.
        private static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text)) return "Unknown";
            var sb = new StringBuilder();
            foreach (var c in text)
            {
                if ("\\*_~`|>[]()@#:".IndexOf(c) >= 0) sb.Append('\\');
                sb.Append(c);
            }
            return sb.ToString();
        }

        private void Send(string title, int color, List<DiscordField> fields, DateTime timestamp)
        {
            var json = BuildPayload(title, color, fields, timestamp);
            var url = config.WebhookUrl.Trim();
            ThreadPool.QueueUserWorkItem(_ => Post(url, json, 0));
        }

        private string BuildPayload(string title, int color, List<DiscordField> fields, DateTime timestamp)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            if (!string.IsNullOrWhiteSpace(config.Username))
                sb.Append("\"username\":").Append(Quote(config.Username)).Append(',');
            if (!string.IsNullOrWhiteSpace(config.AvatarUrl))
                sb.Append("\"avatar_url\":").Append(Quote(config.AvatarUrl)).Append(',');
            sb.Append("\"allowed_mentions\":{\"parse\":[]},");
            sb.Append("\"embeds\":[{");
            sb.Append("\"title\":").Append(Quote(title)).Append(',');
            sb.Append("\"color\":").Append(color.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"timestamp\":").Append(Quote(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture))).Append(',');
            if (!string.IsNullOrWhiteSpace(config.ServerName))
                sb.Append("\"footer\":{\"text\":").Append(Quote(config.ServerName)).Append("},");
            sb.Append("\"fields\":[");
            for (var i = 0; i < fields.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var f = fields[i];
                var value = string.IsNullOrEmpty(f.Value) ? "-" : f.Value;
                if (value.Length > 1024) value = value.Substring(0, 1021) + "...";
                sb.Append("{\"name\":").Append(Quote(f.Name))
                  .Append(",\"value\":").Append(Quote(value))
                  .Append(",\"inline\":").Append(f.Inline ? "true" : "false").Append('}');
            }
            sb.Append("]}]}");
            return sb.ToString();
        }

        private static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        private static void Post(string url, string json, int attempt)
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Timeout = 10000;
                var body = Encoding.UTF8.GetBytes(json);
                request.ContentLength = body.Length;
                using (var stream = request.GetRequestStream())
                    stream.Write(body, 0, body.Length);
                using (request.GetResponse()) { }
            }
            catch (WebException ex)
            {
                var response = ex.Response as HttpWebResponse;
                if (response != null && (int)response.StatusCode == 429 && attempt < 3)
                {
                    var wait = 2000;
                    var retryAfter = response.Headers["Retry-After"];
                    double seconds;
                    if (retryAfter != null && double.TryParse(retryAfter, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
                        wait = (int)Math.Min(30000, Math.Max(500, seconds * 1000));
                    response.Close();
                    Thread.Sleep(wait);
                    Post(url, json, attempt + 1);
                    return;
                }

                string detail = ex.Message;
                if (response != null)
                {
                    try { using (var reader = new StreamReader(response.GetResponseStream())) detail += " " + reader.ReadToEnd(); }
                    catch { }
                    response.Close();
                }
                Logger.LogWarning("Discord webhook failed: " + detail);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Discord webhook failed: " + ex.Message);
            }
        }
    }
}

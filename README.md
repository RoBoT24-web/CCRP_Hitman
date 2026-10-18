## Features

- Mod: https://steamcommunity.com/sharedfiles/filedetails/?id=3821700895
- Place hits with `/hit <player> <bounty>`; the bounty is deducted up front.
- One active hit per player at a time.
- Claim by killing a target. Permitted killers are paid the bounty.
- Hits persist across disconnects and restarts and expire after a configurable time, with optional refunds.
- LiteDB or MySQL storage.
- Economy through AdvancedEconomy, with Uconomy as a fallback (both detected at runtime; neither is a hard dependency).
- UI list of the top 10 targets by bounty, shown only to players with `hitman.ui`. 

## Commands

| Command | Permission | Description |
|---------|------------|-------------|
| `/hit <player> <bounty>` | `hit` | Place a bounty on an online player. |
| `/hitui` | `hitman.ui` | Toggle the hit list UI. |

## Permissions

| Permission | Purpose |
|------------|---------|
| `hit` | Use `/hit`. |
| `hitman` | Claim bounties by killing a target. |
| `hitman.ui` | See the hit list UI and use `/hitui`. |
| `hit.exempt` | Prevent hits being placed on the player. |

Admins (including Blue Hammer) can still run `/hit` and `/hitui`, since Rocket lets admins run every command. They get no implicit `hitman`, `hitman.ui` or `hit.exempt`; those must be granted explicitly in the permissions file.

## How it works

- **Placing:** `/hit` validates the syntax, `MinimumBounty`, that the target is online, `CanPlaceOnSelf`, `hit.exempt`, that the target has no existing hit, and that you can afford it. The bounty is withdrawn, then the hit is saved. If saving fails, the withdrawal is refunded.
- **Claiming:** when a player with `hitman` kills a listed target, they receive the bounty and the hit is removed. Suicides, kills by non-players, and kills by the player who placed the hit do not count and the hit stays.
- **Expiry:** hits older than `DurationInDays` are removed every `CheckIntervalInSeconds`. If `RefundExpired` is on, the bounty is refunded to the player who placed it. Expiry is based on real time, so hits on offline players keep counting down.
- **Offline players:** hits stay active when the target disconnects, but you can only place new hits on online players.

## Configuration

Generated in the plugin's folder on first load.

| Setting | Default | Description |
|---------|---------|-------------|
| `DatabaseType` | `LiteDB` | `LiteDB` or `MySQL`. |
| `MySqlConfiguration` | see file | Server address, port, username, password, database name, optional `ConnectionString`. |
| `HitsConfiguration.DurationInDays` | `1` | How long a hit lasts. |
| `HitsConfiguration.CheckIntervalInSeconds` | `30` | How often expiry is checked. |
| `HitsConfiguration.CanPlaceOnSelf` | `false` | Allow hits on yourself. |
| `HitsConfiguration.MinimumBounty` | `100` | Lowest allowed bounty. |
| `HitsConfiguration.RefundExpired` | `false` | Refund the placer when a hit expires. |
| `HitsConfiguration.Placed` / `Completed` / `Expired` | see file | Toggles for announcing and for telling the target or hirer. |
| `UIConfiguration.AutoDisplay` | `true` | Show the UI to `hitman.ui` players when they join. |
| `UIConfiguration.MaxHitsShown` | `10` | Rows shown, capped at 10. |
| `UIConfiguration.EffectGuid` | `48d317b1241a49159dd63f0d0eb0ad2f` | GUID of the HitUI effect. |

Messages are editable in the plugin's translation file. Supported placeholders include `{Bounty:0.00}`, `{TargetPlayerName}`, `{HirerPlayerName}`, `{KillerPlayerName}`, `{PlayerName}` and `{MinimumBounty}`.

## Storage

- **LiteDB:** `Hits.db` in the plugin folder (collection `hits`).
- **MySQL:** table `hitman_hits`, created automatically.

Hit records and the UI are stored by this plugin. Account balances are managed by the economy plugin.
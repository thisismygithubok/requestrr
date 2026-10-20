# Request Status Notifications — Design

Date: 2026-10-20
Status: Approved

## Intent

Users who request media through Requestrr currently hear nothing until the item becomes available — and sometimes never. They cannot tell whether a request failed to add, is waiting on a release that does not exist yet, is downloading slowly, or is stuck and needs an admin.

Requestrr will read the live status of each tracked request from Sonarr / Radarr / Lidarr and proactively notify the requester when a request is stalled, using admin-configurable time thresholds.

## Goals

1. **Status alerts** for tracked requests that pass a configurable threshold in one of these states:
   - **No release found** — item exists and is monitored in the *arr, has no file, and nothing is in the queue for it.
   - **Downloading too long** — item is in the *arr queue and still downloading; message includes progress and ETA.
   - **Stuck in queue** — queue item is in a warning/error state (e.g. import blocked, download failed, `trackedDownloadStatus` = warning/error); needs manual intervention.
2. **Per-episode TV tracking**: for a season request, evaluate each *aired* episode. One message per notification lists the problem episodes grouped by state (e.g. "S02: E04, E07 — no release found; E09 — downloading, ETA 2h").
3. **Per-album music tracking**: for an artist request, evaluate each *monitored, released* album without files. Same rules as TV; message lists the problem albums.
4. **Request re-check**: when a request attempt throws ("an error occurred"), Requestrr waits **30 seconds** (fixed, not configurable), then checks whether the item now exists in the *arr:
   - Exists → "your request did go through" and subscribe the user under the normal subscription rule (so status alerts apply from then on).
   - Does not exist → "your request really failed — try again or contact an admin".
5. **Re-notification**: configurable enable yes/no, maximum count, and interval. Alerts stop immediately once the item is available (existing completion notice still fires as today).
6. **All thresholds and re-notify settings are configurable in the web portal.**

## Non-goals

- **Overseerr / Ombi.** Not supported by this feature. When Overseerr or Ombi is the configured movie/TV backend, Requestrr does not talk to Sonarr/Radarr directly and those services typically expose only pending/processing/available — not queue progress or "no release found". Status alerts are disabled for those backends. Possible future work: read Overseerr's linked *arr services, or query the *arr directly in addition to Overseerr.
- **`/requestrr check` slash command** (out of scope, desirable later): let any user pick a recently requested item and see its current status, regardless of who requested it. The status-evaluation logic in this design should be a reusable component (input: item id → output: status per item/episode/album) so the command can reuse it.
- Changing the existing "available" notification behaviour.
- Notifying admins (only requesters/subscribers are notified).

## Anti-goals

- Spamming users: without re-notify enabled, each (user, item, state) is notified at most once.
- Breaking existing `notifications.json` files or losing existing subscriptions.
- Alerting on things that are not problems: unaired episodes, unreleased albums, unmonitored episodes/albums.

## Decided behaviour

### Who is tracked
Same rule as today: users who clicked "notify me", or every requester when the admin enabled **Automatically notify requesters**. Status alerts piggyback on existing subscriptions; no separate opt-in.

### Delivery
Status alerts and the re-check result (Goal 4) use the existing `NotificationMode` (Disabled / PrivateMessages / Channels) and existing DM/channel notifier code paths. If mode is Disabled, nothing is sent.

### Timestamps and existing data
- New subscriptions in `notifications.json` get a request timestamp (UTC).
- Existing entries are left untouched (no migration). Entries **without** a timestamp are skipped by status alerts; they still receive the normal "available" notification.
- Storage: the existing id arrays (`MovieIds`, TV `Notifications`, `MusicArtistId`) stay unchanged. A new parallel top-level section (e.g. `"Status"`) holds, per (user, subscription): `RequestedAt` (UTC), and per (unit, state): `LastNotifiedAt` and `NotifiedCount`. "No timestamp" = no `RequestedAt` entry for that subscription → skip. Entries are removed together with their subscription.
- Re-notify limits persist across restarts via `LastNotifiedAt` / `NotifiedCount`. No downgrade support required.

### Units, clock and dedupe
- **Unit** = movie (Radarr), episode (Sonarr), album (Lidarr). Notified state is keyed per (user, unit, state).
- **Clock per unit** = `max(RequestedAt, unit air/release date)`. Episodes airing or albums releasing after the request start their own clock at their air/release date. For `FutureSeasonsNotification`, every monitored season is evaluated with the same per-episode clock.
- **Dedupe**: on each evaluation, collect units that (a) passed their state's threshold and have never been notified for that state, or (b) are due a re-notify (enabled, interval elapsed since `LastNotifiedAt`, `NotifiedCount` < max). If the set is non-empty, send **one** aggregated message per (user, subscription) listing all currently problematic units, and update the counters for the units in the set. A unit changing state (e.g. no release → downloading) is a new (unit, state) and may notify once more.

### Thresholds and settings (portal)
Placed alongside the existing notification settings on the Chat Clients page (`ClientApp/src/views/ChatClients.jsx`, backed by `DiscordSettings` / `ChatClientsSettingsModel`).

| Setting | Type | Default (builder's call, see below) |
|---|---|---|
| Status alerts enabled | bool | false |
| No release found after | hours | 24 |
| Downloading longer than | hours | 12 |
| Stuck in queue (warning/error) after | hours | 1 |
| Re-notify enabled | bool | false |
| Re-notify interval | hours | 24 |
| Re-notify maximum count | int | 3 |

One threshold per state; one shared re-notify configuration across all states. A new `SettingsFileUpgrader` step adds the keys with defaults.

Validation (API and portal): thresholds and interval are integers ≥ 1 hour; maximum count is an integer ≥ 1. When the movie or TV backend is Overseerr/Ombi, the portal shows a note that status alerts apply only to Radarr/Sonarr/Lidarr-backed media.

### State evaluation
- Runs inside the existing per-media notification loops (`MovieNotificationEngine`, `TvShowNotificationEngine`, `MusicNotificationEngine`, 1-minute cadence), after the existing availability check. Status queries may run less often than every minute (builder's call) to limit *arr API load.
- Queue data: *arr `queue` endpoint (progress, `timeleft` / `estimatedCompletionTime`, tracked download status/state).
- **Stuck** = queue item with `trackedDownloadStatus` of `warning` or `error`, or `trackedDownloadState` of `importBlocked`, `importPending` (past threshold), `failedPending`, or `failed`. Older API versions without these fields: use `status` = `warning`/`failed`.
- **Radarr eligibility**: movie must be monitored and released/available (`isAvailable` where the API provides it; otherwise physical/digital release date in the past). Unreleased movies are ignored.
- Priority when an item matches multiple states: stuck in queue > downloading too long > no release found.
- TV: Sonarr episode list per series (`hasFile`, `monitored`, `airDateUtc`); only monitored episodes with `airDateUtc` in the past are evaluated. Grouped into one message per (user, season notification).
- Music: Lidarr albums per artist; every `monitored` album of the artist with a release date in the past and no track files is evaluated (the request is per artist, so all monitored albums count). One message per (user, artist).
- Supported *arr versions: whatever Requestrr already supports (Radarr V2–V5, Sonarr V2–V4, Lidarr V1–V2). If a version's API lacks the needed fields, status alerts degrade gracefully for that client (log once, skip) rather than erroring.

### Re-check flow
- Triggered when the request call to Radarr/Sonarr/Lidarr throws (today: `RequestMovieAsync` etc. log and throw; user sees generic error; `NotifyForNewRequestAsync` is never called).
- The user still sees the immediate error message as today.
- After 30 s, look up the item in the *arr by its external id (TMDb / TVDB / MusicBrainz). For TV, "exists" means the series exists and the requested season(s) are monitored; only the season(s) that are monitored get subscribed.
- On success, the subscription is created with `RequestedAt` = the original attempt time. If the user is already subscribed, keep the existing subscription and only send the "did go through" message.
- The re-check result is delivered via the configured `NotificationMode` (portal: Discord Notification Settings → Notifications → "Via private messages" / channels), using the same DM/channel notifier paths as status alerts. If the mode is Disabled, no re-check message is sent.
- Re-check applies only to Radarr/Sonarr/Lidarr backends, not Overseerr/Ombi.
- Pending re-checks are held in memory only (lost on restart is acceptable given the 30 s window).

### Messages
New locale strings for each state, the re-check success/failure, and their channel vs DM variants, added to `Language.cs` and `locales/english.json`. Other locale files receive the English text as placeholders.

## Calls made by Claude (not explicitly confirmed by the user)

- Default threshold values in the table above, and status alerts defaulting to **off** so upgrades do not change behaviour.
- State priority order when multiple states apply.
- Unaired episodes / unreleased / unmonitored albums are ignored.
- Other locales get English placeholder text.
- Pending re-checks are in-memory only.
- Status alerts and re-check are fully disabled for Overseerr/Ombi backends; portal shows a note.
- `notifications.json` gets a parallel `Status` section; existing id arrays untouched.
- Dedupe keyed per (user, unit, state), one aggregated message per subscription.
- Per-unit clock = later of request time and air/release date.
- Unreleased movies ignored; "stuck" state list as defined above.
- Re-check: subscription uses the original attempt time; no message when `NotificationMode` is Disabled.
- Settings validation: hours ≥ 1, max count ≥ 1.

## Left to the builder

- Internal structure of the status evaluator (keep it reusable for the future `/requestrr check` command).
- How often status queries hit the *arr APIs.
- Exact message wording and Discord embed layout, reusing existing embed helpers (`GenerateMovieDetailsAsync` etc.).
- Where exactly to hook the re-check (requesting workflow vs. download client), as long as all three media types are covered.

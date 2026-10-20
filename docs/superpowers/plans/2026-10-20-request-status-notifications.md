# Request Status Notifications Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Notify requesters via Discord when a Radarr/Sonarr/Lidarr request stalls (no release / downloading too long / stuck in queue), with portal-configurable thresholds and re-notify, plus a 30 s re-check after a failed request attempt.

**Architecture:** Pure, unit-tested core (`RequestStatusEvaluator`, `ArrStatusMapping`, `ArrQueueParser`, `RequestStatusMessageBuilder`) in `RequestrrBot/Notifications/Status/`. Thin I/O shells around it: status providers on the existing *arr client wrappers, a `RequestStatusRepository` persisted in a new `Status` section of `notifications.json`, a `RequestStatusMonitor` loop started/stopped by `ChatBot` alongside the existing notification engines, a `DiscordStatusMessageSender`, and a `RequestRecheckService` hooked into the three requesting workflows.

**Tech Stack:** C# / ASP.NET Core 6 (`net6.0`), DSharpPlus 4.5.2, Newtonsoft.Json, React 18 (CRA), xUnit (new test project).

**Spec:** `docs/superpowers/specs/2026-10-20-request-status-notifications-design.md`

## Global Constraints

- Applies only to Radarr / Sonarr / Lidarr backends. Overseerr/Ombi: no status alerts, no re-check.
- Radarr V2 and Sonarr V2 clients: status provider returns `null` (graceful skip, log once per process). Implement for Radarr V3 class (covers V3–V5), Sonarr V3 and V4 classes, Lidarr V1 class.
- Re-check delay: **30 seconds**, fixed constant `RequestRecheckService.DefaultDelay`.
- Settings defaults: `Enabled=false`, `NoReleaseHours=24`, `DownloadingHours=12`, `StuckHours=1`, `RenotifyEnabled=false`, `RenotifyIntervalHours=24`, `RenotifyMaxCount=3`.
- Validation: all hour values ≥ 1, `RenotifyMaxCount` ≥ 1.
- `RenotifyMaxCount` = number of re-notifications after the first alert (max total sends per (unit, state) = 1 + `RenotifyMaxCount`).
- Delivery for alerts and re-check results: configured `NotificationMode`; `Disabled` → nothing sent.
- Existing `notifications.json` id arrays (`Movies`, `TvShows`, `Music`) must not change shape. Subscriptions without a `Status` entry are never alerted.
- Settings file version bump: `2.1.3` → `2.1.4` (upgrader step + `SettingsTemplate.json`).
- No new NuGet/npm dependencies in `Requestrr.WebApi`. The test project may reference xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk.
- Tests run in Docker (no local dotnet): `docker run --rm -v "$(git rev-parse --show-toplevel)":/src -w /src mcr.microsoft.com/dotnet/sdk:6.0 dotnet test Requestrr.Tests/Requestrr.Tests.csproj -c Release`. Always `-c Release` (Debug triggers the csproj's npm-install target).
- Match surrounding code style: 4-space indent, Allman braces, `_camelCase` fields, `Task`-returning `...Async` methods, `ILogger` error logging with `ex.Message`.

## Review Focus

1. *arr unreachable / 401 / timeout during a monitor run → no messages, no counter changes, error logged once per item, loop continues next cycle. (Test: Task 6 `RunOnce_ProviderThrows_NoSendNoMark`.)
2. Item removed from the *arr after subscribing (provider returns `null`) → no alert, no exception. (Test: Task 6 `RunOnce_ProviderReturnsNull_NoSend`.)
3. Same item subscribed by several users with different `RequestedAt` → one provider call per item, each user evaluated on their own clock. (Test: Task 6 `RunOnce_TwoUsersSameMovie_OneProviderCall_IndependentClocks`.)
4. Legacy subscription (no `Status` entry) → never alerted. (Test: Task 6 `RunOnce_LegacySubscription_Skipped`.)
5. Discord send fails (sender returns `false`) → counters not updated so it retries next cycle; permanent failures (user left all guilds / DMs blocked) make the sender return `true`, mirroring `PrivateMessageMovieNotifier`. (Test: Task 6 `RunOnce_SendFails_NotMarked`.)

---

### Task 1: Test project + core evaluator

**Files:**
- Create: `Requestrr.Tests/Requestrr.Tests.csproj`, `Requestrr.Tests/.gitignore` (`bin/`, `obj/`)
- Create: `Requestrr.WebApi/RequestrrBot/Notifications/Status/StatusNotificationSettings.cs`
- Create: `Requestrr.WebApi/RequestrrBot/Notifications/Status/StatusModels.cs`
- Create: `Requestrr.WebApi/RequestrrBot/Notifications/Status/RequestStatusEvaluator.cs`
- Test: `Requestrr.Tests/Status/RequestStatusEvaluatorTests.cs`

**Interfaces (namespace `Requestrr.WebApi.RequestrrBot.Notifications.Status`):**
- Produces:
  - `public record StatusNotificationSettings` with settable props listed in Global Constraints (defaults as initializers) and `public string Validate()` → `null` if valid, else `"Invalid status notification settings, hour values and maximum count must be at least 1."`.
  - `public enum RequestStatusState { NoRelease, Downloading, Stuck }`
  - `public class ArrQueueItem { int? MovieId; int? EpisodeId; int? AlbumId; string Status; string TrackedDownloadStatus; string TrackedDownloadState; double Size; double SizeLeft; string TimeLeft; bool IsStuck { get; } }` (auto-props). `IsStuck` true when `TrackedDownloadStatus` ∈ {warning, error}, or `TrackedDownloadState` ∈ {importBlocked, importPending, failedPending, failed}, or `Status` ∈ {warning, failed} (case-insensitive).
  - `public class StatusUnit { string Key; string Label; bool HasFile; DateTime? ReleasedAtUtc; ArrQueueItem Queue; }`
  - `public class ArrItemStatus { string Title; IReadOnlyList<StatusUnit> Units; }`
  - `public class UnitNotification { DateTime LastNotifiedAtUtc; int NotifiedCount; }`
  - `public class StatusSubscription { DateTime RequestedAtUtc; Dictionary<string, UnitNotification> Units = new(); }` — dictionary key is `RequestStatusEvaluator.TrackingKey(unitKey, state)` = `$"{unitKey}|{state}"`.
  - `public class StatusAlert { StatusUnit Unit; RequestStatusState State; }`
  - `public class StatusEvaluation { IReadOnlyList<StatusAlert> Due; IReadOnlyList<StatusAlert> Problems; }`
  - `public static class RequestStatusEvaluator { static RequestStatusState? Classify(StatusUnit unit); static string TrackingKey(string unitKey, RequestStatusState state); static StatusEvaluation Evaluate(IEnumerable<StatusUnit> units, StatusSubscription subscription, StatusNotificationSettings settings, DateTime nowUtc); }`

Evaluate rules: `Classify`: `HasFile` → null; `Queue == null` → NoRelease; `Queue.IsStuck` → Stuck; else Downloading. Clock start = later of `RequestedAtUtc` and `ReleasedAtUtc`. `Problems` = units whose elapsed ≥ their state's threshold hours. `Due` ⊆ `Problems`: no tracking entry for (unit, state), or `RenotifyEnabled && now - LastNotifiedAtUtc ≥ RenotifyIntervalHours && NotifiedCount <= RenotifyMaxCount`.

- [ ] **Step 1: Create the test project.** `net6.0`, xunit 2.4.2, xunit.runner.visualstudio 2.4.5, Microsoft.NET.Test.Sdk 17.8.0, `<ProjectReference Include="..\Requestrr.WebApi\Requestrr.WebApi.csproj" />`. Place at repo root (not inside `Requestrr.WebApi/`, whose csproj globs all `**/*.cs`).

- [ ] **Step 2: Write failing tests** in `RequestStatusEvaluatorTests` (use `var now = new DateTime(2026,1,10,12,0,0,DateTimeKind.Utc)`, default settings):
  - `Classify_HasFile_ReturnsNull`; `Classify_NoQueue_NoRelease`; `Classify_QueueWarning_Stuck` (`TrackedDownloadStatus="warning"`); `Classify_QueueImportBlocked_Stuck`; `Classify_QueueOk_Downloading`.
  - `Evaluate_NoReleaseBeforeThreshold_NothingDue` (requested 23h ago → `Due` and `Problems` empty).
  - `Evaluate_NoReleaseAfterThreshold_Due` (25h → one Due, State NoRelease).
  - `Evaluate_StuckAfterOneHour_Due` (requested 2h ago, stuck queue).
  - `Evaluate_ClockStartsAtReleaseDate` (requested 48h ago, `ReleasedAtUtc` 2h ago, no file → nothing due).
  - `Evaluate_AlreadyNotified_RenotifyDisabled_NotDue_ButStillProblem` (`Due` empty, `Problems` count 1).
  - `Evaluate_RenotifyIntervalElapsed_Due` (`RenotifyEnabled=true`, last notified 25h ago, count 1).
  - `Evaluate_RenotifyMaxReached_NotDue` (count = 1 + `RenotifyMaxCount` = 4).
  - `Evaluate_StateChange_IsNewDue` (tracked `NoRelease`, unit now downloading 13h after request → Due with Downloading).
  - `Validate_ZeroHours_ReturnsError`; `Validate_Defaults_ReturnsNull`.

- [ ] **Step 3: Run tests, verify they fail** (compile errors are the expected failure). Command: Global Constraints test command.

- [ ] **Step 4: Implement** the types and `RequestStatusEvaluator` per Interfaces.

- [ ] **Step 5: Run tests, verify all pass.**

- [ ] **Step 6: Commit** — `git add Requestrr.Tests Requestrr.WebApi/RequestrrBot/Notifications/Status && git commit -m "Add request status evaluator and test project"`

---

### Task 2: Settings plumbing (backend)

**Files:**
- Modify: `Requestrr.WebApi/config/ChatClientsSettings.cs` (`DiscordSettings`: add `public StatusNotificationSettings StatusNotifications { get; set; }`)
- Modify: `Requestrr.WebApi/RequestrrBot/ChatClients/Discord/DiscordSettings.cs` (same property; include in `Equals` and `GetHashCode` so a change restarts the bot config)
- Modify: `Requestrr.WebApi/RequestrrBot/ChatClients/Discord/DiscordSettingsProvider.cs`, `Requestrr.WebApi/Controllers/ChatClients/ChatClientsSettingsProvider.cs` (read `settings.ChatClients.Discord.StatusNotifications.ToObject<StatusNotificationSettings>()`)
- Modify: `Requestrr.WebApi/Controllers/ChatClients/ChatClientsSettingsRepository.cs` (write `JToken.FromObject(...)`)
- Modify: `Requestrr.WebApi/Controllers/ChatClients/ChatClientsSettingsModel.cs` (add `StatusNotifications`), `ChatClientsController.cs` (GET returns it; POST: if model value null keep existing, else `Validate()` → `BadRequest(error)`, else assign)
- Modify: `Requestrr.WebApi/SettingsFileUpgrader.cs` (step `2.1.3` → `2.1.4` adding `ChatClients.Discord.StatusNotifications = JToken.FromObject(new StatusNotificationSettings())`), `Requestrr.WebApi/SettingsTemplate.json` (`"Version": "2.1.4"` and the object with defaults)
- Test: `Requestrr.Tests/Status/SettingsUpgradeTests.cs`

**Interfaces:**
- Consumes: `StatusNotificationSettings` (Task 1).
- Produces: `RequestrrBot.ChatClients.Discord.DiscordSettings.StatusNotifications` (non-null after upgrade); API JSON field `statusNotifications` (camelCase via ASP.NET default) with the seven props.

- [ ] **Step 1: Write failing test** `Upgrade_From213_AddsStatusNotificationDefaults`: write temp file `{"Version":"2.1.3","ChatClients":{"Discord":{}}}`, call `SettingsFileUpgrader.Upgrade(path)`, assert `Version == "2.1.4"`, `ChatClients.Discord.StatusNotifications.Enabled == false`, `NoReleaseHours == 24`, `RenotifyMaxCount == 3`.
- [ ] **Step 2: Run, verify fail.**
- [ ] **Step 3: Implement** all file changes above.
- [ ] **Step 4: Run, verify pass.**
- [ ] **Step 5: Commit** — `"Add status notification settings plumbing"`

---

### Task 3: *arr JSON mapping + queue parsing

**Files:**
- Create: `Requestrr.WebApi/RequestrrBot/Notifications/Status/ArrQueueParser.cs`, `ArrStatusMapping.cs`
- Test: `Requestrr.Tests/Status/ArrStatusMappingTests.cs`

**Interfaces:**
- Produces:
  - `public static IReadOnlyList<ArrQueueItem> ArrQueueParser.Parse(string json)` — accepts a JSON array (`queue/details`) or an object with `records` array (paged `queue`). Field names: `movieId`, `episodeId`, `albumId`, `status`, `trackedDownloadStatus`, `trackedDownloadState`, `size`, `sizeleft`, `timeleft`.
  - `public static ArrItemStatus ArrStatusMapping.MapMovie(JObject movie, IReadOnlyList<ArrQueueItem> queue)` — one unit, Key `"movie"`, Label = `title`; emitted only if `monitored && isAvailable` (missing `isAvailable` → true); `HasFile` = `hasFile`; queue match on `movieId == id`.
  - `public static ArrItemStatus ArrStatusMapping.MapSeason(string seriesTitle, JArray episodes, int seasonNumber, IReadOnlyList<ArrQueueItem> queue, DateTime nowUtc)` — units for episodes of that season with `monitored && airDateUtc != null && airDateUtc <= nowUtc`; Key `$"e{id}"`, Label `$"S{season:00}E{episode:00}"`, `ReleasedAtUtc = airDateUtc`; queue match on `episodeId`.
  - `public static ArrItemStatus ArrStatusMapping.MapArtist(string artistName, JArray albums, IReadOnlyList<ArrQueueItem> queue, DateTime nowUtc)` — units for albums with `monitored && releaseDate <= nowUtc`; Key `$"a{id}"`, Label = `title`, `HasFile = statistics.trackFileCount > 0`, `ReleasedAtUtc = releaseDate`; queue match on `albumId`.

- [ ] **Step 1: Write failing tests** with inline JSON fixtures:
  - `Parse_PagedRecords`, `Parse_Array`, `Parse_EmptyString_ReturnsEmpty`.
  - `MapMovie_Unmonitored_NoUnits`; `MapMovie_NotAvailable_NoUnits`; `MapMovie_InQueue_AttachesQueue`.
  - `MapSeason_SkipsUnairedAndUnmonitoredAndOtherSeasons` (4 episodes: aired+monitored S2, unaired S2, unmonitored S2, aired S1 → 1 unit, Label `"S02E04"`).
  - `MapArtist_SkipsUnreleasedAndUnmonitored`; `MapArtist_TrackFiles_HasFileTrue`.
- [ ] **Step 2: Run, verify fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, verify pass.**
- [ ] **Step 5: Commit** — `"Add arr status mapping and queue parsing"`

---

### Task 4: Status repository + subscription timestamps

**Files:**
- Create: `Requestrr.WebApi/RequestrrBot/Notifications/Status/RequestStatusRepository.cs`
- Modify: `Requestrr.WebApi/RequestrrBot/NotificationsFile.cs` (add `WriteStatus(Dictionary<string, StatusSubscription>)` setting `_cachedNotifications.Status`; reading tolerates a missing `Status` property)
- Modify: `Requestrr.WebApi/RequestrrBot/Notifications/Movies/MovieNotificationsRepository.cs`, `TvShows/TvShowNotificationsRepository.cs`, `Music/MusicNotificationsRepository.cs`
- Modify: `Requestrr.WebApi/RequestrrBot/ChatBot.cs` (field `_requestStatusRepository = new RequestStatusRepository()` declared before the three repository fields; pass it to their constructors)
- Test: `Requestrr.Tests/Status/RequestStatusRepositoryTests.cs`

**Interfaces:**
- Produces:
  - `public static class RequestStatusKeys { static string Movie(string userId, int tmdbId); static string TvSeason(string userId, int tvdbId, int seasonNumber); static string Music(string userId, string artistId); }` → `"movie:{u}:{id}"`, `"tv:{u}:{id}:{s}"`, `"music:{u}:{id}"`.
  - `public class RequestStatusRepository` — ctors: `()` (loads from `NotificationsFile.Read().Status`, persists via `NotificationsFile.WriteStatus`), and `(Dictionary<string, StatusSubscription> initial, Action<Dictionary<string, StatusSubscription>> write)` for tests. Methods (thread-safe via `lock`, each mutation calls write): `void Start(string key, DateTime requestedAtUtc)` (no-op if exists); `void Backdate(string key, DateTime requestedAtUtc)` (sets to the earlier value; no-op if missing); `StatusSubscription Get(string key)` (copy or null); `void MarkNotified(string key, IEnumerable<string> trackingKeys, DateTime nowUtc)` (sets `LastNotifiedAtUtc`, increments `NotifiedCount`); `void Remove(string key)`.
  - Notification repositories: constructor gains `RequestStatusRepository statusRepository`. `Add*` calls `Start(key, DateTime.UtcNow)` when a subscription is newly added; `Remove*` calls `Remove(key)`. TV: future-season replacement removes the old season's key.

- [ ] **Step 1: Write failing tests:** `Start_ThenGet_ReturnsRequestedAt`; `Start_Existing_DoesNotOverwrite`; `Backdate_KeepsEarlier`; `MarkNotified_IncrementsCountAndPersists` (write callback invoked, count 1 then 2); `Get_Missing_ReturnsNull`; `Remove_DeletesEntry`; `Keys_Format` (exact strings above).
- [ ] **Step 2: Run, verify fail.**
- [ ] **Step 3: Implement** repository, `NotificationsFile.WriteStatus`, repository hooks, `ChatBot` field wiring. Grep for every `new MovieNotificationsRepository(`, `new TvShowNotificationsRepository(`, `new MusicNotificationsRepository(` and update.
- [ ] **Step 4: Run, verify pass.**
- [ ] **Step 5: Commit** — `"Track request timestamps for status notifications"`

---

### Task 5: Status providers on *arr clients

**Files:**
- Create: `Requestrr.WebApi/RequestrrBot/Notifications/Status/IStatusProviders.cs`
- Modify: `DownloadClients/Radarr/RadarrClient.cs`, `RadarrClientV3.cs`; `DownloadClients/Sonarr/SonarrClient.cs`, `SonarrClientV3.cs`, `SonarrClientV4.cs`; `DownloadClients/Lidarr/LidarrClient.cs`, `LidarrClientV1.cs`

**Interfaces:**
- Produces:
  - `IMovieStatusProvider { Task<ArrItemStatus> GetMovieStatusAsync(int theMovieDbId, CancellationToken token); }` — implemented by `RadarrClient` (wrapper) and `RadarrClientV3`.
  - `ITvShowStatusProvider { Task<ArrItemStatus> GetSeasonStatusAsync(int theTvDbId, int seasonNumber, CancellationToken token); }` — `SonarrClient`, `SonarrClientV3`, `SonarrClientV4`.
  - `IMusicStatusProvider { Task<ArrItemStatus> GetArtistStatusAsync(string artistId, CancellationToken token); }` — `LidarrClient`, `LidarrClientV1`.
  - All return `null` when the item is not in the *arr. Wrappers return `null` for V2 and log a warning once (`static bool` flag).
- Consumes: `ArrQueueParser`, `ArrStatusMapping` (Task 3).

Endpoints (relative to each class's existing `BaseURL`, via its existing `HttpGetAsync`): Radarr `movie?tmdbId={id}` then `queue/details?movieId={id}`; Sonarr existing series lookup by tvdbId, `episode?seriesId={id}`, `queue/details?seriesId={id}`; Lidarr existing artist lookup, `album?artistId={id}`, `queue/details?artistId={id}`.

- [ ] **Step 1: Implement** the interfaces and methods (I/O shell only; logic lives in Task 3 code).
- [ ] **Step 2: Build** — `docker run ... mcr.microsoft.com/dotnet/sdk:6.0 dotnet build Requestrr.WebApi/Requestrr.WebApi.csproj -c Release` → `Build succeeded`, 0 errors.
- [ ] **Step 3: Run full test suite** → all pass.
- [ ] **Step 4: Commit** — `"Add status providers to Radarr, Sonarr and Lidarr clients"`

---

### Task 6: Messages, sender, monitor, ChatBot wiring

**Files:**
- Create: `Notifications/Status/RequestStatusMessageBuilder.cs`, `IStatusMessageSender.cs`, `DiscordStatusMessageSender.cs`, `RequestStatusMonitor.cs`
- Modify: `Requestrr.WebApi/RequestrrBot/Locale/Language.cs`, `Requestrr.WebApi/locales/*.json` (all 7; non-English get English text)
- Modify: `Requestrr.WebApi/RequestrrBot/ChatBot.cs` (`ApplyBotConfigurationAsync`: stop old monitor; start new one when `StatusNotifications.Enabled && NotificationMode != Disabled`, passing providers only for clients that are Radarr/Sonarr/Lidarr, else `null`)
- Test: `Requestrr.Tests/Status/RequestStatusMessageBuilderTests.cs`, `RequestStatusMonitorTests.cs`

**Locale keys (english.json, exact text):**
- `Discord.Notification.Status.Header.DM`: `"Status update for **[Title]** that you requested:"`
- `Discord.Notification.Status.Header.Channel`: `"[UserMention] status update for **[Title]** that you requested:"`
- `Discord.Notification.Status.NoRelease`: `"No suitable release found yet: [Items]"`
- `Discord.Notification.Status.Downloading`: `"Still downloading: [Items]"`
- `Discord.Notification.Status.Stuck`: `"Stuck in the download queue, an admin needs to take a look: [Items]"`
- `Discord.Notification.Recheck.Success.DM` / `.Channel`: `"Your request for **[Title]** did go through despite the earlier error."` / `"[UserMention] your request for **[Title]** did go through despite the earlier error."`
- `Discord.Notification.Recheck.Failure.DM` / `.Channel`: `"Your request for **[Title]** really failed. Please try again or contact an admin."` / `"[UserMention] your request for **[Title]** really failed. Please try again or contact an admin."`

**Interfaces:**
- Produces:
  - `public static class RequestStatusMessageBuilder { static string BuildStatus(Language language, string title, IReadOnlyList<StatusAlert> problems, string userMention); static string BuildRecheck(Language language, string title, bool succeeded, string userMention); static string FormatEta(string timeLeft); }` — `userMention == null` → DM variants. Status lines in order Stuck, Downloading, NoRelease, one line each (prefixed `"- "`), items joined by `", "`; Downloading items render `"{Label} ({percent}%, ETA {eta})"` with percent = `round((Size-SizeLeft)/Size*100)`, eta from `FormatEta` (`"02:10:00"`→`"2h 10m"`, `"1.02:10:00"`→`"1d 2h 10m"`, `"00:05:00"`→`"5m"`, null/unparseable → `"unknown"`).
  - `public interface IStatusMessageSender { bool UsesChannels { get; } Task<bool> SendAsync(string userId, string message, CancellationToken token); }`
  - `DiscordStatusMessageSender(DiscordClient client, string notificationMode, ulong[] channelIds, ILogger logger)` — DM path copies member lookup/`UnauthorizedException` handling from `PrivateMessageMovieNotifier` (permanent failures return `true`); channel path posts to each configured notification channel like `ChannelMovieNotifier`.
  - `RequestStatusMonitor(StatusNotificationSettings settings, RequestStatusRepository statusRepository, MovieNotificationsRepository movies, IMovieStatusProvider movieProvider, TvShowNotificationsRepository tvShows, ITvShowStatusProvider tvProvider, MusicNotificationsRepository music, IMusicStatusProvider musicProvider, IStatusMessageSender sender, ILogger logger, Func<DateTime> utcNow)` with `void Start()`, `Task StopAsync()` (same pattern as `MovieNotificationEngine`, 15-minute delay), `Task RunOnceAsync(CancellationToken token)`.
  - RunOnce: for each media with non-null provider, for each subscribed item, call provider once; for each subscribed user with a `Status` entry → `Evaluate`; if `Due` non-empty → `BuildStatus(Language.Current, title, Problems, sender.UsesChannels ? $"<@{userId}>" : null)` → `SendAsync`; on `true` → `MarkNotified(key, Due tracking keys, now)`. Exceptions per item are logged and do not stop the run. TV iterates season notifications (both `SeasonNotification` and `FutureSeasonsNotification`) by their `SeasonNumber`.
  - Monitor tests need repositories without file I/O: notification repositories gain an internal/test constructor taking initial data (or the monitor depends on `Func<...>` enumerators). Choose one and keep production constructors unchanged.

- [ ] **Step 1: Write failing builder tests:** `FormatEta_Hours`, `FormatEta_Days`, `FormatEta_MinutesOnly`, `FormatEta_Null_Unknown`, `BuildStatus_GroupsByStateInOrder`, `BuildStatus_ChannelIncludesMention`, `BuildRecheck_FailureDM`. Construct `Language` by deserializing `Requestrr.WebApi/locales/english.json` (copy it to test output via `<None Include=... CopyToOutputDirectory="PreserveNewest" />`).
- [ ] **Step 2: Write failing monitor tests** with fake providers/sender and fixed clock: the five Review Focus tests plus `RunOnce_DueAlert_SendsAndMarks` and `RunOnce_SecondRunSameState_NoResend`.
- [ ] **Step 3: Run, verify fail.**
- [ ] **Step 4: Implement** builder, locale keys (`Language.cs` `[JsonProperty]` props + 7 json files), sender, monitor, `ChatBot` wiring.
- [ ] **Step 5: Run, verify pass.**
- [ ] **Step 6: Commit** — `"Add request status monitor and Discord status messages"`

---

### Task 7: 30 s re-check after a failed request

**Files:**
- Create: `Notifications/Status/RequestRecheckService.cs`
- Modify: `Movies/MovieRequestingWorkflow.cs`, `TvShows/TvShowRequestingWorkflow.cs`, `Music/MusicRequestingWorkflow.cs` (wrap the `_requester.Request*Async` call: on exception call `_recheck?.Schedule(...)` then rethrow so the user still sees `Language.Current.Error`)
- Modify: `Movies/MovieWorkflowFactory.cs`, `TvShows/TvShowWorkflowFactory.cs`, `Music/MusicWorkflowFactory.cs` (pass the service only when the client is Radarr/Sonarr/Lidarr and `NotificationMode != Disabled`, else `null`)
- Modify: `ChatBot.cs` (construct one `RequestRecheckService` in the ctor with `_requestStatusRepository`, pass to factories; set `Sender` in `ApplyBotConfigurationAsync`)
- Test: `Requestrr.Tests/Status/RequestRecheckServiceTests.cs`

**Interfaces:**
- Consumes: `RequestStatusRepository.Backdate`, `RequestStatusKeys`, `IStatusMessageSender`, `RequestStatusMessageBuilder.BuildRecheck`.
- Produces:
  - `public class RecheckRequest { string UserId; string Title; string StatusKey; DateTime AttemptedAtUtc; Func<Task<bool>> ExistsAsync; Func<Task> SubscribeAsync; }`
  - `public class RequestRecheckService { static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(30); RequestRecheckService(RequestStatusRepository statusRepository, ILogger logger, TimeSpan? delay = null); IStatusMessageSender Sender { get; set; } Task Schedule(RecheckRequest request); }` — returns the background task (tests await it; workflows ignore it). After delay: `ExistsAsync()`; `true` → `SubscribeAsync()` (workflow passes its `_notificationWorkflow.NotifyForNewRequestAsync`, which applies the auto-notify rule), then `Backdate(StatusKey, AttemptedAtUtc)`, then send success; `false` or exception → send failure. `Sender == null` → no send.
  - Existence checks: movie → `SearchMovieAsync(...).DownloadClientId` non-empty; TV → series found and requested season `IsRequested != NotRequested`; music → artist `DownloadClientId` non-empty.

- [ ] **Step 1: Write failing tests** (delay `TimeSpan.Zero`, fake sender): `Exists_SubscribesBackdatesAndSendsSuccess`; `NotExists_SendsFailure_NoSubscribe`; `ExistsThrows_SendsFailure`; `NoSender_NoThrow`; `DefaultDelay_Is30Seconds`.
- [ ] **Step 2: Run, verify fail.**
- [ ] **Step 3: Implement** service, workflow hooks, factory and `ChatBot` wiring.
- [ ] **Step 4: Run, verify pass.**
- [ ] **Step 5: Commit** — `"Re-check failed requests after 30 seconds"`

---

### Task 8: Portal settings UI

**Files:**
- Modify: `Requestrr.WebApi/ClientApp/src/views/ChatClients.jsx` (state `statusNotifications`, load from payload, include in save model; inside the existing `notificationMode !== "Disabled"` block add a checkbox "Notify requesters when a request is stalled" and, when checked, number inputs (min 1) for the six numeric/boolean settings; re-notify interval/count shown only when re-notify is checked; static note: "Status alerts only apply to media handled by Radarr, Sonarr or Lidarr (not Overseerr or Ombi).")
- Modify: `Requestrr.WebApi/ClientApp/src/store/actions/ChatClientsActions.jsx` (send `'StatusNotifications'` in save body; include in dispatched payload), `ClientApp/src/store/reducers/ChatClientsReducer.jsx` (`statusNotifications: action.payload.statusNotifications`)

**Interfaces:** Consumes API field `statusNotifications` from Task 2. Use the same `FormGroup`/`Input`/`custom-checkbox` markup as `automaticRequests`.

- [ ] **Step 1: Implement.**
- [ ] **Step 2: Build** — `cd Requestrr.WebApi/ClientApp && npm install && npm run build` → `Compiled successfully` (warnings that pre-exist are acceptable; no new ones from the changed files).
- [ ] **Step 3: Commit** — `"Add status notification settings to portal"`

---

### Task 9: Whole-branch verification

- [ ] **Step 1:** Full test suite (Global Constraints command) → all pass.
- [ ] **Step 2:** `docker build -t requestrr:status-test Requestrr.WebApi` → completes successfully.
- [ ] **Step 3:** `docker run --rm -d -p 4545:4545 --name rq-test requestrr:status-test`, then `curl -s -o /dev/null -w "%{http_code}" http://localhost:4545/` → `200`; `docker stop rq-test`.
- [ ] **Step 4:** Report to the user what needs a live Radarr/Sonarr/Lidarr + Discord to verify (alerts, ETA text, re-check) — not automatable here.

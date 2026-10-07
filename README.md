# Pet Service

Author: **Anomaly** · **0.3.0.0** · Dalamud API **15** · InternalName **PetService**

A private FFXIV companion for consensual pet/master roleplay and power dynamics.
The master manages the dynamic through Discord, the website, or the in-game Master
portal. The pet keeps the local kill switch and a separate personal interface.
The Asura service is a separate download and project.

## Installation

1. Add [the custom repository](https://raw.githubusercontent.com/anomalousgamer/PetService/main/repo.json)
   in **/xlsettings → Custom Plugin Repositories** and enable it.
2. Open **/xlplugins**, refresh, and install **Pet Service**.
3. Open **/toh** or **/petservice**, choose **Pet → Setup**, and enter the
   one-time pairing code supplied by the master. The service connection is built in.
4. Pairing is saved and reconnects on future logins until revoked or paused.

## Your shared dynamic

- Styled **Pet Home, Contact and Setup** pages with standard Dalamud glyphs.
- Password-protected **Master portal** with overview cards, a playtime chart,
  filtered activity history, older-page loading and CSV export.
- Master messages in a fixed, normal-sized popup with readable text, custom reply
  buttons, optional written replies, and a pet-chosen snooze of 1–1,440 minutes.
- No popup X, dragging or full-screen blackout. Native chat stays accessible while
  gameplay is guarded on supported game builds. FFXIV continues running.
- Daily medication reminders on the pet's local clock, including today's overdue
  reminder on login. **I took them** is the pet's own report.
- Shared character status, world/DC, zone, map X/Y coordinates and housing addresses
  including ward, plot, interior/grounds, apartments and FC rooms when available.
- Configurable contact presets for attention, help, sadness, affection and company.
  Requests share a one-minute cooldown and ping the configured master in Discord.
- Local outgoing speech garbling: on/off, 1–100 strength, and muffled, soft or playful
  styles. Slash-command routing, supported links and game placeholders are preserved.
  Unknown command/payload formats pass through unchanged. Explicit popup replies
  and incoming messages are untouched. No external garbling plugin is required.
- Reusable message templates and a composer preview.
- Profile archive/restore and permanent deletion with a typed-name confirmation.
- Cryptic What's New notes, per-version suppression and Dalamud update notifications.
- Cached schedules, saved replies and buffered observations retry after outages. Gameplay events trigger uploads; playtime accounting uploads in minute batches. Live reports update every five seconds only while a visible Master Overview requests them. Refresh now requests a fresh report and flushes saved events. Hidden pages stop live updates; event recording continues.

The Master portal uses the same administrator password as the website. It keeps
credentials only in memory and clears them when locked or closed. Website updates
and plugin updates stay separate; install the matching service before this plugin.

## Observed history

History begins when this paired plugin is running and resumed. SQL retains it
until the master deletes the profile. No earlier history is manufactured.

| Observation | Recorded behavior |
|---|---|
| Playtime | Logged-in intervals, observed session boundaries, today/week/month/lifetime totals, average/longest observed session, date range, daily chart and time by job/location/character. |
| Location | Zone/world/DC transitions, coordinates in live reports and saved change-event snapshots. Movement alone creates no history entry. Loading coordinates are unknown. |
| Activity | Job/level changes and duty/combat/AFK/game-idle/crafting/gathering/mount/cutscene/unconscious flags. Flags describe game state, not human engagement. |
| Duties | Entered or joined in progress, actual start events, wipes, recommences, completion, departure or interruption. Unknown start/outcome remains unknown. |
| Inventory | Supported own-character bags, equipment, armory, currency/crystals, saddlebags and loaded retainer containers; adds/removes/changes/moves/splits/merges with item ID, quantity and HQ. Initial container loads establish a baseline. |
| Loot | Observed inventory increases. Their source is **unconfirmed**; this version does not identify loot rolls or prove a duty, trade or venture caused the increase. |
| Trades | Trade offers' item IDs and window closure. Quantity, gil, partner identity and completed/cancelled outcome are not captured. Closing a trade window is **unconfirmed**, not completed. |
| Retainers | Venture result screens with retainer NPC name, task, XP, item IDs and quantities. Viewing a result is **collection unconfirmed**. Container transfers are recorded as inventory changes without an assumed source. |
| Pet Service | Explicit requests, replies, buttons, snoozes, display receipts and safety notices. |

Game chat, original or garbled, conversation partners and player rosters are
**never collected or uploaded**. Retainer/trade records do not include other
players. This version does not record every action, quest, ability or market event.

Live reports overwrite the current snapshot without adding activity logs. The default Master Overview displays character, housing/location, world/DC, map coordinates/IDs, job/level and each activity flag with clear labels. Saved status history records changes to these fields, except coordinate-only changes. Duty, inventory, trade and retainer history remains supported. Playtime intervals are separate accounting records and do not carry coordinates.

Only continuously observed intervals of at most 30 seconds count as playtime.
Framework interruptions, unloads and pauses produce unknown gaps rather than
invented time. Alts share the profile but remain distinguishable by their own
character/world. Buffered data persists locally; at 50,000 unsent records recording
pauses until uploads drain. No server history is automatically cleared.

## Local control

The confirmed **kill switch** stops master prompts, input guarding, recording,
normal uploads and garbling immediately. It stays on until the pet resumes it;
the master cannot override it. Only the explicit safety notice continues to retry
while paused. Observations from the paused period are never filled in afterward.

**Safe mode / release** frees gameplay for the current login while status and
history sharing continue. Both safety actions alert the master in Discord.
Game updates can make native hooks unavailable; the local kill switch remains
accessible. Unloading or crashing the plugin cannot guarantee a final notice.

## Commands

Both names support the same commands:

| Command | Action |
|---|---|
| `/toh` or `/petservice` | Open Pet and Master pages. |
| `call` | Open the Pet Contact page to choose a request. |
| `off` | Confirm activation of the local kill switch. |
| `on` | Resume pairing and master input. |
| `release` | Release gameplay guarding for this login. |
| `changes` | Open the cryptic What's New window. |
| `checkupdates` | Check for a Dalamud repository update. |

The installer includes a typographic PS icon; in-game graphics use standard UI
glyphs. No AI-generated artwork is included. The Home page keeps connection and voice status; contact actions are on Contact. The expanded sharing panel is deferred until 1.0.0.0; this README retains the sharing details. CSV exports from the Master portal
are saved under this plugin's configuration directory in **exports**.

## Master chat and travel

Master **Chat messages** are local chat entries with a **[Master]** prefix. They wait
until the paired character is ready, and retry after connection outages. Pet Setup
selects Echo or System output, a colour preset and an optional notification sound.
Normal FFXIV chat filters determine the tabs that show that category. These entries
do not require a reply or guard gameplay. The local kill switch pauses delivery.

The master can send and cancel queued chat messages from either Master portal or
Discord. Optional expiration is 1–10,080 minutes; 0 means no expiration. Delivery
status is Queued, Displayed, Expired or Cancelled. Displayed means the plugin
queued the local entry into chat, not that the pet read it. Cancellation cannot
recall a message already delivered to a running client. The portals show the latest
100 chat messages; older message records remain in SQL until profile deletion.

**Travel** shows completed arrivals with origin/destination, character and available
map coordinates. It records same-zone transfers using loading and zone-initialization
events, rather than coordinate movement. Teleport/Return intent and the native
teleport history signal identify regular teleports; aetheryte event context identifies
aethernet/aetheryte transfers. A transfer whose method is unknown stays labelled
unknown. Interrupted casts without an arrival create no completed travel record.

Counts are available for today/week/month/lifetime, method, destinations and
character. Filters and CSV export cover the recorded travel history. Recording
requires the paired, resumed plugin, client events and a writable local queue.
Game updates can affect native method identification; loading/zone arrival capture
continues. Nothing from before plugin observation or during a kill-switch pause
is reconstructed. Rapid transitions during a client/framework interruption can
remain unobserved. Master availability and a task board are not in this release.

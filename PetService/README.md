# Pet Service

Author: **Anomaly** · **0.2.0.0** · Dalamud API **15** · InternalName **PetService**

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
- Cached schedules, saved replies and buffered observations retry after outages.

The Master portal uses the same administrator password as the website. It keeps
credentials only in memory and clears them when locked or closed. Website updates
and plugin updates stay separate; install the matching service before this plugin.

## Observed history

History begins when this paired plugin is running and resumed. SQL retains it
until the master deletes the profile. No earlier history is manufactured.

| Observation | Recorded behavior |
|---|---|
| Playtime | Logged-in intervals, observed session boundaries, today/week/month/lifetime totals, average/longest observed session, date range, daily chart and time by job/location/character. |
| Location | Zone/world/DC transitions, timestamped map coordinates every 30 seconds, and current coordinates in live reports. Loading coordinates are unknown. |
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
| `call` or `request` | Queue the default Attention request. |
| `off` | Confirm activation of the local kill switch. |
| `on` | Resume pairing and master input. |
| `release` | Release gameplay guarding for this login. |
| `changes` | Open the cryptic What's New window. |
| `checkupdates` | Check for a Dalamud repository update. |

The installer includes a typographic PS icon; in-game graphics use standard UI
glyphs. No AI-generated artwork is included. CSV exports from the Master portal
are saved under this plugin's configuration directory in **exports**.

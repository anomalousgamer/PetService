# Pet Service

Author: **Anomaly**. Version **0.1.0.2**. InternalName **PetService**. Dalamud API **15**.

Pet Service is a private FFXIV tool for consensual pet/master roleplay and power
dynamics. The master directs messages, reminders, and interactive prompts through
Discord, the website, or the plugin's Master page. The pet receives prompts in game and can reply,
snooze, or pause the connection with the local kill switch.

Install and update through the normal Dalamud plugin installer using the custom
repository below.

The Asura service is a separate project/download. This repository contains the
plugin source and its Dalamud repository manifest.

## Installation

1. Open **/xlsettings** and find **Custom Plugin Repositories**.
2. Add [https://raw.githubusercontent.com/anomalousgamer/PetService/main/repo.json](https://raw.githubusercontent.com/anomalousgamer/PetService/main/repo.json).
3. Enable the repository and save the settings.
4. Open **/xlplugins**, refresh, search **Pet Service**, and click **Install**.
5. Type **/petservice**, open the **Pet** page, and enter the one-time pairing code supplied by the master.
   The service address is built in; no website address is needed during setup.
6. Pairing is saved and reconnects automatically after future logins until revoked
   or paused with the local kill switch.

The plugin requires **Dalamud API 15**.

## Features

- Custom master messages in a normal-sized, fixed popup with readable text and a custom snooze duration.
- Optional custom reply buttons, plus a written reply when the master allows it.
- Popups have no X and cannot be moved. Native FFXIV chat stays available while gameplay inputs are guarded.
- Shared character status and location, including current world/data center,
  duty, combat, AFK, and game-idle state.
- Housing addresses in the master's observed location and Discord `!where`:
  district, ward, current plot, subdivision, and inside-house or estate-grounds
  status. Apartment numbers and FC rooms appear when available. Ward streets
  show the ward without assigning a nearby plot.
- Daily medication reminders managed centrally on the pet’s local clock.
- Today's overdue reminder appears after the character logs in.
- **I took them** and a pet-chosen snooze of 1 to 1,440 minutes.
- **Call master** in the Pet page and every prompt, or **/petservice call** and **/petservice request**, pings the configured master in Discord, limited to once a minute.
- Discord notices for kill-switch activation and local safe mode (release).
- Cached schedules and saved-choice retries after a service outage.
- Separate **Pet** and password-protected **Master** pages in the plugin.
- A minimal Pet page with setup, connection status, Call master, a local kill switch, and unpair.
- In-game master controls for profiles, pairing, reminders, messages, custom buttons, status/location, pending prompts, and recent responses.

Prompts appear one at a time, oldest due first. Replies are entered in the popup;
normal game chat is not a reply to the master. Choices are saved locally before
prompt dismissal and retried after outages. **Taken** is a self-reported acknowledgment.

Prompts use a centered window without a full-screen backdrop. FFXIV continues running while a prompt is open.
Press Enter or use **Use game chat** to type in native chat; clicking the popup's
reply field switches typing back to the reply. Long messages and button lists scroll.
Native chat access depends on the supported game UI and input hooks. If those are
unavailable after a game update, the guard uses full input capture. The local kill
switch remains available in every prompt; the release command can also be entered
through Dalamud’s command interface.

The master can add up to **32 reply buttons**, each with up to **80 characters**,
through the portal or Discord. Clicking a button records that exact choice and
resolves the message. A button named **Cancel** is a reply choice. Snooze and the
local kill switch remain available regardless of the button labels.

## Pet and Master pages

Open **/petservice** to choose a page. **Pet** holds local setup and status. The
**Call master** button and commands require a paired, resumed plugin and a ready
character. Calls are queued for delivery to Discord and share a one-minute cooldown.

Unlock **Master** with the same administrator password used by the website. This
page can be used without pairing your own character. It manages the same profiles
and queues as the website and Discord, with these controls:

- Find or add pet profiles, generate/copy pairing codes, and revoke devices.
- Read status, session duration, world/DC, housing location, and duty/idle flags.
- Send messages with optional custom reply buttons and written replies.
- Add daily reminders on the pet's clock and disable existing reminders.
- View display receipts and snooze deadlines, and cancel pending prompts.
- Read the latest 30 responses, snoozes, attention calls, and local safety notices.
- Refresh manually, turn auto-refresh on or off, retry an interrupted action, or lock the portal.

Dates in the Master page use your computer's local clock. The password and master
data stay in memory while the window is open; closing it or choosing **Lock portal**
clears the session, pairing code, and unsent drafts. The password is not saved in
plugin configuration. The Pet page does not reveal the website address or master
data. A local kill switch is also available on the Master page for this device.

## Local kill switch

Every kill-switch activation from either plugin page, a popup, or **/petservice off** first asks
for confirmation. Cancel leaves the current state unchanged. Confirming immediately
closes the popup and releases input capture. It pauses master messages, daily reminders (including cached ones), normal synchronization,
and status/location sharing. One explicit activation notice is saved locally and
retried for delivery to Discord without uploading status or location while paused. It
cancels active synchronization and ignores late responses, including responses from before a pause/resume.

The setting stays on after logins and plugin reloads. The master cannot turn it
off remotely. If the setting cannot be saved, the current session still pauses
and the pet window displays the persistence error.

The kill switch is available in the pet window and every blocking popup. Turn it
off locally to resume. Pending prompts and saved replies remain queued and may
return on resume. Pausing does not mark a reminder as taken or cancel a master
message. Already-sent requests and replies cannot be recalled.

The master's latest status becomes stale after contact is lost, rather than
claiming the character logged out. **/petservice release** releases the guard
for the current login while keeping the service connection active. This is local
safe mode and sends a Discord notice. Disabling Pet Service through Dalamud, a
crash, or an unload cannot guarantee a final notice; missing contact becomes stale.

## Commands

| Command | Action |
|---|---|
| `/petservice` | Open the Pet and Master pages. |
| `/petservice off` | Ask for confirmation, then pause with the kill switch. |
| `/petservice on` | Turn the kill switch OFF and resume the service. |
| `/petservice release` | Activate local safe mode until the next login or reload; notify Discord. |
| `/petservice request` | Queue an attention request that pings the master in Discord. |
| `/petservice call` | Alias of request; shares the same cooldown. |

## Data

Ordinary FFXIV chat is never stored or forwarded. Outgoing commands are classified
locally to allow chat and reject gameplay commands during a prompt. Only text
entered in the popup reply field is sent to the master.

The paired profile follows the currently logged-in character, including alts.
The device token is stored in local plugin configuration; do not share it.
Unpairing clears local credentials, cached reminders, and unsent replies.

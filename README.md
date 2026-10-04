# Pet Service

Author: **Anomaly**. Version **0.1.0.1**. InternalName **PetService**. Dalamud API **15**.

Pet Service is a private FFXIV tool for consensual pet/master roleplay and power
dynamics. The master directs messages, reminders, and interactive prompts through
Discord or a private portal. The pet receives prompts in game and can reply,
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
5. Type **/petservice** and enter the one-time pairing code supplied by the master.
   The service address is built in; no website address is needed during setup.
6. Pairing is saved and reconnects automatically after future logins until revoked
   or paused with the local kill switch.

The plugin requires **Dalamud API 15**.

## Features

- Custom master messages shown in game with a large readable message, a reply box, and a custom snooze duration.
- Interactive prompts over the main game view, with native FFXIV chat kept available while gameplay inputs are guarded.
- Shared character status and location, including current world/data center,
  duty, combat, AFK, and game-idle state.
- Daily medication reminders managed centrally on the pet’s local clock.
- Today's overdue reminder appears after the character logs in.
- **I took them** and a pet-chosen snooze of 1 to 1,440 minutes.
- **/petservice request** pings the configured master in Discord, limited to once a minute.
- Discord notices for kill-switch activation and local safe mode (release).
- Cached schedules and saved-choice retries after a service outage.
- A minimal pet window with setup, connection status, a local kill switch, and unpair.

Prompts appear one at a time, oldest due first. Replies are entered in the popup;
normal game chat is not a reply to the master. Choices are saved locally before
prompt dismissal and retried after outages. **Taken** is a self-reported acknowledgment.

Prompts cover the main game view. FFXIV continues running while a prompt is open.
Native chat access depends on the supported game UI and input hooks. If those are
unavailable after a game update, the guard uses full input capture. The local kill
switch remains available in every prompt; the release command can also be entered
through Dalamud’s command interface.

## Local kill switch

Every kill-switch activation from setup, a popup, or **/petservice off** first asks
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
| `/petservice` | Open setup, connection status, and the kill switch. |
| `/petservice off` | Ask for confirmation, then pause with the kill switch. |
| `/petservice on` | Turn the kill switch OFF and resume the service. |
| `/petservice release` | Activate local safe mode until the next login or reload; notify Discord. |
| `/petservice request` | Queue an attention request that pings the master in Discord. |

## Data

Ordinary FFXIV chat is never stored or forwarded. Outgoing commands are classified
locally to allow chat and reject gameplay commands during a prompt. Only text
entered in the popup reply field is sent to the master.

The paired profile follows the currently logged-in character, including alts.
The device token is stored in local plugin configuration; do not share it.
Unpairing clears local credentials, cached reminders, and unsent replies.

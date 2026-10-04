# Changelog

## 0.1.0.2 — Pet and Master pages

- Add separate Pet and password-protected Master pages in game.
- Provide the website's profile, pairing, status/location, reminder, message, pending-prompt, and recent-response controls in the Master page.
- Keep administrator credentials in memory and clear the master session when the window closes or the portal locks.
- Add /petservice call and Call master buttons to the Pet page and prompts, sharing the existing attention-request cooldown.
- Use a normal-sized, fixed popup without a full-screen backdrop or close button.
- Preserve game-chat focus and detect native text entry independently of chat geometry.
- Add a Use game chat button to switch from popup replies to native chat.
- Add custom reply buttons and optional written replies, managed through the portal or Discord.
- Record and forward the selected button label to the master.
- Remove Reported/Received timestamps and event IDs from Discord notifications.
- Include the plugin icon inside the installer download.
- Retain housing addresses, custom snooze durations, attention requests, and the confirmed local kill switch.

## 0.1.0.1 — Prompt and control update

- Private pet/master roleplay with Discord and website administration.
- Install and update through a custom Dalamud repository.
- Pair once with the private service and reconnect automatically on future logins.
- Code-only pairing with the service connection built into the plugin.
- Minimal pet window with setup, connection status, kill switch, and unpair.
- Large master-message prompts with one header, readable text, and popup replies.
- Centrally configured daily medication reminders on the pet’s local clock and
  today's overdue login catch-up.
- Record **I took them** or choose a snooze duration from 1 to 1,440 minutes.
- Character status and location, including world/data center, duty, combat,
  AFK, and game-idle state.
- Housing addresses in observed location and Discord !where, including district,
  ward, plot, subdivision, estate grounds, interiors, apartments, and FC rooms.
- Cached schedules and saved choices for retry after outages.
- Main-screen prompts with improved native chat access and guarded gameplay input.
- Persistent local kill switch pauses prompts, normal synchronization, and status sharing.
- Confirm kill-switch activation from every plugin screen and /petservice off.
- Queue Discord notices for kill-switch activation and local safe mode.
- Request the master’s attention with /petservice request, limited to once a minute.
- Forward chosen snooze durations to Discord and show them in the master portal.
- Master-local display times in the portal and Discord.
- Cancel active synchronization and ignore late responses through pause/resume.
- Local release command and queued reminders retained until acknowledged.
- Ordinary FFXIV chat is never stored or forwarded.

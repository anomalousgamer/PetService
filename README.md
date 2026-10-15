# Pet Service

Author: **Anomaly** · Version **0.6.0.1**

A private Pet/Master connection for FINAL FANTASY XIV. Open **/toh** or **/petservice** for the Pet and password-protected Master pages. The companion website is a separate package. The service connection is included in the plugin; pairing needs only the Master's one-time code.

The Pet pages offer Home, Contact, Points, Routines, Calendar, Tasks, Characters and Setup. The pet chooses which characters may use the connection and can pause it with the confirmed local kill switch. An unapproved character pauses all sharing and Master effects. Returning to an enabled character resumes the connection. Safe release frees gameplay for the current login.

The Master portal groups pages into Now, Control, History, and Manage. It manages the same profiles, reminders, prompts, inventory, tasks, settings, and history as the website. Pairing codes and profile management are on Profile. Task evidence is attached to individual assignments, with a separate search for relevant saved observations. The global Master identity setting chooses a display name or the Master label and an optional supplied portrait. Both can be changed at any time.

Game chat and conversation partners remain private. Only intentional plugin replies and Contact requests are forwarded. Native game chat remains accessible while the supported gameplay guard is active. The game itself continues running.

| Command | Action |
|---|---|
| `/toh` or `/petservice` | Open the plugin. |
| `chat` | Open the separate private RP chat window. |
| `call` | Open Contact and choose a request. |
| `off` | Confirm the kill switch. |
| `on` | Resume the connection. |
| `release` | Release gameplay guarding for this login. |
| `changes` | Open What's New. |
| `checkupdates` | Check for a repository update. |

The supplied icon is included in downloads. No AI-generated artwork is included.

The gameplay controls show a remaining-time countdown and retained request/outcome history. Daily reminder previews show each custom reply’s behavior before saving. Honorific and Moodles have separate controls, saved selections, color pickers, previews, and request results. Moodles previews use game icons and descriptions; removal is limited to PetService-managed statuses. Control outcomes are reported to the configured Discord channel.

Sleep requests Play Dead, bonds gameplay, and blocks outgoing native and Chat2 chat. The local Emergency Wake Up button ends that Sleep command; it leaves other Master controls active. Chat notification colors use an RGB wheel.


## Private conversations

Open **/toh chat** or **/petservice chat**, or use **Open private RP chat** on either role page. The chat window closes and resizes independently of the main portal. Its message box has its own resize handle. Master chat retains its own unlocked session when the main portal closes; use **Lock chat** to clear that session.

Both participants can create and rename conversations, save speaking names and colors, switch names while writing, and copy/paste paragraphs. Enter sends; Shift+Enter adds a line. Existing messages retain the name and color used when sent. These aliases do not change account permissions.

On first use, both participants open **Chat → Settings**, compare their device fingerprints through a separate trusted channel, and approve each other's device. Approving shares the conversation keys available on that device. Additional Master devices need the same approval or an encrypted backup restored from an existing Master device. Unverified or unavailable keys show a locked conversation.

Messages, titles and speaking identities are encrypted on the sender's device. Asura stores ciphertext and delivery metadata; it receives no private chat keys. Game chat and Discord remain separate. History is fetched in pages and survives disconnections and restarts. Local keys and drafts use Windows protected storage. Export an encrypted backup from Settings and keep its password separately; losing every device and backup loses the ability to decrypt earlier history.

Either participant can delete a selected conversation for both people. The Master can also clear selected history or all conversations for that pet. Hold Ctrl+Shift while clicking the action, then confirm. Online clients clear after synchronizing; offline clients clear on reconnect. Old queued messages and pre-reset conversations cannot restore cleared history. Copied text, exports and historical hosting backups cannot be recalled.

## Master sounds

Use **Master sounds** on the Master page to upload, replace, remove and preview your own PCM WAV clips (up to 15 seconds and 2 MB), play them for the selected pet, and assign optional event sounds. The library starts empty and every event starts at None. No generated or bundled audio is supplied. A reward sound plays once per positive award, regardless of the number of points awarded. A clicker is any clip the Master uploads and assigns.

Optional assignments cover points and milestones, rewards, tasks, reminders and responses, Master messages, bonding, gagging, Sleep, integrations, Contact, and selected activity events. Milestone thresholds, due-soon minutes, cooldowns and location/item filters are configurable. Empty filters match any supported event. The entire shared sound library downloads automatically, including clips with no event assignment, and stays on the machine across logouts, restarts and disconnections. A versioned library snapshot removes deleted clips after the next successful connection, including deletions made while the pet was offline. Replacements download and pass a content-hash check before replacing their old local version. Failed requests keep existing files and retry; confirmed deletions cancel unfinished downloads. Both Master portals show the pet’s latest download and cleanup report. Playback requests expire after ten seconds and old cues are skipped. The pet controls volume from 10–100% in Setup and can preview a cached uploaded clip. The kill switch and unapproved characters stop remote playback.

Discord controls: **!sounds &lt;pet&gt;**, **!sound &lt;pet&gt; &lt;sound-id&gt;**, and **!soundassign &lt;pet&gt; &lt;event&gt; &lt;sound-id|none&gt;**. The portals provide the full event editor. Recent playback requests distinguish requested, started, ended, failed and skipped; playback state does not prove a sound was heard.


Private chat has personal conversation menus: right-click a conversation to rename it, mute/unmute its visual new-message alerts, pause/resume its sending and receiving, or delete it for both participants. Deletion requires Ctrl+Shift when choosing it and a confirmation. Chat Settings provides a global pause for all of your conversations, shared across your devices; it does not pause the other participant. Messages they send remain encrypted on the server and catch up after resuming. Existing received history remains readable. Muting adds no audio and does not stop delivery.

The gameplay kill switch does not affect private RP chat. Only unapproved character access pauses the pet's chat connection; the pet's own chat pause settings are independent. Sleep also leaves private RP chat usable, including typing and sending. Native FFXIV chat and Chat2 remain blocked during Sleep. An Open private chat button on the Sleep panel opens this independent window. Closed chat windows use a small metadata check while the portal/game is focused to notify about unmuted conversations; message pages load only for the open conversation. No private message text appears in those alerts.

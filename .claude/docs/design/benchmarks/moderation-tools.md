# Benchmark: moderation tools and user management

Method: product docs fetched 2026-10-10 (one source URL per row). Ours = `ModerationController.cs`, `CommunityController.cs`, the mass-ban services, and `app/.../feature/{community,moderation}/ui`, read this run. Dashboard client gaps counted by script against `server/openapi/v1.json`. Twitch's Shared Ban Info help page returned 404 to a plain fetch; its rows wait for a Chrome DevTools MCP read and say "not checked".

## Products

- Moobot batch ban tool https://moo.bot/docs/banning-blocking-and-flagging-large-numbers-of-twitch-users
- Fossabot nukes https://fossabot.com/docs/nukes/usage.md
- TwitchMassBan userscript https://openuserjs.org/scripts/victornpb/TwitchMassBan_-_Easily_ban_hate_raid_accounts
- Twitch viewer card and mod comments https://blog.twitch.com/en/2019/01/31/new-channel-moderation-tools-are-now-live-in-chat-4188af21dbf9/

## Features

| Feature | Seen in | Source | Ours | Status | Slice |
| --- | --- | --- | --- | --- | --- |
| Ban one account in many channels at once | Moobot | https://moo.bot/docs/banning-blocking-and-flagging-large-numbers-of-twitch-users | mass-ban API, no screen | partial | S-MASSBAN-SCREEN |
| Enter targets by username or user id | Moobot | https://moo.bot/docs/banning-blocking-and-flagging-large-numbers-of-twitch-users | API takes ids; no screen input | partial | S-MASSBAN-SCREEN |
| Paste or import a list of accounts | TwitchMassBan | https://openuserjs.org/scripts/victornpb/TwitchMassBan_-_Easily_ban_hate_raid_accounts | API takes a list; no screen | partial | S-MASSBAN-SCREEN |
| Reason sent with every ban | Moobot | https://moo.bot/docs/banning-blocking-and-flagging-large-numbers-of-twitch-users | mass-ban request reason | partial | S-MASSBAN-SCREEN |
| Preview before running | TwitchMassBan | https://openuserjs.org/scripts/victornpb/TwitchMassBan_-_Easily_ban_hate_raid_accounts | mass-ban preview API, no screen | partial | S-MASSBAN-SCREEN |
| Per-account result log (succeeded / failed) | Moobot | https://moo.bot/docs/banning-blocking-and-flagging-large-numbers-of-twitch-users | batches API, no screen | partial | S-MASSBAN-HISTORY |
| Pause and resume a running batch | Moobot | https://moo.bot/docs/banning-blocking-and-flagging-large-numbers-of-twitch-users | none | missing | S-MASSBAN-HISTORY |
| Offline-only run mode | Moobot | https://moo.bot/docs/banning-blocking-and-flagging-large-numbers-of-twitch-users | live hold `MassBanChannelPlanner.cs:206` | partial | S-MASSBAN-HOLD-RELEASE |
| Editors may run it for the streamer | Moobot | https://moo.bot/docs/banning-blocking-and-flagging-large-numbers-of-twitch-users | opt-in per channel, API only | partial | S-MASSBAN-OPTIN-TOGGLES |
| Export or share a ban list | Moobot | https://moo.bot/docs/banning-blocking-and-flagging-large-numbers-of-twitch-users | none | missing | S-MASSBAN-HISTORY |
| Ignore (skip) accounts in a list | TwitchMassBan | https://openuserjs.org/scripts/victornpb/TwitchMassBan_-_Easily_ban_hate_raid_accounts | none | missing | S-MASSBAN-SCREEN |
| Show account age next to each target | TwitchMassBan | https://openuserjs.org/scripts/victornpb/TwitchMassBan_-_Easily_ban_hate_raid_accounts | none on mass-ban | missing | S-MASSBAN-SCREEN |
| Skip a target already banned | TwitchMassBan | https://openuserjs.org/scripts/victornpb/TwitchMassBan_-_Easily_ban_hate_raid_accounts | counted as success, but hold still fires `MassBanExecutor.cs:153` | partial | S-MASSBAN-SKIP-BANNED |
| Nuke by phrase | Fossabot | https://fossabot.com/docs/nukes/usage.md | `ModerationController.cs:1180` | partial | S-MOD-NUKE-SCREEN |
| Nuke by regular expression | Fossabot | https://fossabot.com/docs/nukes/usage.md | nuke API, not checked for regex | partial | S-MOD-NUKE-SCREEN |
| Nuke action choice: delete, timeout, ban | Fossabot | https://fossabot.com/docs/nukes/usage.md | nuke API, not checked | partial | S-MOD-NUKE-SCREEN |
| Scrollback window | Fossabot | https://fossabot.com/docs/nukes/usage.md | not checked | partial | S-MOD-NUKE-SCREEN |
| Radiation: keep acting on new messages for a time | Fossabot | https://fossabot.com/docs/nukes/usage.md | none | missing | S-MOD-NUKE-SCREEN |
| Nuke report on the dashboard | Fossabot | https://fossabot.com/docs/nukes/usage.md | `GET nuke` `ModerationController.cs:1212`, no screen | partial | S-MOD-NUKE-SCREEN |
| Roll back a nuke | Fossabot | https://fossabot.com/docs/nukes/usage.md | `nuke/{batchId}/revert` `ModerationController.cs:1197`, no screen | partial | S-MOD-NUKE-SCREEN |
| Roll back a mass ban | Fossabot | https://fossabot.com/docs/nukes/usage.md | none | missing | S-MASSBAN-HISTORY |
| User card from a click on a name | Twitch | https://blog.twitch.com/en/2019/01/31/new-channel-moderation-tools-are-now-live-in-chat-4188af21dbf9/ | `ViewerProfileScreen.kt` | have | |
| Account age on the user card | Twitch | https://blog.twitch.com/en/2019/01/31/new-channel-moderation-tools-are-now-live-in-chat-4188af21dbf9/ | not checked on profile | partial | S-USER-HUB |
| Channel chat, timeout and ban history per user | Twitch | https://blog.twitch.com/en/2019/01/31/new-channel-moderation-tools-are-now-live-in-chat-4188af21dbf9/ | `history/{userId}` `ModerationController.cs:788` | partial | S-USER-HUB |
| Mod comments on a user, shared by mods | Twitch | https://blog.twitch.com/en/2019/01/31/new-channel-moderation-tools-are-now-live-in-chat-4188af21dbf9/ | notes API `ModerationController.cs:1407-1472` | partial | S-USER-HUB |
| Delete one message without a timeout | Twitch | https://blog.twitch.com/en/2019/01/31/new-channel-moderation-tools-are-now-live-in-chat-4188af21dbf9/ | not checked | partial | S-USER-HUB |
| All mod settings on one page | Twitch | https://blog.twitch.com/en/2019/01/31/new-channel-moderation-tools-are-now-live-in-chat-4188af21dbf9/ | split over Community and Moderation | partial | S-USER-HUB |
| Ban list with search and unban | Twitch | https://blog.twitch.com/en/2019/01/31/new-channel-moderation-tools-are-now-live-in-chat-4188af21dbf9/ | `bans` API, no client | partial | S-MOD-BANLIST |
| Share bans with up to 30 channels, request and accept | Twitch | not checked | opt-in model differs | partial | S-MOD-TOOLS-SPEC |

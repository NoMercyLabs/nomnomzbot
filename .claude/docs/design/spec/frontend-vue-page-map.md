# Vue dashboard: the page map (V23)

This file puts every feature of `FEATURES-BY-SIDEBAR.md` on one page, one view and one level.
It is the checklist for the page slices. Decision V23 in `frontend-vue-foundation.md` asks for it.
The script `scripts/check-page-map.py` proves the coverage. Run it after every edit of this file.

## How to read it

- The feature is the bold page or tab name in a table of `FEATURES-BY-SIDEBAR.md`. It is copied word for word.
- The routes are the 16 routes of section 4 in `frontend-vue-components.md`.
- Level `overview` means the first thing you see when the route opens.
- Level `detail` means one click in: a tab, a panel or a dialog.
- Level `advanced` means behind a section, a nav row or a "more options" control.
- The mockup column names the file in `.scratch/layout-mockups/` and the element.
- "nav row, view not drawn" means the mockup lists the name in its side navigation, but no view is drawn for it yet.
- The endpoint column is the method and path from `server/openapi/v1.json`. It shows one endpoint that feeds the view. A page needs more calls than the one shown.
- "no endpoint" means `v1.json` has nothing for it. The page shows the designed empty state with its reason (C11). It is never faked.
- A feature that fits no route is in the last table, "Not placed".

## /app (Home)

| Feature | View / tab | Level | Mockup element | Endpoint |
|---|---|---|---|---|
| Home | dashboard/home, live and offline states | overview | `1-board.html` header, "Review queue", "Needs attention", "Quick actions", "Ready for the next stream" | `GET /api/v{version}/dashboard/{channelId}/stats` |

## /app/chat (Chat)

| Feature | View / tab | Level | Mockup element | Endpoint |
|---|---|---|---|---|
| Chat | dashboard/chat, One channel / All my channels | overview | `3-live-split.html` tabs "One channel" and "All my channels", "Chat modes", "Mod tools" | `GET /api/v{version}/channels/{channelId}/chat/messages` |

## /app/stream (Stream)

| Feature | View / tab | Level | Mockup element | Endpoint |
|---|---|---|---|---|
| Stream | dashboard/stream, Stream info / Run the stream / History | overview | `10-stream.html` tabs "Stream info", "Run the stream", "History", "Ad breaks", "Stream markers" | `GET /api/v{version}/channels/{channelId}/stream` |

## /app/analytics (Analytics)

| Feature | View / tab | Level | Mockup element | Endpoint |
|---|---|---|---|---|
| Analytics | dashboard/analytics, Overview and six more tabs | overview | `11-analytics.html` tabs "Overview", "Cause and effect", "What chat likes", "One stream", "Streams", "Chat", "Community" | `GET /api/v{version}/channels/{channelId}/analytics/channel/summary` |

## /app/commands (Commands)

| Feature | View / tab | Level | Mockup element | Endpoint |
|---|---|---|---|---|
| Commands | dashboard/commands, Custom / Built-in | overview | `2-list-detail.html` list, "Response", "Who and where", "More options: cooldown" | `GET /api/v{version}/channels/{channelId}/commands` |

## /app/pipelines/:id (Pipelines)

| Feature | View / tab | Level | Mockup element | Endpoint |
|---|---|---|---|---|
| Pipelines | dashboard/pipelines, Steps / Runs | detail | `5-workbench.html` "Starts from", "Step by step", "Test runs", "Last test run" | `GET /api/v{version}/channels/{channelId}/pipelines` |

## /app/scripts (Scripts)

| Feature | View / tab | Level | Mockup element | Endpoint |
|---|---|---|---|---|
| Scripts | dashboard/scripts, All / On / Off / Failed | advanced | `13-scripts.html` "Scripts" list, "Open in editor", "Test", "Runs", "Console" | `GET /api/v{version}/code-scripts` |

## /app/overlays (Overlays)

| Feature | View / tab | Level | Mockup element | Endpoint |
|---|---|---|---|---|
| Overlays | dashboard/overlays, All / On / Off / Has a problem | detail | `12-overlays.html` "Overlays" list, "Live preview", "Health", "Settings" | `GET /api/v{version}/channels/{channelId}/widgets` |

## /app/people (People)

| Feature | View / tab | Level | Mockup element | Endpoint |
|---|---|---|---|---|
| Viewers | dashboard/people, All / Moderators / VIPs / Subscribers / Regulars / New | overview | `14-people.html` "Search people" list and the profile panel | `GET /api/v{version}/channels/{channelId}/community` |

## /app/music (Music)

| Feature | View / tab | Level | Mockup element | Endpoint |
|---|---|---|---|---|
| Music | dashboard/music | overview | `15-music.html` "Now playing", "Request queue", "Request rules", "History" | `GET /api/v{version}/channels/{channelId}/music/now-playing` |

## /app/tts (TTS)

| Feature | View / tab | Level | Mockup element | Endpoint |
|---|---|---|---|---|
| TTS | dashboard/tts, Overview / Voices / Rules | overview | `16-tts.html` tabs "Overview", "Voices", "Rules", "Spoken history", "Word filter and pronunciation" | `GET /api/v{version}/channels/{channelId}/tts/config` |

## /app/settings (Settings)

| Feature | View / tab | Level | Mockup element | Endpoint |
|---|---|---|---|---|
| Settings | dashboard/settings, Channel tab, section Bot basics | overview | `4-settings.html` tabs "Channel" and "This device", nav rows "Bot basics", "Bot personality", "Your data", "Channel management" | `GET /api/v{version}/channels/{channelId}/settings/basics` |
| Features | dashboard/settings, Channel tab, section Features | detail | `4-settings.html` nav row "Features" (nav row, view not drawn) | `GET /api/v{version}/channels/{channelId}/features` |
| Integrations | dashboard/settings, Channel tab, section Integrations | detail | `4-settings.html` nav row "Integrations" (nav row, view not drawn); `1-board.html` "Connections" shows the status | `GET /api/v{version}/channels/{channelId}/integrations` |
| Discord | dashboard/settings, Channel tab, section Discord | advanced | `4-settings.html` nav row "Discord" (nav row, view not drawn) | `GET /api/v{version}/channels/{channelId}/discord/connections` |
| Supporters | dashboard/settings, Channel tab, section Supporter sources | advanced | `4-settings.html` "Supporter sources", "Sources", "Last deliveries", "Test delivery" | `GET /api/v{version}/supporters/connections` |
| Federation | dashboard/settings, Channel tab, section Federation | advanced | `4-settings.html` nav row "Federation" (nav row, view not drawn) | `GET /api/v{version}/federation/peers` |
| API & devices | dashboard/settings, Channel tab, section API & devices | advanced | `4-settings.html` "API & devices", "Tokens and devices", "Pair a device" | `GET /api/v{version}/channels/{channelId}/automation/tokens` |
| Webhooks | dashboard/settings, Channel tab, section Webhooks | advanced | `4-settings.html` nav row "Webhooks" (nav row, view not drawn) | `GET /api/v{version}/channels/{channelId}/webhooks/inbound` |
| Custom Events | dashboard/settings, Channel tab, section Custom event feeds | advanced | `4-settings.html` nav row "Custom event feeds" (nav row, view not drawn) | `GET /api/v{version}/custom-data-sources` |

## /app/admin/channels (Platform Admin: Channels)

No feature of `FEATURES-BY-SIDEBAR.md` lives here. Platform Admin is outside the management sidebar.
The mockup is `6-table.html`.

## /setup (First-time setup)

No feature of `FEATURES-BY-SIDEBAR.md` lives here. The setup wizard is outside the management sidebar.
The mockup is `7-focus.html`. The credentials it asks for belong to Integrations, which is placed on /app/settings.

## /me (Viewer home)

No feature of `FEATURES-BY-SIDEBAR.md` lives here. The viewer shell is outside the management sidebar.
The mockup is `8-viewer.html`.

## /app/catalogue (Component catalogue)

No feature lives here. Development builds only. The mockup is `9-components.html`.

## Not placed

These features have no route and no drawn page yet. The reason says what is missing.
The proposed home is where the page builder should put it once a mockup exists. Stoney decides.

| Feature | Reason | Proposed home | Endpoint |
|---|---|---|---|
| Deck | No mockup and no route. `1-board.html` "Quick actions" shows only a few buttons. The button layout has no storage endpoint. | New route /app/deck; Home shows the top 8 | no endpoint |
| OBS | No mockup and no route. Only the connection is drawn, in Integrations. | New route /app/obs | `GET /api/v{version}/channels/{channelId}/obs/state` |
| VTube Studio | No mockup and no route. | New route /app/vtube-studio | `GET /api/v{version}/channels/{channelId}/vts/inventory` |
| Event Responses | No mockup and no route. `1-board.html` shows only a count ("15 event responses on"). | New route /app/event-responses | `GET /api/v{version}/channels/{channelId}/event-responses` |
| Triggers & Timers | No mockup and no route. `1-board.html` has a timers dialog that only switches timers on and off. | New route /app/triggers | `GET /api/v{version}/channels/{channelId}/timers` |
| Library | No mockup and no route. `2-list-detail.html` draws only a sound picker inside the command editor. | New route /app/library | `GET /api/v{version}/sound-clips` |
| Gallery | No mockup and no route. Only `index.html` lists it. | New route /app/gallery | `GET /api/v{version}/channels/{channelId}/platform-templates` |
| Overview | The People mockup has no overview tab. `1-board.html` "Review queue" shows a small part. | Tab on /app/people | `GET /api/v{version}/channels/{channelId}/moderation/stats` |
| Queue | The People mockup has no queue tab. `1-board.html` "Review queue" shows the held messages. | Tab on /app/people | `GET /api/v{version}/channels/{channelId}/moderation/automod/queue` |
| History | The People mockup has no history tab. The viewer card shows one person only. | Tab on /app/people | `GET /api/v{version}/channels/{channelId}/moderation/history` |
| Rules | The People mockup has no rules tab. | Tab on /app/people | `GET /api/v{version}/channels/{channelId}/moderation/rules` |
| Roles (with Individual permissions) | The People mockup has no roles tab. `4-settings.html` has an old "Permissions" nav row. | Tab on /app/people | `GET /api/v{version}/channels/{channelId}/roles` |
| Channel Points | No mockup and no route. `1-board.html` has a rewards dialog that only switches rewards on and off. | New route /app/channel-points | `GET /api/v{version}/channels/{channelId}/rewards` |
| Economy | No mockup and no route. | New route /app/economy | `GET /api/v{version}/channels/{channelId}/economy/config` |
| Games | No mockup and no route. | New route /app/games | `GET /api/v{version}/channels/{channelId}/games/sessions` |
| Giveaways | No mockup and no route. | New route /app/giveaways | `GET /api/v{version}/giveaways` |
| Media Share | No mockup and no route. Only `index.html` lists it. | New route /app/media-share | `GET /api/v{version}/media-share/queue` |

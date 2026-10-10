# Benchmark: dashboard information architecture (who finds what, where)

Method: sources are the three lane notes (L4a desktop bots, L4b web bots, L4c platform tools), read 2026-10-11, and every row keeps a source URL the lane read. Ours comes from the app census in the research paper ui-restructure-research-2026-10-11.md (section 1 for today, sections 3 to 8 for the target). "none in census" means the page census lists no such surface. Lane gaps (login walls, unread pages) stay marked unverified in the lane notes and are not used as proof here.

## Products

- Streamer.bot (desktop) https://docs.streamer.bot/guide/actions
- Firebot (desktop) https://docs.firebot.app/quickstart
- Mix It Up (desktop) https://mixitup.bot/docs/getting-started
- SAMMI (desktop) https://sammi.solutions/docs
- Aitum Nexus (desktop) https://app.aitum.help/
- Lumia Stream (desktop) https://lumiastream.com/
- Fossabot (web) https://fossabot.com/docs/getting-started.md
- Nightbot (web) https://docs.nightbot.tv/setup
- StreamElements (web) https://support.streamelements.com/hc/en-us/articles/10474723554962-StreamElements-Dashboard-Overview
- Streamlabs Cloudbot and dashboard (web) https://support.streamlabs.com/hc/en-us/articles/24731458781851
- Moobot (web) https://moo.bot/docs
- Wizebot (web) https://wizebot.tv/en/services
- Botrix (web) https://botrix.live/assets/i18n/en/menu.json
- Sery_Bot (chat commands, no dashboard) https://docs.sery.bot/
- Twitch Stream Manager https://help.twitch.tv/s/article/stream-manager?language=en_US
- Twitch Mod View https://help.twitch.tv/s/article/mod-view?language=en_US
- Twitch Creator Dashboard https://help.twitch.tv/s/article/creator-dashboard?language=en_US
- YouTube Studio Live Control Room https://support.google.com/youtube/answer/2907883
- Kick creator dashboard and moderator dashboard https://help.kick.com/en/articles/7120642-understanding-your-kick-creator-dashboard
- Twitch Chat and Events (OBS dock) https://help.twitch.tv/s/article/creator-chat-and-events?language=en_US

## Features

| Feature | Seen in | Source | Ours | Status | Slice |
| --- | --- | --- | --- | --- | --- |
| One left sidebar with collapsible groups as the main nav | Streamer.bot | https://github.com/Streamerbot/docs/blob/main/streamerbot/2.guide/01.core/assets/view-actions.png | 9 sidebar groups in ShellNav.kt:113-202; Setup group starts collapsed (ShellScreen.kt:893) | have | - |
| Sidebar groups named by the job the user does | Firebot, Lumia Stream | https://github.com/crowbartools/Firebot/blob/master/src/gui/app/directives/sidebar/sidebar.js | Groups follow the kind of thing (Chat has 12 pages, Music 6); target is 8 job groups | partial | S-IA-NAV |
| Live page is the first link, set-up pages below it | Firebot | https://github.com/crowbartools/Firebot/blob/master/src/gui/app/services/sidebar-manager.service.js | Home is first (Dashboard, read-only for Moderator); no live layout shaped by role and stream state | partial | S-IA-ROLE-HOMES |
| Flat nav of 34 pages with no groups | Mix It Up | https://github.com/MixItUpBot/Desktop/blob/c5a3497/MixItUp.WPF/MainWindow.xaml.cs | - | not-doing | A flat list of 34 pages does not guide users; we group pages by job |
| User hides or reorders nav pages with a drag handle and a checkbox | Mix It Up | https://github.com/MixItUpBot/Desktop/blob/c5a3497/MixItUp.WPF/Controls/MainControls/MainMenuControl.xaml | none in census | missing | S-IA-PINS |
| Sidebar contracts to an icon rail and remembers the state | Firebot | https://github.com/crowbartools/Firebot/blob/master/src/gui/app/services/sidebar-manager.service.js | none in census | missing | S-IA-COMPACT |
| Nav item hidden when the user is below its read floor | Firebot | https://github.com/crowbartools/Firebot/blob/master/src/gui/app/directives/sidebar/sidebar.js | Read floor per page in ShellNav.kt:113-202; page hidden below it | have | - |
| Tabs inside one page instead of many small pages | Nightbot, Streamlabs | https://support.streamlabs.com/hc/en-us/articles/50635570817435 | 22 keyed tab rows today; 11 old pages become tabs in the target | partial | S-IA-NAV |
| Settings as a left list of categories with a description | Firebot | https://github.com/crowbartools/Firebot/blob/master/src/gui/app/controllers/settings.controller.js | Settings page exists in Setup; target is 6 tabs | partial | S-IA-SETUP |
| Account settings behind the avatar menu | Streamlabs | https://support.streamlabs.com/hc/en-us/articles/42197722049947 | Profile menu holds identity, theme, language and the switch into Admin | have | - |
| Language and dark or light theme in the profile menu | StreamElements, Moobot | https://support.streamelements.com/hc/en-us/articles/10474723554962-StreamElements-Dashboard-Overview | Language and theme live in the profile menus of both shells (paper-1 sections 1.3 and 2) | have | - |
| Help icon on every page that opens its own docs page | Mix It Up | https://github.com/MixItUpBot/Desktop/blob/c5a3497/MixItUp.WPF/Controls/MainControls/MainMenuControl.xaml | none in census | missing | S-IA-NAV |
| Language picker that serves 30 or more languages | Wizebot | https://panel.wizebot.tv/ | Language is chosen in 3 places; owner asked for a font per language | partial | S-KMP-FONTS |
| Role ladder with plain role names | Twitch Creator Dashboard | https://help.twitch.tv/s/article/Managing-Roles-for-your-Channel?language=en_US | Ladder Viewer to Broadcaster; the UI still says "Super Mod" instead of Lead Moderator | partial | S-IA-SPEC |
| Numbered user levels shown to streamers | StreamElements | https://support.streamelements.com/hc/en-us/articles/10474423416722-StreamElements-Chatbot-Complete-Guide-Resources | - | not-doing | Users see role names, never numbers (settled rule in the product statement) |
| Permission list shaped like the nav (Read and Modify per module) | Botrix | https://botrix.live/assets/i18n/en/routes/subusers.json | Each page has a read floor and a manage floor; no per-user view of them | partial | S-IA-PEOPLE |
| Action disabled with the reason when the user is below the manage floor | Streamlabs, Botrix | https://support.streamlabs.com/hc/en-us/articles/6094149476123 | Settled rule: hide below read floor, disable with reason below manage floor | have | - |
| Roles manager sits next to the people it governs | Twitch Creator Dashboard | https://help.twitch.tv/s/article/Managing-Roles-for-your-Channel?language=en_US | Roles page is in Setup today; target moves it to People | partial | S-IA-PEOPLE |
| Moderator view is a subset of the streamer view | Twitch Stream Manager, Twitch Mod View, Kick | https://help.twitch.tv/s/article/mod-view?language=en_US | One shell for every role; a role sees a subset (settled rule) | have | - |
| Moderator lobby that lists the live channels you moderate | Twitch Mod View | https://help.twitch.tv/s/article/mod-view?language=en_US | none in census | missing | S-IA-ROLE-HOMES |
| Channel switcher at the top of the nav | Fossabot, Moobot, Twitch Mod View | https://fossabot.com/docs/commands/custom-commands.md | Multi-Chat page covers many channels; old addresses have no channel (#/slug) | partial | S-IA-ROUTES |
| One queue for held messages, reports and unban requests | Twitch Stream Manager, Twitch Mod View | https://help.twitch.tv/s/article/stream-manager?language=en_US | Moderation Queue page exists; target merges held messages, reports, appeals and unbans | partial | S-IA-PEOPLE |
| Mod action log with filters | Twitch Stream Manager, Kick | https://help.kick.com/en/articles/10162074-moderation-features-guide | History page lists moderation actions; target is a People tab with platform filter | partial | S-IA-PEOPLE |
| Moderation settings and live mod tools reachable from one place | Twitch Creator Dashboard, Kick | https://help.kick.com/en/articles/7120642-understanding-your-kick-creator-dashboard | Moderation has 4 routes on 1 screen; Community and Roles sit elsewhere | partial | S-IA-PEOPLE |
| Viewer card beside chat with history, notes, points and roles | Twitch Mod View | https://help.twitch.tv/s/article/mod-view?language=en_US | Community page lists viewers; card in a side panel is the target | partial | S-IA-PEOPLE |
| Grid of buttons for live use, apart from the set-up screens | SAMMI, Firebot | https://github.com/crowbartools/Firebot/blob/master/src/gui/app/templates/_control-deck.html | none in census | missing | S-IA-DECK |
| Button grid for phone or tablet with a QR code to connect | Firebot | https://github.com/crowbartools/Firebot/blob/master/src/gui/app/templates/_control-deck.html | none in census | missing | S-IA-DECK |
| Remote apps that press the deck buttons from any device | SAMMI | https://github.com/SAMMISolutions/docs/blob/3f8819d/doc_posts/_components/deck-app.md | none in census | missing | S-IA-DECK |
| Quick triggers: pin any command as a button on the home page | Aitum Nexus | https://app.aitum.help/commands/quick-triggers | none in census | missing | S-IA-DECK |
| Quick commands pinned into slots of a dashboard window | Mix It Up | https://github.com/MixItUpBot/Desktop/blob/c5a3497/MixItUp.Base/ViewModel/Dashboard/QuickCommandsDashboardControlViewModel.cs | none in census | missing | S-IA-DECK |
| Quick Actions panel with search and categories | Twitch Stream Manager | https://help.twitch.tv/s/article/stream-manager?language=en_US | none in census | missing | S-IA-DECK |
| User picks which quick actions appear | Twitch Stream Manager | https://help.twitch.tv/s/article/how-to-use-polls?language=en_US | none in census | missing | S-IA-DECK |
| Stream Deck plugin that shows the same buttons | Aitum Nexus, SAMMI, Mix It Up | https://github.com/SAMMISolutions/docs/blob/3f8819d/doc_posts/_integrations/elgato_stream_deck.md | Stream Deck spec exists (spec/stream-deck.md) and POST /automation/v1/invoke exists; no Deck page yet | partial | S-IA-DECK |
| Pop-out window for a second monitor | Twitch Stream Manager, YouTube Live Control Room | https://support.google.com/youtube/answer/2907883 | none in census | missing | S-IA-DECK |
| Dashboard window with several layouts and a pick-list per slot | Mix It Up | https://github.com/MixItUpBot/Desktop/blob/c5a3497/MixItUp.Base/ViewModel/Dashboard/DashboardWindowViewModel.cs | none in census | missing | S-IA-DECK |
| One-click panic button for chat modes | Streamlabs | https://support.streamlabs.com/hc/en-us/articles/4406124398235 | none in census | missing | S-IA-DECK |
| Shield mode toggle in the chat header | Twitch Mod View | https://help.twitch.tv/s/article/mod-view?language=en_US | none in census | missing | S-IA-NAV |
| Shield mode swaps the page to a preset layout | Twitch Mod View | https://help.twitch.tv/s/article/mod-view?language=en_US | none in census | missing | S-IA-ROLE-HOMES |
| Same action by panel and by chat command | Twitch Stream Manager, Twitch chat | https://help.twitch.tv/s/article/chat-commands?language=en_US | 36 built-in commands exist; Deck buttons bind the existing action catalogue | partial | S-IA-DECK |
| Catalogue of action types shared by commands and buttons | Mix It Up | https://github.com/MixItUpBot/Desktop/tree/c5a3497/MixItUp.Base/Model/Actions | Existing action catalogue; Deck buttons bind to it (paper-1 section 4) | have | - |
| Search palette over pages and tabs | Twitch Creator Dashboard | https://help.twitch.tv/s/article/creator-dashboard?language=en_US | none in census | missing | S-IA-PALETTE |
| Search that finds and runs buttons, triggers and commands | SAMMI | https://github.com/SAMMISolutions/docs/blob/3f8819d/doc_posts/_components/core.md | none in census | missing | S-IA-PALETTE |
| Favourites the user chooses | Streamer.bot | https://github.com/Streamerbot/docs/blob/main/streamerbot/2.guide/01.core/01.actions.md | none in census | missing | S-IA-PINS |
| Layout choice stored on the account, not in the browser | Twitch Mod View | https://help.twitch.tv/s/article/mod-view?language=en_US | none in census | missing | S-IA-PINS |
| Default layout per role | Twitch Stream Manager | https://help.twitch.tv/s/article/stream-manager?language=en_US | none in census | missing | S-IA-PINS |
| Reset to default after customising | Twitch Stream Manager, Kick | https://help.kick.com/en/articles/7120642-understanding-your-kick-creator-dashboard | none in census | missing | S-IA-PINS |
| Named layouts the user switches from a title drop-down | Twitch Stream Manager | https://help.twitch.tv/s/article/stream-manager?language=en_US | - | not-doing | Home layout follows role and stream state; pins replace hand-built layouts |
| Free drag-and-resize panels in an edit mode | Twitch Mod View, Kick | https://help.kick.com/en/articles/10162074-moderation-features-guide | - | not-doing | Fixed layout per role keeps one tested shell; the Deck covers custom actions |
| Home board where the user picks which widgets show | Moobot | https://moo.bot/docs/adjusting-your-moobot-dashboard | - | not-doing | A new user must see a full home, not a partial board; layout is fixed per role |
| Triggers, actions and overlays made in one place | Streamer.bot, Aitum Nexus, Mix It Up | https://github.com/Streamerbot/docs/blob/main/streamerbot/2.guide/01.core/01.actions.md | Commands, Pipelines, Widgets sit in the Chat group; target is one Build group | partial | S-IA-NAV |
| Default commands and custom commands in separate tabs | Nightbot, StreamElements | https://docs.nightbot.tv/control-panel/commands | Commands page exists; target has Custom and Built-in tabs (36 built-in) | partial | S-IA-TRIGGERS |
| Stream events answered on one page | Mix It Up, StreamElements | https://support.streamelements.com/hc/en-us/articles/10474423416722-StreamElements-Chatbot-Complete-Guide-Resources | Alerts and Event Responses both answer stream events | partial | S-IA-TRIGGERS |
| Chat triggers, voice triggers and timers as tabs | Nightbot, Firebot | https://docs.nightbot.tv/control-panel/timers | Three separate pages today; target is one page with 3 tabs | partial | S-IA-TRIGGERS |
| Overlay gallery with a link and test button per overlay | StreamElements, Streamlabs | https://support.streamlabs.com/hc/en-us/articles/24731458781851 | Widgets page with 23 widget kinds; target is Overlays with Gallery and My overlays | partial | S-IA-GALLERY |
| Start from a template in every create flow | SAMMI, StreamElements | https://support.streamelements.com/hc/en-us/articles/10474662182162-Quick-Start-Guide | none in census | missing | S-IA-GALLERY |
| Import and export of shareable bundles | Streamer.bot, SAMMI | https://github.com/Streamerbot/docs/blob/main/streamerbot/4.examples/starter-pack.md | Bundles page in Setup; target moves it to Gallery | partial | S-IA-GALLERY |
| Starter content in a new channel | Streamer.bot, Mix It Up | https://github.com/Streamerbot/docs/blob/main/streamerbot/4.examples/starter-pack.md | none in census | missing | S-IA-STARTER |
| Sounds, files, quotes and random responses in one library | Streamer.bot, Mix It Up | https://github.com/Streamerbot/docs/blob/main/streamerbot/2.guide/01.core/assets/credits_1.png | Sound Clips, Assets, Pick Lists and Quotes are 4 pages; Sound Clips and Assets share sounds:read | partial | S-IA-LIBRARY |
| One home for each feature and each setting | StreamElements | https://support.streamelements.com/hc/en-us/articles/10474423416722-StreamElements-Chatbot-Complete-Guide-Resources | Ko-fi sits on Webhooks and Supporters; Twitch app credentials sit in 3 places | partial | S-IA-GUARD |
| Song queue and settings as tabs of one page | Nightbot, StreamElements | https://docs.nightbot.tv/control-panel/song-requests | Music and Song Requests are 2 entries for one flow | partial | S-IA-MEDIA |
| TTS controls split into tabs by type | Lumia Stream, Mix It Up | https://github.com/lumiastream/lumia-translations/blob/main/src/translations/en.translations.json | TTS is one page; owner asked for tabs split by type | partial | S-IA-MEDIA |
| Request queue with approve or deny | Fossabot | https://fossabot.com/docs/mediarequest/moderation.md | Media Share page exists; target tabs Queue and Settings | partial | S-IA-MEDIA |
| Public viewer pages for points, store, songs and leaderboard | Botrix, StreamElements | https://support.streamelements.com/hc/en-us/articles/10474806635410-Manage-Your-Public-StreamElements-Profile | Viewer shell has 6 pages (ParticipantNav.kt:115-133) | partial | S-IA-ROLE-HOMES |
| Public commands page with search and role filter | Fossabot, Nightbot | https://fossabot.com/xqc/commands | Public viewer pages stay plain server HTML (OverlayHostController.cs:35, Program.cs:965-970) | partial | S-IA-ROLE-HOMES |
| Separate login for viewers on a separate host | Wizebot | https://panel.wizebot.tv/ | - | not-doing | One sign-in screen serves every role; only the shell differs |
| All settings only by chat command, no screen | Sery_Bot | https://docs.sery.bot/commands/channel-settings/ | - | not-doing | Users need an overview on screen; chat commands stay a second door |
| One always-visible place for connection status | Streamer.bot, SAMMI | https://github.com/Streamerbot/docs/blob/main/streamerbot/2.guide/01.core/assets/view-actions.png | none in census | missing | S-IA-SETUP |
| One-click power button to connect all services | Firebot | https://github.com/crowbartools/Firebot/blob/master/src/gui/app/directives/sidebar/sidebar.js | none in census | missing | S-IA-SETUP |
| Connect step shown inline on the feature page | Aitum Nexus | https://app.aitum.help/concepts/Integrations | OBS page holds the connection form today; target shows the connect step inline | partial | S-IA-SETUP |
| Integrations as the one home for every credential | Mix It Up, Aitum Nexus, Lumia Stream | https://github.com/MixItUpBot/Desktop/blob/c5a3497/MixItUp.WPF/Controls/MainControls/ServicesControl.xaml.cs | Integrations exists in Setup; Twitch app credentials also in the wizard and Settings | partial | S-IA-SETUP |
| First-run wizard | Firebot, Mix It Up, Lumia Stream | https://github.com/crowbartools/Firebot/blob/master/src/gui/app/directives/modals/misc/setup-wizard-modal.js | A setup wizard exists | have | - |
| Sign in with the platform and land in the dashboard | Fossabot, Nightbot | https://fossabot.com/docs/getting-started.md | A sign-in screen exists and leads into the shell | have | - |
| Wizard teaches the model with a worked example | Mix It Up | https://github.com/MixItUpBot/Desktop/blob/c5a3497/MixItUp.WPF/Windows/Wizard/NewUserWizardWindow.xaml | none in census | missing | S-IA-STARTER |
| Empty state that explains the feature and offers one primary action | Firebot | https://github.com/crowbartools/Firebot/blob/master/src/gui/app/templates/_control-deck.html | none in census | missing | S-IA-STARTER |
| Quick tour button inside the product | Moobot | https://moo.bot/docs/adjusting-your-moobot-dashboard | none in census | missing | S-IA-STARTER |
| Set-up checklist before going live | Streamer.bot | https://github.com/Streamerbot/docs/blob/main/streamerbot/1.get-started/2.setup/1.broadcasters.md | none in census | missing | S-IA-ROLE-HOMES |
| Mobile moderator view with reorder or hide of widgets | Twitch Mod View | https://help.twitch.tv/s/article/mod-view?language=en_US | none in census | missing | S-IA-COMPACT |
| Phone layout with a short bottom bar and a More sheet | Twitch Mod View | https://help.twitch.tv/s/article/mod-view?language=en_US | Target: sidebar 840dp and up, icon rail 600 to 839, bottom bar under 600 | missing | S-IA-COMPACT |
| Phone layout of the full bot dashboard (no web bot documents one) | Streamlabs, Moobot, Wizebot, Botrix | https://moo.bot/docs | none in census | missing | S-IA-COMPACT |
| Platform chips that filter chat and feeds | StreamElements, Lumia Stream | https://support.streamelements.com/hc/en-us/articles/10474542090770-Activity-Feed-Overview | none in census | missing | S-IA-PLATFORM |
| One place to edit title and game | StreamElements | https://support.streamelements.com/hc/en-us/articles/10474723554962-StreamElements-Dashboard-Overview | Stream group has Alerts, Analytics, Schedule; no stream info editor | missing | S-IA-STREAM |
| Edit stream info per platform with an All platforms option | Mix It Up, Streamer.bot | https://mixitup.bot/docs/getting-started | none in census | missing | S-IA-PLATFORM |
| Action disabled with the reason when a platform lacks it | Aitum Nexus | https://app.aitum.help/getting-started | none in census | missing | S-IA-PLATFORM |
| Activity feed from every platform in one place | StreamElements | https://support.streamelements.com/hc/en-us/articles/10474542090770-Activity-Feed-Overview | none in census | missing | S-IA-PLATFORM |
| Sidebar group per platform | Streamer.bot | https://github.com/Streamerbot/docs/blob/main/streamerbot/2.guide/01.core/assets/view-actions.png | - | not-doing | One uniform dashboard for all platforms; no per-platform page tree |
| Reward pages per platform | Mix It Up, Lumia Stream | https://github.com/MixItUpBot/Desktop/blob/c5a3497/MixItUp.WPF/MainWindow.xaml.cs | - | not-doing | Channel Points covers rewards; platform limits show as disabled actions |
| Global platform switch in the avatar menu | Streamlabs | https://support.streamlabs.com/hc/en-us/articles/50635570817435 | - | not-doing | Platform chips filter each view; one global switch hides other platforms |
| Live data without a refresh | YouTube Live Control Room | https://support.google.com/youtube/answer/2853833 | 22 of 70 app controllers listen for live changes (ConfigChangeRefresh.kt:21-33) | partial | S-IA-LIVE-EVERYWHERE |
| Attention inbox on the home page | Moobot, Twitch Stream Manager | https://moo.bot/docs/adjusting-your-moobot-dashboard | Dashboard page exists, read-only; no inbox | partial | S-IA-ROLE-HOMES |
| Stream health panel | Twitch Stream Manager, YouTube Live Control Room | https://support.google.com/youtube/answer/2853833 | none in census | missing | S-IA-ROLE-HOMES |
| Activity feed with filters | Twitch Stream Manager | https://help.twitch.tv/s/article/stream-manager?language=en_US | none in census | missing | S-IA-ROLE-HOMES |
| Start polls and predictions from presets | Twitch Stream Manager | https://help.twitch.tv/s/article/channel-points-predictions?language=en_US | none in census | missing | S-IA-STREAM |
| Raid, ad, marker and clip actions on one Interactions tab | Twitch Stream Manager | https://help.twitch.tv/s/article/stream-manager?language=en_US | none in census | missing | S-IA-STREAM |
| Schedule inside the stream page | YouTube Live Control Room | https://support.google.com/youtube/answer/2907883 | Schedule is its own page today | partial | S-IA-STREAM |
| Scene and source control on the live page | Streamer.bot | https://github.com/Streamerbot/docs/blob/main/streamerbot/2.guide/01.core/assets/view-actions.png | OBS page exists; the connection form on it moves to Integrations | partial | S-IA-STREAM |
| VTube Studio hotkeys next to the other live controls | Aitum Nexus | https://app.aitum.help/ | VTube Studio page exists in Connect; target moves it to Live | partial | S-IA-STREAM |
| Analytics tabs for stream, chat and growth | Twitch Creator Dashboard | https://help.twitch.tv/s/article/creator-dashboard?language=en_US | Analytics page exists in Stream; target is a tabbed page in Live | partial | S-IA-STREAM |
| Multi-platform chat with one tab per account | Streamer.bot, Lumia Stream | https://github.com/Streamerbot/docs/blob/main/streamerbot/2.guide/04.extra-features/1.chat.md | Chat and Multi-Chat are 2 pages; target is 1 page with All my channels | partial | S-IA-NAV |

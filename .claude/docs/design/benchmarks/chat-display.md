# Benchmark: chat display options and reading accessibility

Method, 2026-10-12. Twitch's help page returned only a "Loading" shell to a plain fetch, so it was read in a real browser (Chrome DevTools MCP, page text). Chatterino's wiki lists no settings, so its rows come from the settings source file `Settings.hpp`. FrankerFaceZ's home page names only broad areas, so its rows come from `src/modules/chat/index.js`. 7TV's site lists emote features only and no chat display settings (fetched, nothing to source); it is not used for rows. The NoMercy docs rows come from the files in `C:/Projects/NoMercy/docs/nomercy-docs/src` read this run. Grimoira `doc` found nothing for these terms, and its `workspace_search` was unavailable ("set CLAUDE_PROJECT_DIR"), so the code was found with a scoped grep.

Ours = `app/composeApp/src/commonMain/kotlin/bot/nomnomz/dashboard/feature/chat/` read this run. Today no chat display option is user-settable: a grep of the app for text scale, font scale, reduce motion, dyslexia and high contrast found only `FallbackFonts.kt`. Every "have" below therefore means "rendered, fixed, no user control", and the control is the slice.

## Products

- Twitch chat settings (Chat Basics) https://help.twitch.tv/s/article/chat-basics
- Chatterino 2 settings source https://raw.githubusercontent.com/Chatterino/chatterino2/master/src/singletons/Settings.hpp
- FrankerFaceZ chat settings source https://raw.githubusercontent.com/FrankerFaceZ/FrankerFaceZ/master/src/modules/chat/index.js
- NoMercy docs reading menu (A11yMenu) https://docs.nomercy.tv

## Features

| Feature | Seen in | Source | Ours | Status | Slice |
| --- | --- | --- | --- | --- | --- |
| Show or hide timestamps | Twitch "Show Timestamp"; Chatterino `showTimestamps` | https://help.twitch.tv/s/article/chat-basics | fixed HH:mm per line `ChatScreen.kt:392-393`, no toggle | partial | S-CHAT-DISPLAY-OPTIONS |
| Timestamp format (12h, 24h, seconds) | Chatterino `timestampFormat`; FFZ `chat.timestamp-format` | https://raw.githubusercontent.com/Chatterino/chatterino2/master/src/singletons/Settings.hpp | local 24h only `ChatScreen.kt:1244` | partial | S-CHAT-DISPLAY-OPTIONS |
| Timestamp size | FFZ `chat.timestamp-size` | https://raw.githubusercontent.com/FrankerFaceZ/FrankerFaceZ/master/src/modules/chat/index.js | none | not-doing | Timestamp size follows the one font size; a second size control adds clutter for no reading gain |
| Show or hide badges | Twitch "Customizable Badge"; Chatterino `showBadges*` | https://raw.githubusercontent.com/Chatterino/chatterino2/master/src/singletons/Settings.hpp | badge strip 18 dp `ChatScreen.kt:456-457`, no toggle | partial | S-CHAT-DISPLAY-OPTIONS |
| Hide badges by kind (sub, vanity, third-party) | Chatterino `showBadgesSubscription`, `showBadgesVanity`, `showBadgesFfz`, `showBadgesSevenTV` | https://raw.githubusercontent.com/Chatterino/chatterino2/master/src/singletons/Settings.hpp | none | missing | S-CHAT-DISPLAY-OPTIONS |
| Show or hide avatars | Chatterino and FFZ do not; NoMercy mockup need | https://raw.githubusercontent.com/FrankerFaceZ/FrankerFaceZ/master/src/modules/chat/index.js | avatar before badges `ChatScreen.kt:448-449`, no toggle | partial | S-CHAT-DISPLAY-OPTIONS |
| Platform icon per line (multi-platform) | none of the four; ours only | https://help.twitch.tv/s/article/chat-basics | not in the feed line | missing | S-CHAT-DISPLAY-OPTIONS |
| Name colour: on / adjusted / off | Chatterino `colorUsernames`, `colorizeNicknames` | https://raw.githubusercontent.com/Chatterino/chatterino2/master/src/singletons/Settings.hpp | name colour shown as sent, no control | partial | S-CHAT-DISPLAY-OPTIONS |
| Readable colours (adjust hard-to-read names) | Twitch "Readable Colors" | https://help.twitch.tv/s/article/chat-basics | none | missing | S-CHAT-DISPLAY-OPTIONS |
| Minimum contrast for name colours | FFZ `chat.adjustment-mode`, `chat.adjustment-contrast` | https://raw.githubusercontent.com/FrankerFaceZ/FrankerFaceZ/master/src/modules/chat/index.js | none | missing | S-CHAT-DISPLAY-OPTIONS |
| Bold names | Chatterino `boldUsernames` | https://raw.githubusercontent.com/Chatterino/chatterino2/master/src/singletons/Settings.hpp | none | not-doing | Bold name is a fixed design-system weight; the owner asked for fonts and size, and weight adds no access gain |
| Font family | Chatterino `chatFontFamily`; FFZ `chat.font-family`; NoMercy docs font menu | https://docs.nomercy.tv | app theme font only, no choice | missing | S-CHAT-DISPLAY-OPTIONS |
| Dyslexia-friendly font (Hyperlegible, Lexend) | NoMercy docs `FONTS` list | https://docs.nomercy.tv | none (`FallbackFonts.kt` is glyph fallback only) | missing | S-CHAT-DISPLAY-OPTIONS |
| Monospace font | NoMercy docs `FONTS` list | https://docs.nomercy.tv | none | missing | S-CHAT-DISPLAY-OPTIONS |
| Font size, several steps | Twitch "Font Size" (four sizes); Chatterino `chatFontSize`; FFZ `chat.font-size`; NoMercy docs 100/112/125/150 percent | https://help.twitch.tv/s/article/chat-basics | fixed sizes, no control | missing | S-CHAT-DISPLAY-OPTIONS |
| Whole row scales with font size (name, emote, badge) | Twitch "all other elements scale proportionally" | https://help.twitch.tv/s/article/chat-basics | badge 18 dp and emote 24 dp are fixed | missing | S-CHAT-DISPLAY-OPTIONS |
| Line and message spacing | Chatterino `separateMessages`; NoMercy docs "Wide" spacing | https://docs.nomercy.tv | fixed row padding | missing | S-CHAT-DISPLAY-OPTIONS |
| WCAG 1.4.12 text spacing preset | NoMercy docs `data-spacing='wide'` (letter 0.12em, word 0.16em, line 2) | https://docs.nomercy.tv | none | missing | S-CHAT-DISPLAY-OPTIONS |
| Alternating row backgrounds | Chatterino `alternateMessages` | https://raw.githubusercontent.com/Chatterino/chatterino2/master/src/singletons/Settings.hpp | none | missing | S-CHAT-DISPLAY-OPTIONS |
| Emote size | Chatterino `emoteScale`; FFZ emote scale | https://raw.githubusercontent.com/Chatterino/chatterino2/master/src/singletons/Settings.hpp | `emoteSize: Dp = 24.dp` parameter `ChatMessageFragments.kt:48`, not user-set | partial | S-CHAT-DISPLAY-OPTIONS |
| Animated emotes on or off | Twitch "Animated Emotes"; Chatterino `animateEmotes`; FFZ `chat.emotes.animated` | https://help.twitch.tv/s/article/chat-basics | `AnimatedNetworkImage` always animates `ChatMessageFragments.kt:64` | partial | S-CHAT-DISPLAY-OPTIONS |
| Follow the system reduced-motion setting | NoMercy docs `prefers-reduced-motion` rule | https://docs.nomercy.tv | not checked in the app | missing | S-CHAT-DISPLAY-OPTIONS |
| Deleted messages: hide, strike, show | Chatterino `hideModerated`; FFZ `chat.filtering.remove-deleted`, `chat.filtering.display-deleted` | https://raw.githubusercontent.com/FrankerFaceZ/FrankerFaceZ/master/src/modules/chat/index.js | deleted mark on the line `ChatController.kt:279`, one fixed look | partial | S-CHAT-DISPLAY-OPTIONS |
| Click to reveal a deleted message | FFZ `chat.filtering.click-to-reveal` | https://raw.githubusercontent.com/FrankerFaceZ/FrankerFaceZ/master/src/modules/chat/index.js | none | missing | S-CHAT-DISPLAY-OPTIONS |
| Highlight messages that mention you | FFZ `chat.filtering.highlight-mentions` | https://raw.githubusercontent.com/FrankerFaceZ/FrankerFaceZ/master/src/modules/chat/index.js | mentions coloured in text `ChatMessageFragments.kt:86`, no row highlight | partial | S-CHAT-DISPLAY-OPTIONS |
| Custom highlight colour | FFZ `chat.filtering.mention-color` | https://raw.githubusercontent.com/FrankerFaceZ/FrankerFaceZ/master/src/modules/chat/index.js | none | not-doing | The highlight uses the design-system accent tint; a free colour picker can break contrast, so we offer on/off and strength only |
| High contrast mode | NoMercy docs syntax theme "High contrast" | https://docs.nomercy.tv | none | missing | S-CHAT-DISPLAY-OPTIONS |
| Narrow reading width (about 65 characters) | NoMercy docs `data-reading-width='narrow'` | https://docs.nomercy.tv | feed fills the panel | missing | S-CHAT-DISPLAY-OPTIONS |
| Read aloud with speed control | NoMercy docs `ReadAloud` (0.75, 1, 1.25, 1.5) | https://docs.nomercy.tv | none | missing | S-CHAT-DISPLAY-OPTIONS |
| Settings kept per user, applied before first paint | NoMercy docs: one attribute on html plus one localStorage key each | https://docs.nomercy.tv | no per-user chat settings | missing | S-CHAT-DISPLAY-OPTIONS |
| Settings reachable from the page, not only a menu | NoMercy docs A11yMenu popover; Twitch chat settings gear | https://help.twitch.tv/s/article/chat-basics | none | missing | S-CHAT-DISPLAY-OPTIONS |
| Hide commands from the feed | Chatterino `hideModerationActions` style filters; ours | https://raw.githubusercontent.com/Chatterino/chatterino2/master/src/singletons/Settings.hpp | "Hide commands" button in the mockup header | partial | S-CHAT-DISPLAY-OPTIONS |
| Rich link previews in chat | FFZ `chat.rich.enabled` | https://raw.githubusercontent.com/FrankerFaceZ/FrankerFaceZ/master/src/modules/chat/index.js | none | not-doing | Link previews fetch third-party pages from the viewer's browser; privacy cost outweighs the gain for a moderation feed |
| Light, dark and system theme | NoMercy docs `THEMES` | https://docs.nomercy.tv | app theme (design system tokens) | have | |

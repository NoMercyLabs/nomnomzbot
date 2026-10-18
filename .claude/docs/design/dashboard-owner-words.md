# Dashboard: Stoney's words, verbatim

Stoney's own words about the Vue dashboard, kept as he said them. Nothing here is derived.
Derived text in other files starts "Arc's reading:" or "Proposal, not settled:".

## 2026-10-14

"no regular, affilate or partnered status, this is needed to gate the owned emotes because a channel that is not affilated or partnered can not have emotes."

"i want every tts example to introduce itself in the language it's made for, and multi langual should speak a hand full of translations of the same sentence, they should be short but powerful."

"the emote pannel is still not grouped by channel as i asked for."

"i want to sort people by last seen and alphabetical, and lazy load all users known to my channel, so i never have to search for someone already known to me."

"and anout the platform standing i want affiliate and partnered to also be addressed acording to youtube verified, music channel and for twitter i want the badge status and so on for this."

"subscribers and regulars are empty while i have a ton! vip toggle shows 0 when the list is not empty."

"the now playing needs to be converted into a proper player pannel that transforms into spotify or youtube player depending on what we control currently, no major layout shifts on this please and have some class, you know my streamdeck plugin."

"i want spotify to be always available if its connected and youtuve has not taken over on a playback."

"the hello chat, this is how i sound is good, but it needs to have english first and then native second if applicable, but make it introduce itself by its name."

"also... where did you leave the frankerfacez and the other tabs?"

"instead of  removing the tabs, make them grayed out and locked and keep the remaining tabs enabled, but pleae add the channel grouping and use the channel avatar instead of its first emote" (with a screenshot of the emote picker search results, ungrouped)

"this is twitch, i want this but stay with our design" (with a screenshot of the Twitch emote picker: Favorites and Channel Emotes sections with a source label and a collapse arrow, locked emotes with a lock badge, favorites with a star badge, a rail of channel avatars on the right, the search at the bottom with a skin tone button and an eye button; a second screenshot shows the skin tone button open as a column of tones)

"constrained layouts need to be considered for" and "its the same for home and other 3 column pages" (Music and Home at about 1160 px, three panels squeezed into strips)

"question: can i see if someone is subscribed from a gift sub and who gifted it in the profile?"

"these are users, so there needs to be a profile toggle, and its an event so it needs a replay for obs related actions and events" (Home, Last stream rows)

"someone needs to be able to choose a skin tone once and the picker needs to swap them to that skin tone for all emojis, this is very tedious"

"Kana was in chat But has not spoken, is marked as last week... Is this correct? And there are others that don't look right, aaoa has been in my chat this month. The full profile view page is the old broken version"

"Account age and more more more moooreeee"

"And still not every triggerable menu allows the back button to close it without redirect"

## Earlier in the Vue build (standing)

"Can we capture all menu's as navigation and closes as history back for mobile for the native feeling?"

"A scroll within a scroll is bad for mobile and expanding all full is not an option, we need something better for those pages"

"Remove the input from emojis on mobile and remove all double side margins for a better "mobile app" feel, something closer to the real twitch mobile app"

"reduced motion should pick the static emotes"

"make variant emotes yellow and give the user the choice of which and make long press clear the choice and the user pick a new version"

"make sure you group by channel and allow for favorites and prioritize own emotes -> channel emotes -> rest on alphabetical channel name -> regular emojis"

"i also like to see the user's channel emotes in their profile as an asset inspection section or something in that idea"

"emote search input at the bottom, section buttons as icon only on the side of the emote scroll list. still the same grouping as i described before"

"no speak as bot option, can be abused to get mine banned"

"remove all line wrapping needs by making it wider. twitch subscribers and youtube subscribers don't mean the same thing so if subscriber is used it must be accompanied by a platform indication, ... tier 1000 also needs to be converted"

"if i don't have permission to change something then don't let me change it in the ui when saving that change can't work"

"audio and video are reserved for orchestrated events made by us or the bot user, chat must NEVER be able to inject unwanted code anywhere. that is why the html is heavily sanitized"

"someone without a fixed chat collor should get one asigned that is least used by the chatters in the last 2 weeks of streaming, once they set one they take president"

"every action must be reachable form any place if the user desires this, that is why we have an SDK, it must all be exposed everywhere a user can create actions and reactions"

2026-10-17: "menu's that open and close MUST contain an indication like a chevron"

2026-10-18: "you can't offer what's not there... and every tab and pannel and button and page needs to have proper permission checks before being shown"
2026-10-18: "we need one place for the mandatory overlays and not spread them all over, lets keep the user choice overlays at the overlay browser section and have the mandatory one on an easy to reach place that can be shown in an onboarding flow guide"
2026-10-18: "i like separate role permissions between youtube and spotify, spotify is safe by default, youtube requires us to get it right"
2026-10-18: "the breakpoints need to be better defined based on the other component's needs"
2026-10-18: "and i want extra pannels to be always toggle-able"
2026-10-18: "all overlay pannels need a close button"
2026-10-18: "no autocomplete search? we know all the options up front"
2026-10-18: "90 days is crooked"
2026-10-18: "i want more details and accuracy" (OBS stats against OBS's own status bar)
2026-10-18: "we probably need a add to youtube playlist and add to spotify playlist action" — decided: one "Add to playlist" step with a Spotify/YouTube service choice; the playlist picker lists that service's playlists.
2026-10-18: "make a linter that does the work! STOP DOING THINGS BY HAND!"
2026-10-18: "i don't want any "string" value returned anywhere, i want it to be 100% strict typed"
2026-10-18: "when mapping i also want you to type the return as const instead of the argument"
2026-10-18: "callbacks that return a boolean or a single variable should not linewrap, same rule as a direct return (scripts, () => false)"
2026-10-18: "HOLY MOLY THAT OBS PAGE!!! can you slap more things on in pannels that large?! how is that managible by the streamer while live?"

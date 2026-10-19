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
2026-10-18: "let's also not set a return type when we can as const here too" (on emojiSections in emotePicker.ts: no return type annotation; the returned value carries `as const satisfies T`)
2026-10-19: "do NOT count banned people" (stream recap)
2026-10-18: "the raid command JUST needs to collect everything that happened between the start of the stream and the when i use raid"; then: "and i like to get mine if today, i want to collect them as achievements over time"
2026-10-18: "we need a separate widget for the music, and one for the tts and other audio comming from the bot. music must be controllable individually and be allowed to run on the users machine with a different volume than chat hears."
2026-10-18: "i need youtube and spotify to get equal treatment, controls via streamdeck/ui and the now playing widgets should ALL work with both. the queue MUST be able to switch effortlessly and obey the fair queue system."
2026-10-18: "i paused spotify and it started playing through my old tts widget instead of the new audio source. it then started spotify again while it was still active. i need a better and more reliable solution and more deterninistic behavior from this"
2026-10-18: "why is his background color STILL not his 7tv special color like i asked 5 times now?" (chat overlay name bar must wear the viewer's 7TV paint)
2026-10-18: "bit messages should say in chat with the users message", then: "bot reply quote and use the users own voice for THEIR message"
2026-10-18: "ok, no more of me asking to make something as const satisfies... apply to all of the code with the linter"
2026-10-18: a viewer with no saved TTS voice keeps a random first voice, saved (owner choice); "english or muli language and prefer a pronoun gender voice"
2026-10-18: "HOLY MOLY THAT OBS PAGE!!! can you slap more things on in pannels that large?! how is that managible by the streamer while live?"
2026-10-19: "client edge is supposed to be a 1 to 1 replica of the server edge tts api, not some integrated crappy tts! i told you to completely remove it 1 month ago"
2026-10-19: on where client edge runs (Edge TTS refuses a plain browser page): "desktop local and server relay on paid tier?"
2026-10-19 (said in Dutch, faithful English): "I want you to make sure I do not have to repeat myself again with all the nitpicks I have said about all the other pages. I want you to make sure that all settings work, that everything is configurable, that all defaults are set, and that I wake up with everything done, pushed, tested. Can I count on that?"
2026-10-19: "i onboard as a streamer and a mod of 32 channels and i can't mod for any other than my own? that is not helpfull what so ever dude"
2026-10-19 (OBS page in iFiLLz's channel, uptime 1:15:23): "he's only been live for 15 minutes... and i probably should not be able to change obs settings as a normal mod, this should probably be channel editor and up, with an IAM to permit manually. and speaking of, there needs to be a role for bot:manager:x or something that specifically allows making changes to scripts, overlays and all the "bot shenanigans" for stream."
2026-10-19 (asked whether OBS control stays Editor-only or can be lowered): "obs is a new thing, editor with lowering options to mods and individuals "
2026-10-19 (qtkitte live, YouTube requests skipped): "so let me be clear one more time about the logic.
spotify plays it's own auto queue and does its own magic, the fair queue system will ALWAYS dispatch ONE song into the manual spotify queue if there is one requested, so spotify has its auto queue AND 1 song dispatched automatically. and then handle the youtube transition on top of that by pausing spotify playback right as the next song plays, then make youtube play, when youtube is end - 3s we resume spotify again
it is really important to keep spotify doing what it does best while we correctly handle youtube's takeover and its ending of playback"
2026-10-19 (follow-up): "so if the queue says: 1 yt:abcd, 2: sp:abcd then we queue sp:abcd to spotify even if yt:abcd needs to play first, it ensures that spotify plays the right song when we resume it's playback after youtube has finished, the same goes for when there are 3 youtube songs before the next spotify song"
2026-10-19 (on "only YouTube, no requests: the player stays silent"): "I don't know what stream elements does for youtube but I believe they do have something for keeping youtube playing"
2026-10-19 (Stoney asked bamo16 in chat "what to do when youtube songs are empty in the queue? what do you normally play?"; bamo16's answer, verbatim): "StreamElements had something called a "backup playlist". You add a Playlist and it selects songs from it, but it does a really bad job of keeping it random. I use the same sort of idea, but I pulled from many of my playlists I track in a local database. Basically I select from the set of unique video IDs across all the playlists I want to include with a few exceptions (no super long songs, no songs that are already in the queue, maybe some other rules). I think I perform a check each time a new song starts playing to see if there is something queued up next and that is when I would add if it's empty."
2026-10-19 (Stoney, after bamo16's answer): "we probably need something that can provide us tracks to the taste of the streamer but not sure how we can do this with a similar quality as youtube"
2026-10-19 (Event responses, Incoming raid showed its pipeline as a raw id with "This target is not in this channel."): "i switched back to my own channel"
2026-10-19 (music page header: the "Request rules" button with a focus ring after its panel closed, a one-item "..." menu, and the requester picker beside "Remove all"): "double borders, extra useless dropdowns... also a releated pattern that keeps comming back"
2026-10-19 (after asking earlier "how are widgets still not subscribing to events they want?"): "ok, so let me ask one last time: do every widget, browser source, sdk, webhook and other live event now subscribe to only what it wants?"
2026-10-20 (on the hub method names JoinChannelClasses and DeclareWidgetEvents): "why are you not using logical names like Subscribe or something in that area?"
2026-10-20 (People, Twitch subscribers: rows read "Gifted by iFiLLz · Last seen ..." while the profile panel shows no gifter; the profile badge row shows the raw key "profile.roles.artist"): "why is this listed in the list items but not in the main pannel? this makes no sense, remove it from the list name and put it where it belongs"
2026-10-20 (chat page, the "Mod tools" button and panel with chat modes, raids and cleanup): "change this to chat settings, these are not mod tools at all"
2026-10-20 (chat page, Chat identity panel listing only the three badges worn now, with "Twitch has no way to choose which ones show."): "where are all my badges that i can set on my profile?"
2026-10-20 (chat page): "where is the toggle for changing html rendering to plain text?"
2026-10-20: "the obs page is a mess that is STILL not easy to use, and the settings are hidden in a hidden dropdown all the way down, this page needs a proper reorganization"
2026-10-20 (Event responses, "Ad break starts": four platform default lines shown as input-like rows above an empty Reply box, and "Send a test" with "Not available yet"): "i don't know what is going on here, but it makes no sense at all"
2026-10-20 (Overlays page: the audio levels card clipped above the overlay list, settings rows with key and type names, a raw JSON field, Health, Preview and Reacts cards): "also super messy"
2026-10-20 (Scripts page: Powers, a read-only Code block, "No settings available yet.", a sample list of every event, and an empty "This session" column): "messy"
2026-10-20 (Games page: "Start a game" rows reading "1 to 0 players", and four mini-games): "ALL games don't work and make no sense so remove them and mark as comming soon", then: "or at least remove the seeded ones"
2026-10-20 (Quotes list: the number, the text block and the actions sit on different lines): "list items are missing visual balance"
2026-10-20: "the music settings menu needs an overhaul and be better oranized too"
2026-10-20 (account menu: "Readability" with a focus ring on open, and every item underlined like a link): "need i say more about this?"
2026-10-20 (TTS Pronunciation: every row repeats the column labels, the add row's columns do not line up with the list, and the Match picker shows Dutch on an English page): "bad alignment"
2026-10-20 (TTS voice picker, "Play sample" per voice): "all voices need to be pre-generated to speed it up"
2026-10-20 (media share queue: rows with no controls): "still no proper item controls and the whole page is just bad"
2026-10-20: "discord page is useless"
2026-10-20 (Settings › Readability): "readability is not using the right selection component for each option"
2026-10-20 (Settings, "Your channel's own bot" card with a "Sign in with the browser — Open Twitch" row): "for what would i click the login button there?"
2026-10-20 (Settings › Features): "i think we have WAY more features that are configurable"
2026-10-20 (Settings › Integrations: Spotify connected, and below it "Your own Spotify app" with Client ID and Client secret inputs): "why is spotify showing an input when i got onboarded after clicking connect?"
2026-10-20: "why is discord duplicated on main and settings?"
2026-10-20: "supporter sources is a mess"
2026-10-20 (Settings › API keys, the "Events a key can listen to" card): "serves no purpose, add it to an info button to each of the token items"
2026-10-20: "permissions is 10 miles long"
2026-10-20 (Settings › Event journal; the export took 102.7 s on the server with no progress shown): "the event journal is not providing anything useful for a user and the download button is broken"
2026-10-20 (Settings › Resource limits: "Sound clip storage 3,394,022 of 2,147,483,648", "Channel asset storage 10,735,410 of 2,147,483,648"): "you think i have that much storage? these need to be properly set based on realistic values for the 3 tiers", then: "and reorganized"
2026-10-20 (Settings › Billing › Plans: each plan lists raw keys and byte counts such as "sound_clip_storage_bytes 104857600"): "geez..."
2026-10-20 (Settings › Your data, the personal data export): "how do i only have 19k lines of json for my data? should that not be 10 times more?"
2026-10-20: "event sub health is a total mess and way too verbose"
2026-10-20: "channels and users have no info when i click"
2026-10-20: "invite codes can't be deleted and do they even work?"
2026-10-20: "i want a FULL spam detection event log to do spot validations to see how things are classified and to know when it's safe to turn on automated items"
2026-10-20: "and i don't know user ids so how am i supposed to ban someone?"
2026-10-20 (Admin › Data requests: a ledger card of six counts for one request, the export row reading "Requested by self_service" and "2738 rows changed"): "sigh..."
2026-10-20 (Admin › System: health rows named api, postgresql, pending-migrations, eventsub-handover, shutdown with "0 ms" each; Version 0.1.0.0; Memory 3758 MB; Active channels 0 of 29): "uhhhh"
2026-10-20 (Admin › Providers: every provider shows open Client ID and New client secret boxes under its stored values): "they are stored so collapse them with an edit button", then: "with modal"
2026-10-20 (Admin › Platform defaults, https://vue.nomnomz.bot/app/admin/platform-defaults): "dear god..."
2026-10-20 (the same page while "Viewing as a viewer": three sections read "No ... are listed", the voice section "could not load", and the sidebar footer still says Moderator): "guess im not viewing as viewer since im not redirected..."
2026-10-20 (in "view as viewer" on Settings › Your data, the sidebar shows only Readability and Your data): "how am i getting back to the main pages"

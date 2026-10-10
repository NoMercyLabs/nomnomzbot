# Moderation and safety: research paper and one plan (2026-10-10)

Owner request, verbatim: "i like you to do deep research on moderation tooling that help streamers, moderators and viewers to keep the platform safe and fun, and cover all shortcommings of twith lacking proper oversight on its users. i want this to be a dedicated research paper in plain english terms that have all the other work folded in, and every already settled on things reevaluated and pulled out of the other docs and reorganized into one plan. leave no stone unturned"

How to read this paper:

- Short sentences. One idea per line.
- "PLAN" means `.claude/docs/design/SHORTCOMINGS-EXECUTION-PLAN.md`.
- "R: name" means a research note from this session (session scratchpad, `research/name.md`). Each note lists its own URLs. Section 9 lists every URL.
- "via R: internal-settled" means a doc line that a research worker read and quoted. This writer did not open that doc.
- A source marked "open" is listed in the coverage ledger at the end of the paper.

Words used one way only:

| Word | Meaning here |
|---|---|
| Ban | The user is removed from one channel's chat, with no end time. |
| Timeout | A ban with an end time. |
| Restrict | Twitch suspicious-user status RESTRICTED. Only mods and the streamer see the user's messages. |
| Monitor | Twitch status ACTIVE_MONITORING. Messages show, and mods see a flag. |
| Mass ban | One moderator asks for a ban of one account in many channels. |
| Request | One mass-ban ask to one channel. It waits for a decision in that channel. |
| Entrust | A streamer gives one named moderator the mass-ban power for their own channel. |
| Decision card | The card a channel's mods see for one request: who asked, why, the last messages, and "Ban here too". |
| Queue | A chat message that waits for a mod (Twitch AutoMod, our chat filters). |
| Lockdown | The room-wide tightening of chat settings during an attack. |

---

## 1. Summary for the owner (one page)

### 1.1 The most important findings

1. **A ban across channels must be a request, not a command.** A mass ban was misused for self-gain on 2026-10-10 (PLAN:103-104). Kick tells streamers that "Mass-banning followers, attacking other channels, or organising counter-action puts your account at risk" (R: third-party-2). Bluesky saw shared block lists become vendetta tools (R: bluesky-lists).
2. **Twitch gives us the "silence first" tool.** A bot can set a user to Restrict or Monitor with Helix "Add Suspicious Status to Chat User" (scope `moderator:manage:suspicious_users`) (R: twitch-native). This closes guard (h) (PLAN:123-125). The Helix reference confirms it: an app token, or a user token with `moderator:manage:suspicious_users`; `moderator_id` is required; only Add and Remove exist, and there is no Get. Our code already calls it (`ITwitchModerationApi.cs:316` AddSuspiciousStatusAsync, `:340` Remove; `TwitchModerationApi.cs:937`). Only the manual route uses it (`ModerationService.cs:1357`). No automatic caller exists. The troll gets no airtime while each channel decides.
3. **Twitch has no list of restricted users.** No endpoint returns the suspicious-user list. We must keep our own record from the EventSub `channel.suspicious_user.*` events (R: twitch-native).
4. **Twitch shared bans have no API.** Shared Ban Info lives only in the Twitch UI. It allows at most 50 partner channels, needs consent per pair, and shares every ban, joke bans included (R: twitch-native-help). Our bot cannot read it or add to it.
5. **The streamer needs to see why.** thecodedrift: "What's missing is me knowing why someone was banned from stoney's channel, seeing the last N messages, and with one click going "yeah, fuck that guy"" (PLAN:160-162). No tool we read does this across channels (R: third-party, third-party-3, ffz-7tv-mod).
6. **Twitch hides key settings from bots.** No API for: verified chat (phone or email), Block Hyperlinks, permitted terms, private blocked terms, ban-evasion level, the Shield Mode bundle, mod comments, the Lead Moderator role (R: twitch-native, twitch-shortcomings-3). Our dashboard must say "set this on Twitch" with a link. It must never pretend to control these.
7. **Hate raids are identity attacks.** In a 2021 sample of 9,664 channels, 98% of hate-raid messages were identity attacks (R: twitch-shortcomings, arXiv 2301.03946). Incoming-raid settings are owner-only and static. The raid event names only the raiding channel and a viewer count, not the raiders (R: twitch-native-help-2; PLAN:762-768).
8. **Scam links and impersonation are old and still open.** Block Hyperlinks is all or nothing. Mods and VIPs are exempt, so a stolen VIP account posts links freely (R: twitch-shortcomings-3). No Twitch feature compares a new name with the streamer, the mods or "Twitch Support" (R: twitch-shortcomings-3).
9. **Mature tools warn first and can undo.** Nightbot and Moobot warn on a first offence (R: third-party). Fossabot has a nuke rollback (R: third-party). Discord ships a "Report Issues" button on its machine spam decisions (R: third-party-2). Every automatic action in our bot needs a one-click "this was wrong".
10. **Our own docs disagree.** Fifteen contradictions (K1-K15) sit between the 2026-06-16 design, the user-flow spec, the spam spec and the 2026-10-10 decisions (via R: internal-settled-2). Fifteen more (C1-C15) came from a second read (via R: internal-settled). Section 6 settles each one.
11. The bot has one output filter, the TTS censor with 24 mild words. Alerts, command echoes, reward input, quotes, media titles and Discord text have none (S-MOD-OUTBOUND-FILTER).
12. A message the chat filter deletes still runs the command, and the bot posts the same words. The overlay also keeps the line (S-MOD-ECHO-AFTER-FILTER).
13. Lockdown stops Twitch chat only. During a hate raid the bot keeps showing hateful names and speaking them (S-MOD-LOCKDOWN-OUTPUT).
14. One stolen session, API key or impersonated session can start a mass ban of up to 1000 accounts in all channels, with no second check and no ban budget (S-MOD-BAN-RATE-STEPUP).
15. Two guards are set by the caller, the nuke confirmation and the "attacked" list, and an act-as token has no limits, so a thief controls them (S-MOD-NUKE-CAPS-AUDIT).
16. The streamer often gets no notice, and an admin cannot revoke all sessions of one user, so a noticed theft keeps working for up to 1 hour (S-MOD-REVOKE-SESSIONS).
17. Lockdown works on Twitch only: Kick and YouTube have no chat-mode API and X Live has no chat-moderation API, so those channels need an honest "not available" state and a bot-side filter (S-MOD-LOCKDOWN-MULTI).
18. Five EventSub moderation topics reach the journal but no screen, so a chat-mode or AutoMod change made outside our bot stays invisible to the mod (S-MOD-EVENTSUB-CONSUMERS).
19. A YouTube ban or unban costs 200 of 10,000 daily units, and bans made outside our bot are invisible and cannot be lifted, because the code has no quota budget and no ban or tombstone translation (S-MOD-YOUTUBE-GAPS).
20. Viewer text from channel point redemptions reaches the pipeline with no check, and poll and prediction titles go to Twitch unchecked, so a slur or a dox can reach TTS or an overlay (S-MOD-TEXT-GATE).
21. Twitch's API cannot create a Private blocked term, so a streamer's address stored through our API would show to mods, and the bot has no scan for a leaked address (S-MOD-DOX-TERMS).
22. No source gives a burnout rate for volunteer Twitch mods, but interviews show mods work alone and without cover, and the bot has no coverage or cover-ping feature (S-MOD-MOD-LOAD).

### 1.2 The plan on one screen

| Order | Goal | Main slices |
|---|---|---|
| 1 | Prevent misuse of our own tools (the incident) | S-MOD-TOOLS-SPEC, S-MASSBAN-ABUSE-GUARDS, S-MOD-MASSBAN-FLOOR, S-MASSBAN-ENTRUST, S-MASSBAN-OPTIN-TOGGLES, S-MOD-DECISION-CARD, S-MASSBAN-RETRY |
| 2 | Oversight and audit | S-MASSBAN-HISTORY, S-MASSBAN-REQUESTER-RECORD, S-MOD-WRONG-ACTION-REPORT, S-SAFETY-MONITOR |
| 3 | Protect the stream live | S-MOD-LOCKDOWN, S-SPAM-LOCKDOWN-WIRE, S-RAID-VET, S-SPAM-CONTEXT-TRUST, S-UF-M7a |
| 4 | Help moderators decide | S-SUSPICIOUS-USERS, S-UF-M1a, S-UF-M6a, S-MOD-EVIDENCE-PACK |
| 5 | Protect viewers | S-FILTER-LINKPOLICY (wider), S-SAFETY-FAKE-NAME, S-SAFETY-DOXX, S-MOD-FILTER-TEST |
| 6 | One user hub, live | S-USER-HUB, S-LIVE-EVERYWHERE, S-MOD-BANLIST, S-MOD-NUKE-SCREEN, S-MOD-TWITCH-ONLY-SETTINGS |
| 7 | Our own output surfaces (alerts, TTS, overlay, echoes) | see section 11.3 |
| 8 | Stolen accounts and insider abuse | see section 12.4 |
| 9 | Multi-platform moderation (Kick, YouTube, X Live, EventSub) | see section 13.4 |
| 10 | Other harms (IRL, self-harm, clips, rewards, minors, mod burnout) | see section 14.7 |

The rule above all others, from the owner: **"WE MUST ENSURE THAT USERS NEVER GET BANNED FOR THE WRONG REASON"** (PLAN:104).

---

## 2. The problem

### 2.1 Who faces what

| Person | What goes wrong | Source |
|---|---|---|
| Streamer | Hate raids, spam waves, follow bots and scam links. The streamer must act alone when no mod is online. | R: twitch-shortcomings, twitch-shortcomings-2 |
| Streamer with no mods | About 3.3% of Twitch traffic had no mods and no AutoMod in H2 2025 (96.7% had one or both). The split by channel size is not in the data. | R: twitch-shortcomings (H2 2025 report CSV) |
| Moderator of many channels | Bans the same troll by hand in each channel. Owner: "i am tired of playing whack-a-mole on 32 channels" (PLAN:162-163). | PLAN:162-164 |
| Moderator | Unpaid, often the only mod online late at night, and coordinates on Discord beside the tools. | R: twitch-shortcomings-2 (arXiv 2203.13292) |
| Viewer | Loses accounts to fake giveaways and fake "support" whispers. | R: twitch-shortcomings-3 |
| Viewer hit by mistake | A broad nuke pattern, a bad list or a name match can ban the wrong person. Sery_Bot says wrongly banned accounts "likely had compromised history". | R: third-party, third-party-2 |
| Banned viewer | Twitch allows one unban request per channel, and only if the channel turned the feature on. A denied request cannot be sent again. | R: twitch-native, twitch-native-help-2 |

### 2.2 The 2026-10-10 incident

- The run: a mass ban of turk_bert / not_turk_bert, done by hand through the API because no screen exists (PLAN:38).
- Fault 1: two held channels do not use the bot. `!allow massban` there is never heard (`MassBanChannelPlanner.cs:254`, cited at PLAN:40).
- Fault 2: the hold also fired in channels where the owner had already banned both accounts (PLAN:40-41).
- Fault 3: the dashboard has no client for the moderation endpoints (PLAN:41-44).
- The misuse. Owner, verbatim: "here is some feedback from one of the streamers caught tonight by that user, and we agree that this is a dangerous tool that can be abused for self-gain. WE MUST ENSURE THAT USERS NEVER GET BANNED FOR THE WRONG REASON" (PLAN:103-104).
- thecodedrift, verbatim: "Yep, and one bad node can ruin the trust layer" / "it's the auto-ban that causes issues. But flag+notify is usually just as effective" (PLAN:108-112).
- thecodedrift, verbatim: "if you centralize ban authority, you'll both be targeted and hated because you cannot trust every leaf in the node" / "Bluesky went through this with ban lists, where a few centralized "lists" suddenly became personal vendetta tools" (PLAN:152-155).
- Owner VETO, verbatim: "that is because i VETO'd the decition as system owner and i vouch to not abuse my powers, and stand by my community. mass bans are for accounts that only spread hate and insults or cause harm to the community" (PLAN:131-133).

### 2.3 What the owner wants, verbatim

- "dude... i want to ban someone off all channels i manage for  easily, i had tasked you with this feature" (PLAN:27).
- "mods with this power should be aware of the consequenses of abusing it" / "a dispute on someone's stream should never result in using that tool" / "so basically we need to join our heads together and decide which mods can be objective with this power" (PLAN:114-115).
- "a system that sends a message in chat that says: this user is marked. could work" (PLAN:115-116).
- "but i do want to have hate raids and follow bots and best viewer spam to be autoban" (PLAN:116-117).
- "i have a trust scoring system that looks at your ban status on all channels part of the system and based on that it either lets things slide or acts" (PLAN:117-118).
- "an established viewer mentioning "i am tired of those best viewers spam" should not trigger the ban because they say best viewers" (PLAN:118-119).
- "right, i think we SHOULD fill the list. just so twitch starts paying attention about the fact that we joined forces to do THEIR job" (PLAN:144-145).
- "streamers can "entrust" a mod with this power." (PLAN:145-146).
- "the powers in my hand are to make it auto cleanup permanently banned accounts" (PLAN:151).
- "that is my primary goal, close the gaps twitch leaves open." (PLAN:162).
- "EVERYTHING must update live and fast, that includes all other pages on the dashboard, no delayed states" (PLAN:92-93).
- Owner answers 2026-10-10: "1: leadmods and up." / "2: opt outs should be an option for the broadcaster to make, and mods can use their own judgement on what channels the mass ban should cover, so the toggles should persist across sessions and changes should persist at toggle time." (PLAN:86-88).
- 2026-10-05 (memory feedback-massban-bans-happen-live-only-holds.md, via R: internal-settled): "bans happen! only when the streamer is live there is a need to hold them back. the actively botted channel will not need the allow command. bans happen when streamers are offline."
- 2026-10-08 (memory goal-streamer-safety-2026-10-08.md, via R: internal-settled): "a streamer should never have to deal with hate raids, spam or other stuff that twitch and youtube come short when it comes to veing vigilant. it MUST be reliable, configurable and easy to reach and easy to understand. all monitoring must be flawless."

---

## 3. What Twitch gives today, and what it does not

### 3.1 What Twitch gives, and what a bot can reach

| Tool | What it does | Can a bot use it? | Source |
|---|---|---|---|
| AutoMod | Queues risky messages for mods. Levels 0-4. Level 1 by default if the channel has a mod, Level 2 if not. It never bans. | Yes: read and set levels, approve or deny queued messages, event `automod.message.hold`. | R: twitch-native, twitch-native-help |
| Blocked terms | Queues messages with a term. Wildcards. "Tens of thousands" of terms. | Public terms only. Private terms are visible to the owner only. | R: twitch-native, twitch-native-help |
| Permitted terms | Lets a term through AutoMod. No wildcards. | No API. | R: twitch-native-help |
| Shield Mode | One button for a bundle: chat modes, "No First-Time Chatters", verification, AutoMod level, ban by term, and a Banned Chatters tab. Ban terms reset each session. | On/off and status only. The bundle is set in the Twitch UI. | R: twitch-native, twitch-native-help |
| Restrict / Monitor | Hides or flags one user's messages. | Yes, set and remove. No list read. | R: twitch-native |
| Ban evasion detection | Flags "Likely" (Restrict by default) and "Possible" (Monitor by default). Covers channel bans only. Not on mobile. Signals are not published. | Read the result from events only. | R: twitch-native-help |
| Shared Ban Info | Up to 50 partner channels. Restrict by default. A red border names the banning channel. Mod comments are shared by default. Joke bans are shared too. | No API. | R: twitch-native-help |
| Chat verification | Email or phone, for all or some users, by account age (1 hour to 6 months) or follow age (10 minutes to 3 months). A banned phone bans all accounts on that phone. | No API field. | R: twitch-native-help, twitch-native |
| Warn | The user must acknowledge before chatting again. Reason max 500 characters. | Yes. | R: twitch-native |
| Timeout and ban | Timeout from 1 s to 1,209,600 s (14 days). | Yes. A ban cannot become a timeout. | R: twitch-native |
| Unban requests | One request per user per channel. Cannot be edited. Cooldown setting. Opt-in per channel. | Yes: list, resolve, events. | R: twitch-native, twitch-native-help-2 |
| Delete and clear | Remove one message or all chat. | Yes, only messages under 6 hours old, never another mod's message. | R: twitch-native |
| Mod action feed | `channel.moderate` v2 reports every mod action. | Yes, but it needs 8 scope groups. | R: twitch-native |
| Chat modes | Followers-only (0 min to 3 months), slow (3-120 s), sub-only, emote-only, unique, non-mod delay 2/4/6 s. | Yes. | R: twitch-native, twitch-shortcomings |
| Mods and VIPs | Add and remove. | Yes, at most 10 changes per 10 s. | R: twitch-native |
| Raids | Start and cancel. Incoming-raid filters (viewer range, raider account age, groups), "Stop raids for 1 hour", "Review Recent Raids" with Report and Ban. | Start and cancel (10 per 10 min) and the `channel.raid` event. Filters, stop and review are UI only. | R: twitch-native, twitch-native-help-2 |
| Pinned message | One pinned message per channel. | Yes. | R: twitch-native |
| Mod View and reports | Batch Reporting Tool keeps users for 8 hours. Message logs are kept 3.5 years. Mod actions and comments are kept with no end date. | No report API. | R: twitch-native-help |
| Lead Moderator role | Adds and removes mods and VIPs, deletes mod comments. Only the owner assigns it. | No API. | R: twitch-native-help |
| Whispers | Private messages. | Send needs a verified phone. 40 new recipients per day, 3 per second, 100 per minute. Twitch may drop a whisper and still answer 204. The bot sees only whispers to itself. | R: twitch-native, twitch-shortcomings-2 |
| Check AutoMod Status | Asks if AutoMod would queue a message. | 5 per minute and 50 per hour (Normal), 10 per minute (Affiliate), 30 per minute (Partner). | R: twitch-native |
| Shared chat | One chat across several channels. | An app-token chat message goes to the source channel only, unless `for_source_only=false`. | R: twitch-native |

### 3.2 What Twitch does not give a bot

| Gap | What it means for us | Source |
|---|---|---|
| No shared-ban API | We cannot read, join or fill Twitch Shared Ban Info. The owner's "fill the list" (PLAN:144) can only mean real Twitch bans. Twitch then shares them through each channel's own Shared Ban Info. | R: twitch-native |
| No ban-evasion settings API | We see the verdict, not the setting. | R: twitch-native |
| No verified-chat field | We cannot set phone or email verification. | R: twitch-native |
| No mod comments API | Twitch mod comments and our notes never meet. | R: twitch-native |
| No Block Hyperlinks or permitted-terms API | We cannot read or set Twitch link handling. Our own link filter must carry the load. | R: twitch-shortcomings-3 |
| No list of suspicious users | We must store the state from events. We cannot back-fill a gap. | R: twitch-native |
| No Lead Moderator or Editor role API | We cannot read who holds Lead Moderator on Twitch. Our LeadModerator is our own role. | R: twitch-native |
| No appeal or report API | Only the unban-request flow. | R: twitch-native |
| Timeout cap | 1,209,600 s. A help page says a timeout can last "any number" of seconds; the API reference wins. | R: twitch-native-help-2 |
| Raider names | Unknown until each raider talks. | R: twitch-shortcomings-2; PLAN:762-768 |
| Whisper strangers | Two Twitch help articles define who may whisper whom in different ways. | R: twitch-native-help-2 |

### 3.3 AutoMod: 5 help categories against 8 API fields

Twitch publishes no map. This map is the research worker's reading. Mark it "unconfirmed" in any spec (R: twitch-native-help-2).

| Help page category | API fields | Confidence |
|---|---|---|
| Discrimination and Slurs | disability, sexuality_sex_or_gender, misogyny, race_ethnicity_or_religion | Medium |
| Sexual Content | sex_based_terms | High |
| Harassment | aggression, bullying | Medium |
| Profanity | swearing | High |
| Smart Detection | no field (it learns from the channel's mod actions) | n/a |

API trap: an update overwrites every field. A one-field update sets `overall_level` to null (R: twitch-native-help-2). Our AutoMod screen must always send the full set.

### 3.4 How Twitch enforces against accounts

| Fact | Source |
|---|---|
| Suspensions last from 24 hours to 30 days, or have no end. | R: twitch-native-help-2 |
| A violation expires after 90 days (lower severity) or 1-2 years (higher). | R: twitch-native-help |
| Appeals go to appeals.twitch.tv within 6 months. | R: twitch-native-help-2 |
| A reporter gets no detail on the outcome. Many reports on one target do not speed review. | R: twitch-native-help-2 |
| H2 2025: 77,563 appeals, 15,143 accepted (19.5%). | R: twitch-shortcomings |
| H2 2025: 4.9M reports and about 1.22M enforcements from reports. 83.8% of reports reviewed in under 10 minutes. | R: twitch-shortcomings |
| H2 2025: suspension evasion 23,653 enforcements, 4,771 appeals, 714 accepted. | R: twitch-shortcomings |
| H2 2025: 42,751,749 spam and scam enforcements. 97.17% of proactive enforcements hit inauthentic accounts. | R: twitch-shortcomings |
| Twitch says it does not endorse third-party safety tools. | R: twitch-shortcomings-3 (Combating Targeted Attacks FAQ) |

---

## 4. Every Twitch shortcoming, ranked by harm

The rank is this writer's judgement. It weighs how many people a gap hurts and how badly. The owner may reorder it.

| Rank | Shortcoming | What goes wrong | Who it hurts | What our bot can do inside the API | What our bot cannot do | Source |
|---|---|---|---|---|---|---|
| 1 | Hate raids | A wave of new or bought accounts posts identity attacks. 98% of hate-raid messages in the 2021 sample were identity attacks. Sery_Bot reached 55k+ channels in 2 months as a stop-gap. | Streamer, viewers from the targeted groups, mods | Tighten chat modes at once (lockdown), turn Shield Mode on, restrict or ban at high confidence, show a raider card on an incoming raid. | Block the raid at the source (raid filters are UI only). See the raiders before they talk. Set phone or email verification. | R: twitch-shortcomings; PLAN:734-736, 762-768 |
| 2 | No shared view of why a user was banned elsewhere | Each channel bans the same troll by hand. Twitch Shared Ban Info shows only "banned in X", is capped at 50 pairs, and shares joke bans. | Mods of many channels, streamers | Restrict first in each entrusting channel, then show a decision card with the reason and the last messages. | Read or write Twitch Shared Ban Info. Read ban-evasion signals. | R: twitch-native-help; PLAN:160-168 |
| 3 | No guard against misuse of moderation power | A mod can ban for a private dispute. Twitch has no rate brake on a mod and no outside review. Kick warns against mass-banning followers. | Viewers banned for the wrong reason, the streamer's name | Named grants, entrust per channel, a required ground, a record per requester, one-step reversal, and an audit log. | Stop a mod who acts in the Twitch UI directly. We only see it after the fact through `channel.moderate`. | PLAN:121-168; R: third-party-2 |
| 4 | Scam links and phishing | Fake giveaways and malware links steal accounts. Block Hyperlinks is all or nothing, and mods and VIPs are exempt, so a stolen VIP account posts freely. | Viewers, streamer | Our own link policy: allow-list, short-link and lookalike-domain checks, "new account plus link" rule. Our filter can also check VIPs. | Read or set Twitch Block Hyperlinks. Delete another mod's message. | R: twitch-shortcomings-3; PLAN:745-747 |
| 5 | Impersonation | Fake channels copy a streamer (about 2,900 fake channels in one 2020 count). Fake "Twitch Support" accounts ask for logins. | Viewers, streamer | Compare a new chatter's name with the streamer, the mods and "support" words, using Helix user `created_at`. Post a calm notice that staff never ask for passwords. | See whispers to viewers. Act on fake channels outside our channel. | R: twitch-shortcomings-3 |
| 6 | Private information posted in chat (doxxing) | An address or phone number appears in chat. Twitch reported 2,434 private-information enforcements in H2 2025. | The target, often the streamer | Queue a message that looks like an address or phone number for a mod (never auto-ban on it). | Remove it from places outside chat. | R: twitch-shortcomings |
| 7 | Follow-bot and view-bot waves | Thousands of fake follows, or fake viewers. The 2025 viewbot crackdown cut some channels' viewership by up to 22%. | Streamer's stats, the streamer's standing | Detect the spike and block the bot accounts. Show the batch so the streamer can restore real followers. | Stop the follows at the source. See view bots. Does a block remove the follow? Yes for a block in the Twitch UI: it removes them from your followers and prevents future follows (help.twitch.tv how-to-manage-harassment-in-chat). The Helix Block User reference is silent, so the API effect is open (see the coverage ledger). | R: twitch-shortcomings; via R: internal-settled-2 (spam-defense s.7) |
| 8 | Spam campaigns ("best viewers" and similar) | The same sales pitch from many fresh accounts. | Streamer, viewers | Near-duplicate and signature match, weighted by trust, so a regular who quotes the phrase is safe. | Share signatures across instances today (S-SPAM-NETWORK is blocked). | PLAN:169-172, 729-733 |
| 9 | Channels with no mods | About 3.3% of traffic had no mods and no AutoMod. AutoMod defaults to Level 2 only if no mod exists. | Small streamers | A safe preset for a channel with no mods, and a protection-status card. | Assign mods for the streamer. | R: twitch-shortcomings, twitch-native-help |
| 10 | Reports go into a black box | The reporter learns no outcome. Many reports do not speed review. | Streamers, mods, viewers | Build an evidence pack and open Twitch's own report flow for one user at a time. | File reports by API. See the outcome. | R: twitch-native-help-2; via R: internal-settled-2 (2026-06-16 :21) |
| 11 | Weak path back for a wrongly banned viewer | One unban request per channel, opt-in. A denial is final. 19.5% of account appeals succeed. | Viewers banned by mistake | Show unban requests in one queue, a "this was wrong" button on every automatic action, and history that names who acted. | Change Twitch's appeal rules. | R: twitch-native, twitch-shortcomings |
| 12 | Moderators work alone and burn out | Mods coordinate on Discord beside the tools. One mod covers a whole night. | Moderators | Shared notes on each user, one queue, a hand-over note between shifts. | Pay or recruit mods. | R: twitch-shortcomings-2 |
| 13 | Whisper abuse | Scammers whisper viewers. Whisper limits stop a bot from warning many viewers. Twitch can drop a whisper and still answer 204. | Viewers | Read whispers sent to the bot (a report inbox). Pin or post a short safety notice, rate-limited. | See whispers between other users. Whisper many viewers. | R: twitch-shortcomings-2 |
| 14 | Key settings hidden from bots | Verified chat, Block Hyperlinks, permitted terms, private terms, ban-evasion level, Shield bundle, raid filters. | Streamers who trust the dashboard | Show each one as "set on Twitch" with a direct link and a short why. | Set them. | R: twitch-native |
| 15 | Youth safety | Twitch reported 12,393 youth-safety enforcements and 1,409 NCMEC reports in H2 2025. | Minors | Route a mod to Twitch's own report flow at once. | Verify age. Handle the case ourselves. | R: twitch-shortcomings |

Also from the record:

- #TwitchDoBetter (August 2021) asked Twitch to keep new accounts out of chat and to let streamers approve raids. #ADayOffTwitch followed on 1 September 2021 (R: twitch-shortcomings).
- Twitch sued two hate-raid operators in September 2021 (R: twitch-shortcomings).

---

## 5. What others do, and the lessons

### 5.1 The products read

| Product | Kind | How it was read | Source note |
|---|---|---|---|
| Fossabot | Twitch bot | Docs pages | R: third-party |
| StreamElements | Twitch, YouTube and Kick bot | Docs pages | R: third-party |
| Nightbot | Twitch and YouTube bot | Docs pages | R: third-party |
| Moobot | Twitch bot | Docs pages | R: third-party |
| Sery_Bot | Twitch protection bot | Docs pages | R: third-party |
| Streamer.bot | Desktop automation | Docs pages. The moderation page says "Documentation Needed". | R: third-party |
| Firebot | Desktop bot | docs.firebot.app has no moderation section. | R: third-party |
| CommanderRoot tools | Web tools | Index pages only | R: third-party |
| Chatterino | Chat client | Wiki | R: third-party, third-party-2 |
| FrankerFaceZ | Browser extension | Website unreachable (timeout 30 s in Chrome, HTTP 522 by curl). Features read from the GitHub change log old_changes.html; all 11 rows re-read. | R: ffz-7tv-mod |
| 7TV | Browser extension | Website reachable (HTTP 200); the home page has no moderation text. The Help Center help.7tv.app documents Mod Slider, Moderation Messages, Ignore Clear Chat, Mod Logs, Nuke Command (before/after window, action, duration, reason) and Improve the User Card. The Mod View and Shield Mode buttons stay sourced from the GitHub change log. | R: ffz-7tv-mod |
| Discord AutoMod and raid protection | Platform | Help articles | R: third-party, third-party-2 |
| Dyno, Wick | Discord bots | Docs pages | R: third-party-2 |
| Kick | Platform | Help articles | R: third-party-2 |
| YouTube | Platform | Help article | R: third-party, twitch-shortcomings-2 |
| Mastodon | Platform | Admin docs | R: third-party |
| Bluesky (lists, labels, Ozone) | Platform | Docs, blog, GitHub issue, news (real browser) | R: bluesky-lists |

Correction: an earlier note (R: third-party-3) said "7TV and FrankerFaceZ are emote platforms only, not moderation tools". That claim is wrong. FrankerFaceZ has mod cards with chat history, name history, user stats with ban count, notes count and inline mod icons. 7TV has Mod View, Mod Logs and Shield Mode buttons, a warn icon, and Lead Moderator support (change log v3.1.17) (R: ffz-7tv-mod). The 7TV Help Center adds a Mod Slider, a Nuke Command, Ignore Clear Chat, and Moderation Messages visible to non-mods (help.7tv.app). The same note also merged "Firebot/Moobot" and cited firebot.tv. Its other rows are not used here.

### 5.2 Feature table: others against NomNomzBot

Status: **have** (built), **partial** (built in part, or built but not reachable), **missing** (a gap to build), **not doing** (rejected, with the reason).

| # | Feature | Seen in | NomNomzBot status | Source for our status |
|---|---|---|---|---|
| 1 | Nuke: delete, timeout or ban everyone who said a phrase or regex in the last N seconds | Fossabot (1-600 s, radiation), StreamElements | partial: the dashboard has a nuke action, a batch list and a revert (`ModerationScreen.kt:2635`, `:1593`, `:692`, `:778`). The server nuke bans ONE named user in every channel where the actor is LeadModerator or higher (`NetworkNukeService.cs:47`, `:60-130`). `MatchTerm` is stored and never used to scan chat. A phrase, regex or last-N-seconds nuke is missing. No per-batch list of who was hit: `NetworkNukeBatch` (`ModerationApi.kt:1410`) holds counts only. Slice S-MOD-NUKE-SCREEN | code read this run; PLAN S-MOD-NUKE-SCREEN (:82) |
| 2 | Nuke rollback and a report of who was hit | Fossabot (ROLLBACK, COPY JSON) | partial: the reversal is built (`NetworkNukeService.cs:155-207`; route `ModerationController.cs:1196`). The per-channel who-was-hit report is missing on the server and in the app: no route lists the legs (`NetworkNukeDtos.cs:14-28`) | code read this run; S-MOD-NUKE-SCREEN |
| 3 | Nuke by username pattern, with a warning about innocent users | StreamElements | missing (confirmed) | R: third-party; code read this run |
| 4 | Ban one account in many channels | Moobot batch tool (own channel only), none across channels | partial (confirmed): the API exists (`ModerationController.cs:171`, `:215`, `:258-312`, `:337`). The app has no mass-ban client. | code read this run; PLAN:49-53 |
| 5 | Decision card with the reason and last messages, per channel | none found | missing. No build slice existed. New slice S-MOD-DECISION-CARD (PLAN:298). Today only guard (k) of S-MASSBAN-ABUSE-GUARDS holds the idea | PLAN S-MOD-DECISION-CARD (:298); guard (k) (:166-168) |
| 6 | Lookalike letters folded before matching | Fossabot, Moobot | have. `MessageNormalizer.cs` holds a curated UTS #39 subset plus NFKD. It is used at `SpamDefenseService.cs:419` and `LinkDetector.cs:364`. Tests exist (`MessageNormalizerTests.cs:32-96`); this research did not run them. Open: whether the chat filters (`ChatFilterExecutionHandler`) fold with it | code read this run |
| 7 | Phrase groups and per-phrase severity | Fossabot, Moobot | partial, exact: 3 kinds (Regex, Blocklist, LinkPolicy) and 5 actions (Delete, Timeout, Hold, Flag, Escalate). One action and one timeout apply per FILTER. No per-phrase severity and no phrase groups. S-CHATFILTER-HOLD and S-CHATFILTER-ENUM-WIRE do not add groups | code read this run; PLAN S-CHATFILTER-HOLD (:1195) |
| 8 | Test a filter before saving | none found directly; Fossabot silently ignores a bad regex | server have: `POST` test route (`ChatFiltersController.cs:153-161`) calls `ChatFilterService.TestPattern` (`:285`) and persists nothing. An invalid regex is refused on create and update. The app client and screen are missing (`ModerationApi.kt:328-341`). Slice S-MOD-FILTER-TEST is the dashboard half | code read this run; PLAN S-MOD-FILTER-TEST (:84) |
| 9 | Link filter with an allow-list | Nightbot, StreamElements, Moobot (hidden links, IP links) | partial. An earlier version of this row said "stored, never read". That was wrong. The policy is read and applied (`ChatFilterExecutionHandler.cs:432-433`, `LinkPolicy.cs:29`, `:32`). It has only 2 fields (AllowedDomains, MatchBareDomains). The wider part (short link, lookalike domain, new account plus link, VIP) is open: S-FILTER-LINKPOLICY | code read this run; PLAN S-FILTER-LINKPOLICY (:766) |
| 10 | Temporary link pass for one viewer | Fossabot permit (60 s default, max 3600 s), Nightbot | missing. Fossabot `!permit` was read live: 60 s default, max 3600, blocks any timeout. Nightbot `!permit` was read live: 60 s example, needs `!filters`. Ours (`PermitBuiltins.cs:246-270`, `PermitAction.cs:20-36`) grants a role or capability, not a link pass. It needs a slice: S-FILTER-LINKPOLICY (the wider part) or a new one | Fossabot docs; docs.nightbot.tv/spam-protection/links; `PermitBuiltins.cs` |
| 11 | Warn first, then act | Nightbot, Moobot, Twitch Warn | partial (confirmed): Twitch Warn is reachable (`ModerationController.cs:1051-1052`). No pipeline `warn` action exists. Slice S-MOD-PIPELINE-ACTIONS | code read this run; PLAN S-MOD-PIPELINE-ACTIONS (:1203) |
| 12 | Escalating timeouts on repeat offences | Wick multiplier, Dyno ladder | have, off by default per channel. `ModerationEscalationService.cs`: ladder warn, 60 s, 600 s, 3600 s, 86400 s, ban; window 168 h (`ModerationEscalationPolicy.cs:33`); atomic increment. Wired at `ChatFilterExecutionHandler.cs:335`, `AutoModerationHandler.cs:528` and `SpamEnforcementExecutor.cs:57`. Policy GET, PUT and reset at `ModerationController.cs:1128-1159`. Dashboard editor `ModerationScreen.kt:4563`, `:4625`. No unit test of the rung maths was found | code read this run |
| 13 | Caps, emote, symbol, repeat, length, zalgo and language filters | Nightbot, StreamElements | partial, exact kinds: `ChatFilterType` = Regex, Blocklist, LinkPolicy. `AutoModerationHandler.cs:99-102` holds the rules caps, links, banned_phrases and emote_spam. Zalgo is only a spam-defence signal (`MessageNormalizer.cs:192`, `:260`). Missing: symbol, single-message repeat, length, language. New slice S-MOD-FILTER-KINDS. The old cite (S-CHATFILTER-ENUM-WIRE) is about enum serialization | code read this run; PLAN S-MOD-FILTER-KINDS (:307) |
| 14 | Protection presets (low, medium, high) | StreamElements | partial (confirmed): `DryRun=true` is the default (`SpamDefenseSettings.cs:35`). No Low, Medium or High preset type exists, so presets are missing. The 7 days is only a doc comment | code read this run |
| 15 | Account-age and follow-age gate | Kick, Twitch verification (UI only), Dyno | have, default off: `AccountAgeGateDays` (`SpamDefenseSettings.cs:51`), `FollowAgeGateDays` (`:54`), `AccountAgeGate.cs:49-76`, `SpamDefenseService.cs:460`, `AccountAgeGateExecutor` in DI (`:1167`). Limit: the follow-age gate applies only when the viewer IS following | code read this run |
| 16 | Raid shield: one switch tightens the room, then restores it | Discord raid protection (1 hour), Wick panic mode, Twitch Shield | partial (confirmed): manual lockdown exists (`SpamDefenseController.cs:236`, `:260`, `:289`). No automatic caller. No app lockdown client | code read this run; PLAN:734-736 |
| 17 | Incoming-raid check of the raiding channel | Twitch (UI only) | missing (name-based search found nothing) | code read this run; PLAN S-RAID-VET (:783-789) |
| 18 | Silent block of follow bots, with restore | Sery_Bot, CommanderRoot | have in code. An earlier version of this row said "spec built; not in PLAN". That was wrong. It is wired end to end: `FollowSpikeHandler.cs:26` to `FollowBotSweepService.cs:92`, `:102`, `:122`; DI `:1171-1172`; routes `SpamDefenseController.cs:161`, `:189`; app `SpamDefenseApi.kt:46-96`, `ModerationScreen.kt:1537`. The spec header lines (`spam-defense.md:27`, `:270`, `:464`, `:584`) are stale. It was not run on a live box (see the coverage ledger) | code read this run |
| 19 | Hate-raid detection | Sery_Bot (no thresholds published) | partial: spam defence L0-L5 are all live (`SpamDefenseService.cs:419`, `:698`, `:420`, `:437-444`, `:464`; `SpamCorrelationService` via `SpamDefenseHandler.cs:78-79`). Missing: the automatic lockdown trigger. `JoinBurstFactor` is never measured (PendingSlices.LockdownWire) | code read this run; PLAN S-SPAM-LOCKDOWN-WIRE (:755-757) |
| 20 | Mod action audit log | Fossabot, Dyno, Ozone | have (confirmed): History page mod log | via R: internal-settled-2 (FEATURES-BY-SIDEBAR:68-77); code read this run |
| 21 | User card with history, notes and stats | FrankerFaceZ, Chatterino, Twitch Mod View | partial (confirmed): per-user panel exists (`ModerationScreen.kt:2438`, `:2716`) | code read this run |
| 22 | Shared notes between mods | Twitch mod comments, FrankerFaceZ notes | have on the server. An earlier version of this row said "two stores". That was wrong. Commit `be732f692` (2026-10-06) merged them: `ModerationHistoryService.cs:22-24`, `:84-102` delegates to `IModerationService.AddUserNoteAsync`. Records use RecordType `user_note` (`ModerationService.cs:1708-1710`). Migrations exist in both sets, plus `MergeModNotesIntoRecordsMigrationTests.cs`. Two routes remain (`ModerationController.cs:802`, `:1425`) and write one store. The server half of S-UF-M1a is stale | code read this run |
| 23 | Unified mod queue | Twitch Mod View, Ozone | partial (confirmed): only the AutoMod queue read exists, newest first (`ModerationQueueService.cs:230` OrderByDescending CreatedAt). The app stacks per-kind cards (`ModerationScreen.kt:1114`, `:1149`, `:1184-1188`). New slice S-MOD-QUEUE-ONE-LIST (one list, oldest first; TTS and media items are in no queue read). Keep S-UF-M6a | code read this run; PLAN S-UF-M6a (:2164) |
| 24 | Custom roles and a separate nuke permission | Moobot, Fossabot, Wick | partial (confirmed): Critical tier exists. The mass-ban endpoint checks `moderation:ban` only (`ModerationController.cs:337`). The nuke uses `moderation:nuke` (`:1179`, `:1196`, `:1211`) | code read this run |
| 25 | Restrict or monitor a user | Twitch, Twitch shared bans, Kick | partial. An earlier version of this row said "UI and ledger missing". That was wrong. The ledger entity `ChannelLowTrustStatus.cs`, `LowTrustStatusService.cs`, the set and clear routes (`ModerationController.cs:1078-1105`) and the app `LowTrustStatusNotice.kt` exist. Handlers consume the events (`SuspiciousUserHandlers.cs:23`, `:44`); the PLAN text "no handler consumes them" is stale. Open: does the ledger hold restrictions made in the Twitch UI, and does it survive a restart (done-when of order 27) | code read this run |
| 26 | Hide a user from others without telling them | YouTube hide user, bot-side shadowban | partial (confirmed): built per platform identity, not per person (`ChannelModerationStanding.cs:31`, `:35`) | code read this run; PLAN S-VIEWER-RESTRICTION-D1 (:2459) |
| 27 | One moderation action on Twitch, YouTube and Kick | StreamElements nuke on all three | partial (confirmed): room modes are Twitch only. Only `TwitchLockdownAdapter` exists (DI `:1179`) | code read this run; PLAN:737-739 |
| 28 | "Report Issues" on every automatic decision | Discord | partial: an overturn route exists for spam detections only (`SpamDefenseController.cs:211-213`). None exists for chat-filter or heat actions | R: third-party-2; code read this run |
| 29 | Rate brake on staff who ban too fast | Wick anti-nuke | missing (name-based search found nothing) | R: third-party-2; code read this run |
| 30 | Shared ban lists with consent | Twitch Shared Ban Info, Mastodon CSV import | partial: same-instance shared-chat propagation and inbound federation are built. Publish-out to other instances is missing (no `.SignAsync` callers; `FederationOptInType.SharedBanList` and `SharedTrustList` are declared and unused) | code read this run; PLAN S-SHAREDBAN-FEDERATE-OUT (:772-773) |
| 31 | Import a ban list from a paste or a file | Moobot, Mastodon | not doing: a central list becomes a vendetta tool (guard l) | PLAN:166-168; R: bluesky-lists |
| 32 | Subscribe to someone else's label or block list | Bluesky | not doing: same reason as row 31 | R: bluesky-lists |
| 33 | Vote-kick by viewers | StreamElements (51% yes), Fossabot | not doing: a crowd can ban the wrong person | R: third-party |
| 34 | CAPTCHA for new chatters | Discord | not doing: no Twitch API; point to Twitch verification instead | R: twitch-native |
| 35 | Live updates on every page | Ozone event stream | partial: S-LIVE-EVERYWHERE. 22 of 70 controllers refresh live (`ConfigChangeRefresh.kt:21-33`; see order 40) | code read this run; PLAN S-LIVE-EVERYWHERE (:174) |
| 36 | Doxx pattern detection | none found | missing (name-based search found nothing) | code read this run |
| 37 | Name impersonation check | none found (Fossabot lookalikes match words, not names) | missing (name-based search found nothing) | R: third-party; code read this run |

### 5.3 Lessons

| # | Lesson | Source |
|---|---|---|
| L1 | Shared lists turn into vendetta tools. Bluesky hides abusive lists from others but keeps subscribers, because deleting a list "would unintentionally erase blocks". | R: bluesky-lists |
| L2 | The list title itself can harm ("spammer", "pedophile"), even with no action attached. | R: bluesky-lists (GitHub issue 7076) |
| L3 | Mass reporting is a second vendetta tool. Bluesky acts on evidence in the app, not on reputation. | R: bluesky-lists |
| L4 | Kick: "Mass-banning followers, attacking other channels, or organising counter-action puts your account at risk". | R: third-party-2 |
| L5 | Kick: username similarity is not proof of ban evasion, because "names can coincide". | R: third-party-2 |
| L6 | Kick: do not announce an attack again and again; it draws more attackers. | R: third-party-2 |
| L7 | Fossabot: "Improper usage may result in a permanent ban from Fossabot." A misuse penalty is normal practice. | R: third-party |
| L8 | StreamElements warns that broad nukes and username nukes can hit legitimate users. | R: third-party |
| L9 | Fossabot ignores an invalid regex with no error. A filter must be tested before it saves. | R: third-party |
| L10 | Dyno uses "warn" for two different things. Keep one meaning per word. | R: third-party-2 |
| L11 | Dyno autoban cannot keep up in big raids because of rate limits. A bulk action needs a queue, progress and a report. | R: third-party-2 |
| L12 | Wick: kicking all new accounts after a raid "leads to a loss of genuine new members". Blunt responses hurt real newcomers. | R: third-party-2 |
| L13 | Wick: a short timeout fails against raiders who wait it out. Escalate on repeats. | R: third-party-2 |
| L14 | Wick and Discord: power without a guided setup goes unused. A wizard was added. | R: third-party-2 |
| L15 | Discord ML spam filter "is not perfect" and ships a Report Issues button. | R: third-party-2 |
| L16 | Discord AutoMod does not check messages from installed apps. Decide on purpose whether our bot's own posts are filtered. | R: third-party-2 |
| L17 | Sery_Bot and other tools publish no thresholds. Users cannot predict them. Our rules must be visible and explainable (SD7). | R: third-party; via R: internal-settled |
| L18 | Moobot's removal log clears on refresh. A log must persist. | R: third-party |
| L19 | Bluesky shows the policy, severity and strike count in every notice, and a won appeal undoes the action. | R: bluesky-lists |
| L20 | Twitch says it does not endorse third-party tools. We must never look like Twitch, and must say what we change. | R: twitch-shortcomings-3 |

---

## 6. Re-evaluation of every settled decision

Rule used to settle a conflict: the owner's newest words win by date. The 2026-10-10 words (PLAN:86-168) are the newest. Where the research gives a strong reason against a decision, the row says so, and section 8 asks the owner.

"DOC" means `.claude/docs/design/`. "SPEC" means `.claude/docs/design/spec/`. Rows marked "via R" were quoted by a research worker. Doc and code lines in the K and C tables below were re-read in a later pass and corrected where they were wrong.

PLAN line numbers moved while this paper was written (about +1 above line 700, +21 from line 727, +16 at line 3260). A slice NAME is the stable key. Where a PLAN line is given for a slice, it is the line read in the later pass.

### 6.1 Decisions

| # | Source (doc:line) | Decision | Still right? | Reason |
|---|---|---|---|---|
| D01 | DOC/2026-06-16-moderation.md:6 (via R) | One mod dashboard, Moderator and up; what a mod sees scales with level. | Yes | Every product read has a mod surface; FrankerFaceZ and 7TV add one on top of Twitch (R: ffz-7tv-mod). |
| D02 | DOC/2026-06-16-moderation.md:9-12 (via R) | One action queue, user context, opt-in filters per channel, mod audit log. | Yes | Twitch Mod View, Ozone and Fossabot all have these (R: twitch-native-help, bluesky-lists, third-party). |
| D03 | DOC/2026-06-16-moderation.md:15 (via R) | No cross-channel ban by default. | Yes | Each channel is off until one of its moderators switches it on, and the streamer's refusal wins (S-MASSBAN-OPTIN-TOGGLES). |
| D04 | DOC/2026-06-16-moderation.md:16 (via R) | Shared-chat ban propagates directly to an opted-in partner. | Changed | See K3. A ban into another channel is a request, even in Shared Chat. |
| D05 | DOC/2026-06-16-moderation.md:17 (via R) | Shared ban and trust lists between trusted channels, opt-in. | Changed | Guard (l): no central list (PLAN:166-168). Lists became vendetta tools on Bluesky (L1-L3). A shared ban arrives as a request, never as a ban. |
| D06 | DOC/2026-06-16-moderation.md:20 (via R) | Network nuke: LeadModerator and up, one confirmation, audited, reversible. | Changed | See K2. Audited and reversible stay. Authority moves to a named grant plus entrust. |
| D07 | DOC/2026-06-16-moderation.md:21 (via R) | No automated mass-reporting; an evidence pack for Twitch's own report flow. | Yes | Twitch: many reports do not speed review (R: twitch-native-help-2). Bluesky: mass reporting is a vendetta tool (L3). |
| D08 | docs/bot-capabilities.md:33 (via R) | Role ladder Moderator, SuperMod, Editor, Broadcaster. | Changed | See K5. The name is LeadModerator. |
| D09 | docs/bot-capabilities.md:60-63 (via R) | Users see role names, never numbers. | Yes | No conflict found. |
| D10 | docs/bot-capabilities.md:72-80 (via R) | `moderation:nuke` and `moderation:sharedban:write` are Critical: one named person only. | Yes, add a second grant | See K4: the origin grant (who may ask) plus entrust per target channel (who is trusted there). |
| D11 | docs/bot-capabilities.md:85 (via R) | Roles and Permits page is Broadcaster only. | Yes | Entrust is a broadcaster act, so it fits on this page. |
| D12 | FEATURES.md:54 (via R) | Persona priority: streamer, then moderator of many, then viewer. | Yes | The decision card puts the streamer's choice first (PLAN:165-166). |
| D13 | FEATURES.md:317-329 (via R) | Community data is real platform data only. | Yes | House rule. |
| D14 | PLAN:95-96 (D-MB1) | A lead moderator and up releases a hold with `!allow massban`. | Yes | Kept for live channels: own, attacked and offline channels ban at once, and a live switched-on channel waits for `!allow massban` (the S-MASSBAN-ABUSE-GUARDS correction of 2026-10-10). |
| D15 | PLAN:96-98 (D-MB2) | The requester picks channels with toggles; the broadcaster's opt-out wins. | Changed | See C10. The toggles stay, but list only channels that entrusted this mod. Opt-out becomes "withdraw trust". |
| D16 | PLAN:99-101 (D-MB3) | Community and Moderation fold into one user hub. | Yes | Owner answer 3 (PLAN:88-93). |
| D17 | PLAN:122-125 guard (a) | Own channels and auto-accept channels ban at once. | Changed | A switched-on channel bans at once with no accept step, and the streamer can refuse (the S-MASSBAN-ABUSE-GUARDS correction of 2026-10-10). See C4. |
| D18 | PLAN:125-126 guard (b) | Every request carries evidence. | Yes | Core of the decision card (PLAN:165-166). |
| D19 | PLAN:126-127 guard (c) | Mass-ban power is a named grant, never a role. | Yes | The code checks the wrong permission today. See C11. |
| D20 | PLAN:127-128 guard (d) | A reversed disputed ban counts against the requester; repeats revoke. | Yes | Fossabot bans misusers of its nuke (L7). The count: question Q5. |
| D21 | PLAN:128-130 guard (e) | Every batch reverses in one action. Auto-ban only for hate raids, follow bots and "best viewers" spam. | Yes, narrowed | S-SPAM-CONTEXT-TRUST keeps regulars safe (PLAN:169-172). Follow bots: auto-ban, settled in Q1 (S-FOLLOWBOT-AUTOBAN). |
| D22 | PLAN:133-135 guard (f) | The system owner may skip the accept step for one batch, named and visible. | Yes | The owner's VETO (PLAN:131-133). Bluesky also keeps a baseline layer above the lists (R: bluesky-lists). |
| D23 | PLAN:135-136 guard (g) | A mass ban is only for hate, insults or harm. | Yes | Kick warns against counter-action (L4). |
| D24 | PLAN:137-142 guard (h) | Restrict in each channel while it decides. | Yes, now verified | Helix "Add Suspicious Status to Chat User" exists, scope `moderator:manage:suspicious_users` (R: twitch-native). It needs a mod token in that channel. |
| D25 | PLAN:146-147 guard (i) | An accepted request ends as a real Twitch ban. | Yes | It is the only way to "fill the list" (no shared-ban API, R: twitch-native). |
| D26 | PLAN:147-150 guard (j) | Each streamer entrusts named mods; trust can be withdrawn. | Yes | Matches Twitch Shared Ban Info consent per pair (R: twitch-native-help). |
| D27 | PLAN:165-166 guard (k) | The decision card is the core: who, why, origin, last messages, one primary "Ban here too". | Yes | No tool read offers it (section 5.2 row 5). |
| D28 | PLAN:166-168 guard (l) | No central list. | Yes | L1-L3. |
| D29 | SPEC/spam-defense.md:82-100 SD0 (via R) | A legit viewer is never punished; act on the room, not the person. | Yes | Wick: blunt join-wave kicks lose real newcomers (L12). |
| D30 | SD1 (via R) | Split action by confidence. | Yes | Auto-ban rests on high confidence only (K9). |
| D31 | SD7 (via R) | Every action is explainable. | Yes | Sery_Bot publishes no thresholds (L17). |
| D32 | SD8 (via R) | Established regulars are immune; only a flag. | Yes | Owner's "best viewers" quote (PLAN:118-119). |
| D33 | SD9 (via R) | Presence is never an offence. | Changed | A proven follow-bot account is banned; an account with chat history or standing is left out (Q1, S-FOLLOWBOT-AUTOBAN). |
| D34 | SD10 (via R) | Risk multiplies, never adds. | Yes | No research against it. |
| D35 | SD11 (via R) | Portable standing is positive evidence first. | Changed in part | See C5: a ban elsewhere may raise the watch level, never cause a ban alone. |
| D36 | SD12 (via R) | We cannot hold a message on hosted platforms. | Yes | Rename the word (C3). |
| D37 | SPEC/spam-defense.md:645-668 (via R) | Dry run is the default for 7 days; replay on threshold change. | Yes | Discord's spam filter "is not perfect" (L15). Replay is not built (K15, Q11). |
| D38 | SPEC/spam-defense.md tiers (via R) | Trust tiers Untrusted to Established. | Yes | Kick and Dyno use account-age gates too (section 5.2 row 15). |
| D39 | SPEC/moderation.md:1068+ decision 3 (via R) | Bot-side restriction per person, not per platform account. | Yes | PRODUCT-ALIGNMENT D1. Built per platform today (PLAN:2438). |
| D40 | SPEC/moderation.md:390-411 (via R) | INetworkNukeService: LeadModerator and up bans directly everywhere. | Changed | See C2. |
| D41 | SPEC/moderation.md:~425-445 (via R) | Same-channel fan-out across our own platforms is always on. | Yes | One viewer, one ban (D1). This is one channel, not many. |
| D42 | PRODUCT-ALIGNMENT D1, D4, D10, D11, D12 (via R) | One viewer one ban; free viewer account; security first; cheap actions never 429; admin "impersonation" only for the platform owner. | Yes | No conflict. Note: D12 "impersonation" means an admin acting as a user. This paper calls fake accounts "copycat accounts" to keep one term per concept. |
| D43 | PRODUCT-ALIGNMENT D8 (via R) | Stabilize before adding. | Yes | The plan fixes our own tool first. |
| D44 | PLAN:2014-2165 S-UF-M5a, M4b (via R) | Routine action: no confirm, 10 s Undo. | Yes for local actions | See K7. |
| D45 | PLAN:2066 S-UF-M1a | One shared notes store. | Yes | FrankerFaceZ and Twitch both keep notes on the user (R: ffz-7tv-mod). |
| D46 | PLAN:2143 S-UF-M6a | One oldest-first queue. | Yes | Mod View, Ozone. |
| D47 | PLAN:1685 S-UF-M5b | Ban in every moderated channel with a count and a reason. | Changed | See C15. |
| D48 | Memory 2026-10-05 (via R: internal-settled) | Own, attacked and offline channels ban at once; only live channels hold. | Changed | See C9. |
| D49 | Memory 2026-10-06 (via R: internal-settled) | Opt-in per channel; a mod may vouch for a streamer who is not onboarded. | Changed | See C10 and Q3. |
| D50 | PLAN:729-733 S-SPAM-NETWORK | Blocked on a signature service. | Yes | It shares spam signatures, never bans, so guard (l) holds. |
| D51 | OWNER-QUESTIONS s.62 (via R) | A failed load never shows an empty list. | Yes | Truthful data. |
| D52 | LEGACY-PARITY-LEDGER:118-121 (via R) | Ban, unban, mod add and remove chat lines ship on. | Yes, with a brake | Kick: repeated attack notices draw more attackers (L6). Add S-MOD-ANNOUNCE-RATELIMIT. |

### 6.2 Contradictions K1-K15 settled

| # | Conflict (via R: internal-settled-2) | Winner | Doc line that must change |
|---|---|---|---|
| K1 | User-flow M5 bans directly in every channel. | Switched-on channels ban at once and a live one waits; switched-off channels get a decision card (the S-MASSBAN-ABUSE-GUARDS correction of 2026-10-10). | DOC/user-flow-behavioral-spec-2026-10-03.md:573-589 (M5) and :1036 (X2 Confirm ladder row). |
| K2 | 2026-06-16: LeadModerator and up bans everywhere at once. | Proposal, not settled: lead moderator and up plus a named entrust, across switched-on channels. | DOC/2026-06-16-moderation.md:20. |
| K3 | Shared-chat ban propagates directly. | A decision card in the partner channel, unless a moderator switched mass bans on there. A partner auto-accept for one live session: Q4. | DOC/2026-06-16-moderation.md:16. |
| K4 | `moderation:nuke` granted by the origin broadcaster only. | Two grants: the origin grant (who may ask) and entrust per target (who is trusted there). | docs/bot-capabilities.md:72-80; SPEC/moderation.md:392. |
| K5 | SuperMod vs LeadModerator. | LeadModerator. | docs/bot-capabilities.md:33; FEATURES.md:49; FEATURES-BY-SIDEBAR.md:138; run S-LEADMOD-APP-RENAME (PLAN:3043). More "SuperMod" text remains in code and app: `IOperatorNetworkBanService.cs:21`, `NetworkNukeService.cs:248`, `ISharedBanService.cs:19-21`, app `ModerationApi.kt:287`, `ModerationController.kt:357` and `:725`, `ModerationScreen.kt:592`, `RolesApi.kt:218`, `ViewerProfileController.kt:297`. Add these to S-LEADMOD-APP-RENAME. Order 45 is text and UI only. |
| K6 | History treats bans as per-channel records only. | S-MASSBAN-HISTORY shows requests, per-channel decisions and overrides. | DOC/owner-punch-list-2026-09-08.md s.12 (`:325`). That section does not say bans are per-channel only. The conflict is this paper's own inference, so read s.12 before you change it. |
| K7 | Ban here: 10 s Undo only. | Keep for local bans. A cross-channel request is undone by withdrawing it, which lifts the restriction. | DOC/user-flow-behavioral-spec-2026-10-03.md:582 (add the withdraw rule). |
| K8 | Reason chips include Spam, Bot, Off-topic. | Local actions keep all chips. The mass-ban picker offers only hate, insults, harm. | DOC/user-flow-behavioral-spec-2026-10-03.md:547 (add the mass-ban rule). |
| K9 | SD0 test asserts zero bans; owner wants auto-ban. | Auto-ban only at high confidence, only for hate raids and "best viewers" spam, never for Trusted and up. The SD0 test stays for medium confidence. Follow bots: auto-ban (Q1). Build: S-SPAM-AUTOBAN, S-FOLLOWBOT-AUTOBAN. | SPEC/spam-defense.md:740-743 (the SD0 test; name the confidence level; `:734-738` is the SD11 test); :756-759 SD9 test after Q1. |
| K10 | Trust is positive-only vs "looks at your ban status on all channels". | A ban elsewhere may raise the watch level (Monitor). It never bans alone. | SPEC/spam-defense.md s.6.1 (`:611`; Semi-Trusted, SD11 text at `:97`). The quote "looks at your ban status on all channels" was NOT FOUND in `spam-defense.md`. It matches the owner's words at PLAN:117-118. |
| K11 | "Approve and trust for this stream" is a third trust idea. | No conflict. Keep three names: trust tier, stream trust, entrust. | None. |
| K12 | Punch list: bans are a text blob. | Half stale. The ban list is now structured (`ModerationController.cs:574-577` returns `List<BannedUserDto>`) and a typed `ModerationHistoryEntry` table exists (`ModerationHistoryEntry.cs:29-97`). But the Community ban list still reads Configuration keys `ban:<userId>` (`CommunityController.cs:584-594`, `:714-716`, `:984-993`). | DOC/owner-punch-list-2026-09-08.md s.3 (change the first half only). |
| K13 | bot-capabilities says network nuke and shared bans are "Specced-only". | Stale (confirmed): the code has them (`ModerationController.cs:1179`, `:1234-1298`, `:1489-1530`); FEATURES.md lists them as built. | docs/bot-capabilities.md:151-156. |
| K14 | M7 spike banner vs automatic lockdown. | One feature: S-MOD-LOCKDOWN owns both the banner and the automatic trigger. | DOC/user-flow-behavioral-spec-2026-10-03.md:613-622 (point to S-MOD-LOCKDOWN). |
| K15 | Dry-run auto-flip and replay: spec target vs "document as shipped". | Open: Q11. | SPEC/spam-defense.md:645-668 after Q11. |

### 6.3 Contradictions C1-C15 settled

C1, C2, C5, C8 and C15 are the same as K9, K2/K4, K10, K5 and K1.

| # | Conflict (via R: internal-settled) | Winner | Doc line that must change |
|---|---|---|---|
| C3 | "Hold" has three meanings. | Mass ban: "waiting for a decision" (restricted). Chat filters: "queue". Spam spec SD12: "cannot hold a message" stays as plain English. | PLAN:60 (S-MASSBAN-HOLD-RELEASE text), PLAN:1174 (S-CHATFILTER-HOLD text), SPEC/moderation.md. |
| C4 | Guard (a) keeps auto-accept; guard (j) drops it. | Neither: the per-channel switch decides. | Done: the S-MASSBAN-ABUSE-GUARDS correction of 2026-10-10 withdraws both. |
| C6 | Trust weights fixed in the spec; editable in the shipped code. Partly wrong. The "fixed" sentence is OWNER-QUESTIONS `:192`; the spam spec (`:597`) says every threshold is an editable setting. In code, `TrustThresholds` and `SemiTrustedWatchHours` are editable (`SpamDefenseSettings.cs:40-46`), but six AccountRisk multipliers are hard-coded (`AccountRisk.cs:128-133`). | Editable with defaults, as every spam knob is. New slice S-MOD-RISK-WEIGHTS (PLAN:344) makes the six multipliers settings. | OWNER-QUESTIONS `:192` (the "fixed" sentence). |
| C7 | Nuke reversal: 24 h in the spec, no limit in the code. | Open: Q6. | PLAN:3260 after Q6. |
| C9 | 10-05: only live channels hold. 10-10: every other channel waits for a decision. | 10-10 (newer). Restriction silences at once, so waiting costs nothing in a live chat. | Memory feedback-massban-bans-happen-live-only-holds.md; `MassBanChannelPlanner.cs:216-224`. The code is the 10-05 rule today, so a code slice is needed for the 10-10 rule (orders 6 and 8). |
| C10 | Three consent models: opt-in (10-06), opt-out (10-10 answer 2), entrust (10-10 guard j). | The moderator's switch per channel (owner answer 2, confirmed 2026-10-10). The broadcaster's refusal wins. Entrust names who holds the power, not where it reaches. | PLAN:57-59 (S-MASSBAN-OPTIN-TOGGLES); PLAN:96-98; memory feedback-massban-opt-in-only.md. |
| C11 | The mass-ban endpoint checks `moderation:ban` (Moderator floor). | The named `moderation:nuke` grant. New slice S-MOD-MASSBAN-FLOOR. | `ModerationController.cs:337` (confirmed: checks `moderation:ban`). `moderation:nuke` is seeded in `ActionDefinitionSeeder.cs:214` and used by the nuke routes (`:1179`, `:1196`, `:1211`). |
| C12 | PLAN says nothing consumes suspicious-user events; handlers exist (`SuspiciousUserHandlers.cs:23`, `:44`). Lockdown is half built. | The code. | PLAN S-SUSPICIOUS-USERS (:790-793; rewrite the gap to the UI and ledger check); PLAN S-SPAM-LOCKDOWN-WIRE (:755-757; automatic trigger only). |
| C13 | Floors per tab in the user hub are not stated. | Each S-USER-HUB tab names its floor. Followers, Subscribers and Donations tiles stay Editor and up. The words "money tiles" are not in PLAN S075 (:2537-2545). | PLAN S-USER-HUB (:77-80; add floors to the done-when). |
| C14 | The spam spec names two open slices that PLAN does not list. | Do NOT add S-SPAM-SEED-CORPUS and S-SPAM-FOLLOWBOT-WIRE to PLAN as new work. Both are BUILT: `SpamCorpusSeeder.cs:31` is an `ISeeder` that runs through `SeedRunner` on every boot (`Program.cs:685-696`); `FollowSpikeHandler` is registered by assembly scan (DI `:241-245`). PLAN has 0 matches for both names. | Fix the spam spec header (`spam-defense.md:26-34`). Neither PLAN nor the slice list needs a new entry. Neither was run on a live box (see the coverage ledger). |

---

## 7. The one plan

Build order runs top to bottom, number 1 first. It follows PRODUCT-ALIGNMENT D8: fix our own tool before adding. "New" marks a slice ID minted here for a real gap; every other ID already exists in PLAN.

### Goal A: prevent misuse of our own tools

| Order | Slice | What it does | Done-when | Rests on |
|---|---|---|---|---|
| 1 | S-MOD-TOOLS-SPEC (PLAN:49-53) | One spec for the moderator-of-many surface, built from this paper. | The spec is committed; every section 8 answer is in it; every gap maps to a slice. | Owner request 2026-10-10 |
| 2 | S-MASSBAN-ABUSE-GUARDS (PLAN:121) | Settle guards (a)-(l) as amended in section 6. | As in PLAN:129-130: a request to a channel that has not accepted bans nobody. | PLAN:104 |
| 3 | S-MOD-MASSBAN-FLOOR (in PLAN; was MASSBAN-GRANT-GATE) | The mass-ban endpoint checks the named `moderation:nuke` grant. | A test: a Moderator without the grant gets refused; with the grant, the request goes out. | Guard (c); C11 |
| 4 | S-MASSBAN-ENTRUST (new) | A streamer entrusts or withdraws a named mod; only a holder of the power starts a batch. | Entrust, reload, read back from the server. A mod without the power cannot start a batch. | Owner "entrust" quote |
| 5 | S-MASSBAN-OPTIN-TOGGLES (PLAN:58-60, changed) | One switch per moderated channel, off at the start, saved on the click; the refusal wins; each channel shows its mark. | Reload reads back; a switched-off channel is never banned; the refusal wins; the marks show. | Owner answer 2; C10 |
| 6 | S-SPAM-AUTOBAN (new) | A high-confidence hate-raid line and a confirmed campaign line ban an account below established standing. Shields and dry run stay. | A fresh account's lines each end in a Twitch ban; an established viewer bans nobody. | Owner auto-ban quote |
| 7 | S-MASSBAN-SKIP-BANNED (PLAN:65) | Skip a channel where the account is already banned. | As in PLAN:65-66. | Incident fault 2 |
| 8 | S-MOD-DECISION-CARD (in PLAN; was MASSBAN-DECISION-CARD) | The card in each channel: requester, ground, origin channel, last N messages, "Ban here too" (primary) and "Reject". Shown in the dashboard inbox; `!allow massban` is its chat form. | E2E on the dev box: a request appears live on channel B's card; "Ban here too" bans on Twitch; the history row shows who decided. | Guard (k); thecodedrift (PLAN:157-162) |
| 9 | S-MASSBAN-HOLD-RELEASE (PLAN:60, folded) | Folded into order 8: a channel without our bot decides on the dashboard card. | Order 8 passes for a channel where the bot is not in chat. | Incident fault 1 |
| 10 | S-MASSBAN-SCREEN (PLAN:54, changed) | Username, ground (hate, insults, harm only), evidence, the channel preview, then one primary "Ban here and ask N channels". | As in PLAN:54-55, with the counts split into banned, restricted and waiting. | Guard (g); K8 |
| 11 | S-MASSBAN-RETRY (PLAN:748) | Retry a failed ban with backoff and show the result. | As in PLAN:749-750. | Dyno lesson L11 |

### Goal B: oversight and audit

| Order | Slice | What it does | Done-when | Rests on |
|---|---|---|---|---|
| 12 | S-MASSBAN-HISTORY (PLAN:69, wider) | Batches, per-channel decisions, who decided, overrides, reversal. | As in PLAN:68-69, plus each decision row names the deciding mod. | K6 |
| 13 | S-MASSBAN-REQUESTER-RECORD (new) | Counts rejected requests and undone batches per requester. The streamer sees the count and takes the power back by hand. | A test: an undone batch raises the count on the entrust screen. | Q5 withdrawn |
| 14 | S-FOLLOWBOT-AUTOBAN (new) | A proven follow-bot account is banned with the reason "follow bot". Chatters and accounts with standing are left out. | A fresh wave is banned, a chatter is spared, undo lifts one ban. | Q1 |
| 15 | S-MOD-WRONG-ACTION-REPORT (new) | A "this was wrong" button on every automatic action. It undoes the action and logs a false positive for tuning. | Pressing it on a test auto-timeout lifts the timeout on Twitch and adds a row to the false-positive log. | L15; owner rule PLAN:104 |
| 16 | S-SAFETY-MONITOR (PLAN:740) with S-MOD-PROTECTION-STATUS (PLAN:72) | One live protection-status card. | As in PLAN:741-744 and PLAN:72-73. | Goal 2026-10-08 |
| 17 | S-NETBLOCK-LEGS (PLAN:753), S-NETBLOCK-REACH (PLAN:758) | Store failed legs, back off retries, never lift a ban the streamer placed. | As in PLAN:753-761. | Owner rule PLAN:104 |

### Goal C: protect the stream live

| Order | Slice | What it does | Done-when | Rests on |
|---|---|---|---|---|
| 18 | S-SPAM-LOCKDOWN-WIRE (PLAN:755-757) | A raid or burst triggers lockdown, then restores. | As in PLAN:735-736. | Finding 7 |
| 19 | S-MOD-LOCKDOWN (PLAN:75, wider) | Manual start and end with a timer. Owns the M7 spike banner (S-UF-M7a, PLAN:2702) and turns Shield Mode on. | As in PLAN:74-75, plus the banner starts the same lockdown. | K14 |
| 20 | S-RAID-VET (PLAN:783-789) | Raider card and a hold-in-lockdown setting for unknown or banned raiders. | As in PLAN:765-768. | Finding 7 |
| 21 | S-SPAM-CONTEXT-TRUST (PLAN:169) | Trust and context weigh every auto-ban. | As in PLAN:170-172. | PLAN:118-119 |
| 22 | S-SPAM-FOLLOWBOT-WIRE (BUILT; named in the spam spec header only) | The follow-bot track is wired: `FollowSpikeHandler.cs:26` to `FollowBotSweepService.cs:92`, `:102`, `:122`; routes `SpamDefenseController.cs:161`, `:189`; app `SpamDefenseApi.kt:46-96`. No build work. Open: prove it on a live box, and build the ban step (S-FOLLOWBOT-AUTOBAN, Q1 settled). Fix the stale spec header. | A follow spike test bans the proven bots, spares a chatter, and a batch undo works. Run it live once. | SD9; Q1; C14 |
| 23 | S-SPAM-SEED-CORPUS (BUILT; named in the spam spec header only) | The spam signature store is seeded on every boot: `SpamCorpusSeeder.cs:31` is an `ISeeder` run by `SeedRunner` (`Program.cs:685-696`). No build work. Fix the stale spec header (`spam-defense.md:26-34`). | A live-box check shows the seeded signatures after a fresh boot. | C14 |
| 24 | S-MOD-ANNOUNCE-RATELIMIT (new) | Rate-limit the bot's ban and attack notices in chat during a raid. | A test raid of 50 bans posts at most the set number of notices. | L6 |
| 25 | S-MOD-ZERO-MOD-PRESET (new) | A safe default for a channel with no mods: stricter filters, auto-lockdown on, and a nudge to add a mod. | A channel with no mods shows the preset on first setup and the protection card names it. | Shortcoming 9 |
| 26 | S-SAFETY-ROOM-MODES (PLAN:758-760) | Lockdown room controls on YouTube and Kick. | As in PLAN:738-739. | PRODUCT-ALIGNMENT D1 |

### Goal D: help moderators decide

| Order | Slice | What it does | Done-when | Rests on |
|---|---|---|---|---|
| 27 | S-SUSPICIOUS-USERS (PLAN:790-793, rewritten; absorbs the old suspicious-user ledger idea) | Show restricted and monitored users in chat, the card and the queue. Keep our own list from events, since Twitch has no list endpoint. | As in PLAN:770-772, plus: a user restricted in the Twitch UI appears in our list, and the list survives a restart. | Finding 3; C12 |
| 28 | S-UF-M1a (PLAN:2087) | One shared notes store. | As in PLAN. | D45 |
| 29 | S-UF-M6a (PLAN:2164) | One oldest-first queue. | As in PLAN. | D46 |
| 30 | S-MOD-PIPELINE-ACTIONS (PLAN:1203) | `warn`, filter-hit and heat actions in the pipeline. | As in PLAN. | Warn first (section 5.2 row 11) |
| 31 | S-MOD-EVIDENCE-PACK (new) | One-click evidence for Twitch's own report flow: messages, times, channel. Viewer report triage is built; the evidence pack itself was not found in code. | A mod opens the pack from a user card and it holds the last messages with times. | D07 |
| 32 | S-MOD-MOD-LOAD (in PLAN; was MOD-TEAM-HANDOFF) | A short shift note mods leave for the next mod. | A note left by one mod shows on the next mod's home tab. | R: twitch-shortcomings-2 (mods coordinate off-tool) |

### Goal E: protect viewers

| Order | Slice | What it does | Done-when | Rests on |
|---|---|---|---|---|
| 33 | S-FILTER-LINKPOLICY (PLAN:766, wider) | Apply the link policy. Add short-link and lookalike-domain checks, "new account plus link", and a VIP option, since Twitch exempts VIPs. | As in PLAN:746-747, plus a test: a link from a VIP account with the VIP option on is queued. | Shortcoming 4 |
| 34 | S-MOD-FILTER-TEST (PLAN:84) | Test a filter before saving; refuse an invalid regex with a clear error. | As in PLAN:83-84, plus an invalid regex cannot be saved. | L9 |
| 35 | S-SAFETY-FAKE-NAME (new) | Flag a new chatter whose name copies the streamer, a mod or "support" words. It flags, never bans. | A test: a fresh account named like the streamer is flagged to mods; a regular with a similar name is not. | Shortcoming 5; L5 |
| 36 | S-SAFETY-DOXX (new) | Queue a message that looks like an address or phone number for a mod. It never auto-bans. | A test message with a phone number is queued; a stream schedule time is not. | Shortcoming 6 |
| 37 | S-CHATFILTER-HOLD (PLAN:1195) | Queue = delete plus a queue entry; flag = queue entry only. Rename "hold" to "queue" in the text. | As in PLAN. | C3 |
| 38 | S-VIEWER-RESTRICTION-D1 (PLAN:2459) | Bot-side restriction per person, not per platform account. | As in PLAN. | D39 |

### Goal F: one user hub, live

| Order | Slice | What it does | Done-when | Rests on |
|---|---|---|---|---|
| 39 | S-USER-HUB (PLAN:77-80) | One users area with tabs; each tab names its floor. | As in PLAN:78-80, plus a floor per tab. | Owner answer 3; C13 |
| 40 | S-LIVE-EVERYWHERE (PLAN:174) | Every page updates live. | As in PLAN:173-175. | Owner answer 3 |
| 41 | S-MOD-BANLIST (PLAN:70) | Ban list with search and unban. | As in PLAN:70-71. | Incident fault 3 |
| 42 | S-MOD-NUKE-SCREEN (PLAN:82) | Nuke from the dashboard with rollback. | As in PLAN:81-82. | Section 5.2 rows 1-2 |
| 43 | S-MOD-TWITCH-ONLY-SETTINGS (new) | One card that lists every Twitch setting our bot cannot set, each with a link and a one-line why. | The card lists all settings in section 3.2 and each link opens the right Twitch page. | Finding 6 |
| 44 | S-MOD-PASSTHROUGH-TESTS (in PLAN; was MOD-AUTOMOD-MAP) | The AutoMod screen writes the full set every time and shows Twitch's 5 categories mapped to the 8 fields. | A test: changing one category keeps every other field. | Section 3.3 |
| 45 | S-LEADMOD-APP-RENAME (PLAN:3043) | Rename SuperMod to the LeadModerator display name. | As in PLAN; needs Q8. | K5 |

Kept blocked or changed outside the order:

- S-SPAM-NETWORK (PLAN:729): stays blocked. It shares signatures, never bans.
- S-SHAREDBAN-FEDERATE-OUT (PLAN:772-773): changed. A published ban arrives in a subscriber channel as a request (restrict plus decision card), never as a ban.
- S-UF-M5b (PLAN:1685): replaced by orders 6, 8 and 10. Delete it with `scripts/close-slice.ps1` once order 10 ships.

### Slices added by sections 11 to 14

| Slice | What it does | Done-when | Rests on |
|---|---|---|---|
| S-MOD-OUTBOUND-FILTER | One outbound filter at ChatPlatformRouter and the TTS gate for every bot output. Normalises leet and spaced spelling. Implements BlockExplicit. | Tests send sh1t, s h i t and s.h.i.t through the filter and TTS gate and assert a mask. A command reply with bad args is masked in the sent line. An explicit track is refused with BlockExplicit on. | 11.2 findings 1, 3, 6, 9 |
| S-MOD-ECHO-AFTER-FILTER | Command handler reads the chat-filter result. Order is explicit. The filter publishes ChatMessageDeletedEvent. | A test with both handlers registered asserts no echo after a filter delete. A second test asserts the deleted event is published and the overlay drops the line. | 11.2 findings 1, 5 |
| S-MOD-ALERT-NAME-HOLD | Alerts pass a name check. Hits and spike arrivals go to a hold-for-approval queue. Deny masks the name. | A held follow alert shows no name on the overlay. A deny shows a masked name. A spike holds all alerts. | 11.2 finding 2 |
| S-MOD-FILTER-WORDLIST | Default slur and hate list. Custom wildcard words per channel. Actions replace, hide, disable. Strength setting. | Each action changes the output as named. A custom wildcard word is masked. The default list holds slurs. | 11.2 finding 3 |
| S-MOD-OVERLAY-CHAT-GATE | The overlay chat feed waits for the filter result. | A deleted line produces no ChatMessage on the overlay feed or the widget route. | 11.2 finding 5 |
| S-MOD-LOCKDOWN-OUTPUT | Lockdown also stops alerts, TTS and overlay chat. | With lockdown on, a follow event shows no alert and a TTS request is refused. With lockdown off, both work. | 11.2 finding 4 |
| S-MOD-LOCKDOWN-MULTI | Lockdown covers Kick and YouTube where the API allows. States plainly what cannot be locked. | Per platform, the adapter calls the expected controls. An unsupported control shows a "cannot lock" state. | 11.2 finding 4 |
| S-MOD-TEMPLATE-ESCAPE | A viewer value is not run as a transform. An unknown transform prints no chat error. | A viewer value with a transform stays literal. An unknown name gives no error text and no Error log. | 11.2 finding 7 |
| S-MOD-TTS-STANDING | Event-response TTS passes real standing or skips the floor on purpose. ReplyTtsPipeline seeds real bits and role. | With a floor set, a mod reply is spoken, a viewer under the floor is refused, a follow response follows the documented rule. | 11.2 finding 8 |
| S-MOD-BAN-RATE-STEPUP | Rolling ban budget per operator and channel (lower for Mod than Lead); over budget holds the batch and asks the streamer; step-up proof for mass ban, nuke and network ban; per-channel switch; new-device notice. | Fake Twitch client tests: a second 1000-target batch in one window is held and no ban is sent; big writes with no fresh proof are refused; a new-device login writes a notice. | Findings 1, 2; Security Bot; Discord mfa_level |
| S-MOD-MASSBAN-FLOOR | Own action key for mass ban, above plain Mod, not delegable. | A plain Mod is refused; a Lead is allowed; the seeded row is Critical and not delegable. | Finding 3 |
| S-MOD-NUKE-CAPS-AUDIT | Server-issued nuke confirmation; count cap; cooling-off; cap, pause, audit row per leg and event for all_moderated. | Client flag with no server token is refused; over-cap is refused; all_moderated writes one audit row per leg and one event. | Findings 4, 7 |
| S-MOD-ATTACKED-PROOF | Server decides "attacked" from its own evidence, not from the request body. | A body-declared attacked live channel keeps the live hold; with evidence the hold is skipped and the reason stored. | Finding 4 |
| S-MOD-IMPERSONATION-LIMITS | Act-as tokens cannot start mass ban, nuke, network ban or mod changes without the streamer's yes. | Each of the four actions is refused or held with an act-as token; a normal token still works. | Finding 5 |
| S-MOD-NOTICE-ALL-PATHS | Notice to the streamer on every path: attacked, own, offline, nuke, network ban, impersonation start. | Each path writes a notice with actor, count and time; a path with no notice fails the test. | Finding 6; Wick |
| S-MOD-ACTOR-AUDIT | Every leg stores the requesting person and the act-as operator, also on the streamer's token. | Mod stored as requester on a streamer-token batch; act-as ban stores target and operator; the mod log shows both. | Finding 7; Twitch Ban User rule |
| S-MOD-REVOKE-SESSIONS | Admin action revokes every session and API key of user X; the network block calls it. | Old access and refresh tokens are refused; the user's keys fail auth; an audit row names the admin. | Finding 8 |
| S-MOD-APIKEY-LIMITS | Maximum key life; alert on strange use; ban budget on pipeline ban steps run by a key. | A key with no expiry gets the maximum; an expired key is refused; a key-run ban pipeline stops at the budget. | Finding 9 |
| S-MOD-OPTIN-VISIBILITY | Opt-in records who and when; the streamer lists all batches made in their channels. | Opt-in stores actor and time; the streamer's list shows a mod's batch; a mod's list shows no other channel's batch. | Finding 10 |
| S-MOD-OWNER-WATCH | Notice to owner and co-owners when the mod list changes; survive a channel that goes dark and returns. | A fake mod-added event writes a notice; a dead token marks needs_reauth and asks to reconnect; a new token restores the channel. | Finding 11; Twitch help page |
| S-MOD-YOUTUBE-GAPS | Translate YouTube userBannedEvent and tombstone. Add liveChatModerators list, insert, delete. Add a quota budget | Translator test per item type asserts a canonical ban event and a mod-log row. Fake-HTTP test asserts POST and DELETE to liveChat/moderators. Quota test: at 9,900 used a ban is refused and a poll still runs | youtube.manage scope (OAuthProviderRegistry.cs:225-236), S-MOD-OWN-ACTION-LOG |
| S-MOD-EVENTSUB-CONSUMERS | Consumer for the five topics and the other channel.moderate actions. Warning push. Shield mode in the mod log | Per topic: publish the event, assert a mod-log row and a dashboard push with the payload fields. A chat-mode change made outside our bot changes the lockdown view | Event journal, translators in Platform/Eventing/Translators |
| S-MOD-RATE-HEADERS | Pace mass ban from Ratelimit headers and 429 hints. Per-broadcaster counter. Kick 429 retry | Fake transport with Ratelimit-Remaining 0 stops Ban User calls for that broadcaster until reset. Two broadcasters have separate counters. A Kick 429 is retried | TwitchHelixTransport.cs:341 and :384 |
| S-MOD-LOCKDOWN-MULTI | Lockdown on Kick and YouTube where the API allows. Per-platform result. X Live returns not available | Three-platform lockdown test asserts Twitch chat-settings call, a stated result for Kick and YouTube, and X Live not available with a reason | S-MOD-CAPABILITY-FLAGS, S-MOD-PLATFORM-INBOUND-FILTER |
| S-MOD-ROUTER-UNBAN | Add Unban (and a moderator-add contract) to the unified router | Unban test on Twitch, Kick, YouTube asserts the platform call per origin. YouTube unban of an outside ban returns a plain error | IInboundOriginModerator.cs, YouTubeLiveChatBanLedger |
| S-MOD-CAPABILITY-FLAGS | Capability table per platform. Dashboard shows not available with reason. Manual mod instruction for Kick and YouTube. X docs release check | Kick capability response marks chat modes, blocked terms, hold queue unavailable with a reason and the dashboard shows no switch. X Live all unavailable | IInboundOriginModerator.cs, S-MOD-ROUTER-UNBAN |
| S-MOD-OWN-ACTION-LOG | Write the mod-log row when our bot acts on Kick and YouTube | Bot ban on Kick writes one row. Bot unban closes it. The later moderation.banned webhook adds no second row | KickWebhookIngest.cs:595, S-MOD-ROUTER-UNBAN |
| S-MOD-PLATFORM-INBOUND-FILTER | Bot-side blocked terms and hold logic for Kick and YouTube chat | Kick blocked-term message is deleted through the delete endpoint and logged. YouTube with low quota degrades to a logged state and does not delete | S-MOD-YOUTUBE-GAPS, S-MOD-OWN-ACTION-LOG |
| S-MOD-CROSS-PLATFORM-BAN | Cross-platform ban names only proven ids. Blast radius per platform. Proof required for merge callers | Proven Twitch and Kick links ban both ids. An unlinked same-name chatter is not banned. Merge without proof is refused | IExternalLoginService.cs:13-22, UserIdentityService.cs:320 |
| S-MOD-PROJECTION-PROVIDER | Projection rebuild uses the stored provider | Rebuild from stored Kick and YouTube actions resolves each subject by its own provider and writes one history row per action | ModerationProjectionService.cs:253 and :296 |
| S-MOD-DOX-TERMS | Private, encrypted store for the streamer's address and names. Scan chat with spacing and leet normalisation. Delete and alert without the matched text. | Fake term "123 Example Street": three spelling variants each give one Delete Chat Messages call and one private alert. Alert, log and dashboard payload hold no part of the term. Stored column differs from clear text. "Hello chat" triggers nothing. | S-MOD-OUTBOUND-FILTER |
| S-MOD-OTHER-HARMS | !support command with the Crisis Prevention link. IRL help page. Parents guide link and mod report card naming "Child Endangerment". | !support posts saved text plus the Crisis Prevention URL only. IRL page states Twitch cannot call police. Report card gives the Twitch report link and reason key to a mod and 403 to a plain viewer. | S-MOD-DECISION-CARD |
| S-MOD-PHRASE-ALERT | Optional crisis and minor-opener phrase lists. Private alert with message link. | Fixture crisis message and fixture opener each emit one private alert with a link. Zero ban, timeout, delete-user and DM calls. Joke fixture gives only an alert. Setting off gives no alert. | S-MOD-OUTBOUND-FILTER, S-MOD-DECISION-CARD |
| S-MOD-CLIP-WATCH | Poll Get Clips with a started_at cursor. Flag clips from a user just timed out or from a new account. State that delete is dashboard-only. | Fake Get Clips with 3 clips, one by a user timed out 5 minutes ago: one flag row with creator, title, URL. Second poll adds no row. Zero Ban User calls. Payload carries the "delete in Twitch" text. | none |
| S-MOD-TEXT-GATE | Check redemption text, poll and prediction text before use. Cancel with refund and a visible reason for bot-owned rewards. | Blocked-term input gives one CANCELED call, one saved reason, one viewer message and no TTS or overlay text. Clean input unchanged. Another app's reward gets no status call. Poll title with a blocked term returns 400 with zero Helix calls. Length limits 60, 45 and 25 enforced. | S-MOD-OUTBOUND-FILTER, S-MOD-TEMPLATE-ESCAPE |
| S-MOD-MOD-LOAD | Coverage view, cover ping, event heads-up, optional roster, private per-mod workload. | 3-mod fake log: hours with one mod alone are marked. Ping reaches other mods only. Heads-up arrives before the event time. Per-mod view only for that mod. Streamer sees totals. No ranking endpoint. | S-MOD-EVENTSUB-CONSUMERS |

### Code state of each order (read in a later pass)

"Open" means no code was found. "Built" means the code exists. A test run is not claimed unless the row says so.

| Order | State | Evidence |
|---|---|---|
| 3 | open | `ModerationController.cs:337` checks `moderation:ban` |
| 4 | open | No "entrust" in the server code |
| 5 | partly built | The old opt-in model is built. The toggles screen is open |
| 6 | open | The app has no mass-ban client (benchmark row 4) |
| 7 | built | `MassBanExecutor.cs:153`, `:223-224`: `IsAlreadyBanned` counts as success |
| 8, 9, 10 | open | No mass-ban code in the app |
| 11 | built on the server | `MassBanRetryPolicy.cs:25` (MaxAttempts 5); migration `20261008051818_AddMassBanTargetRetry`. The app has no result display |
| 12 | endpoint built, UI open | See section 5.2 rows 1-2 |
| 13, 14 | open | No requester record; a follow-bot wave ends in blocks, never a ban |
| 15 | partial | Overturn route for spam detections only (`SpamDefenseController.cs:211-213`). Slice S-MOD-WRONG-ACTION-REPORT |
| 16 | backend built, app card not found | `ModerationController.cs:635-640` GetProtectionStatus |
| 18 | open | No automatic lockdown caller |
| 19 | backend manual built, screen open | `SpamDefenseController.cs:236`, `:260`, `:289` |
| 20 | open | S-RAID-VET |
| 21 | partly built | The rule is built and unit-tested (`SpamEnforcement.cs:98-129`; `SpamEnforcementTests.cs:33`; `SpamEnforcementExecutorTests.cs:198`). Missing: an end-to-end test with the exact message "i am tired of those best viewers spam" as an Established viewer through `SpamDefenseService`, and the same phrase from a fresh campaign account that still bans. New slice S-MOD-PASSTHROUGH-TESTS (PLAN:367) |
| 22, 23 | built | See the order 22 and 23 rows above. Their "new to PLAN" text was stale |
| 24, 25, 26 | open | |
| 27 | partial | Ledger, routes and app notice exist (section 5.2 row 25). Open: the Twitch-UI case and the restart case |
| 28 | server merged | Section 5.2 row 22 |
| 29 | open | S-MOD-QUEUE-ONE-LIST (PLAN:2367 extension) adds one oldest-first list that includes TTS and media items. S-UF-M6a stays |
| 30 | open | No pipeline `warn` action |
| 31 | partial | Viewer report triage is built. The evidence pack was not found |
| 32 | open (confirmed) | Only per-viewer notes exist. No channel-level note entity, endpoint or widget |
| 33 | policy applied, widening open | `ChatFilterExecutionHandler.cs:432-433`. Row 10 of section 5.2 adds the link pass |
| 34 | server built, app open | Section 5.2 row 8 |
| 35, 36 | open | Section 5.2 rows 36, 37 |
| 37 | partly built | The enum already has Hold and Flag. The rename and semantics are open |
| 38 | open | `ChannelModerationStanding.cs:31`, `:35` |
| 39 | open (confirmed) | `ShellNav.kt` keeps Moderation (`:146-152`), Community (`:172`) and Roles (`:196`) apart. No spec with tab floors was found |
| 40 | partly built | 22 of 70 controllers subscribe (`ConfigChangeRefresh.kt:21-33`). Community, ViewerProfile, Roles, Alerts, Analytics, Discord, Giveaways, EventResponses, Integrations, TtsQueue and settings do not. No page-to-hub-event script. The other 48 were not each traced (see the coverage ledger) |
| 41 | mostly built | `BansList` at `ModerationScreen.kt:834` |
| 42 | screen exists | It is the user-ban nuke. The phrase nuke is missing (section 5.2 row 1) |
| 43 | open (confirmed) | No card, no strings |
| 44 | partly built | The app writes the full set (`TrustAutomationSection.kt:776-789`, `:863-880`; `ModerationApi.kt:1505-1545`). The server still accepts a partial body (`ModerationController.cs:1588-1640`; `TwitchModerationApi.cs:1177-1213`). The 5-category map is not in the UI (titles are the 8 fields, `:756-765`). The named test is missing (`ModerationControllerTest.kt:2382`, `:2409` only prove that categories never carry overall). Slice S-MOD-PASSTHROUGH-TESTS |
| 45 | open | Text and UI only; more SuperMod sites listed at K5 |

New slices named in this pass, all in PLAN: S-MOD-DECISION-CARD (:298, for order 8 and section 5.2 row 5), S-MOD-FILTER-KINDS (:307, row 13), S-MOD-RISK-WEIGHTS (:344, C6), S-MOD-PASSTHROUGH-TESTS (:367, orders 21, 44 and 40).

### Doc lines to change once the owner settles section 8

| Doc | Line | Change |
|---|---|---|
| DOC/user-flow-behavioral-spec-2026-10-03.md | 547, 573-589, 582, 613-622, 1036 | K1, K7, K8, K14 |
| DOC/2026-06-16-moderation.md | 16, 17, 20 | K2, K3, D05 |
| docs/bot-capabilities.md | 33, 72-80, 151-156 | K4, K5, K13 |
| SPEC/moderation.md | 392-411, ~425-445 | C2, D05 |
| SPEC/spam-defense.md | 26-34 (header), 645-668, 740-743, 756-759, s.6.1 | C14, K9, K10, K15 |
| PLAN | 57-60, 95-98, 123-125, 727, 734-736, 751, 769-772, 1174, 1685 | C3, C4, C10, C12, C14, C15 |
| Memory | feedback-massban-bans-happen-live-only-holds.md, feedback-massban-opt-in-only.md | C9, C10 |
| Code | `ModerationController.cs:337`, `MassBanChannelPlanner.cs:216-224` | C11, C9 |

---

## 8. Open questions only the owner can answer

| # | Question | Recommendation |
|---|---|---|
| Q1 | Follow bots: auto-ban (your words, PLAN:116-117) or block only (SD9)? | Settled 2026-10-10: auto-ban (S-FOLLOWBOT-AUTOBAN), chatters left out. Risk kept in view: Kick warns that mass-banning followers puts the account at risk (L4). |
| Q2 | Who in a target channel may press "Ban here too"? LeadModerator and up (your answer 1), or any mod? | LeadModerator and up, plus the streamer. |
| Q3 | A channel without our bot: may a mod still vouch for the streamer (your 10-06 words), or must the streamer entrust with their own click? | Settled 2026-10-10: the mod's own judgement per channel, with the streamer's word for a channel without our bot. The streamer can refuse. |
| Q4 | In a live Shared Chat session, may a partner streamer choose "accept this session's bans at once"? | Yes, as the partner's own choice, for that session only. |
| Q5 | How many reversed bans revoke a mass-ban grant, and over what time? | Withdrawn 2026-10-10: nothing revokes the power on its own; the count is shown (S-MASSBAN-REQUESTER-RECORD). |
| Q6 | Nuke reversal: 24 hours (spec) or no limit (code)? | No limit. A wrong ban should always be undoable. |
| Q7 | A request nobody decides: how long does the restriction stay? | Open only if the restriction proposal in S-MASSBAN-ABUSE-GUARDS settles. Then a default of 7 days, after which it lifts and the requester is told. |
| Q8 | The app display name for LeadModerator? | "Lead Moderator", the Twitch name (R: twitch-native-help). |
| Q9 | May a ban in other channels raise a viewer to Monitor here? | Yes, Monitor only, never a ban. |
| Q10 | Your idea "this user is marked" in chat (PLAN:115-116): post it publicly? | Mods only. A public mark is the list harm (L2) and draws attackers (L6). |
| Q11 | Spam dry run: build the 7-day auto-flip and replay, or document what shipped? | Build the replay first. |

---

## 9. Sources

### 9.1 Repository docs and code

- `.claude/docs/design/SHORTCOMINGS-EXECUTION-PLAN.md` lines 24-176, 727-772, 1174, 1182, 1685, 2066, 2143, 2438, 2702, 3022 (read this run).
- Via research notes (not opened by this writer): `.claude/docs/design/PRODUCT-ALIGNMENT.md`; DOC/2026-06-16-moderation.md; DOC/user-flow-behavioral-spec-2026-10-03.md; DOC/OWNER-QUESTIONS-2026-09-30.md; DOC/owner-punch-list-2026-09-08.md; DOC/LEGACY-PARITY-LEDGER; SPEC/moderation.md; SPEC/spam-defense.md; docs/bot-capabilities.md; FEATURES.md; FEATURES-BY-SIDEBAR.md; `.claude/docs/design/benchmarks/moderation-tools.md`; code `ModerationController.cs`, `MassBanChannelPlanner.cs`, `MassBanExecutor.cs`, `SuspiciousUserHandlers.cs`; memory files feedback-massban-bans-happen-live-only-holds.md, feedback-massban-opt-in-only.md, goal-streamer-safety-2026-10-08.md.
- Research notes (session scratchpad `research/`): twitch-native, twitch-native-help, twitch-native-help-2, twitch-shortcomings, twitch-shortcomings-2, twitch-shortcomings-3, third-party, third-party-2, third-party-3, ffz-7tv-mod, bluesky-lists, internal-settled, internal-settled-2.

### 9.2 Twitch

- https://dev.twitch.tv/docs/api/reference/ (anchors: add-blocked-term, add-channel-moderator, add-suspicious-status-to-chat-user, ban-user, block-user, check-automod-status, delete-chat-messages, get-automod-settings, get-pinned-chat-message, get-shared-chat-session, resolve-unban-requests, start-a-raid, update-automod-settings, update-chat-settings, update-shield-mode-status, warn-chat-user)
- https://dev.twitch.tv/docs/eventsub/eventsub-subscription-types/ (anchors: automodmessagehold, channelmoderate-v2, channelsuspicious_usermessage)
- https://dev.twitch.tv/docs/change-log/
- https://dev.twitch.tv/docs/chat/moderation/
- https://help.twitch.tv/s/article/how-to-use-automod
- https://help.twitch.tv/s/article/how-to-manage-harassment-in-chat
- https://help.twitch.tv/s/article/suspicious-user-controls
- https://help.twitch.tv/s/article/chat-verification-settings
- https://help.twitch.tv/s/article/mod-view
- https://help.twitch.tv/s/article/Managing-Roles-for-your-Channel
- https://help.twitch.tv/s/article/setting-up-moderation-for-your-twitch-channel
- https://help.twitch.tv/s/article/how-to-use-raids
- https://help.twitch.tv/s/article/how-to-use-whispers
- https://help.twitch.tv/s/article/chat-commands
- https://help.twitch.tv/s/article/how-to-file-a-user-report
- https://help.twitch.tv/s/article/about-bans-blocking-and-suspensions
- https://help.twitch.tv/s/article/about-account-suspensions-dmca-suspensions-and-chat-bans
- https://appeals.twitch.tv
- https://safety.twitch.tv/s/article/Modernizing-Our-Approach-to-Enforcements
- https://safety.twitch.tv/s/article/H2-2025-Transparency-Report
- https://safety.twitch.tv/sfc/servlet.shepherd/document/download/069at00000MeUUBAA3?operationContext=S1
- https://safety.twitch.tv/s/article/Combating-Targeted-Attacks
- https://safety.twitch.tv/s/article/Community-Guidelines
- http://discuss.dev.twitch.com/t/whisper-bot-to-warn-people-about-phishing-streams-whispering-all-viewers/18995

### 9.3 Research and press on Twitch

- https://arxiv.org/pdf/2301.03946 (Han et al., CSCW 2023, hate raids)
- https://ar5iv.labs.arxiv.org/html/2203.13292 (moderators' practice)
- https://www.amazon.science/publications/who-moderates-on-twitch-and-what-do-they-do-quantifying-practices-in-community-moderation-on-twitch (Seering and Kairam, GROUP 2023)
- https://doi.org/10.1145/3452918.3458796 (Cai, Wohn, Almoqbel, IMX 2021)
- https://digitalcommons.njit.edu/fac_pubs/7199
- https://par.nsf.gov/biblio/10259948
- https://thepinknews.com/2021/08/12/twitch-twitchdobetter-hate-raids/
- https://tubefilter.com/2021/09/10/twitch-hate-raid-users-lawsuit/
- https://techcrunch.com/2021/11/30/twitch-flag-banned-users-from-channel
- https://kotaku.com/twitchs-first-transparency-report-is-a-start-but-strea-1846390202
- https://www.inverse.com/input/gaming/twitch-releases-first-ever-transparency-report-without-much-transparency
- https://www.engadget.com/entertainment/streaming/twitch-will-do-a-better-job-of-telling-rulebreakers-why-their-accounts-were-suspended-191502111.html
- https://www.tubefilter.com/2025/08/27/twitch-viewbot-crackdown-zach-bussey/
- https://kotaku.com/scammers-are-impersonating-popular-twitch-streamers-1840326738
- https://kotaku.com/thousands-of-twitch-scam-channels-are-still-trying-to-s-1842219702
- https://www.dexerto.com/csgo/twitch-scammers-impersonating-s1mple-csgo-ban-1296185
- https://trendlife.com/en-us/blog/2021/11/18/4-popular-scams-targeting-gamers-live-streamers-and-viewers
- https://trendlife.com/en-us/blog/2022/07/21/twitchgive-com-scam-or-legit
- https://www.pcworld.com/article/435208/new-malware-spreads-over-twitch-chat-targets-steam-accounts.html
- https://itpro.com/security/23117/steam-accounts-emptied-by-twitch-malware-scam
- https://www.infosecurity-magazine.com/news/twitch-botnetsforhire-inflate/

### 9.4 Other tools and platforms

- Fossabot: https://fossabot.com/ ; https://fossabot.com/docs/nukes/index.md ; https://fossabot.com/docs/nukes/usage.md ; https://fossabot.com/docs/lookalikes/index.md ; https://fossabot.com/docs/keywords/index.md ; https://fossabot.com/docs/keywords/flags.md ; https://fossabot.com/docs/keywords/phrase-groups.md ; https://fossabot.com/docs/keywords/autoreason.md ; https://fossabot.com/docs/commands/default/permit.md ; https://fossabot.com/docs/commands/default/vanish.md ; https://fossabot.com/docs/commands/default/votekick.md
- StreamElements: https://docs.streamelements.com/chatbot/commands/default/nuke ; https://docs.streamelements.com/chatbot/commands/default/nukeusername ; https://docs.streamelements.com/chatbot/filters ; https://docs.streamelements.com/chatbot/filters/language ; https://docs.streamelements.com/chatbot/filters/links ; https://docs.streamelements.com/chatbot/filters/one-man-spam ; https://docs.streamelements.com/chatbot/filters/zalgo ; https://docs.streamelements.com/chatbot/modules/votekick
- Nightbot: https://docs.nightbot.tv/spam-protection/blacklist ; https://docs.nightbot.tv/spam-protection/caps ; https://docs.nightbot.tv/spam-protection/links
- Moobot: https://moo.bot/ ; https://moo.bot/docs/banning-blocking-and-flagging-large-numbers-of-twitch-users ; https://moo.bot/docs/giving-your-twitch-mods-access-to-your-bot ; https://moo.bot/docs/twitch-chat-auto-mod-bot ; https://moo.bot/docs/twitch-chat-auto-mod-bot-message-filters ; https://moo.bot/docs/twitch-chat-remove-words-phrases-and-behavior ; https://moo.bot/docs/twitch-chat-restrict-what-can-be-posted
- Sery_Bot: https://docs.sery.bot/ ; https://docs.sery.bot/understanding-protection/ ; https://docs.sery.bot/commands/mod-tools/ ; https://docs.sery.bot/commands/raids/ ; https://docs.sery.bot/commands/spam/
- Streamer.bot: https://docs.streamer.bot/api/sub-actions ; https://docs.streamer.bot/api/sub-actions/core/twitch/moderation
- Firebot: https://firebot.app/ ; https://docs.firebot.app/
- CommanderRoot: https://github.com/CommanderRoot ; https://twitch-tools.rootonline.de/
- Chatterino: https://chatterino.com/ ; https://wiki.chatterino.com/Moderation/ ; https://wiki.chatterino.com/Filters/ ; https://github.com/Chatterino/chatterino2/wiki/Moderation
- FrankerFaceZ: https://github.com/FrankerFaceZ/FrankerFaceZ/blob/master/old_changes.html
- 7TV: https://github.com/SevenTV/Extension/blob/master/CHANGELOG.md ; https://7tv.app/ and https://help.7tv.app/ (not reachable this run)
- Discord: https://support.discord.com/hc/en-us/articles/4421269296535-AutoMod-FAQ ; https://support.discord.com/hc/en-us/articles/10989121220631-How-to-Protect-Your-Server-from-Raids-101
- Dyno: https://docs.dyno.gg/en/modules ; https://docs.dyno.gg/en/modules/automod ; https://docs.dyno.gg/en/modules/autoban
- Wick: https://docs.wick.bot/intro/what-is-wick/ ; https://docs.wick.bot/intro/features/ ; https://docs.wick.bot/changelog/v5.x/5.0.0/ ; https://docs.wickbot.com/
- Kick: https://help.kick.com/en/articles/7109164-how-to-moderate-your-kick-chat ; help.kick.com/en/articles/15064286 ; help.kick.com/en/articles/15063715 ; https://docs.kick.com
- YouTube: https://support.google.com/youtube/answer/9826490
- Mastodon: https://docs.joinmastodon.org/admin/moderation/
- Bluesky: https://atproto.com/guides/labels ; https://atproto.com/guides/moderation ; https://atproto.com/guides/subscriptions ; https://atproto.com/guides/using-ozone ; https://github.com/bluesky-social/ozone ; https://bsky.social/about/blog/4-13-2023-moderation ; https://bsky.social/about/blog/03-12-2024-stackable-moderation ; https://bsky.network/blog/blueskys-moderation-architecture ; https://bsky.social/about/blog/09-18-2024-trust-safety-update ; https://bsky.social/about/blog/09-19-2025-building-healthier-social-media ; https://www.bsky.social/about/blog/11-19-2025-moderation-updates ; https://bsky.app/profile/safety.bsky.app/post/3lmwf4534jc2s ; https://github.com/bluesky-social/social-app/issues/7076 ; https://techcrunch.com/2023/12/01/bluesky-rolls-out-automated-moderation-tools-plus-user-and-moderation-lists/ ; https://techcrunch.com/2024/09/18/bluesky-addresses-trust-and-safety-concerns-around-abuse-spam-and-more ; https://techcrunch.com/2024/12/13/bluesky-is-at-a-crossroads-as-users-petition-to-ban-newcomer-over-anti-trans-views ; https://techcrunch.com/2025/01/17/bluesky-saw-17x-increase-in-moderation-reports-in-2024-after-rapid-growth

---

## 10. Account safety: the tokens we hold, and the three hosting models

### 10.0 Why this section exists

The owner wrote this on 2026-10-10 at 19:36 (PLAN, slice S-TOKEN-CUSTODY-THREAT, `SHORTCOMINGS-EXECUTION-PLAN.md:177-185`). Verbatim:

> "one thing that i am concerned about is the fact that i hold your account access with permanent refresh token cycles. i do encrypt them but i am sooo fucking scared something is going to happen and i loose control"
>
> "specially if big streamers join in then i become an attack vector"
>
> "the bot is build from the ground up to support 3 hosting models, self-contained into one locally run application, a split model where you host it on docker or some other server on your network or at a friend. or the saas hosted version where i do the hosting. this last one concerns me the most."
>
> "i can have webhooks and communication hooks so streamers can still self-host everything and join the community aspect of the bot but that is something i am not skilled enough for yet"
>
> "i know im a badass when it comes to programming but i know enough to know that i don't know anything yet"

Answer in one line: the worry is right. The hosted model is the weakest one. The fix is in two parts. Make a stolen token die fast. Keep big streamers' tokens off the hosted service.

How to read this section:

- Code paths start at `server/src/NomNomzBot.`. So `Infrastructure/X.cs` means `server/src/NomNomzBot.Infrastructure/X.cs`.
- Code facts were read this run unless the text says "from the code note". The code note is `research/token-custody-code.md`. A line number from the code note was not re-read.
- Anything not read is listed as blocked in the coverage ledger, with its reason.
- No secret value appears in this section.
- The hosted (saas) mode is a RESTRICTED option. Only NoMercy Labs may run it for third parties. Self-hosting your own bot is always free (`CLAUDE.md`, Critical Rules).

### 10.1 What we hold

All rows read from the code note, section 3, unless another cite is given.

| Provider | Token | Scopes asked | Where stored | How protected |
|---|---|---|---|---|
| Twitch (streamer) | Access and refresh token | Login: `user:read:email`, `user:read:chat`, `user:write:chat`, `user:read:moderated_channels` (`Infrastructure/Identity/AuthService.cs:89`). It then widens with each missing scope. The full catalogue is about 79 scopes (`AuthService.cs:183`). | Table `IntegrationToken` (`Domain/Integrations/Entities/IntegrationToken.cs`). Scopes in plain text in `IntegrationConnection`. | AES-256-GCM envelope per (provider, subject). Key per subject, wrapped by one root key. |
| Twitch (bot account) | Access and refresh token | `user:read:chat`, `user:write:chat`, `user:bot`, `chat:read`, `chat:edit`, `user:read:whispers` (`AuthService.cs:101`) | Same table. `BroadcasterId` is null for a shared bot. | Same. |
| Twitch (our app) | Client id and client secret | Not applicable | Config or env. Saved copies are sealed (`ProviderCredentialService`). `docker-compose.yml:60-130` passes the secrets as plain env vars. | Env var: none. Saved copy: same envelope. |
| Spotify | Access and refresh token (PKCE) | Playback, library, and (progressive) `streaming` (`Infrastructure/Integrations/OAuthProviderRegistry.cs:61-92`) | `IntegrationToken` | Same envelope. |
| Kick and kick_bot | Access and refresh token. Kick rotates the refresh token on each refresh (`Infrastructure/Integrations/Kick/KickAccessTokenProvider.cs:34`). | `user:read`, `chat:write`, `moderation:ban`, `moderation:chat_message:manage`, `events:subscribe` | `IntegrationToken` | Same envelope. |
| YouTube | Access and refresh token. Also an API key in env. | `youtube`, `youtube.force-ssl`, `youtube.readonly` (`OAuthProviderRegistry.cs:215-240`) | `IntegrationToken`. API key in env. | Same envelope. The env key has none. |
| Patreon | Access and refresh token | Nine scopes, including `w:campaigns.webhook` (`OAuthProviderRegistry.cs:147`) | `IntegrationToken` | Same envelope. |
| Shopify, Treatstream | Access token | `read_orders`; `userinfo` | `IntegrationToken` | Same envelope. |
| Discord | One static platform bot token. Per-channel OAuth tokens are stored but not used as the bot credential. | Not listed here (ledger: Discord OAuth scopes). | Platform vault first, then config (`Discord/Gateway/DiscordRestBotGateway.cs:35`). | Vault copy sealed. Config copy none. |
| Other secrets | TTS keys, webhook secrets, VTS plugin token, custom-data and supporter secrets | Not applicable | Cipher columns by name, such as `TtsConfig.AzureApiKeyCipher` (code note s.3.1; line `:84`). Seal on write for each column is in the ledger. | Same envelope. |
| Old `Service` rows | Access, refresh, client secret as text columns | Text column | `Domain/Platform/Entities/Service.cs:34-48`. The music mirror seals access and refresh token (`MusicProviderTokenMirror.cs:91-96`). `MusicService.cs:1389` and `YouTubeServiceConnectionBackfillSeeder.cs:75` read these rows (code note s.3.1). Client secret sealing and other writers are in the ledger. | Partly: the music mirror seals access and refresh token. |
| Our own keys | JWT secret; Data Protection key ring; federation signing key | Not applicable | JWT: file `jwt-secret.bin` (`Platform/Security/SelfHostSecretStore.cs:60-62`). Ring: `<DataDir>/dataprotection-keys`. Federation key: config PEM (`Infrastructure/Federation/FederationEventSigner.cs:128-150`). | Windows: DPAPI. Linux: plain file with owner-only mode. Federation key: none. |

Revoke at the provider today:

| Provider | Revoke call exists? | Called? |
|---|---|---|
| Twitch | Yes: `Infrastructure/Platform/Auth/TwitchAuthService.cs:365-411`. It sends the access token only (`:392`). | No caller. Only the interface (`Application/Abstractions/Auth/ITwitchAuthService.cs:28`) and the class mention it. |
| Kick, YouTube | Endpoint is in the descriptor (`OAuthProviderRegistry.cs:99,129,220`) | No. Nothing reads `RevokeEndpoint` outside the registry (grep, this run). |
| Spotify, Patreon, others | Descriptor says none (`:66,152,182,203`) | Not applicable |
| Discord | Not found | Not applicable |

### 10.2 What an attacker gets, per hosting model

First, the facts that hold in every model:

- One call chain seals every token: `ITokenProtector`, then `ISubjectKeyService`, then `IKeyVault` (code note s.1; `Infrastructure/DependencyInjection.cs:927`).
- There is one `IKeyVault` class, `OsSecureStoreKeyVault`. It has no branch per hosting mode (`DependencyInjection.cs:927`).
- The comment at `DependencyInjection.cs:925-926` names an Azure Managed-HSM branch for SaaS. Grep finds no such class. So the branch is not in code.
- Root key (KEK) order: (1) `Encryption:Key` from config, run through HKDF-SHA256 with no salt (`Infrastructure/Platform/Security/OsSecureStoreKeyVault.cs:143-156`); (2) on Windows, a DPAPI file; (3) on Linux or macOS with no config key, the app throws and does not start (`:176-180`).
- One root key serves all tenants of one deployment (code note s.2).
- The database also holds plain-text facts: provider, account name, granted scopes (`Domain/Integrations/Entities/IntegrationConnection.cs`; code note s.3.1).

| | Self-contained (one local app) | Split (Docker, own server or a friend's) | Hosted (saas, restricted) |
|---|---|---|---|
| Whose tokens | One person's | A small group's | Every streamer on the service |
| Root key lives | Windows: DPAPI file bound to the Windows user (`Platform/Security/DpapiKekStore.cs:59,64`). If `Encryption:Key` is set, that wins. | Env var `Encryption__Key` (`docker-compose.yml:80`) | Same code path as split. An HSM branch is named in a comment but missing (`DependencyInjection.cs:925-926`). |
| Attacker has the database only | Ciphertext, wrapped keys, plain scope lists. No tokens. | Same | Same, for all streamers. They also learn who uses the bot and which scopes each granted. |
| Attacker has the host or container | Code that runs as the same Windows user can unseal the root key. Another Windows user cannot. | Root key in the container env, ring and any JWT file on the volume in plain form (`Platform/Security/DataProtectionRegistration.cs:31-38`, `SelfHostSecretStore.cs:60-62`). Host access gives root key plus database. That opens every token. | Same, for every streamer. |
| Attacker has the root key only | Useless without the database. | Useless without a database copy. With any copy, every key opens, because HKDF has no salt (`OsSecureStoreKeyVault.cs:148-154`). | Same, for every streamer. |
| Blast radius | One person | One group | All streamers. Big streamers make it a prize. |
| Revoke speed after a breach | Slow. No kill switch. Twitch has no bulk revoke in the pages read (`research/token-custody-practice.md` row 23). | Same | Same, and scale makes it worse. |

Check on the dev box (read-only ssh, 2026-10-10, from the code note s.8 and the orchestrator): it runs the config-key mode. The env var name `Encryption__Key` is set. No `/app/data/keys` folder exists. One `dataprotection-keys` file exists. No value was read.

### 10.3 Findings, ranked by harm

The rank is a judgement. The facts under it are cited.

| # | Finding | Evidence | Harm |
|---|---|---|---|
| F1 | A disconnect does not revoke the token at the provider. The refresh token and the access token keep working until they expire or the user cancels them in the provider's settings. | `Infrastructure/Identity/IntegrationTokenVault.cs:342-377` soft-deletes locally and sets `Status=Revoked`. Callers: `IntegrationOAuthService.cs:336`, `AuthService.cs:1030,1250`, `Api/Controllers/V1/IntegrationsController.cs:269`, `Discord/DiscordGuildService.cs:305`, `Marketplace/MarketplacePublisherTokenService.cs:82`. Erasure also revokes locally only (`Identity/ErasureService.cs:568-585`, code note s.5). Erasure also crypto-shreds the keys (`ErasureService.cs:625-653`, code note s.5), so our copy dies. The provider copy still lives. `RevokeTokenAsync` has no caller (grep this run). Twitch confidential refresh tokens do not expire (https://dev.twitch.tv/docs/authentication/refresh-tokens/). | A user who clicks Disconnect believes the bot lost access. A thief with a copy keeps access. Twitch also demands deletion on revocation (Developer Agreement, "Deletion"). |
| F2 | One root key opens every tenant. On Docker and on hosted, it is a plain env var. | `OsSecureStoreKeyVault.cs:143-156`, `docker-compose.yml:80`, `:176-180` (no Linux secure store). The start guard rejects only the exact public dev key (`Api/Configuration/StartupSecretGuard.cs:43-51`; the key is in `.env.example:39`). It does not test length or strength of another value. | Host access or one leaked env file opens all tokens. This is the owner's "attack vector". |
| F3 | No kill switch. Nothing revokes all tokens. The only admin key action re-wraps keys. Twitch gives no bulk revoke. | Admin action `security/rotate-encryption-key` (`Api/Controllers/V1/AdminController.cs:369`) is key rotation, not token revocation. Twitch revoke doc covers one access token (https://dev.twitch.tv/docs/authentication/revoke-tokens/). A community leader says it leaves the refresh token alive (https://discuss.dev.twitch.com/t/removing-my-app-from-a-twitch-users-connections-with-twitchs-revoke-oauth-api/32614). That is a forum post, not Twitch. The effect of a new client secret on user tokens is undocumented. The New Secret button has timed out for developers, and Twitch has ticketed it (https://github.com/twitchdev/issues/issues/1196, https://github.com/twitchdev/issues/issues/1050). | On breach day we cannot cut access quickly. |
| F4 | Scopes grow with use, up to 70. | `AuthService.cs:181-186` (catalogue size), `Infrastructure/Identity/TwitchScopeRegistry.cs`. The registry is a reflection over `[RequiresTwitchScope]` plus `ResidualEventSubScopes` (code note s.3.2). The catalogue holds 70 scopes: 57 from the Helix clients and 13 residual EventSub scopes. The "about 79" in a code comment is stale. | A stolen broadcaster token can do more as the channel uses more features. |
| F5 | No record of who read a token and why. | `KeyUsageBindings` lists what a key protects, not each read (`Platform/Security/SubjectKeyService.cs:177-236`). The vault logs only a warning on a failed decrypt (`IntegrationTokenVault.cs:428,480`). `IamAuditLog` exists for permission operations (`Domain/Identity/Entities/IamAuditLog.cs`, code note s.6). No audit row per token read was seen. Whether it covers token use is in the ledger. | A quiet theft by our own process or an insider leaves no trace. |
| F6 | No hourly token check and no disconnect signal in the server code. | Grep for `id.twitch.tv` in non-test C# finds `authorize`, `token`, `revoke` and `device`, and no `validate` (this run). Grep for `user.authorization.revoke` finds nothing. The grep covers this repo's non-test C# only. A 30-minute refresh sweep exists (`Platform/Scheduling/TokenRefreshService.cs:25`, code note s.4). It refreshes tokens that expire soon. It does not call validate (grep). Twitch says: "Your app must validate the OAuth token when it starts and on an hourly basis thereafter." It also says: "Twitch reserves the right to take punitive action, such as revoking the developer's API key or throttling the application's performance." It says too: "When a user disconnects the integration, Twitch invalidates the access tokens associated with it." (https://dev.twitch.tv/docs/authentication/validate-tokens/, read raw in `research/token-custody-practice-2.md` s.B). A validate hidden in a library call is in the ledger. | Possible breach of the Twitch rules. We learn late that a user cut the link. |
| F7 | On Linux, two files sit in plain form on the data volume. | Data Protection ring (`DataProtectionRegistration.cs:31-38`). JWT secret file, mode owner-only (`SelfHostSecretStore.cs:60-62`). No direct caller of `IDataProtector` or `CreateProtector` was found in `server/src` (code note s.1), so only framework features use the ring. Which ones is in the ledger. | A volume copy gives the ring and the JWT secret. A JWT secret lets the holder forge sessions (my inference). |
| F8 | Platform app secrets travel as plain env vars. | `docker-compose.yml:60-130`. Twitch refresh needs the client secret (https://dev.twitch.tv/docs/authentication/refresh-tokens/). | A leaked env file gives the secret. With a stolen refresh token it gives refresh power. |
| F9 | A wrong root key does not stop the app. It mints a new data key and the old secrets are lost. | `Platform/Security/SubjectKeyService.cs:160-190`, gated to `UNWRAP_FAILED`. A shredded key is never re-keyed. | Loss of availability, not theft. Users must sign in again. It is silent apart from a log line. |
| F10 | The federation signing key is a config PEM, and no sender uses it. | `FederationEventSigner.cs:128-150`. The signer is referenced only by the inbound gateway and DI (grep this run). The code read shows an inbound gateway and a signer. No outbound sender class found by grep. Peer key storage was not read (ledger). | The design in 10.5 has a gap to close first. |

### 10.4 What others do, and known incidents

Rules from Twitch (read from the page text; `research/token-custody-practice.md` s.2):

- Treat tokens like passwords (https://dev.twitch.tv/docs/authentication/).
- The Developer Agreement says keep Keys secret and do not "sell, transfer, or sub-license them". It also says report a security incident in writing to legal@twitch.tv and start remediation at once (https://legal.twitch.com/en/legal/developer-agreement/).
- Access and refresh tokens die when: they expire; the user changes the password; Twitch revokes them; the user clicks Disconnect in the Twitch settings (https://dev.twitch.tv/docs/authentication/).
- A public-client refresh token expires after 30 days and works once (https://dev.twitch.tv/docs/authentication/getting-tokens-oauth/). A user report says reuse of a spent public-client refresh token kills the whole grant (https://github.com/twitchdev/issues/issues/903). That is a user report, not an official rule.

Incidents (all re-read this run in `research/token-custody-practice-2.md` s.B):

| Incident | What happened | Lesson for us | Source |
|---|---|---|---|
| Heroku and Travis CI, Apr 2022 | An attacker used stolen OAuth tokens held by two apps to call the GitHub API. They listed orgs, chose targets and cloned private repos. GitHub revoked the tokens tied to its own use. | A fleet of user tokens is a target. The stolen tokens did the harm. | https://github.blog/news-insights/company-news/security-alert-stolen-oauth-user-tokens/ |
| Twitch, Oct 2021 | A server configuration change let a third party in. Source documents and some payout data left. Twitch reset all stream keys. The post does not mention OAuth tokens. | The provider itself reset secrets "out of an abundance of caution". We need that button. | https://blog.twitch.tv/en/2021/10/15/updates-on-the-twitch-security-incident/ |
| StreamElements, Mar 2025 | A hacker claimed 210,000 customers' contact data via a former third-party provider. StreamElements says none of its servers were breached. The infostealer story is the hacker's claim. Tokens were not mentioned. | Data can leak through a partner. Not a token case. | https://www.bleepingcomputer.com/news/security/streamelements-discloses-third-party-data-breach-after-hacker-leaks-data/ |
| Twitch extension, Sep 2026 | A browser extension sent the user's Twitch web-session token to its proxy in the URL. The proxy logs held it in clear text. This was a web-session token, not a bot token. | Never put a token in a URL or a log. | https://www.bleepingcomputer.com/news/security/twitch-extension-with-30k-installs-exposes-users-oauth-tokens/ |

No leak of a chat-bot service's Twitch tokens was found in the searches listed in the research notes.

How rivals handle it:

| Product | What it publishes | Source |
|---|---|---|
| Nightbot, StreamElements | Generic security wording. No token detail. | https://nightbot.tv/privacy ; https://streamelements.com/privacy |
| Fossabot | For its import feature, tokens stay in memory and are discarded. Its own platform tokens: not stated on the page read. | https://fossadev.com/privacy |
| Streamer.bot (local app) | Tokens sit in a settings file on the user's PC and never leave the app. Under the YouTube part only. The Twitch part says "Documentation Needed". | https://streamer.bot/privacy ; https://docs.streamer.bot/en/Quick-Start/Twitch |
| Firebot (local app) | Tokens go to a JSON file in the user's profile. No encryption was found in `account-access.ts` (one file grepped). | https://github.com/crowbartools/Firebot |
| Streamlabs, Moobot | Nothing found on token storage. Moobot's page showed only a login shell. | https://streamlabs.com/privacy |

Pattern: hosted bots publish no custody detail. Local bots keep the token on the user's own disk. A clear public custody statement would stand out.



### 10.5 The federation design, in plain words

The idea: a big streamer runs their own bot. Their tokens stay on their own machine. Our hosted service never sees them. They still join the community features.

Four rules:

1. Each self-hosted bot keeps its own tokens. They never leave that machine.
2. Instances exchange signed requests. A request says what to do, never how to log in.
3. Each channel's own bot acts after its own channel decides. The decision is local and opt-in.
4. The hosted service holds tokens only for streamers who chose hosting. It never needs a big streamer's tokens.

Why this works on Twitch. The Ban User call checks the token's own user against the channel. The `moderator_id` must match the user in the token, and a user who is not a moderator of the channel gets 403 (https://dev.twitch.tv/docs/api/reference/#ban-user, read raw in `research/token-custody-practice.md` s.7). The call does not look at which server sent it. So channel B's own bot can ban in channel B after a signed request from another instance. The central service needs no token for B. The 403 proves only that the bot is a moderator. It does not prove the ban is wise. So the receiving bot must still decide, as the decision card in section 7 already says.

What already exists in code (from the code note s.7; not all re-read):

- Peer registration, trust and revoke: `Api/Controllers/V1/FederationController.cs`.
- Per-channel opt-in, default deny, LeadModerator only: `ChannelFederationController.cs`.
- RSA signing of a sorted-key JSON envelope: `Infrastructure/Federation/FederationEventSigner.cs:23-51`.
- Inbound gateway: signature check, bind to trusted peer, replay guard, opt-in gate, journal (`FederationInboundGateway.cs:24-30`).
- Outbound webhooks: a subscribed endpoint receives events by type, including `ModerationActionTakenEvent` (`Webhooks/OutboundWebhookEventCatalogue.cs:118`, `OutboundWebhookFanoutHandler.cs:42-96`, `OutboundWebhookDispatcher.cs:48-66`; source: `research/handoff-gap-final.md` lines 12-13). Their secrets are HMAC `whsec_` values sealed with `ITokenProtector` (code note s.7). `ModerationActionTakenEvent` is journaled: `JournalingEventBusDecorator` journals every event on the bus before delegation (`EventStore/JournalingEventBusDecorator.cs:20-30`; source: `research/gap-final.md` line 23).
- What is missing: an outbound sender (none found by grep, F10), the signing key in the sealed vault (it is a config PEM), peer key rotation. Peer key storage was not read (ledger).

Why signed requests and not a full federation protocol. Twitch EventSub already uses the simple form: HMAC-SHA256 over message id, timestamp and body, with a time-safe compare (https://dev.twitch.tv/docs/eventsub/handling-webhook-events/, read raw). Our need is one narrow action. The existing RSA signer fits.

Twitch rule that shapes it: Keys may not be sold, transferred or sub-licensed (Developer Agreement). So we must not hand our client secret to self-hosters. Each self-host either registers its own Twitch app or signs users in some other way. Which one is open question Q2.

The smallest first step a small team can build:

1. Pick one message type: the mass-ban request from section 7.
2. Fields: sender instance, requester, target account, reason, evidence summary, timestamp, nonce. No token. No scope.
3. The sender signs with its instance key (existing signer). It posts to each peer that is opted in.
4. The receiver verifies, checks replay and opt-in (existing gateway), and shows the decision card.
5. On "Ban here too", the receiver's own bot calls Ban User with its own token.
6. A test with two instances proves it, and proves no field of the message holds a token.

Before step 3 someone must check whether an outbound sender exists. Grep found none.

### 10.6 Fix slices

Order of work, with reasons. Each slice has the evidence it rests on.

| ID | What it does | Done-when | Evidence |
|---|---|---|---|
| S-TOKEN-REVOKE-ON-DISCONNECT | On every disconnect and on erasure, call the provider's revoke endpoint before the local revoke. Use Twitch `oauth2/revoke`, Kick `oauth/revoke`, Google `revoke` (descriptors exist). A provider failure never blocks the local revoke. It queues a retry and shows the user "also disconnect on the provider's settings page", with the link. | Tests with a fake provider: each disconnect path posts to the right endpoint with the right token; a failed post still leaves the connection revoked and queues a retry; erasure does the same. A live test on a throwaway Twitch app and a test account, never a real channel: after the revoke call, does the refresh token still return new tokens? The result goes into this section, because Twitch does not document it (research notes A1: docs silent, a community leader says no). If the refresh token lives, the slice also tells the user to disconnect in Twitch settings. | F1 |
| S-TOKEN-KEY-CUSTODY | Stop the root key from being only an env var. Accept it from a secret file. Make the start guard reject a short or low-entropy key. Add a second `IKeyVault` for hosted that keeps the root key in a KMS or HSM. Raise an incident entry, not only a log line, when a data key fails to unwrap. | Tests: a short key is refused outside Development; a key read from a file opens the same data as the env value. The hosted vault is tested against a fake KMS. A drill on the dev box moves the root key and proves no token was lost. | F2, F7, F8, F9. The KMS part rests on code only: the missing HSM branch at `DependencyInjection.cs:925-926`. Thinner evidence than the rest. |
| S-TOKEN-LEAST-SCOPE | Build a table of every scope we request, the feature that needs it, and the harm class. Request a scope only while its feature is on. Show the streamer which scopes are held. | A script lists every requested scope with its feature and harm class and fails on an unmapped scope. A test shows a scope for a disabled feature is not requested. | F4 |
| S-TOKEN-KILL-SWITCH | One admin action and one per-channel action: validate, revoke at the provider, mark `needs_reauth`, notify the owner. Plus a written breach runbook: revoke first, then investigate, then email legal@twitch.tv. The runbook records the rotate-secret route and its New Secret timeout risk, and a second route. | A drill on the dev box with a throwaway app: every connection ends unusable, and the elapsed time for N connections is written down. The runbook names who sends the Twitch email. | F3; Developer Agreement; GitHub 2022 |
| S-TOKEN-AUDIT | Write one audit row for each token read: who, which component, why, when. Never the value. Add the hourly Twitch validate and a handler for the Twitch EventSub authorization-revoke type (name to be read from the subscription-types page first), so a user disconnect marks the connection revoked at once. | A test shows each `GetAccessTokenAsync` writes a row with no secret in it. A test shows a validate failure marks the connection `needs_reauth`. The subscription type name is checked against https://dev.twitch.tv/docs/eventsub/eventsub-subscription-types/ first. | F5, F6; Twitch validate page. The exact EventSub type name is in the ledger. |
| S-FEDERATION-SIGNED-REQUESTS | Build the first step in 10.5: an outbound sender for the mass-ban request, the signing key moved into the sealed vault, a key rotation path for peers. | Two instances run on the dev box. A signed request from A shows a decision card on B. "Ban here too" makes B's own bot call Ban User. A request with a bad signature, a replay, or a missing opt-in does nothing. No message field holds a token. | F10; Helix Ban User reference; existing gateway |

Suggested order: REVOKE-ON-DISCONNECT, KILL-SWITCH, KEY-CUSTODY, AUDIT, LEAST-SCOPE, FEDERATION. The first two cut the harm of any breach. The rest shrink what a breach can reach. Federation is last because it needs the mass-ban slices from section 7 first.

### 10.7 Open questions for the owner

| # | Question | Recommendation |
|---|---|---|
| Q1 | Does the hosted service refuse tokens of big streamers? By what rule: follower count, or the streamer's own choice? | Own choice, with a clear default of "self-host or split" for large channels. A size cut-off is easy to game and hard to explain. |
| Q2 | Does each self-host register its own Twitch app, or sign in some other way? The Developer Agreement bans passing Keys on. | Own app per self-host. |
| Q3 | May we create a throwaway Twitch app and a test account for the live tests (revoke vs refresh token; New Secret effect)? Only you can make them. | Yes. These tests decide the kill-switch design. |
| Q4 | Accept the cost of a cloud KMS or HSM for the hosted mode? | Yes, if hosted ever runs for third parties. Not needed for self-host. |
| Q5 | If the provider revoke fails, does the disconnect still complete locally? | Yes, and retry. |
| Q6 | Who sends the written security incident mail to legal@twitch.tv? | You, with a template in the runbook. |
| Q7 | A wrong root key today mints new keys and loses old tokens. Should it stop the app instead? | Stop the app on hosted. Keep self-heal on self-contained. Your call. |
| Q8 | Do you agree with the slice order in 10.6? | Yes. |

### 10.x Sources

New in this section. Tags: raw = read as page text; forum = community post.

Twitch (raw unless marked):

- https://dev.twitch.tv/docs/authentication/
- https://dev.twitch.tv/docs/authentication/refresh-tokens/
- https://dev.twitch.tv/docs/authentication/revoke-tokens/
- https://dev.twitch.tv/docs/authentication/validate-tokens/
- https://dev.twitch.tv/docs/authentication/register-app/
- https://dev.twitch.tv/docs/authentication/scopes/
- https://dev.twitch.tv/docs/authentication/getting-tokens-oauth/
- https://dev.twitch.tv/docs/eventsub/handling-webhook-events/
- https://dev.twitch.tv/docs/eventsub/eventsub-subscription-types/ (named for a check, not yet read for the revoke type)
- https://dev.twitch.tv/docs/api/reference/#ban-user
- https://legal.twitch.com/en/legal/developer-agreement/
- https://blog.twitch.tv/en/2021/10/15/updates-on-the-twitch-security-incident/

Community and issue trackers (forum, user reports):

- https://discuss.dev.twitch.com/t/removing-my-app-from-a-twitch-users-connections-with-twitchs-revoke-oauth-api/32614
- https://github.com/twitchdev/issues/issues/903
- https://github.com/twitchdev/issues/issues/1050
- https://github.com/twitchdev/issues/issues/1196

Incidents:

- https://github.blog/news-insights/company-news/security-alert-stolen-oauth-user-tokens/
- https://www.bleepingcomputer.com/news/security/streamelements-discloses-third-party-data-breach-after-hacker-leaks-data/
- https://www.bleepingcomputer.com/news/security/twitch-extension-with-30k-installs-exposes-users-oauth-tokens/

Other bots (raw):

- https://nightbot.tv/privacy
- https://fossadev.com/privacy
- https://streamelements.com/privacy
- https://streamlabs.com/privacy
- https://streamer.bot/privacy
- https://docs.streamer.bot/en/Quick-Start/Twitch
- https://github.com/crowbartools/Firebot



Research notes (session scratchpad, `research/`): token-custody-code, token-custody-practice, token-custody-practice-2.

---

## 11. Our own output surfaces: what the bot says, shows and speaks

### 11.0 Why this section exists

The owner asked: "and you are sure you covered all bases?"

Sections 1 to 10 look at attackers and at the tools Twitch gives us. They do not look at our own bot.

The bot can also be the source of harm. It repeats viewer text and viewer names in chat, on the overlay, in TTS and in Discord.

How to read this section:

- Code paths start at `server/src/`. Line numbers were re-read in code in the research note unless the text says "reasoned" or "read, not run".
- Web facts come from pages opened in a real browser. Vendor pages are claims, not proof.
- No secret value appears in this section.

Answer in one line: the bot has ONE output filter. It is the TTS censor. It holds 24 mild words. Every other surface that repeats viewer text or viewer names has no content filter.

### 11.1 Surfaces read

| Surface | What it outputs | Filtered today? | Evidence (file:line) |
|---|---|---|---|
| TTS gate (all callers) | Spoken viewer text in the bot voice | Partly. Censor of 24 mild words. No slurs. No leet, spaced or look-alike letters (reasoned from the regex, not run). Mod approval is off by default. | `NomNomzBot.Infrastructure/Tts/TtsDispatchService.cs:92`, `:163-165`; `Tts/TtsProfanityCensor.cs:28-54`, `:61-77`; `Domain/Tts/Entities/TtsConfig.cs:55`, `:58` |
| TTS from event responses (follow, raid, sub, cheer) | Template text with the viewer display name | Text is censored. The name is not. The standing floor never blocks it. | `Platform/Eventing/EventResponseExecutor.cs:314-339` |
| TTS spoken names | Display names feed pronunciation | No. The censor gets message text only. | `Tts/TtsDispatchService.cs:165`, `:310`, `:541-547` |
| TTS from quotes | A stored viewer quote | TTS gate only. No filter at `!quote add`. | `Quotes/Builtins/QuoteBuiltin.cs:189`, `:227-305` |
| TTS from shoutout | Target display name | TTS gate only. Low risk. | `Stream/ShoutoutSender.cs:166-179` |
| TTS from `play_tts`, scripts, dashboard test, denial notice, command replies | Templated viewer text | TTS gate. Command replies run as bits 0 and standing "everyone" (read, not run). | `Tts/PipelineActions/PlayTtsAction.cs:193`; `Tts/PipelineActions/ReplyTtsPipeline.cs:25-39`; `Chat/EventHandlers/ChatMessageHandler.cs:589`, `:875` |
| TTS text echoed to the overlay widget | The censored spoken text | Yes, it carries the censored text. Two tests pin it. | `Api/Hubs/Broadcasters/TtsSpeakBroadcastHandler.cs:42-90`; `TtsDispatchServiceTests.cs:984-1004`, `:538-541` |
| Follow, raid, sub, cheer, reward alerts on the overlay | Viewer display name | No. The event filter looks at event TYPE only. | `Api/Hubs/Broadcasters/FollowBroadcastHandlers.cs:48-90`; `RaidBroadcastHandler.cs:44-79`; `Overlays/OverlayEventFilter.cs:21-111` |
| Alert queue | Queued alerts | No. | `Alerts/Services/AlertQueueService.cs` |
| Reward redemption with viewer-typed input | Free text on overlay, in templates, TTS and pipelines | No. A 500-character cap only. | `Api/Hubs/Broadcasters/RewardBroadcastHandler.cs:76`; `Rewards/EventHandlers/RewardRedeemedHandler.cs:217`; `Platform/Persistence/Configurations/RedemptionConfiguration.cs:28` |
| Custom command reply with `{args}`, `{target}`, `{user}` | The bot repeats viewer text in its own voice | No. Permission and cooldown only. | `Chat/EventHandlers/ChatMessageHandler.cs:1627-1667`, `:573-585`, `:753`, `:297` |
| `!voice` not-found reply | Echo of the viewer query | No. | `Tts/Builtins/VoiceBuiltin.cs:134`, `:519-521` |
| Template resolver | Raw viewer values in every template | No escape, filter or censor. | `Platform/Templating/TemplateResolver.cs:146-160`, `:99`, `:165`, `:340-368` |
| Outbound chat seam (every bot line, all platforms) | Word-wrap and a repeat marker | No content filter. | `Chat/ChatPlatformRouter.cs:212-262`; `Chat/OutboundChatShaper.cs:37-70` |
| Inbound chat filters | Incoming chat only | Not an output filter. The command handler ignores the result. | `Moderation/EventHandlers/ChatFilterExecutionHandler.cs:52-140`, `:62`; `Platform/Eventing/EventBus.cs:64-69` |
| Chat widget on the overlay | The raw viewer line | No. Sent at once. Removed only if a platform delete event arrives. | `Api/Hubs/Broadcasters/DashboardBroadcastHandler.cs:29`, `:107`, `:137-141`, `:147-159`; `ChatModerationBroadcastHandlers.cs:38-113` |
| Song requests | Track title on stream | URI block list only. The `BlockExplicit` gate is documented and not used. | `Music/MusicService.cs:807`; `Music/BlockedTrackService.cs:106-114`; `Domain/Music/Interfaces/IMusicProvider.cs:313` |
| Media share | Viewer video and title on the overlay | Optional mod approval. No title filter. | `MediaShare/MediaShareService.cs:123-135`, `:172`, `:470` |
| Discord relay and `send_discord_notification` | Stream title, or viewer text in the streamer's template | Mentions are safe (allow-list of one ping role). Content is not filtered. | `Discord/Gateway/DiscordRestBotGateway.cs:83-90`, `:909-913`; `Discord/PipelineActions/SendDiscordNotificationAction.cs:68-79`; `Discord/DiscordNotificationDispatcher.cs:157-183` |
| Chat HTML fragment (subscriber HTML) | Allowed HTML tags | XSS and URL sanitiser only. It does not stop hate text. | `Chat/ChatHtmlSanitizer.cs`; `Chat/Adapters/HtmlFragmentAdapter.cs:84`, `:199` |
| `!whisper` | A DM from the bot | Mods and broadcaster only. No content filter. | `Commands/Builtins/WhisperBuiltin.cs:57-75`, `:136` |
| Lockdown (panic button) | Twitch chat controls | Does not touch alerts, TTS or overlay chat. | `Domain/Moderation/SpamDefense/Lockdown.cs:20-42`; `Infrastructure/Moderation/Lockdown/TwitchLockdownAdapter.cs:56-59` |
| Account-age and trust gates | Incoming messages | Guard no output surface. | `Moderation/AccountAgeGateExecutor.cs`; `Trust/TrustPolicyService.cs` |

### 11.2 Findings, ranked by harm

The rank is a judgement. The facts under it are cited.

| # | Finding | Evidence (file:line or URL) | Harm | Slice ID |
|---|---|---|---|---|
| 1 | A viewer can make the bot repeat any text. The command reply echoes `{args}`. No filter sits on the reply or on the outbound seam. The chat filter and the command handler are two handlers on one event. The command handler never reads the filter result. So a filtered viewer line is deleted, and the bot still posts the same words. | `Chat/EventHandlers/ChatMessageHandler.cs:1627-1667`, `:573-585`, `:753`; `Chat/ChatPlatformRouter.cs:212-262`; `Moderation/EventHandlers/ChatFilterExecutionHandler.cs:62`; `Platform/Eventing/EventBus.cs:64-69` | Hate text goes out in the bot's own voice on Twitch, Kick and YouTube. | S-MOD-ECHO-AFTER-FILTER, S-MOD-OUTBOUND-FILTER |
| 2 | Alerts show viewer names with no filter. A follow bot with a hateful name goes straight to the alert queue and the widgets. The follow-bot defence blocks accounts in a spike. It holds back no single alert and no single name. The 2023 study found 99.6 percent of bot names were random letters and digits, so a word filter on names alone misses them. | `Api/Hubs/Broadcasters/FollowBroadcastHandlers.cs:48-90`; `RaidBroadcastHandler.cs:44-79`; `Overlays/OverlayEventFilter.cs:21-111`; `Moderation/FollowBotSweepService.cs:102`; https://arxiv.org/html/2301.03946v1 | Hate on stream during a hate raid. | S-MOD-ALERT-NAME-HOLD, S-MOD-OUTBOUND-FILTER |
| 3 | The TTS censor is weak. It has 24 mild words and no slurs. It runs one word-boundary regex with no normalisation. So `sh1t`, `s h i t`, `s.h.i.t` and a look-alike letter pass (reasoned from the regex, not run). No test sends such input. Streamlabs and StreamElements ship slurs in the default list. They also let the streamer add words and pick an action. | `Tts/TtsProfanityCensor.cs:28-54`, `:61-77`; `Tts/TtsProfanityCensorTests.cs`; https://support.streamelements.com/hc/en-us/articles/10474807584914-Tip-Moderation; https://streamlabs.com/content-hub/post/how-to-add-text-to-speech-to-donations-to-your-stream | Slur or hate text spoken in the bot voice. | S-MOD-OUTBOUND-FILTER, S-MOD-FILTER-WORDLIST |
| 4 | Lockdown drives Twitch chat controls only. During a hate raid the bot keeps speaking names and showing alerts. Hate-raid streamers used a panic button that "turned off alerts, locked down chat". Lockdown has no link to alerts, TTS or overlay chat (grep found none). It also covers Twitch only. | `Domain/Moderation/SpamDefense/Lockdown.cs:20-42`; `Infrastructure/Moderation/Lockdown/TwitchLockdownAdapter.cs:56-59`; https://arxiv.org/html/2301.03946v1 | The attack goes on in the bot's output while chat is locked. | S-MOD-LOCKDOWN-OUTPUT, S-MOD-LOCKDOWN-MULTI |
| 5 | The chat widget sends the raw viewer line to the overlay at once. It reads no filter result. A line the filter deletes stays on the overlay until a platform delete event arrives. The filter publishes no `ChatMessageDeletedEvent`. | `Api/Hubs/Broadcasters/DashboardBroadcastHandler.cs:29`, `:107`, `:137-141`, `:147-159`; `Moderation/EventHandlers/ChatFilterExecutionHandler.cs:241`; `Widgets/EventHandlers/OverlayModerationRetractionHandler.cs` | A hateful line is on stream, even when the filter removes it one moment later. | S-MOD-OVERLAY-CHAT-GATE, S-MOD-ECHO-AFTER-FILTER |
| 6 | Other viewer-text paths have no filter: reward input (500-character cap only), stored quotes (spoken later by anyone who runs `!quote`), media titles when approval is off, Discord templates with `{raw.message}` or `{args}`, the `!voice` not-found echo, and `!whisper` (mods only, low risk). | `Api/Hubs/Broadcasters/RewardBroadcastHandler.cs:76`; `Quotes/Builtins/QuoteBuiltin.cs:189`; `MediaShare/MediaShareService.cs:123-135`; `Discord/PipelineActions/SendDiscordNotificationAction.cs:68-79`; `Tts/Builtins/VoiceBuiltin.cs:519-521`; `Commands/Builtins/WhisperBuiltin.cs:136` | Viewer text, hate words and invite links reach the stream, TTS or Discord. Only mention pings are blocked. | S-MOD-OUTBOUND-FILTER |
| 7 | The template resolver does no escaping. A viewer value that holds `{transform.<name>:<text>}` is run as a transform. There are 9 names. All are pure string functions. They leak no data and run no code. A viewer can reshape their own text. An unknown letters-only name makes the bot print an error to chat, and logs an Error each time. | `Platform/Templating/TemplateResolver.cs:99`, `:146-160`, `:165`, `:340-372`, `:362`, `:367`, `:1617`; `TextTransformCatalog.cs:38-56` | The bot echoes viewer-shaped text and writes error noise. No data leak. | S-MOD-TEMPLATE-ESCAPE |
| 8 | Event-response TTS passes standing "broadcaster", on purpose. So the `MinPermission` floor never applies to names. Command replies run as bits 0 and standing "everyone". A channel with `MinBitsToTts` or a floor above everyone would reject every `SpeakWithTts` reply, also from a mod (read, not run). Cost and floor gates are a moderation tool that competitors use. | `Platform/Eventing/EventResponseExecutor.cs:307-313`, `:336`; `Tts/PipelineActions/ReplyTtsPipeline.cs:25-39`; `Platform/Pipeline/PipelineEngine.cs:281-282`; `Tts/PipelineActions/PlayTtsAction.cs:172-181`; `Tts/TtsDispatchService.cs:115`, `:127`, `:957-968` | A gate that is set is not applied, or applies to the wrong person. | S-MOD-TTS-STANDING |
| 9 | `BlockExplicit` is documented and nothing uses it. Explicit songs can be requested and the title shows on stream. | `Domain/Music/Interfaces/IMusicProvider.cs:313`; `SpotifyMusicProvider.cs:2222`; `YouTubeMusicProvider.cs:1302` | Explicit titles on stream. | S-MOD-OUTBOUND-FILTER |

Good news, to keep as the pattern: Discord mentions are safe. The gateway always sends an allow-list of at most the one ping role (`Discord/Gateway/DiscordRestBotGateway.cs:83-90`, `:909-913`).

What competitors offer, from pages read in a real browser:

- Streamlabs: the profanity filter is on by default. Options are replace, hide or disable the alert. It also has a TTS spam filter.
- StreamElements: standard filter for curse words and slurs, custom words with wildcards, TTS anti-spam, and manual tip approval where deny masks the name. A community doc says it has no filter for follow alerts. That is a third-party claim.
- Sound Alerts: a basic profanity check, Twitch AutoMod on channel-point TTS, manual approval, and a profanity strength setting.
- ControlPlay (vendor page, a claim): a bypass-resistant word filter plus an AI check. Rejected text is never synthesised.

### 11.3 Fix slices

| Slice | What it does | Done-when | Rests on |
|---|---|---|---|
| S-MOD-OUTBOUND-FILTER | One outbound filter at `ChatPlatformRouter` and at the TTS gate. It covers alerts, event-response chat, command echoes, reward input, media titles, Discord text, the `!voice` echo and `!whisper`. It normalises leet and spaced spelling. It also implements the documented `BlockExplicit`. | Unit tests send `sh1t`, `s h i t` and `s.h.i.t` through the filter and the TTS gate, and assert the text is masked and the spoken text carries the mask. A test posts a command reply with a bad `{args}` and asserts the sent chat line is masked. A test requests an explicit track with `BlockExplicit` on and asserts the request is refused and no title reaches the overlay. | Findings 1, 3, 6, 9 |
| S-MOD-ECHO-AFTER-FILTER | The command handler reads the chat-filter result. A filtered message runs no command echo. The handler order is explicit and not compiler order. The filter publishes `ChatMessageDeletedEvent` so the overlay drops a deleted line. | A test registers both handlers and sends a line the filter deletes. It asserts the bot sends no echo of `{args}`. A second test asserts a `ChatMessageDeletedEvent` is published and the overlay retraction handler removes the line. | Findings 1, 5 |
| S-MOD-ALERT-NAME-HOLD | Alerts pass a name check before they show. A name that hits the filter, or arrives in a spike, goes to a hold-for-approval queue. A deny masks the name. | A test sends a follow alert with a filtered name. It asserts the alert is held, the overlay gets no name, and a deny shows a masked name. A test sends a follow spike and asserts the held alerts are not shown. | Finding 2; StreamElements manual tip approval |
| S-MOD-FILTER-WORDLIST | A default list with slurs and hate terms. Custom words with wildcards per channel. Three actions per channel: replace the word, hide the message, disable the alert. A strength setting. | Tests assert each action changes the output as named: replace masks only the hit word, hide drops the whole message, disable shows no alert. A test asserts a custom wildcard word is masked. A test asserts the default list holds slurs. | Finding 3; Streamlabs, StreamElements, Sound Alerts |
| S-MOD-OVERLAY-CHAT-GATE | The overlay chat feed waits for the filter result before it sends the line. | A test sends a line the filter deletes. It asserts the overlay feed and the widget route get no `ChatMessage` for it. | Finding 5 |
| S-MOD-LOCKDOWN-OUTPUT | Lockdown gains controls for alerts, TTS and overlay chat. One press turns them off together with the Twitch chat controls. | A test turns lockdown on, then sends a follow event and a TTS request. It asserts the alert queue gets no alert and the TTS gate refuses the request. A test turns lockdown off and asserts both work again. | Finding 4; the 2023 study |
| S-MOD-LOCKDOWN-MULTI | Lockdown covers Kick and YouTube where the API allows. The dashboard says plainly what cannot be locked. X Live has no chat-moderation API. | A test per platform asserts the controls the adapter calls, and that an unsupported control shows an explicit "cannot lock" state. | Finding 4 |
| S-MOD-TEMPLATE-ESCAPE | A viewer value is not run as a transform. An unknown transform name prints no error to chat. | A test resolves a template with a viewer value `{transform.upper:x}` and asserts the output holds the literal text. A test with an unknown name asserts no error text in the output and no Error log. | Finding 7 |
| S-MOD-TTS-STANDING | Event-response TTS passes the real standing, or skips the floor on purpose, with the reason written in the code. `ReplyTtsPipeline` seeds the real bits and role. | A test sets `MinPermission` above everyone and `MinBitsToTts` above 0. It asserts a mod command reply is spoken, a viewer under the floor is refused, and a follow response follows the documented rule. | Finding 8 |

### 11.x Sources

https://streamlabs.com/content-hub/post/how-to-add-text-to-speech-to-donations-to-your-stream
https://support.streamlabs.com/hc/en-us/articles/115001861193-How-to-Ban-Words-from-Alerts-Widgets-and-Recent-Events-Profanity-Filter
https://support.streamelements.com/hc/en-us/articles/10474807584914-Tip-Moderation
https://docs.streamelements.com/chatbot/filters/bannedwords
https://github.com/t-affeldt/hate-raid-prevention/blob/main/streamelements-name-filters.md
https://controlplay.app/en/guides/twitch-tts-moderation
https://support.soundalerts.com/article/text-to-speech-tts
https://speechify.com/blog/twitch-text-to-speech/
https://help.twitch.tv/s/article/alerts-by-twitch-customization
https://safety.twitch.tv/s/article/Combating-Targeted-Attacks?language=en_US
https://arxiv.org/html/2301.03946v1

## 12. Stolen accounts and insider abuse

### 12.0 Why this section exists

A moderator account can be stolen. A streamer account can be stolen. A trusted person can also abuse the power they hold.

The 2026-10-10 incident (section 2.2) was a misuse of the mass-ban tool by a mod. The owner wrote: "WE MUST ENSURE THAT USERS NEVER GET BANNED FOR THE WRONG REASON".

This section asks one question. What can one stolen or abusing account do with our moderation tools, and what stops it? Token custody is a different topic. It is in section 10. This section does not repeat it.

How to read this section:

- Code paths start at `server/src/`. `Api/` means `NomNomzBot.Api/`. `Infra/` means `NomNomzBot.Infrastructure/`.
- Code facts come from the research note `gap-account-takeover.md`, which gives file and line.
- No incident of a hijacked mod abusing ban power was found. All three incidents found are hijacked streamer accounts that Twitch then banned. So the mod-abuse risk is a design inference from Discord and anti-nuke practice. It is not a Twitch incident.
- Every item the research could not read is a blocked row in the ledger. It is not a claim in this section.

### 12.1 What a stolen or abusing account can do today

| Power | Who holds it | Limit today | Evidence (file:line) |
|---|---|---|---|
| Mass ban, up to 1000 targets, across channels | Any mod. The action `moderation:ban` has a plain Mod floor. | 1000 targets per request. 120 writes per minute per principal. Held while the channel is live, unless the channel is "attacked", own or offline. The operator must be a Twitch mod of the channel. | `Api/Controllers/V1/ModerationController.cs:337`; `Infra/Content/Identity/ActionDefinitionSeeder.cs:203`; `Infra/Moderation/MassBan/MassBanConsentService.cs:29`; `Api/RateLimiting/WriteCheapRateLimitPolicy.cs:23-35`; `MassBanChannelPlanner.cs:275`; `MassBanExecutor.cs:108-146`; `MassBanChannelPlanner.cs:53` |
| Network nuke | Lead mod and up. `moderation:nuke` is Critical and not delegable. | The client must send `RequireConfirmation`. Super mod is re-checked on each channel. Audit: one batch row, one `moderation_action` row per channel, one event. One-shot revert. | `ActionDefinitionSeeder.cs:214`; `Infra/Moderation/NetworkNukeService.cs:56-60`; `NetworkNukeService.cs:112-121`; `NetworkNukeService.cs:130,139-148`; `NetworkNukeService.cs:155` |
| Ban in every moderated channel (`all_moderated`) | Any mod. Same `moderation:ban` gate. | Loops over the Twitch-listed channels, one by one. A failure writes a log line only. | `ModerationController.cs:110-164`; `Infra/Moderation/OperatorNetworkBanService.cs:200-240` |
| Add or remove mods | Broadcaster only. Critical and not delegable. | A lead mod cannot add mods. | `ActionDefinitionSeeder.cs:237` |
| Block a user network-wide | Platform admin | The block is checked before the level bar. | `Infra/Identity/ActionAuthorizationService.cs:60-75`; `Api/Controllers/V1/AdminTrustSafetyController.cs` |
| Act as another user (impersonation) | Platform admin with `user:impersonate` | Written justification. Audit row. Open, time-boxed grant. Target must belong to the grant's channel. Streamer gets an inbox notice. The token never outlives the grant. | `Infra/Identity/PlatformAdminService.cs:387-470`; `Infra/Identity/ImpersonationTokenMinter.cs:52-63`; `Infra/Identity/ImpersonationSessionService.cs:126-137`; `Infra/Notifications/Sources/SecurityNoticeSource.cs:30-45` |
| Mass ban in every opted-in channel, without a roster entry | Platform principal (system owner) | The channel must have `AcceptsModeratorMassBans`. The batch runs on the streamer's token. | `MassBanChannelPlanner.cs:125-146`; `MassBanChannelPlanner.cs:137` |
| Dashboard session | Whoever holds the session or JWT | Access token lives 1 hour. Refresh and session live 30 days. Refresh reuse revokes the whole lineage and emits an event. Each request checks the session id. Sensitive writes: 3 per minute. | `Infra/Identity/SessionService.cs:35-37`; `SessionService.cs:140-157`; `Api/Authentication/DashboardJwtBearer.cs:48-60`; `Api/RateLimiting/SecuritySensitiveRateLimitPolicy.cs:27` |
| Automation API key | Whoever holds the key | Stored only as a SHA-256 hash. Scopes. Optional pipeline allowlist. Limits per minute per key: invoke 60, chat 20, read 120. | `Infra/AutomationApi/AutomationApiTokenService.cs:34,117-120,266-276`; `Infra/AutomationApi/AutomationCommandService.cs:43-46,93-114`; `Infra/AutomationApi/AutomationTokenAuthenticator.cs:40-80` |

What works and stays:

- Only the broadcaster can add mods (`ActionDefinitionSeeder.cs:237`).
- Refresh reuse is detected and the lineage is revoked (`SessionService.cs:140-157`).
- Each request checks the session id (`DashboardJwtBearer.cs:48-60`).
- API keys are hashed (`AutomationApiTokenService.cs:266-276`).
- Impersonation grants have a time box (`PlatformAdminService.cs:418-430`).

### 12.2 Findings, ranked by harm

| # | Finding | Evidence (file:line or URL) | Harm | Slice ID |
|---|---|---|---|---|
| 1 | No second check for big writes. A stolen session, a stolen API key or an impersonated session can start a mass ban or a nuke. A search for step-up, re-auth, MFA, TOTP, new-device and login-notice finds only the integration `needs_reauth` status. | `Api/Controllers/V1/ModerationController.cs:337`; `Domain/Identity/Enums/AuthEnums.cs:110`; Discord guild field `mfa_level`: https://docs.discord.com/developers/resources/guild | One thief can ban up to 1000 accounts in all channels. Innocent viewers are banned. | S-MOD-BAN-RATE-STEPUP |
| 2 | No ban budget per operator. Up to 1000 targets can be sent again and again. The only limit is 120 writes per minute, which is not a ban budget. Nothing reacts to a burst: no quarantine, no demotion. A streamer cannot stop a mod inside NomNomzBot, only on Twitch. | `MassBanConsentService.cs:29`; `Api/RateLimiting/WriteCheapRateLimitPolicy.cs:23-35`; https://docs.securitybot.gg/anti-nuke/anti-nuke/changing-limits | A rogue or hijacked mod keeps banning until someone notices. | S-MOD-BAN-RATE-STEPUP |
| 3 | Mass ban sits on the plain Mod floor. Nuke sits on the Lead floor and is Critical. Mass ban can reach as many channels as a nuke. | `ActionDefinitionSeeder.cs:203` against `ActionDefinitionSeeder.cs:214` | The weaker role holds the larger power. | S-MOD-MASSBAN-FLOOR |
| 4 | Two guards are set by the caller. The nuke "confirmation" is a boolean the client sends. The "attacked" list comes from the request body and skips the live hold. A thief controls both. Nuke has no count cap and no cooling-off. `all_moderated` has no cap and no pause between legs. | `NetworkNukeService.cs:56-60`; `MassBanChannelPlanner.cs:73-76,197-199`; `OperatorNetworkBanService.cs:200-240` | The two controls that look like guards do not guard. | S-MOD-NUKE-CAPS-AUDIT; S-MOD-ATTACKED-PROOF |
| 5 | Impersonation has no limit on what the act-as token may do. A search for `ActorClaim`, `IsImpersonat` and `IsActAs` finds no authorization code. By reading, an impersonated session can call ban, mass ban and nuke with the target's rights. This was read in code. It was not run live. | `Infra/Platform/Auth/JwtTokenService.cs:41,123`; `Infra/Platform/Auth/CurrentUserService.cs:50-70`; `PlatformAdminService.cs:387-470` | Support access becomes a path to a mass ban. | S-MOD-IMPERSONATION-LIMITS |
| 6 | The streamer gets no notice on most paths. Attacked, own and offline batches, network bans and the start of impersonation send nothing outside the dashboard inbox. `MassBanFailureSource.cs` tells the operator only. | `MassBanConsentService.cs:342-397`; `MassBanExecutor.cs:231`; `SecurityNoticeSource.cs:30-45`; Wick "notifies the owners immediately": https://docs.wickbot.com/intro/features/ | The streamer learns about a ban storm late or never. | S-MOD-NOTICE-ALL-PATHS |
| 7 | The audit trail is uneven. Nuke has a record per channel. `all_moderated` writes a log line only. A batch on the streamer's token shows the streamer, not the mod, as `moderator_id` on Twitch's side. Our batch row is the only record of who asked. For act-as calls, the mod-log row most likely names the target as actor (an inference from the claim that the minter sets). | `NetworkNukeService.cs:130,139-148`; `OperatorNetworkBanService.cs:200-240`; `MassBanChannelPlanner.cs:182`; `MassBanExecutor.cs:203-211`; https://dev.twitch.tv/docs/api/reference/#ban-user | After an abuse, nobody can prove who did it. | S-MOD-NUKE-CAPS-AUDIT; S-MOD-ACTOR-AUDIT |
| 8 | An admin cannot revoke every session of one user. The network block exists but does not end sessions. A stolen access token works up to 1 hour. Only the user can log out everywhere. | `ActionAuthorizationService.cs:60-75`; `SessionService.cs:35`; `Api/Controllers/V1/AuthController.cs:1368-1408` | The kill switch is thin. A noticed theft keeps working. | S-MOD-REVOKE-SESSIONS |
| 9 | A stolen automation key has few brakes. `ExpiresAt` is optional. No IP or device binding. No alert on strange use. The invoke path re-checks rights only for music-control steps. The ban pipeline action shows no authorization hit in a search. So a ban step run by a stolen key is limited only by the allowlist and 60 per minute. | `AutomationApiTokenService.cs:117-120`; `AutomationTokenAuthenticator.cs:40-80`; `AutomationCommandService.cs:43-46,110`; `Infra/Moderation/PipelineActions/BanAction.cs` | A leaked key can run bans for as long as it lives. | S-MOD-APIKEY-LIMITS |
| 10 | Opt-in and batch visibility are weak. The "streamer's word" opt-in is any free-text note. A streamer cannot list batches made by mods. `ListBatchesAsync` shows the caller's own. | `MassBanConsentService.cs:149-201`; `MassBanConsentService.cs:222-260` | A mod can claim consent. The streamer cannot see what was done in the channel. | S-MOD-OPTIN-VISIBILITY |
| 11 | Nothing watches a hijacked streamer account. The broadcaster can do all of the above and add mods. Twitch bans a compromised channel quickly (Kai Cenat 2025-04-03; Jynxzi 2025-07-17, restored after 41 minutes; Xyp9x 2024-11-07). So our tokens go dead and the channel returns later. In all three incidents the attacker changed the channel. None used ban power on viewers. | https://www.dexerto.com/twitch/kai-cenat-twitch-channel-hacked-by-hacker-3175300/ ; https://www.dexerto.com/twitch/jynxzi-banned-on-twitch-after-account-gets-hacked-3227768/ ; https://www.dexerto.com/twitch/cs-pro-xyp9x-banned-from-twitch-after-hacker-steals-account-2974591/ ; `ActionDefinitionSeeder.cs:237` | A hijacker adds a mod, and that mod starts bans. The channel also goes dark with no warning to co-owners. | S-MOD-OWNER-WATCH |

### 12.3 What others do

| Product | Control | Source URL |
|---|---|---|
| Security Bot | Counts bans per user in a fixed 10-minute window. At limit 3, the 4th ban is punished. Suggested ban limit: 2-3 for small servers, 5-10 for large ones. | https://docs.securitybot.gg/anti-nuke/anti-nuke/changing-limits |
| Security Bot | Punishment options: Ban, Kick, Remove Roles, Quarantine. "Quarantine to investigate first". "Remove roles is reversible". | https://docs.securitybot.gg/anti-nuke/anti-nuke/changing-punishments |
| Security Bot | Trusted staff go on a whitelist. This replaces raising the limits. The limits count dangerous actions per actor, so bot actors are covered too. | https://docs.securitybot.gg/anti-nuke/anti-nuke |
| Wick | Quarantines a rogue admin and tells the owners at once. | https://docs.wickbot.com/intro/features/ |
| Wick | Panic Mode locks the server. Only the owner keeps authority. A restore system rolls back damage. Our nuke revert is the same idea for bans. | https://docs.wickbot.com/intro/features/ |
| Discord | A server owner can require 2FA before any admin action (ban, kick, manage channels). | https://discord.com/safety/360043653152-four-steps-to-a-super-safe-server |
| Discord | Ban, kick, administrator, manage channels, manage roles and others need a 2FA owner account when server-wide 2FA is on. | https://discord.com/developers/docs/topics/permissions |
| Discord | One owner switch with two states: `mfa_level` NONE or ELEVATED ("a 2FA requirement for moderation actions"). | https://docs.discord.com/developers/resources/guild |
| Discord | Lists server 2FA as a baseline setting for verified servers. | https://support.discord.com/hc/en-us/articles/115001987272-Verified-Server-Moderation-Guidelines |
| Discord | MFA options: passkeys and security keys ("phishing resistant"), authenticator app, SMS ("better than no MFA, but not by a lot"). Support cannot remove MFA from an account. | https://support.discord.com/hc/en-us/articles/219576828-Setting-up-Multi-Factor-Authentication |
| Twitch | The Ban User call carries `moderator_id`. It must match the token user. The response names the moderator. | https://dev.twitch.tv/docs/api/reference/#ban-user |
| Twitch | After a takeover: change password, check email, secure email, turn on 2FA, remove third-party connections. The page says nothing about moderators or role limits. | https://safety.twitch.tv/s/article/Combatting-Account-Takeover?language=en_US |
| Twitch | Help page: "Sign Out Everywhere", "Remove Connections" for third-party apps, check payout method. The page says nothing about mods the attacker added. | https://help.twitch.tv/s/article/account-hacked |
| Twitch (incident) | Kai Cenat, 2025-04-03: attacker replaced photo and header. Twitch showed the channel as banned. Restored 2025-04-04. | https://www.dexerto.com/twitch/kai-cenat-twitch-channel-hacked-by-hacker-3175300/ ; https://www.sportskeeda.com/us/streamers/news-kai-cenat-s-twitch-account-restored-day-hacked ; https://www.yardbarker.com/entertainment/articles/top_streamer_kai_cenat_has_his_twitch_account_hacked_banned/s1_17456_42004481 |
| Twitch (incident) | Jynxzi, 2025-07-17: attacker changed the broadcast title. Twitch banned the account. Access returned after 41 minutes. | https://www.dexerto.com/twitch/jynxzi-banned-on-twitch-after-account-gets-hacked-3227768/ ; https://www.primetimer.com/news/twitch-star-jynxzi-banned-account-hacked-livestream |
| Twitch (incident) | Xyp9x, 2024-11-07: attacker renamed the channel to a random string. Twitch banned it. A rename plus a ban looks like a channel that vanished. | https://www.dexerto.com/twitch/cs-pro-xyp9x-banned-from-twitch-after-hacker-steals-account-2974591/ |

Lessons for us:

- Discord gates the dangerous verbs behind 2FA. Twitch has no equal gate for mods. So our own step-up has real value. Prefer a passkey or an authenticator app over SMS or email codes. Keep recovery self-service.
- Anti-nuke bots count dangerous actions per actor per window. They punish at the limit. Quarantine is the reversible punishment. For us, quarantine means: hold the batch and ask the streamer.
- After a streamer recovers an account, Twitch tells the owner to remove third-party apps. Our bot must then see the dead token and ask the owner to reconnect. It must not keep acting on old authority.

### 12.4 Fix slices

| Slice | What it does | Done-when | Rests on |
|---|---|---|---|
| S-MOD-BAN-RATE-STEPUP | Adds a rolling ban budget per operator and per channel. The budget is lower for a Mod than for a Lead. Over budget, the batch is held and the streamer gets a decision item. Mass ban, nuke and network ban need a fresh step-up proof (passkey or authenticator code). A per-channel switch turns the rule on, like Discord `mfa_level`. A login from a new device creates a notice for the user. | Tests with a fake Twitch client. (1) A Mod sends 1000 targets twice in one window. The second batch is held. No ban call reaches the fake client. The streamer has a decision item. (2) Mass ban, nuke and network ban with no fresh proof return a typed refusal. Nothing is sent. With proof they run. (3) A login from a new device writes a notice row. | Findings 1, 2. Security Bot limits and punishments. Discord `mfa_level`. |
| S-MOD-MASSBAN-FLOOR | Gives mass ban its own action key. The key sits above plain Mod and is not delegable, like `moderation:nuke`. | A test seeds a plain Mod. A mass-ban request is refused. A Lead is allowed. The seeded action row shows Critical and not delegable. | Finding 3. |
| S-MOD-NUKE-CAPS-AUDIT | Moves the nuke confirmation to the server (a one-time token the server issues, not a client boolean). Adds a count cap and a cooling-off. Adds a cap, a pause between legs, an audit row per leg and an event for `all_moderated`. | Tests. A nuke with the client flag set but no server token is refused. A request over the cap is refused. `all_moderated` over a fake list of channels writes one audit row per leg and one event. A failed leg writes a row with the failure. | Finding 4, 7. |
| S-MOD-ATTACKED-PROOF | Stops the request body from declaring a channel "attacked". The server decides from its own evidence. | A test sends a body that lists a live channel as attacked, with no server-side evidence. The live hold still applies. With evidence, the hold is skipped and the reason is stored. | Finding 4. |
| S-MOD-IMPERSONATION-LIMITS | Refuses mass ban, nuke, network ban and mod changes for an act-as token, or asks the streamer's yes first. | Tests. An act-as token calls each of the four actions. Each is refused, or held until the streamer approves. A normal token for the same user still works. | Finding 5. |
| S-MOD-NOTICE-ALL-PATHS | Sends a notice to the streamer on every path: attacked, own and offline batches, nuke, network ban, and the start of impersonation. The notice goes beyond the dashboard inbox (channel and form decided in the slice). | Tests. Each path writes a notice for the channel owner with the actor, the count and the time. The notice for impersonation names the platform admin. A path with no notice fails the test. | Finding 6. Wick owner notice. |
| S-MOD-ACTOR-AUDIT | Every leg of every batch stores the requesting person and, for an act-as call, the operator behind it. This holds also when the batch runs on the streamer's token. | Tests. A batch on the streamer's token stores the mod as requester. An act-as ban stores both the target and the operator. A read of the mod log shows both. | Finding 7. Twitch Ban User `moderator_id` rule. |
| S-MOD-REVOKE-SESSIONS | Adds an admin action "revoke every session and every API key of user X". The network block calls it. | Tests. After the action, a request with the user's old access token is refused. A refresh with the old refresh token is refused. The user's API keys return an auth failure. An audit row names the admin. | Finding 8. |
| S-MOD-APIKEY-LIMITS | Gives every key a maximum life. Adds an alert on strange use. Puts a ban budget on pipeline ban steps run by a key. | Tests. A key made with no expiry gets the maximum life. A key past its life is refused. A pipeline with a ban step, run by a key, stops at the ban budget. | Finding 9. |
| S-MOD-OPTIN-VISIBILITY | Records who gave the "streamer's word" opt-in, and when. Lets the streamer list all batches made in their channels, including those made by mods. | Tests. An opt-in stores the actor and the time. The streamer's batch list returns a batch made by a mod. A mod's list does not return other channels' batches. | Finding 10. |
| S-MOD-OWNER-WATCH | Warns the owner and co-owners when a mod is added or the mod list changes. Shows a "mods changed recently" notice. Makes the integration survive a channel that goes dark and returns, with no manual re-setup. | Tests. A fake mod-added event writes a notice for the owner. A dead token marks the connection `needs_reauth` and shows a reconnect request. After a new token, the old EventSub setup recovers and the channel works. | Finding 11. Twitch help page. Incidents. |

### 12.x Sources

https://docs.securitybot.gg/anti-nuke/anti-nuke
https://docs.securitybot.gg/anti-nuke/anti-nuke/changing-limits
https://docs.securitybot.gg/anti-nuke/anti-nuke/changing-punishments
https://docs.wickbot.com/intro/features/
https://discord.com/safety/360043653152-four-steps-to-a-super-safe-server
https://discord.com/developers/docs/topics/permissions
https://docs.discord.com/developers/resources/guild
https://support.discord.com/hc/en-us/articles/115001987272-Verified-Server-Moderation-Guidelines
https://support.discord.com/hc/en-us/articles/219576828-Setting-up-Multi-Factor-Authentication
https://dev.twitch.tv/docs/api/reference/#ban-user
https://safety.twitch.tv/s/article/Combatting-Account-Takeover?language=en_US
https://help.twitch.tv/s/article/account-hacked
https://www.dexerto.com/twitch/kai-cenat-twitch-channel-hacked-by-hacker-3175300/
https://www.dexerto.com/twitch/jynxzi-banned-on-twitch-after-account-gets-hacked-3227768/
https://www.dexerto.com/twitch/cs-pro-xyp9x-banned-from-twitch-after-hacker-steals-account-2974591/
https://www.sportskeeda.com/us/streamers/news-kai-cenat-s-twitch-account-restored-day-hacked
https://www.yardbarker.com/entertainment/articles/top_streamer_kai_cenat_has_his_twitch_account_hacked_banned/s1_17456_42004481
https://www.primetimer.com/news/twitch-star-jynxzi-banned-account-hacked-livestream

## 13. Multi-platform moderation: Twitch, Kick, YouTube, X Live and EventSub

### 13.0 Why this section exists

NomNomzBot is one channel with many platform connections: Twitch, Kick, YouTube and X Live at the same time.
The owner goal is streamer safety on all platforms.
This section lists what each platform lets a bot do, what our code does today, and what is missing.
Date read: 2026-10-10. Official docs only, read in a real browser.

Path note. Code paths are under `server/src/`. A leading `Infrastructure/` stands for `NomNomzBot.Infrastructure/`. Same for `Application/`, `Domain/` and `Api/`. In 13.2, paths are under `server/src/NomNomzBot.Infrastructure/` unless a project name is given.

### 13.1 Moderation power per platform

| Capability | Twitch | Kick | YouTube | X Live | Ours today (evidence file:line) |
|---|---|---|---|---|---|
| Ban | Ban User, `moderator:manage:banned_users` | `POST /public/v1/moderation/bans` (no duration), `moderation:ban`, reason max 100 | `liveChatBans.insert`, type `permanent`, owner or moderator only, cannot ban owner or another moderator, quota 200 | None found | Twitch `NomNomzBot.Infrastructure/Platform/Transport/Helix/SubClients/TwitchModerationApi.cs:35`. Kick `Infrastructure/Chat/Kick/KickApiClient.cs:127`, `KickChatPlatform.cs:95`. YouTube `Infrastructure/Chat/YouTube/YouTubeChatPlatform.cs:113`, `YouTubeLiveChatClient.cs:206` |
| Timeout | Ban User with `duration` (1 to 1,209,600 s) | Same endpoint with `duration` 1 to 10080 minutes | `liveChatBans.insert`, type `temporary`, `banDurationSeconds` (default 300) | None found | Twitch `TwitchModerationApi.cs:42`. Kick `KickApiClient.cs:106`, `KickChatPlatform.cs:73` (seconds to minutes, ceiling). YouTube `YouTubeChatPlatform.cs:103` |
| Unban | Unban User, `moderator:manage:banned_users`. Get Banned Users lists bans | `DELETE /public/v1/moderation/bans` by (broadcaster, user). Lifts ban and timeout | `liveChatBans.delete` by ban ID only. No list method for bans. A moderator cannot lift a ban from another moderator. Quota 200 | None found | Twitch `TwitchModerationApi.cs:191`, `:239` (list). Kick `KickApiClient.cs:146`, `KickChatPlatform.cs:119`. YouTube `YouTubeChatPlatform.cs:120` (needs our own ledger of ban ids), `YouTubeLiveChatClient.cs:263` |
| Delete one message | Delete Chat Messages, `moderator:manage:chat_messages` | `DELETE /public/v1/chat/{message_id}`, `moderation:chat_message:manage` | `liveChatMessages.delete`, owner or moderator, quota 20. Fails for messages by a moderator or other protected user | None found | Twitch `TwitchModerationApi.cs:653` (one), `:659` (all). Kick `KickApiClient.cs:92`, `KickChatPlatform.cs:171`. YouTube `YouTubeChatPlatform.cs:179`, `YouTubeLiveChatClient.cs:276` |
| Chat modes (slow, followers, subscribers, emote-only) | Update Chat Settings, `moderator:manage:chat_settings` | None. Channels PATCH takes only `category_id`, `custom_tags`, `stream_title` | None to set. Chat only emits `sponsorOnlyModeStartedEvent` and `sponsorOnlyModeEndedEvent` | None found | Twitch only: `Infrastructure/Moderation/Lockdown/TwitchLockdownAdapter.cs:131`. No Kick or YouTube lockdown adapter (the `Lockdown/` folder holds only the Twitch one) |
| Blocked terms | Get, Add, Remove Blocked Term, `moderator:manage:blocked_terms` | None | None | None found | Twitch `TwitchModerationApi.cs:439`, `:516`, `:583`. Kick and YouTube: none, and no API |
| AutoMod-like hold queue | Check AutoMod Status, Manage Held AutoMod Messages, Get and Update AutoMod Settings, `moderator:manage:automod` | None | None | None found | Twitch `TwitchModerationApi.cs:1085`, `:1117`, `:1149`, `:1178`. Kick and YouTube: none. Our own hold logic would run inside the bot |
| Moderator add or remove | Add and Remove Channel Moderator, `channel:manage:moderators`, 10 per 10 s | None. The owner uses `/mod` in chat | `liveChatModerators.insert` and `.delete` (owner only, quota 200 each), `.list` (owner only, quota 50) | None found | Twitch `Infrastructure/Identity/EventHandlers/BotJoinOnOnboardingHandler.cs:164`. YouTube: no code (grep for `liveChatModerators` and `liveChat/moderators` found nothing). Kick: no API |
| Suspicious-user marks | Add and Remove Suspicious Status, `moderator:manage:suspicious_users` | None | None | None found | Twitch `TwitchModerationApi.cs:937`, `:1014`. Others: none |
| Warn, Shield mode, unban requests | Warn Chat User, Shield Mode, Get and Resolve Unban Requests | None | None | None found | Twitch `TwitchModerationApi.cs:869`, `:741`, `:802`, `:273`, `:354`. Others: none |
| Event: ban or timeout | EventSub `channel.ban` | Webhook `moderation.banned` (`expires_at` null = permanent) | Chat item `userBannedEvent` (`banType` permanent or temporary) | None found | Kick subscribe `Infrastructure/Chat/Kick/KickEventSubscriptionWorker.cs:54`, handler `KickWebhookIngest.cs:595`. YouTube: translator `YouTubeLiveChatEventTranslator.cs:54` to `:126` lists only money and member types. `userBannedEvent` has no hit in `server/src` |
| Event: unban | EventSub `channel.unban` | None in the 10 event types | None found | None found | No source event on Kick or YouTube |
| Event: message deleted | EventSub `channel.chat.message_delete`, `channel.chat.clear`, `channel.chat.clear_user_messages` | None | `tombstone` item marks a deleted message. It is not sent at deletion, only shown later in the list. No `messageDeletedEvent` found | None found | Kick and YouTube: not handled. `tombstone` has no YouTube hit in `server/src` |
| Event: chat message with role | EventSub `channel.chat.message` | `chat.message.sent`, sender badge types (`moderator`, `subscriber`, `sub_gifter`) | `authorDetails.isChatModerator` | None found | Kick `KickWebhookIngest.cs:276` (IsModerator from badges) |
| Event: other moderation | `channel.moderator.add` and `.remove`, `channel.chat_settings.update`, `channel.shield_mode.*`, `channel.warning.*`, `channel.unban_request.*`, `channel.moderate` | None | Only sponsor-only mode start and end items | None found | Twitch side in 13.2 |
| Rate limits | Add and remove mod: 10 per 10 s. Token bucket, 1 point per call, 429 when empty. Ban User: 429 "per minute for this broadcaster", no number published | Chat POST lists 429. Moderation pages list 400, 401, 403, 500 only. No per-endpoint limit found. Subscriptions: 10,000 per event type per app, `chat.message.sent` 1,000 for unverified apps | Quota 10,000 units per day per project, reset midnight PT. Ban insert and delete 200, moderator insert and delete 200, moderator list 50, message delete 20, message list 5, streamList 5 | Window 15 minutes. Mute POST and DELETE 50 per 15 min per user. Hide reply 50 per 15 min per user. These act on accounts and posts, not on live chat | Twitch: `Infrastructure/Platform/Transport/Helix/TwitchRateLimiter.cs:34-52` (bucket per key), `TwitchRateLimitHandler.cs:57` (429 as hard limit). Kick and YouTube quota: no budget in code |
| Account-level extras | n/a | n/a | n/a | Block (`POST /2/users/:id/blocking`) is Enterprise only. Mute and hide reply are not Enterprise-marked | None. These do not apply to live chat |
| Scopes | `user:bot` also needed for an app token | `moderation:ban`, `moderation:chat_message:manage`, `chat:write`, `events:subscribe`, `channel:read` | `youtube` or `youtube.force-ssl` for all writes. `liveChatModerators.list` and `liveChatMessages.list` also take `youtube.readonly` | n/a | Kick `Infrastructure/Integrations/OAuthProviderRegistry.cs:110-111`, `:138`. YouTube `OAuthProviderRegistry.cs:225-236`: set `youtube.manage` = `youtube` + `youtube.force-ssl` covers ban, unban and moderator list, insert, delete |

X Live, said plainly. I found no chat-moderation API for X Live broadcasts.
The X API index (`docs.x.com/x-api/llms.txt`) has no broadcast or live video section.
"Chat" in that index is X Chat: encrypted direct messages. "Spaces" is live audio lookup.
Conclusion: X Live is not a chat platform the bot can read, write or moderate through the public API.
The project rule is "implement, never remove". So the X Live state is "not available", and the docs are read again before each release.

The common set across Twitch, Kick and YouTube is small: ban, timeout, unban, delete message.
Everything else in the table is Twitch-only in the API.

What others do. Streamer.bot has one sub-action per platform. Kick: Ban, Timeout, Unban, UnTimeout. YouTube: Ban, Timeout. StreamElements says on Kick: "Can the bot automatically ban or timeout users? No, not automatically, for now, but it's on the roadmap". Restream filters only hide messages in Restream Chat, not in the chat on the streaming channels. No product found unifies moderation across platforms with honest capability flags.

### 13.2 EventSub moderation topics

Every topic below is subscribed (`BackgroundServices/BotLifecycleService.cs`) and translated (`Platform/Eventing/Translators/`). The table lists 23 topics when add and remove, and begin and end, count as separate topics.
Every EventSub notification is stored in the event journal before the typed fan-out (`Platform/Eventing/NotificationDispatcher.cs:104-117` and `:158`). The journal has no event-type filter (`EventStore/EventJournalService.cs:54-110`). So the five topics with no consumer are stored. No screen reads them.

| Topic | Consumer in our code | What the mod sees | Gap | Slice ID |
|---|---|---|---|---|
| `channel.moderate` (v2) | `NomNomzBot.Api/Hubs/Broadcasters/ChatModerationBroadcastHandlers.cs:127-160` (`delete` only). `Stream/EventHandlers/OutgoingRaidStartedAlertHandler.cs:32,66` (`raid` only) | A deleted-message push on the dashboard. A raid alert | Every other action (ban, timeout, warn, mod, vip, chat-mode and term changes, unban request actions) has no consumer from this event. It is stored only | S-MOD-EVENTSUB-CONSUMERS |
| `channel.ban` | `Moderation/EventHandlers/ModerationAlertHandlers.cs:30`, `:72`. `ModerationProjectionHandlers.cs:24`, `:42`. `SharedChatBanSharePublisher.cs:29`. `Widgets/EventHandlers/OverlayModerationRetractionHandler.cs:34`. `NomNomzBot.Api/Hubs/Broadcasters/BanBroadcastHandlers.cs:21`, `:91` | An alert, a history row, an overlay retraction, a dashboard push | None found | none |
| `channel.unban` | `ModerationAlertHandlers.cs:115`. `ModerationProjectionHandlers.cs:61`. `BanBroadcastHandlers.cs:161` | An alert, a history row, a dashboard push | None found | none |
| `channel.unban_request.create` | `Moderation/EventHandlers/UnbanRequestConfigPushHandlers.cs:23` | A config push | None found | none |
| `channel.unban_request.resolve` | `UnbanRequestConfigPushHandlers.cs:37` | A config push | None found | none |
| `channel.moderator.add` and `.remove` | `Identity/EventHandlers/BotModeratorRoleChangeHandler.cs:27`. `ModerationAlertHandlers.cs:154`, `:191`. `NomNomzBot.Api/Hubs/Broadcasters/RoleBroadcastHandlers.cs:64`, `:144` | An alert and a role push on the dashboard | None found | none |
| `channel.warning.send` | `ModerationProjectionHandlers.cs:77` only | A history row. No dashboard push | No live push for a warning | S-MOD-EVENTSUB-CONSUMERS |
| `channel.warning.acknowledge` | `ModerationProjectionHandlers.cs:94`. `NomNomzBot.Api/Hubs/Broadcasters/WarningAcknowledgedBroadcastHandler.cs:22` | A history row and a dashboard push | None found | none |
| `channel.suspicious_user.message` | `Moderation/EventHandlers/SuspiciousUserHandlers.cs:47` | A typed handler runs | None found | none |
| `channel.suspicious_user.update` | `SuspiciousUserHandlers.cs:24` | A typed handler runs | None found | none |
| `channel.shield_mode.begin` and `.end` | `NomNomzBot.Api/Hubs/Broadcasters/ShieldModeBroadcastHandlers.cs:18`, `:43` | A dashboard push only | Not in the mod log | S-MOD-EVENTSUB-CONSUMERS |
| `automod.message.hold` (v2) | `Moderation/EventHandlers/ModerationQueueEventHandlers.cs:25`. `NomNomzBot.Api/Hubs/Broadcasters/AutoModQueueBroadcastHandlers.cs:24` | A queue entry and a dashboard push | None found | none |
| `automod.message.update` (v2) | `ModerationQueueEventHandlers.cs:40`. `ModerationProjectionHandlers.cs:110`. `AutoModQueueBroadcastHandlers.cs:49` | A queue update, a history row, a dashboard push | None found | none |
| `automod.settings.update` | None found | Nothing | No consumer. A settings change made outside our bot stays invisible | S-MOD-EVENTSUB-CONSUMERS |
| `automod.terms.update` | None found | Nothing | No consumer. A term change made outside our bot stays invisible | S-MOD-EVENTSUB-CONSUMERS |
| `channel.chat.message_delete` | `Chat/EventHandlers/ChatDeletionStoreHandler.cs:26`. `OverlayModerationRetractionHandler.cs:32`. `ChatModerationBroadcastHandlers.cs:71` | Overlay retraction and a dashboard push | None found | none |
| `channel.chat.clear` | `ChatDeletionStoreHandler.cs:25`. `ChatModerationBroadcastHandlers.cs:21` | A dashboard push | None found | none |
| `channel.chat.clear_user_messages` | `ChatDeletionStoreHandler.cs:27`. `ChatModerationBroadcastHandlers.cs:168` | A dashboard push | None found | none |
| `channel.chat_settings.update` | None found | Nothing | No consumer. A chat-mode change made outside our bot stays invisible. The lockdown view can be wrong | S-MOD-EVENTSUB-CONSUMERS |
| `channel.chat.user_message_hold` | None found | Nothing | No consumer | S-MOD-EVENTSUB-CONSUMERS |
| `channel.chat.user_message_update` | None found | Nothing | No consumer | S-MOD-EVENTSUB-CONSUMERS |

Version map: `Platform/Eventing/EventSubConditionBuilder.cs:111-127` (`channel.moderate` and `automod.message.*` are v2). Moderator-plane conditions: `:156-180`. Chat-read conditions: `:183-193`.

### 13.3 Findings, ranked by harm

| # | Finding | Evidence (file:line or URL) | Harm | Slice ID |
|---|---|---|---|---|
| 1 | Lockdown covers Twitch chat only. Kick and YouTube have no chat-mode API. X Live has no chat-moderation API. A Kick or YouTube streamer under attack has no lockdown | `Infrastructure/Moderation/Lockdown/TwitchLockdownAdapter.cs:131`. https://docs.kick.com/apis/channels . https://docs.x.com/x-api/llms.txt | A raid on a non-Twitch channel gets no automatic protection | S-MOD-LOCKDOWN-MULTI |
| 2 | YouTube quota is 10,000 units a day per project. Ban and unban cost 200. Delete costs 20. List costs 5. 500 deletes use the whole day and leave nothing for polling. The code has no quota budget | https://developers.google.com/youtube/v3/determine_quota_cost . `Infrastructure/Chat/YouTube/YouTubeLiveChatClient.cs:206`, `:263`, `:276` | The bot goes blind on YouTube in the middle of an attack | S-MOD-YOUTUBE-GAPS |
| 3 | Five EventSub topics have no consumer: `automod.settings.update`, `automod.terms.update`, `channel.chat_settings.update`, `channel.chat.user_message_hold`, `channel.chat.user_message_update`. They are stored in the journal and shown nowhere | `Platform/Eventing/Translators/AutoModTranslators.cs:177`, `:229`. `ChatTranslators.cs:464`, `:501`, `:531`. `Platform/Eventing/NotificationDispatcher.cs:104-117` | A mod cannot see a chat-mode or AutoMod change made outside our bot | S-MOD-EVENTSUB-CONSUMERS |
| 4 | `channel.moderate` has consumers only for `delete` and `raid`. Warning and shield mode reach only a history row or a dashboard push | `NomNomzBot.Api/Hubs/Broadcasters/ChatModerationBroadcastHandlers.cs:138-143`. `ModerationProjectionHandlers.cs:77`. `ShieldModeBroadcastHandlers.cs:18` | The mod log is incomplete for most Twitch moderation actions | S-MOD-EVENTSUB-CONSUMERS |
| 5 | Mass-ban pace is a constant: 25 bans per 5 s tick, up to 300 a minute, no delay between calls. The code reads no `Ratelimit-*` header and keeps no per-broadcaster counter. Twitch publishes no number for Ban User. Kick publishes no limit | `Moderation/MassBan/MassBanExecutionWorker.cs:24`, `:27`, `:29`. `Moderation/MassBan/MassBanExecutor.cs:70-88`. https://dev.twitch.tv/docs/api/reference/#ban-user . https://dev.twitch.tv/docs/api/guide/ . https://docs.kick.com/llms.txt | Bans meet 429 and retry. After 5 tries a target closes with "Gave up after N attempts" (`MassBanExecutor.cs:182`, `MassBanRetryPolicy.cs:25`, `:40`) | S-MOD-RATE-HEADERS |
| 6 | YouTube `userBannedEvent` and `tombstone` are not translated. YouTube has no list of bans. A ban made outside our bot (for example in YouTube Studio) is invisible and cannot be lifted by us. Our ledger knows only bans we made | `Infrastructure/Chat/YouTube/YouTubeLiveChatEventTranslator.cs:54` to `:126`. https://developers.google.com/youtube/v3/live/docs/liveChatBans . `YouTubeChatPlatform.cs:120` | The mod log lies by omission on YouTube | S-MOD-YOUTUBE-GAPS |
| 7 | The unified router has 3 actions (DeleteMessage, Timeout, Ban) and a Supports check. It has no Unban, no ModeratorAdd, no ChatMode member | `Application/Chat/Services/IInboundOriginModerator.cs` | A mod cannot lift a Kick or YouTube ban through one uniform action | S-MOD-ROUTER-UNBAN |
| 8 | YouTube moderator add and remove exist in the API. Our code has no call. The `youtube.manage` scope already covers them. Missing API methods are gaps to add | https://developers.google.com/youtube/v3/live/docs/liveChatModerators . `Integrations/OAuthProviderRegistry.cs:225-236`. grep for `liveChatModerators` and `liveChat/moderators` found nothing | The onboarding step "make the bot a moderator" is automated on Twitch only (`BotJoinOnOnboardingHandler.cs:164`) | S-MOD-YOUTUBE-GAPS |
| 9 | Events are uneven. Kick sends ban and timeout but no unban and no delete event. YouTube has no unban event. The mod log is incomplete on those platforms unless our own bot action writes the log when it acts | https://docs.kick.com/events/event-types . `Infrastructure/Chat/Kick/KickWebhookIngest.cs:595` | A ban lifted in the platform UI stays in our log as active | S-MOD-OWN-ACTION-LOG |
| 10 | The API gives Kick and YouTube no chat modes, blocked terms, hold queue, suspicious marks, warnings, shield mode or unban requests. The dashboard must say "not available on this platform" with the reason, never a switch that does nothing | https://docs.kick.com/apis/moderation . https://developers.google.com/youtube/v3/live/docs/liveChatMessages . `Infrastructure/Chat/Kick/KickApiClient.cs:92-146` | A dead switch gives a false sense of safety | S-MOD-CAPABILITY-FLAGS |
| 11 | On Kick and YouTube, any AutoMod or blocked-terms feature must run inside our bot: read each message, then delete or time out | https://docs.kick.com/apis/moderation . https://developers.google.com/youtube/v3/live/docs/liveChatMessages/delete | Kick and YouTube chats have no filter at all today | S-MOD-PLATFORM-INBOUND-FILTER |
| 12 | A cross-platform ban needs proof that two accounts are one person. A link exists only after OAuth on both. `MergeIdentitiesAsync` has no production caller and demands no proof. A chatter with an unknown (provider, id) gets a new separate user | `Application/Identity/Services/IExternalLoginService.cs:13-22`. `Infrastructure/Identity/UserIdentityService.cs:78`, `:154`, `:320`. https://discord.com/developers/docs/resources/user | A wrong merge would ban a stranger on another platform | S-MOD-CROSS-PLATFORM-BAN |
| 13 | `ModerationProjectionService` resolves the moderator with the subject's provider (`:102`). The rebuild path passes `AuthEnums.Platform.Twitch` hard-coded (`:253`, `:296`). A rebuild would resolve non-Twitch subjects as Twitch | `Moderation/ModerationProjectionService.cs:77`, `:102`, `:253`, `:296` | A rebuilt history can drop or mis-assign Kick and YouTube actions | S-MOD-PROJECTION-PROVIDER |
| 14 | X Live: no chat-moderation API. Mute and block act on the connected account. Block is Enterprise only | https://docs.x.com/x-api/llms.txt . https://docs.x.com/x-api/users/blocks/introduction . https://docs.x.com/x-api/users/mutes/introduction | An X Live streamer has no bot-side safety. The product must say so | S-MOD-CAPABILITY-FLAGS |

### 13.4 Fix slices

| Slice | What it does | Done-when | Rests on |
|---|---|---|---|
| S-MOD-YOUTUBE-GAPS | Translate YouTube `userBannedEvent` and `tombstone`. Add `liveChatModerators` list, insert and delete. Add a daily quota budget with a per-channel cap | Test 1: feed a chat page with a `userBannedEvent` item (permanent and temporary) to the translator. Assert one canonical ban event per item with the ban type and a mod-log row. Test 2: feed a `tombstone` item. Assert the message turns deleted. Test 3: with a fake HTTP handler, call moderator add and remove. Assert a `POST` and a `DELETE` to `liveChat/moderators` with the channel id, and that list parses to channel ids. Test 4: with a quota counter at 9,900 used, a ban (200) is refused with a plain reason and a poll (5) still runs | `OAuthProviderRegistry.cs:225-236` (`youtube.manage` scope), `YouTubeLiveChatClient.cs:206`, S-MOD-OWN-ACTION-LOG |
| S-MOD-EVENTSUB-CONSUMERS | Add a consumer for each of the five topics. Add consumers for the other `channel.moderate` actions keyed by `ActionType`. Add a dashboard push for `WarningSentEvent`. Write shield mode into the mod log. Show all of it live | Test per topic: publish the canonical event (for example `ChatSettingsUpdatedEvent` with slow mode on). Assert a mod-log row with the fields of the payload and a dashboard push with the same fields. Test: `channel.moderate` with action `timeout` writes a log row. Test: a chat-mode change made outside our bot changes the lockdown view state | Event journal (`NotificationDispatcher.cs:104-117`), translators in `Platform/Eventing/Translators/` |
| S-MOD-RATE-HEADERS | Pace mass ban from the live `Ratelimit-*` values and the 429 retry hint. Add a per-broadcaster counter. Add a retry rule for Kick 429 on every call | Test: a fake transport answers the first calls with `Ratelimit-Remaining: 0` and a reset time. Assert the executor sends no further Ban User call for that broadcaster until the reset and then continues. Test: 40 targets on two broadcasters. Assert each broadcaster has its own counter. Test: a Kick 429 on a ban is retried and the target is not closed as failed | `Platform/Transport/Helix/TwitchHelixTransport.cs:341`, `:384`. Whether the transport hands headers to callers is open (ledger) |
| S-MOD-LOCKDOWN-MULTI | Extend lockdown to Kick and YouTube where the API allows. Return, per platform, "applied" or "not available" with the reason. X Live always returns "not available" | Test: start lockdown on a channel with Twitch, Kick and YouTube connections. Assert Twitch gets the chat-settings call. Assert Kick and YouTube each return a result with a stated reason or an applied bot-side filter. Assert X Live returns "not available" with the reason "no chat-moderation API". Assert no platform result is silent | S-MOD-CAPABILITY-FLAGS, S-MOD-PLATFORM-INBOUND-FILTER |
| S-MOD-ROUTER-UNBAN (new) | Add Unban to the unified router. Use the four common actions: ban, timeout, unban, delete message. Add moderator add to the contract with a "not supported" answer for Kick | Test: unban through the router on Twitch, Kick and YouTube. Assert the right platform call per origin. Assert YouTube unban uses the stored ban id and returns a plain error when the ban was made outside our bot | `Application/Chat/Services/IInboundOriginModerator.cs`, `YouTubeLiveChatBanLedger` |
| S-MOD-CAPABILITY-FLAGS (new) | Add a capability table per platform. The dashboard shows "not available on this platform" with the reason for each missing capability. Add a manual "make the bot a moderator" instruction for Kick (`/mod`) and YouTube. Add the date the API docs were last read, and a release check that the X docs have no live chat-moderation section | Test: for a Kick channel, the capability response marks chat modes, blocked terms and hold queue as unavailable with a reason, and the dashboard shows no active switch for them. Test: for an X Live connection every moderation capability is unavailable with a reason | `IInboundOriginModerator.cs`, S-MOD-ROUTER-UNBAN |
| S-MOD-OWN-ACTION-LOG (new) | Write the mod-log row at the moment our own bot acts on Kick and YouTube. Handle unban and delete, which have no source event | Test: a bot ban on Kick writes one log row with actor, subject, reason and platform. A bot unban on Kick closes it. No second row appears when `moderation.banned` arrives for the same ban | `Infrastructure/Chat/Kick/KickWebhookIngest.cs:595`, S-MOD-ROUTER-UNBAN |
| S-MOD-PLATFORM-INBOUND-FILTER (new) | Run blocked terms and hold logic inside the bot for Kick and YouTube chat. Delete or time out on a hit. Respect the YouTube quota budget | Test: a Kick message with a blocked term is deleted through `DELETE /public/v1/chat/{message_id}` and a log row is written. Test: on YouTube with quota nearly used, the filter degrades to a logged "quota low" state and does not delete | S-MOD-YOUTUBE-GAPS, S-MOD-OWN-ACTION-LOG |
| S-MOD-CROSS-PLATFORM-BAN (new) | A ban on several platforms names only ids from an OAuth-proven link. It shows the blast radius per platform before it runs. `MergeIdentitiesAsync` gets a proof requirement for any production caller | Test: a user with proven Twitch and Kick links gets a ban on both ids. A chatter with the same name but no link gets no ban. The preview lists each platform and id. Test: a merge call without proof of both accounts is refused | `Application/Identity/Services/IExternalLoginService.cs:13-22`, `Infrastructure/Identity/UserIdentityService.cs:320` |
| S-MOD-PROJECTION-PROVIDER (new) | The projection rebuild uses the stored provider of each action instead of `Twitch`. The moderator resolves with its own provider | Test: rebuild history from stored Kick and YouTube actions. Assert each subject resolves through its own provider and a history row exists per action. Test: a Kick subject with a Twitch moderator resolves both correctly | `Moderation/ModerationProjectionService.cs:253`, `:296` |

Open items for this section are in the ledger as blocked rows.

### 13.5 Closing results: the last open questions, answered

Each item below was a blocked row in an earlier pass. Each is now read in code (paths under `server/src/`) or on a page that was opened. See the ledger in section 15.

**Rate-limit headers (Q-headers).**
- Twitch: `TwitchRateLimitHandler.cs:83-90` reads `Ratelimit-Limit`, `Ratelimit-Remaining` and `Ratelimit-Reset`. It publishes `TwitchHelixRateLimitedEvent` on a 429 (about lines 55-68). It is wired at `DependencyInjection.cs:1581-1586`. Retry-After is not read for Twitch. A 429 is excluded from the resilience retry (`ResiliencePolicies.cs:53-54`).
- Kick: no header is read. `ResiliencePolicies.cs:333-339` says Kick has no Retry-After contract. The retry at `:349-362` uses plain exponential backoff, 2 tries. `KickApiClient.cs:388-395` maps status codes only.
- Mass ban uses no rate limiter. `MassBanExecutionWorker.cs:24,29` bans 25 users every 5 seconds on a timer, under a run-once lease (`:77`). Retries use `MassBanRetryPolicy.DelayAfter` (`MassBanExecutor.cs:170`).
- Twitch publishes no number for Ban User. The API guide (https://dev.twitch.tv/docs/api/guide) gives only the token-bucket rule: a bucket of points, 1 point per call by default, a 429 when the bucket runs out within one minute. The Ban User reference (https://dev.twitch.tv/docs/api/reference/#ban-user) has one limit line: a 429 when the app "has exceeded the number of requests it may make per minute for this broadcaster". It also returns a 409 when another update is in progress. Unban User has the same 429 text. The only number on the guide page is the example header `Ratelimit-Limit: 800`. So the 25-per-5-seconds timer is our own choice and has no published basis (slice S-MOD-BAN-RATE-STEPUP).

**Outbound forwarding (Q-forward).**
- `ModerationActionTakenEvent` is in the outbound-webhook catalogue (`OutboundWebhookEventCatalogue.cs:118`). `ChatMessageDeletedEvent` is at `:124`. The fan-out handler runs for every journaled event that has a broadcaster (`OutboundWebhookFanoutHandler.cs:42-96`). An endpoint that subscribes to the event name, or to `*`, receives it (`OutboundWebhookDispatcher.cs:48-66`, `Subscribes` at `:424-429`).
- `*` is always allowed at save time (`OutboundWebhookEndpointService.cs:402-416`). The event bus journals every event before delivery (`JournalingEventBusDecorator.cs:20-30`), so the event is journaled.
- Federation has inbound code only. The one handler is `SharedChatBanInboundHandler.cs:35`. No code builds or sends an outgoing federation envelope (`FederationEnvelope.cs:17` is only the record).

**Does a chat-filter delete reach the overlay (Q-overlaydelete).**
- The filter calls the delete API (`ChatFilterExecutionHandler.cs:182-196`). It does not publish `ChatMessageDeletedEvent` itself.
- Twitch echoes the delete as `channel.chat.message_delete` (subscribed at `BotLifecycleService.cs:90`). The translator publishes the event (`ChatTranslators.cs:269-293`). Two handlers then act: `OverlayModerationRetractionHandler.cs:52-70` sends the overlay `Retract`, and `ChatModerationBroadcastHandlers.cs:71-118` sends `MessageDeleted`.
- Answer: yes for Twitch, through the platform echo, while that subscription is live. A delete that arrives as a `channel.moderate` action goes to the dashboard only (`ChatModerationBroadcastHandlers.cs:127-160`).
- The SignalR notifier wins in dependency injection: `AddInfrastructure` registers the Null notifier (`DependencyInjection.cs:607`), and `Program.cs:251-254` registers the adapter later. The last registration is the one used.

**Reply TTS and standing (Q-replytts).** `ReplyTtsPipeline.cs:19` is one `play_tts` step with only the `tts.reply` variable (`:38`). `PlayTtsAction.cs:172-179` reads bits from `user.bits` (else 0) and standing from `user.role` (else "everyone"). `PipelineEngine.cs:281-282` copies only the initial variables. `TtsDispatchService.cs:115-121` rejects with `bits_gate` when `MinBitsToTts` is set; `:127-134` rejects with `standing_gate` (`MeetsStandingFloor`, `:952-975`). So a command reply is refused when the channel sets a bits minimum above 0 or a permission floor above everyone. This was read in code and not run (slice S-MOD-TTS-STANDING holds the test).

**YouTube read scope (Q-ytscope and Q-streamlist).**
- The dashboard asks for the set `youtube.manage` (`IntegrationsScreen.kt:148`). That set is the scopes `youtube` and `youtube.force-ssl` (`OAuthProviderRegistry.cs:231-235`). The set `youtube.readonly` exists (`:236`) but the dashboard never asks for it. The authorize URL is built at `IntegrationOAuthService.cs:159-172`.
- Our code polls the list method (`YouTubeLiveChatClient.cs:101-106`). It never calls `streamList`.
- The `streamList` page (https://developers.google.com/youtube/v3/live/docs/liveChatMessages/streamList) names no scope and has no Authorization section. It states a quota cost of 5 units. The sibling `list` page's try-it widget defaults to `youtube.readonly`. The `insert` page lists `youtube` and `youtube.force-ssl`. The docs therefore do not prove which scope `streamList` needs. The plan should request `youtube.readonly` for reading and prove it live (slice S-MOD-YOUTUBE-GAPS must hold that live test).

**Mixed moderators across platforms (Q-mixedmods).**
- A role is not stored per platform. `RoleResolver.cs:131-230` reads standing, membership and permits by user and channel and takes the highest. A person is one user with one identity row per platform (`UserIdentityService.cs:152-232`). A Twitch moderator counts on Kick or YouTube only when those accounts are linked to the same user.
- The dashboard ban path is Twitch-only. `ModerationService.cs:152` resolves the broadcaster's Twitch id, and `:164-170` calls `BanAsOperatorAsync` with the operator's own Twitch token. Timeout uses the same pattern (`:94`, `:106`). Twitch refuses the call if that person is not a Twitch moderator there. No dashboard ban, timeout or unban reaches Kick or YouTube through this service. Kick and YouTube actions go only through the origin router (three actions, section 13.1).
- Gap: the dashboard does not say that a ban is Twitch-only. Slices S-MOD-CAPABILITY-FLAGS and S-MOD-CROSS-PLATFORM-BAN hold it.

**Voice command output (Q-voicebuiltin).** `VoiceBuiltin.cs` never calls chat, TTS or overlay directly. It replies through the composer (`BuiltinResponseComposer.cs:45-85`), which censors and escapes nothing. A viewer's raw query is echoed in the reply. The chat path is `ChatMessageHandler.cs:993-1060` and `:753-785`. A voice reply is not spoken (only `QuoteBuiltin.cs:139` reads `SpeakWithTts`). The bot's own lines never reach the overlay chat: every ingest path drops them through `BotSelfEchoGuard` (`ChatTranslators.cs:219-230`, `KickWebhookIngest.cs:243`, `YouTubeLiveChatPollWorker.cs:542`). One risk is open in code: the template resolver substitutes first and runs transforms after (`TemplateResolver.cs:166`), so a viewer who types a transform such as `{transform.upper:text}` gets it applied. The "reverse" transform can turn a reversed slur back into a slur after the inbound filter has run. The outbound filter must therefore check the final text (slices S-MOD-OUTBOUND-FILTER and S-MOD-TEMPLATE-ESCAPE).

**Discord moderator guidance (Q-discord).** discord.com/safety has no moderator page of its own; it lists hubs only. The page https://discord.com/safety/developing-moderator-guidelines advises that moderators hold themselves to a higher standard, that rules serve the community, and that moderators use judgment rather than the letter of the rule. The Moderator Academy course index was not opened (blocked row in section 15).

**TTS and AI speech incidents (Q-ttsnews).** Every case ended in a suspension of the streamer, so the filter must run before speech is spoken.
- Alfie, Twitch, 2019-05-21: a donation TTS read a racial slur for 15 to 20 seconds while the streamer was away; Twitch gave a 24-hour ban (https://www.dailydot.com/upstream/twitch-streamer-racist-text-to-speech/ and https://www.dexerto.com/entertainment/twitch-ban-streamer-after-viewer-sends-racist-text-to-speech-donation-644634/).
- Neuro-sama, Twitch, banned 2023-01-11: an AI VTuber denied the Holocaust; temporary ban (https://vtubernewsdrop.com/controversial-ai-vtuber-neuro-sama-banned-from-twitch/).
- "Nothing, Forever", Twitch, 2023-02-06: transphobic jokes by an AI character; 14-day suspension (https://techcrunch.com/2023/02/06/ai-generated-seinfeld-suspended-on-twitch-for-ai-generated-transphobic-jokes/).
- No 2024 to 2026 incident of a TTS or chat bot reading slurs was found. A Sportskeeda page returned HTTP 405.

### 13.x Sources

https://dev.twitch.tv/docs/api/reference/
https://dev.twitch.tv/docs/api/reference/#ban-user
https://dev.twitch.tv/docs/api/guide/
https://dev.twitch.tv/docs/eventsub/eventsub-subscription-types/
https://docs.kick.com/apis/moderation
https://docs.kick.com/apis/chat
https://docs.kick.com/apis/channels
https://docs.kick.com/apis/users
https://docs.kick.com/apis/kicks
https://docs.kick.com/apis/ads
https://docs.kick.com/apis/faqs.md
https://docs.kick.com/getting-started/scopes
https://docs.kick.com/getting-started/rate-limits
https://docs.kick.com/events/introduction
https://docs.kick.com/events/event-types
https://docs.kick.com/events/subscribe-to-events.md
https://docs.kick.com/events/webhook-security
https://docs.kick.com/changelog.md
https://docs.kick.com/llms.txt
https://developers.google.com/youtube/v3/live/docs/liveChatBans
https://developers.google.com/youtube/v3/live/docs/liveChatMessages
https://developers.google.com/youtube/v3/live/docs/liveChatMessages/delete
https://developers.google.com/youtube/v3/live/docs/liveChatModerators
https://developers.google.com/youtube/v3/determine_quota_cost
https://youtube.googleapis.com/$discovery/rest?version=v3
https://docs.x.com/x-api/introduction
https://docs.x.com/llms.txt
https://docs.x.com/x-api/llms.txt
https://docs.x.com/enterprise-api/llms.txt
https://docs.x.com/x-api/fundamentals/rate-limits
https://docs.x.com/x-api/users/mutes/introduction
https://docs.x.com/x-api/users/blocks/introduction
https://discord.com/developers/docs/resources/user
https://docs.streamer.bot/api/sub-actions
https://support.streamelements.com/hc/en-us/articles/25794373983122-StreamElements-Chatbot-on-Kick
https://support.streamelements.com/hc/en-us/articles/18486326016402-StreamElements-Chatbot-on-YouTube
https://support.restream.io/en/articles/2676959-filter-chat-comments

## 14. Other harms: swatting and IRL, self-harm, clips, channel points, polls and predictions, minors, moderator burnout

### 14.0 Why this section exists

Sections 3 to 13 cover spam, raids, bans and accounts.
Six harms had no home in the paper. They do not look like spam.
Each harm hurts a person more than a channel.
For each one: what Twitch gives, what our bot does today, the gap, the slice.

Rule for all six: route to Twitch tools and pages. The bot adds speed and privacy. It does not add authority.
Alert humans first for a person in crisis, a possible minor or a leaked address.
Say plainly in the dashboard what the API cannot do, and link the Twitch page.

### 14.1 Swatting and IRL

What the harm is
- Someone finds a streamer's home address and shares it (a dox). Someone may then file a false emergency report so armed police go there (a swat).
- IRL streamers can leak their position live: street signs, chat questions, viewers who watch a map.

What Twitch gives
- Doxxing and swatting page, https://safety.twitch.tv/s/article/Preventing-Doxxing-Swatting-and-other-IRL-Harm?language=en_US
  - Doxxing "of any kind" is prohibited, even when the data is in the public record.
  - Twitch can only act on doxxing that happens on Twitch.
  - Twitch advice: "On Twitch, add variations of your PII to your list of private blocked terms".
  - Twitch cannot contact local police for the streamer. Police can send Twitch a data request. Twitch says there is "no guaranteed way to prevent being doxxed".
- Law Enforcement Response page, https://safety.twitch.tv/s/article/Law-Enforcement-Response?language=en_US: police use the ALERT portal. The page is for police, not for streamers or bots.
- Stream delay page, https://help.twitch.tv/s/article/how-to-add-stream-delay: Partners can set up to 15 minutes. Twitch says a delay "can help prevent viewers from knowing your exact location in real time".
- Add Blocked Term, https://dev.twitch.tv/docs/api/reference/: POST /helix/moderation/blocked_terms, text 2 to 500 characters, wildcard * only at the start or end, scope moderator:manage:blocked_terms.
  - The request and the response have no Private flag. Get Blocked Terms returns non-private terms only. Mods see blocked terms by default unless a term is Private (https://help.twitch.tv/s/article/how-to-use-automod?language=en_US).
  - A multi-word term blocks a message that holds all its words in any order (https://help.twitch.tv/s/article/how-to-manage-harassment-in-chat?language=en_US).
- Update Shield Mode Status takes only is_active. Update Chat Settings has nine body fields (emote, follower, non-moderator delay, slow, subscriber, unique chat). Raid block, incoming raid settings and email or phone verification have no Helix endpoint in the reference.

What our bot does today
- Blocked-term add, list and remove exist: ModerationController.cs:884-935, service call at IModerationService.cs:127.
- Shield Mode read and write exist: ModerationController.cs:834-854.
- The channel stores its stream delay: Channel.cs:163. Onboarding seeds it from Helix: ChannelInfoSeedOnOnboardingHandler.cs:114-116.
- A grep of server/src for swat, dox and IRL found no code for this harm. The only hits are unrelated words and StreamDelay.

The gap
- No private store for the streamer's address, street, real name and family names.
- No fuzzy scan for spacing and leet spelling.
- Twitch's API cannot make a Private term. A term added through our API shows to mods (inference from the points above). So a dox term stays only in the bot, encrypted, or the streamer adds it in the Twitch dashboard.
- No IRL help page and no stream-delay reminder.

Rules for the fix
- Never post, repeat or log the matched text. Alert the broadcaster privately.
- Never contact police or claim to. Show the streamer the local-police route.
- Never promise that a blocked term stops a dox.
- Never search the web for the streamer's data.

Slices: S-MOD-DOX-TERMS (private term store and scan), S-MOD-OTHER-HARMS (IRL help page).

### 14.2 Self-harm

What the harm is
- A viewer or the streamer posts about suicide or self-harm.
- Chat reacts badly with jokes, a pile-on or graphic detail. Or a bot punishes the person in distress.

What Twitch gives
- Crisis Prevention page, https://safety.twitch.tv/s/article/Crisis-Prevention: a helpline finder by location and topic. No Twitch account is needed. For self-harm in chat, Twitch says: contact local authorities if the user is in danger; if not immediate, "we recommend sharing this page to the user".
- Community Guidelines, https://safety.twitch.tv/s/article/Community-Guidelines?language=en_US: people may talk about their struggles. Prohibited: glorifying self-harm, graphic details of suicide attempts, and selling harmful acts for subs, Bits or tips (example: "I will take a shot for every 100 Bits").
- Delete Chat Messages is in the Helix reference. The note found no Helix or EventSub field that marks a user at risk.

What our bot does today
- A grep of server/src for crisis, self-harm and suicide found no match in any .cs file.
- So no !support command and no crisis alert exist in the code that grep covers.

The gap
- No command that posts the Crisis Prevention link.
- No private alert to mods when a crisis phrase appears.
- No mod script for what to say and what not to say (TakeThis tips on the Twitch page).
- No audit of rewards that sell harmful acts for points (see 14.4).

Rules for the fix
- No diagnosis, no risk score, no counselling from the bot.
- Never ban, time out or shame a person for a crisis message.
- Never DM the person. Never promise help or a response time.
- Never show one country's hotline to everyone. Link the Twitch page, which asks for the location.
- Phrase matching will give false positives (inference). So the action is an alert to humans. Never an automatic punishment.

Slices: S-MOD-OTHER-HARMS (!support command and mod script), S-MOD-PHRASE-ALERT (private alert).

### 14.3 Clips

What the harm is
- A viewer clips a few seconds that look hateful or shameful and shares them. Clips live on after the stream.
- Reposts on other sites are out of Twitch's reach.

What Twitch gives
- Clips Settings, https://help.twitch.tv/s/article/clips-settings?language=en_US: Clips on or off, excluded categories, followers-only with a follow age, subscriber-only, "Let viewers upload clips to their socials" (default off). If a clip is reposted on TikTok or YouTube, Twitch cannot remove it.
- Clips Manager, https://help.twitch.tv/s/article/how-to-delete-and-manage-clips?language=en: per clip, timeout the creator for 24 hours, ban the creator, delete. Delete is permanent.
- Helix has four clip endpoints: Create Clip, Create Clip From VOD, Get Clips, Get Clips Download (https://dev.twitch.tv/docs/api/reference/).
  - Get Clips filters: broadcaster_id, game_id, id, started_at, ended_at, first, is_featured. No filter by creator. The response has creator_id and creator_name.
- The changelog (https://dev.twitch.tv/docs/change-log/, newest entry 2026-10-01) adds no clip delete, clip settings or clip report endpoint. The EventSub types page has zero mentions of "clip". So clip moderation has no API and no clip-created event as of 2026-10-10.

What our bot does today
- Create clip: LiveOpsController.cs:389-398.
- The clips API wrapper has create, create from VOD, get by broadcaster, get by ids and download URLs: ITwitchClipsApi.cs:23-68.
- Only LiveOpsController.cs and MediaSourceResolver.cs call the clips API (grep). Neither watches for hateful clips.

The gap
- No poll of Get Clips that flags clips from a user just timed out or from a new account.
- No dashboard text that says clip delete and clip settings are Twitch-dashboard only.
- No incident reminder to switch clips to followers or subscribers only.
- Twitch blocks the delete and settings part. Mark it "blocked by Twitch", not "not-doing". Re-check on each Twitch release.

Rules for the fix
- Never claim to delete a clip or change clip settings. Do not scrape twitch.tv.
- Never ban the creator on a guess. Show the creator and let a mod use Ban User.
- Never promise that reposts on other sites can be removed.

Slice: S-MOD-CLIP-WATCH.

### 14.4 Channel points, polls and predictions

What the harm is
- A redemption with viewer text carries slurs, dox text or a self-harm dare into TTS, an alert or chat.
- A tool or mod puts harmful text into a poll or prediction title. Channel points can buy extra poll votes, so a brigade can swing a poll.

What Twitch gives
- The redemption events carry the viewer text raw: user_input on channel.channel_points_custom_reward_redemption.add and .update, user_input and message on the automatic reward event, and user_input on channel.custom_power_up_redemption.add (https://dev.twitch.tv/docs/eventsub/eventsub-subscription-types/). So the bot is the first filter.
- Update Redemption Status: CANCELED or FULFILLED. CANCELED refunds the points. Only the app that created the reward may update its redemptions.
- Update Custom Reward has is_paused ("Viewers can't redeem paused rewards").
- Create Poll: title max 60, 2 to 5 choices of max 25, optional channel-point voting. Create Prediction: title max 45, 2 to 10 outcomes of max 25. The Helix 400 lists say the choice title and the outcome title "failed AutoMod checks". They show only a "too long" error for the poll title and the prediction title. So AutoMod on the main title is not documented.
- Mods already have a rewards queue (open with /requests): complete, reject, refund, "report rewards as abusive", pause redemptions (https://help.twitch.tv/s/article/channel-points-guide?language=en_US).
- The How to Use AutoMod page has zero hits for redemptions, polls or predictions.

What our bot does today
- The redemption handler puts the viewer text into the pipeline variables with no change: RewardRedeemedHandler.cs:217. A grep of that file for filter, blocked and moderat found no hit.
- The poll create action passes dto.Title and the choices straight to the Helix call: LiveOpsController.cs:109-124. The prediction create action does the same: LiveOpsController.cs:203-210.
- The reward model keeps the user-input flag: Reward.cs:47. Redemption keeps the text with a 500-character limit: RedemptionConfiguration.cs:28.

The gap
- No check of redemption text before it reaches TTS, an overlay or chat.
- No hold-and-review (cancel with refund and a visible reason) for rewards the bot owns.
- No check of poll and prediction text before the Helix call.
- No setup audit for rewards that take input with no filter, or that sell a physical stunt or drinking for points.
- Power-up redemptions have free text. The note found no fulfill or cancel endpoint for them in the sections it read.

Rules for the fix
- Never edit or cancel a redemption made by another app.
- Never refund silently. The viewer sees why. Cancel only on a rule hit.
- Never hide or change poll results.

Slice: S-MOD-TEXT-GATE.

### 14.5 Minors

What the harm is
- Adults contact minors in chat or whispers, ask for photos, move them to other apps, or coerce money or images.
- Children under 13 appear in chat.

What Twitch gives
- Youth Safety in the Community Guidelines (https://safety.twitch.tv/s/article/Community-Guidelines?language=en_US): youth means under 18. Grooming and sexual talk with youth are prohibited. Twitch reports illegal content to NCMEC. Stating you are under 13 gets the account permanently deactivated, even as a joke.
- Guide for Parents and Educators, https://safety.twitch.tv/s/article/Guide-Parents-Educators: Twitch is for ages 13 and up. Ages 13 to the age of majority may use Twitch only with a parent or guardian. To report a suspected under-13 who is not your child, use the reporting tool. To report someone who asks a minor for sexual things, use the report reason "Child Endangerment".
- Age Assurance on Twitch, https://safety.twitch.tv/s/article/Age-Assurance-on-Twitch?language=en_US: UK face-scan age checks. Australia bars under 16 from 2025-12-10.
- Parental Controls for Teen Accounts, https://safety.twitch.tv/s/article/Introducing-Parental-Controls-for-Teen-Accounts?language=en_US: a parent can disable Whispers, set time limits and restrict going live.
- The note found no age or minor flag in the Helix reference headings or the changelog.

What our bot does today
- A grep of server/src for grooming, NCMEC, under-13 and ChildEndangerment found no match in any .cs file.
- Ban and delete exist (ModerationController.cs). No report card for mods and no phrase watch exist.

The gap
- No mod card with the Twitch report path and the reason name "Child Endangerment".
- No private alert for an under-13 statement or a "move to another app" or "send a photo" opener.
- No link to the parents guide in the dashboard.

Rules for the fix
- Treat age as unknown for everyone. Never ask for an age, never run an age check, never ask for ID, never build an age field.
- Never label a person a predator or a minor in public. Never publish a suspected minor's name.
- Never keep or forward sexual content that involves a minor. Delete, ban, report to Twitch. Store nothing.

Slices: S-MOD-OTHER-HARMS (parents guide link and report card), S-MOD-PHRASE-ALERT (private alert).

### 14.6 Moderator burnout

What the harm is
- Volunteer mods read abuse for hours and make fast calls. Wohn 2019 (abstract only) names "hardships of dealing with negative content" and mixed emotional and physical labor.
- Mods tire out. They leave. Then the channel has no cover.

What Twitch gives
- Building a Moderation Team, https://safety.twitch.tv/s/article/Building-a-Moderation-Team?language=en_US: a sizing guide (small channel 2 to 3 mods; medium one active mod per about 200 viewers; large about one per 600 plus AutoMod or bots). The whole page has no text on wellbeing, breaks or burnout.
- Guide for Moderators, https://safety.twitch.tv/s/article/Guide-for-Moderators?language=en_US: Mod View widgets, an Active Mods widget and a Batch Reporting Tool. Banned users stay listed for 8 hours so mods can report "during a lull in the stream or after the stream has ended".
- Helix: Get Moderators, Get Moderated Channels, Manage Held AutoMod Messages, Get and Resolve Unban Requests. EventSub: automod.message.hold, channel.moderate, channel.unban_request.create, channel.warning.send.

What the studies say (read this run)
- Schopke-Gonzalez et al., New Media and Society 2022, https://journals.sagepub.com/doi/10.1177/14614448221138529: 71 former Facebook Group and Reddit mods. The top reasons to quit: "struggles with other moderators in the group" and too little time. Secondary traumatic stress was low. The authors say the odds ratios "should be read cautiously". Not Twitch.
- Cai and Wohn 2022, https://arxiv.org/abs/2203.13292: 40 Twitch mods. Channels appoint many mods but few are active. Large channels use shifts. Small channels use "on call" in Discord. Not warning mods before a big event made work "overwhelming". Informal work means "there is no fairness about the workload".
- AppealMod, https://arxiv.org/abs/2301.07163: a 4-month field test, 880 ban appeals. An extra-questions step cut the mod queue by 70%. Mods kept the final decision.
- Gauthier 2025 (https://arxiv.org/abs/2511.09813) and Abdelkadir 2026 (https://arxiv.org/abs/2604.15321) study PAID moderators. Never quote their numbers for volunteers.
- No source gives a burnout rate for volunteer Twitch mods. Quote no burnout percentage.

What our bot does today
- It lists the channel's mods: ModerationController.cs:617.
- A grep of server/src for mod shift, presence, load and roster found no such feature.

The gap
- No view of who is active now, which hours have no cover, or who is alone for long.
- No one-click "I need cover" ping and no handoff note.
- No heads-up to mods before a planned raid or big stream.
- No private workload view per mod. No optional shift roster.

Rules for the fix
- Every mod feature is opt-in and private. The streamer sees totals only.
- No public ranking of mod "productivity". No covert mood tracking.
- No claim that the bot "prevents burnout". No study read here shows it.
- Show the reason for each item. Let the mod decide (Cao 2023, https://arxiv.org/abs/2311.07879).
- The coverage, ping and heads-up ideas are inference from interview reports. No study read here measured that rosters cut burnout.

Slice: S-MOD-MOD-LOAD.

### 14.7 Fix slices

| Slice | What it does | Done-when | Rests on |
|---|---|---|---|
| S-MOD-DOX-TERMS | Private, encrypted store for the streamer's address, street, real name and family names. Scan chat with spacing and leet normalisation. Delete the message and alert the broadcaster without the matched text. | A fake term is stored. The messages "123 Example Street", "1 2 3 Ex@mple Str33t" and "street example 123" each trigger one Delete Chat Messages call and one private alert. The alert text, the log rows and the dashboard payload hold no part of the term. The stored column differs from the clear text. "Hello chat" triggers nothing. | S-MOD-OUTBOUND-FILTER (normaliser) |
| S-MOD-OTHER-HARMS | !support command with the Crisis Prevention link and streamer-editable text. IRL help page (what Twitch cannot do, stream delay, local-police route). Parents guide link and a mod report card that names "Child Endangerment". | !support posts the saved text plus the Crisis Prevention URL and nothing else. The IRL page renders its resource keys and states that Twitch cannot call police. The report card returns the Twitch report link and the reason key for a mod and 403 for a plain viewer. | S-MOD-DECISION-CARD |
| S-MOD-PHRASE-ALERT | Optional phrase lists for crisis phrases and for under-13 and "move to another app" openers. Private alert to broadcaster and mods with the message link. | A fixture crisis message and a fixture opener message each emit one private alert with a message link. The test asserts zero ban, timeout, delete-user and DM calls. A joke fixture still gives only an alert. With the setting off, no chat post and no alert occur. | S-MOD-OUTBOUND-FILTER (normaliser), S-MOD-DECISION-CARD |
| S-MOD-CLIP-WATCH | Poll Get Clips with a started_at cursor. Flag clips from a user just timed out or from a new account. Show creator, title, URL and a Clips Manager link. State that delete is dashboard-only. | A fake Get Clips returns 3 clips and one has a creator timed out 5 minutes ago. One flag row is saved with creator, title and URL. A second poll with the same data adds no row. The test asserts zero Ban User calls. The dashboard payload carries the "delete in Twitch" text. | none |
| S-MOD-TEXT-GATE | Check redemption text, poll titles and choices, and prediction titles and outcomes against the channel's blocked terms and the filter. For bot-owned rewards: cancel with refund and a viewer-visible reason. | A redemption whose input holds a blocked term gets one CANCELED status call, one saved reason and one message to the viewer. TTS and overlay get no text. A clean input reaches the pipeline unchanged. A reward made by another app gets no status call. A poll title with a blocked term returns 400 and the fake Helix transport records zero calls. Titles over 60 (poll), 45 (prediction) and choices over 25 characters are rejected. | S-MOD-OUTBOUND-FILTER, S-MOD-TEMPLATE-ESCAPE |
| S-MOD-MOD-LOAD | Mod coverage view, "I need cover" ping with handoff note, heads-up before a planned raid or big stream, optional shift roster, private workload view per mod. | A fake log with 3 mods, where one mod acts alone for 2 hours, returns hourly coverage with those hours marked "alone". The cover ping reaches the other mods and not the sender. The heads-up event reaches all mods before the planned time. The per-mod view returns data only to that mod. The streamer endpoint returns totals only. No endpoint ranks mods. | S-MOD-EVENTSUB-CONSUMERS |

### 14.x Sources

https://safety.twitch.tv/s/article/Preventing-Doxxing-Swatting-and-other-IRL-Harm?language=en_US
https://safety.twitch.tv/s/article/Community-Guidelines?language=en_US
https://help.twitch.tv/s/article/how-to-add-stream-delay
https://dev.twitch.tv/docs/api/reference/
https://dev.twitch.tv/docs/eventsub/eventsub-subscription-types/
https://dev.twitch.tv/docs/change-log/
https://safety.twitch.tv/s/article/Law-Enforcement-Response?language=en_US
https://help.twitch.tv/s/article/how-to-use-automod?language=en_US
https://help.twitch.tv/s/article/how-to-manage-harassment-in-chat?language=en_US
https://safety.twitch.tv/s/article/Crisis-Prevention
https://help.twitch.tv/s/article/clips-settings?language=en_US
https://help.twitch.tv/s/article/how-to-delete-and-manage-clips?language=en
https://help.twitch.tv/s/article/channel-points-guide?language=en_US
https://safety.twitch.tv/s/article/Guide-Parents-Educators
https://safety.twitch.tv/s/article/Age-Assurance-on-Twitch?language=en_US
https://safety.twitch.tv/s/article/Introducing-Parental-Controls-for-Teen-Accounts?language=en_US
https://safety.twitch.tv/s/article/Building-a-Moderation-Team?language=en_US
https://safety.twitch.tv/s/article/Guide-for-Moderators?language=en_US
https://journals.sagepub.com/doi/10.1177/14614448221138529
https://arxiv.org/abs/2203.13292
https://arxiv.org/abs/2301.07163
https://arxiv.org/abs/2511.09813
https://arxiv.org/abs/2604.15321
https://arxiv.org/abs/2311.07879
https://doi.org/10.1145/3290605.3300390

---

## 15. Coverage ledger

Every topic this paper set out to cover, with its status. Closed means a section settles it from a page or file that was read. Blocked means no source read settles it; the reason names what is missing.

Closed 121 of 154. Blocked 33.

| Topic | Origin | Status | Section | Reason |
|---|---|---|---|---|
| Twitch native tools | outside source | closed | 3.1 |  |
| Twitch shortcomings, ranked | outside source | closed | 4 |  |
| AutoMod categories against the API | outside source | closed | 3.3 |  |
| Twitch account enforcement | outside source | closed | 3.4 |  |
| Third-party bots (Fossabot, Nightbot, StreamElements, Moobot and others) | benchmark | closed | 5.1, 5.2 |  |
| FrankerFaceZ and 7TV features | benchmark | closed | 5.1 |  |
| Bluesky lists and labels | benchmark | closed | 5.3 |  |
| Settled decisions re-evaluated (D01-D52) | code | closed | 6.1 |  |
| Contradictions K1-K15 and C1-C15 | code | closed | 6.2, 6.3 |  |
| Mass-ban design and the build order | owner ask | closed | 7 |  |
| Token custody and hosting models | code | closed | 10 |  |
| User hub live refresh (22 of 70 controllers) | code | closed | 7 |  |
| Paper status claims against the code | code | closed | 5.2, 6 |  |
| Guard (h): Helix suspicious-user endpoint | outside source | closed | 1.1 finding 2 |  |
| frankerfacez.com website | outside source | closed | 15.1 | - |
| 7TV Help articles Highlights, Extension Onboarding, New Extension FAQ | outside source | closed | 15.1 | - |
| Twitch UserVoice | outside source | blocked | 4 | The site sits behind Cloudflare and refused a plain fetch. |
| Helix Block User effect on the follow | outside source | closed | 15.1 | - |
| Live-box run of SpamCorpusSeeder, FollowSpikeHandler, ProtectionStatusService | code | blocked | 5.2, 6.3 | No live run happened. Wiring was read in code and DI scan only. |
| 48 of 70 controllers not traced for hub refresh | code | closed | 15.1 | - |
| benchmarks/moderation-tools.md user-hub tab list | code | closed | 15.1 | - |
| Whether chat filters fold lookalike letters with MessageNormalizer | code | closed | 15.1 | - |
| Whether the low-trust ledger holds restrictions made in the Twitch UI and survives a restart | code | blocked | 5.2, 7 | Needs a live test on the dev box. Code shows the ledger and handlers only. |
| Escalation rung maths unit test | code | closed | 15.1 | - |
| Hourly validate rule of Twitch (raw quote) | token-custody-practice-2 s.B | closed | 10.3 F6 | Quote read on the validate-tokens page |
| Twitch invalidates access tokens when a user disconnects | token-custody-practice-2 s.B | closed | 10.3 F6 | Quote read on the validate-tokens page |
| 30-minute refresh sweep exists, no validate call | token-custody-code s.4 | closed | 10.3 F6 | `TokenRefreshService.cs:25` plus the paper grep |
| Developer Agreement clauses (Keys, incident mail, deletion) | token-custody-practice s.2 | closed | 10.4 | Read as page text |
| Incidents: Heroku and Travis, Twitch 2021, StreamElements, Twitch extension | token-custody-practice-2 s.B | closed | 10.4 | Re-read as page text |
| Ban User `moderator_id` rule | token-custody-practice s.7 | closed | 10.5 | Read raw on the API reference |
| EventSub HMAC-SHA256 signing | token-custody-practice s.6 | closed | 10.5 | Read raw on the handling-webhook-events page |
| Revoke endpoint exists but has no caller | token-custody-code s.5 | closed | 10.1 and 10.3 F1 | Grep in this repo found no caller |
| Erasure revokes locally and crypto-shreds keys | token-custody-code s.5 | closed | 10.3 F1 | `ErasureService.cs:568-585` and `:625-653` |
| Outbound webhooks fan out by event type, `ModerationActionTakenEvent` in the catalogue | handoff-gap-final lines 12-13 | closed | 10.5 | `OutboundWebhookEventCatalogue.cs:118` and fanout files |
| No direct `IDataProtector` caller in the server source | token-custody-code s.1 | closed | 10.3 F7 | Grep result in the code note |
| Discord bot token storage (vault or config) | 10.1 Discord row | closed | 15.1 | - |
| Discord OAuth scope list | 10.1 Discord row | closed | 15.1 | - |
| Seal on write for CustomDataSource, SupporterConnection, VtsConnection, webhook and TTS secret columns | 10.1 Other secrets row | closed | 15.1 | - |
| `Service.ClientSecret` sealing and other `Service` writers | 10.1 Old Service rows | closed | 15.1 | - |
| Exact list of Twitch scopes we request (about 79) | 10.3 F4 | closed | 15.1 | - |
| Whether `IamAuditLog` covers token use | 10.3 F5 | closed | 15.1 | - |
| Hidden validate call inside a library | 10.3 F6 | closed | 15.1 | - |
| EventSub type name for authorization revoke (`user.authorization.revoke` came from a forum post) | 10.6 S-TOKEN-AUDIT | closed | 15.1 | - |
| Which framework features use the Data Protection ring | 10.3 F7 | closed | 15.1 | - |
| Federation outbound transport and peer key storage | 10.3 F10 and 10.5 | closed | 15.1 | - |
| Whether `ModerationActionTakenEvent` is journaled | 10.5 webhooks bullet | closed | 15.1 | - |
| Public leak of a chat-bot service's Twitch tokens | 10.4 | blocked | 10.4 | Only search results exist, no further source |
| Firebot token encryption in files other than `account-access.ts` | 10.4 rivals table | blocked | 10.4 | Only one file was grepped |
| Refresh token after `oauth2/revoke` (does it still refresh?) | 10.3 F3 | blocked | 10.3 F3 | Docs are silent, a forum says no; needs a live test with a throwaway Twitch app (Q3) |
| Effect of a new client secret on user tokens | 10.3 F3 | blocked | 10.3 F3 | Undocumented; needs a live test with a throwaway Twitch app (Q3) |
| OWASP Secrets Management cheat sheet | 10.4 General practice | closed | 15.1 | - |
| AWS KMS concepts | 10.4 General practice | closed | 15.1 | - |
| RFC 9700 (OAuth security best practice) | 10.4 General practice | closed | 15.1 | - |
| RFC 7009 (token revocation) | 10.4 General practice | closed | 15.1 | - |
| ActivityPub (W3C) | 10.5 signed requests | closed | 15.1 | - |
| Mastodon HTTP signatures | 10.5 signed requests | closed | 15.1 | - |
| Matrix server-server API | 10.5 signed requests | closed | 15.1 | - |
| Owner question: are all bases covered, including our own output | owner ask | closed | 11.0 | Section 11 reads every output surface in the research note. |
| TTS censor: 24 words, no slurs, on by default | code | closed | 11.2 | Finding 3. Read in code with line numbers. |
| TTS gate and its callers (event responses, quotes, shoutout, pipeline, scripts, dashboard test, denial notice) | code | closed | 11.1 | All callers listed in the surface table. |
| Leet, spaced and look-alike spelling pass the censor | code | blocked | 11.2 | Reasoned from the regex, not run. Needs a test run. The failing test is Censor("sh1t") expecting a mask. Slice S-MOD-OUTBOUND-FILTER. |
| Alerts and alert queue show names with no filter | code | closed | 11.2 | Finding 2. Grep over Hubs, Alerts and Overlays found no filter. |
| Follow-bot defence does not hold back one alert | code | closed | 11.2 | Finding 2. Sweep service blocks accounts only. |
| Command echo of args and the chat-filter result | code | closed | 11.2 | Finding 1. The command handler reads no filter result. |
| Handler order for one chat message | code | closed | 15.1 | - |
| Template resolver runs viewer transforms | code | closed | 11.2 | Finding 7. Nine transform names read. All pure string functions. |
| Event-response TTS passes broadcaster standing | code | closed | 11.2 | Finding 8. Deliberate per the doc at EventResponseExecutor.cs:307-313. |
| ReplyTtsPipeline runs as bits 0 and standing everyone | code | blocked | 11.2 | Read from code, not run. A live check needs a channel with a floor set, and a test must not write into a real channel row. Slice S-MOD-TTS-STANDING. |
| Overlay chat line sent before and regardless of the filter | code | closed | 11.2 | Finding 5. Broadcast handler read in full, 185 lines. |
| Overlay retraction after a chat-filter delete | code | blocked | 11.2 | Unread whether Twitch EventSub channel.chat.message_delete reaches ChatMessageDeletedEvent after a Helix delete. Needs a live or integration run. AutoMod-held lines before delivery also unread. Slice S-MOD-ECHO-AFTER-FILTER. |
| TTS text echoed to the overlay widget is the censored text | code | closed | 11.1 | Two existing tests pin it. TtsSpeakBroadcastHandlerTests does not test censoring. |
| Lockdown does not touch alerts, TTS or overlay chat | code | closed | 11.2 | Finding 4. Grep found no link. |
| Reward input, quotes, media share, Discord templates, voice echo, whisper | code | closed | 11.2 | Finding 6. |
| BlockExplicit documented and not implemented | code | closed | 11.2 | Finding 9. Grep over Domain, Application and Infrastructure found no other use. |
| Discord mention allow-list | code | closed | 11.2 | Kept as the pattern. Gateway sends at most the one ping role. |
| Competitor filters: Streamlabs, StreamElements, Sound Alerts, ControlPlay | benchmark | closed | 11.2 | Pages opened in a real browser. ControlPlay and Speechify are vendor claims. |
| Hate raid study and Twitch targeted-attack advice | outside source | closed | 11.2 | Findings 2 and 4. arXiv page and Twitch safety page read in a real browser. |
| TTS.Monster moderation behaviour | benchmark | blocked | 11.2 | Home page, login page and terms page hold no moderation text. The Discord invite was not read. Not usable as a source. |
| Reddit r/Twitch thread on StreamElements chat bot filter | outside source | blocked | 11.2 | Reddit returned the page "You've been blocked by network security" on the retry. Not retried further. |
| Discord Safety guide | outside source | closed | 15.1 | - |
| News item on a bot TTS slur incident | outside source | blocked | 11.2 | Two earlier searches found none. No page to read. |
| Competitor checks for command echo, quotes, shoutout, voice echo and template escaping | benchmark | blocked | 11.2 | No competitor page was read for these surfaces. The research note names no named competitor for them. |
| VoiceBuiltin echo of the query | code | closed | 15.1 | - |
| Chat HTML fragment allows img and marquee | code | closed | 15.1 | - |
| UpdateUserInfo, Game and Lurk builtins | code | closed | 15.1 | - |
| Spoken-name competitor behaviour | benchmark | blocked | 11.1 | The research note gives no competitor source for name handling in TTS pronunciation lists. |
| Owner ask: users never banned for the wrong reason | owner ask | closed | 12.0 | Quoted in 12.0; the fixes are the slices in 12.4 |
| Hijacked mod: mass ban | code | closed | 12.2 | Findings 1-4; S-MOD-BAN-RATE-STEPUP, S-MOD-MASSBAN-FLOOR, S-MOD-ATTACKED-PROOF |
| Hijacked lead mod: nuke and all_moderated ban | code | closed | 12.2 | Findings 4, 7; S-MOD-NUKE-CAPS-AUDIT |
| Rogue mod (real mod abusing power) | code | closed | 12.2 | Findings 2, 6, 7, 10; S-MOD-BAN-RATE-STEPUP, S-MOD-NOTICE-ALL-PATHS, S-MOD-ACTOR-AUDIT, S-MOD-OPTIN-VISIBILITY |
| Platform admin impersonation | code | closed | 12.2 | Finding 5; S-MOD-IMPERSONATION-LIMITS |
| System owner override on mass ban | code | closed | 12.2 | Finding 6 and 7 cover notice and audit; runs on streamer token; S-MOD-NOTICE-ALL-PATHS, S-MOD-ACTOR-AUDIT |
| Stolen dashboard session or JWT | code | closed | 12.2 | Findings 1, 8; S-MOD-BAN-RATE-STEPUP, S-MOD-REVOKE-SESSIONS |
| Stolen automation API key | code | closed | 12.2 | Finding 9; S-MOD-APIKEY-LIMITS |
| Hijacked streamer account | outside source | closed | 12.2 | Finding 11 with three incidents; S-MOD-OWNER-WATCH |
| Anti-nuke limits and quarantine (Security Bot, Wick) | benchmark | closed | 12.3 | Rows in 12.3; S-MOD-BAN-RATE-STEPUP |
| 2FA for moderation actions (Discord) | benchmark | closed | 12.3 | Rows in 12.3; S-MOD-BAN-RATE-STEPUP |
| Twitch takeover guidance and incidents | outside source | closed | 12.3 | Rows in 12.3; S-MOD-OWNER-WATCH |
| Twitch UserVoice page "Moderation 2FA requirement" | outside source | blocked | 12.3 | Cloudflare security check page; one retry with an 8 second wait; no content read |
| tribune.com.pk Jynxzi article | outside source | blocked | 12.3 | Cloudflare block in the browser; nothing read; the Dexerto article was used instead |
| Wick exact ban-per-minute and per-hour numbers | benchmark | blocked | 12.3 | Only one search-result sentence; the Wick docs that were opened give no number |
| Discord community post on 2FA for moderation | outside source | blocked | 12.3 | Page was never opened; the Discord Safety page was opened and states the feature |
| Dedicated Discord support article "Require 2FA for moderator actions" | outside source | blocked | 12.3 | No such official article found; searches returned only community feedback posts |
| Twitch mod log shows streamer as banner for a batch on the streamer's token | code | blocked | 12.2 | Follows from the Ban User rule; never run live; add the live check to S-MOD-ACTOR-AUDIT |
| Whether moderation records store the operator behind an act-as call | code | closed | 15.1 | - |
| Whether a validator forces ApiKey ExpiresAt | code | closed | 15.1 | - |
| AutomationPairingService.cs and the rest of BanAction.cs | code | closed | 15.1 | - |
| How our integration behaves when a channel is banned by Twitch and returns | code | closed | 15.1 | - |
| Source for the support-impersonation model at other products | benchmark | blocked | 12.3 | No source found; Wick and Security Bot are not impersonation models |
| Source for the hosted-service-owner model at other products | benchmark | blocked | 12.3 | No source found; Discord exempts the server owner, which is a different case |
| Incident of a hijacked mod abusing ban power | outside source | blocked | 12.0 | None found in search; the mod-abuse risk stays a design inference |
| Kick | owner ask | closed | 13.3 | Kick API has only ban, timeout, unban, delete message and a ban event. Gaps and tests: S-MOD-ROUTER-UNBAN, S-MOD-OWN-ACTION-LOG, S-MOD-CAPABILITY-FLAGS, S-MOD-PLATFORM-INBOUND-FILTER |
| YouTube | owner ask | closed | 13.3 | Ban, timeout, unban, delete exist. Events, moderator calls and quota budget are gaps. Tests: S-MOD-YOUTUBE-GAPS |
| X Live | owner ask | closed | 13.1 | No chat-moderation API found. Mute and block act on the account. State "not available" is tested in S-MOD-CAPABILITY-FLAGS and S-MOD-LOCKDOWN-MULTI |
| EventSub moderation topics | owner ask | closed | 13.2 | 23 topics subscribed and translated. Five have no consumer. Tests: S-MOD-EVENTSUB-CONSUMERS |
| rate limits | outside source | closed | 13.3 | Twitch, Kick, YouTube and X limits read. Kick publishes none. Pacing fix and tests: S-MOD-RATE-HEADERS |
| Cross-platform identity | code | closed | 13.3 | Links need OAuth on both accounts. Merge has no production caller. Tests: S-MOD-CROSS-PLATFORM-BAN |
| Projection rebuild provider | code | closed | 13.3 | Rebuild hard-codes Twitch at ModerationProjectionService.cs:253 and :296. Test: S-MOD-PROJECTION-PROVIDER |
| Event journal stores all EventSub topics | code | closed | 13.2 | NotificationDispatcher.cs:104-117 appends before fan-out. No event-type filter |
| Competitors on multi-platform moderation | benchmark | closed | 13.1 | Streamer.bot, StreamElements and Restream read. None unifies moderation across platforms |
| Twitch Ban User numeric rate limit | outside source | blocked | - | The Twitch docs publish no number. A live Ratelimit-Limit header read on a throwaway channel is needed |
| YouTube liveChatMessages.streamList scope | outside source | closed | 15.1 | - |
| Whether TwitchHelixTransport hands Ratelimit headers to callers | code | closed | 15.1 | - |
| Whether the outbound webhook dispatcher forwards ModerationActionTakenEvent | code | closed | 15.1 | - |
| Where the youtube.manage scope is requested at connect time | code | closed | 15.1 | - |
| Whether any ModerationProjectionService caller mixes platforms | code | closed | 15.1 | - |
| Whether the admin support page lists every journal event type | code | closed | 15.1 | - |
| Full channel.moderate action list in live Twitch docs | outside source | closed | 15.1 | - |
| Who emits OutgoingRaidStartedEvent | code | closed | 15.1 | - |
| Twitch rate limits for endpoints other than add and remove moderator | outside source | closed | 15.1 | - |
| What the suspicious-user, unban-request and moderator handlers show on screen | code | closed | 15.1 | - |
| StreamElements Twitch Moderation Guide | benchmark | closed | 15.1 | - |
| swatting and IRL | outside source | closed | 14.1 | Twitch doxxing page, blocked-term API and stream-delay page read. Tests to write: S-MOD-DOX-TERMS, S-MOD-OTHER-HARMS |
| self-harm | outside source | closed | 14.2 | Crisis Prevention page and Community Guidelines read. Tests to write: S-MOD-OTHER-HARMS, S-MOD-PHRASE-ALERT |
| clips | outside source | closed | 14.3 | Changelog, reference and EventSub types read; no clip moderation API exists. Test to write: S-MOD-CLIP-WATCH |
| channel points, polls, predictions | code | closed | 14.4 | RewardRedeemedHandler.cs:217 and LiveOpsController.cs:109-124 pass text unchecked. Test to write: S-MOD-TEXT-GATE |
| minors | outside source | closed | 14.5 | Guide for Parents and Educators, Age Assurance and Parental Controls pages read. Tests to write: S-MOD-OTHER-HARMS, S-MOD-PHRASE-ALERT |
| mod burnout | outside source | closed | 14.6 | Four studies read plus Twitch team pages; no rate for volunteer Twitch mods exists in them. Test to write: S-MOD-MOD-LOAD |
| Burnout rate of volunteer Twitch mods | outside source | blocked | 14.6 | No source found; Semantic Scholar answered Too Many Requests (429) and the arXiv phrase searches listed no such paper |
| Wohn 2019 CHI full text (doi 10.1145/3290605.3300390) | outside source | blocked | 14.6 | The ACM page sits behind a Cloudflare check ("Just a moment..."); only the abstract was read |
| Seering and Kairam 2023 "Who Moderates on Twitch and What Do They Do?" | outside source | blocked | 14.6 | Title is not on arXiv and the ACM site search sits behind a Cloudflare check |
| Dosono and Semaan 2019 (doi 10.1145/3290605.3300372) and Steiger et al. CHI 2021 | outside source | blocked | 14.6 | Google Scholar was not opened and the ACM site is behind Cloudflare; neither paper was read |
| Burnout tools for volunteers (blur, exposure caps, recognition) | outside source | blocked | 14.6 | No paper read tests them; they stay design ideas under S-MOD-MOD-LOAD |
| Discord volunteer-mod burnout | outside source | blocked | 14.6 | Only arXiv listings were searched and no Discord paper was found |
| Twitch page "Filing a Report" | outside source | closed | 15.1 | - |
| Twitch page "Navigating our Policy on Discussions and Depictions of Self-Harm" | outside source | closed | 15.1 | - |
| Dedicated Twitch IRL-streaming safety page | outside source | closed | 15.1 | - |
| Blocked-term matching of case, spacing and leet spelling | outside source | blocked | 14.1 | The Twitch help pages do not state it; our normaliser is tested in S-MOD-DOX-TERMS |
| AutoMod on the main poll and prediction title | outside source | blocked | 14.4 | The Helix 400 lists show only a "too long" error for the title; our own check is tested in S-MOD-TEXT-GATE |
| AutoMod on a clip title edited later in the editor | outside source | closed | 15.1 | - |
| Fulfill or cancel endpoint for Custom Power-ups | outside source | closed | 15.1 | - |
| Teen whisper and DM default restrictions in detail | outside source | closed | 15.1 | - |
| ConnectSafely Parent's Guide to Twitch | outside source | closed | 15.1 | - |
| Which of our providers support token revoke | outside source | closed | 15.1 | - |

### 15.1 Closing reads

A second pass read the sources behind rows that were blocked. Each entry gives the finding, its source, and what it means for the plan.

**frankerfacez.com website.** The site loaded in Chrome in this run (title "FrankerFaceZ"; the earlier timeout and HTTP 522 did not repeat). It says "Filter chat for messages with keywords to be either highlighted or completely removed from chat." and under Advanced Mod Tools: "Customize buttons, keyboard shortcuts, and layout to make moderating chat faster and more reliable." and "With profiles, you can customize your mod tools and stream layout per channel". Emote safety line: "FFZ proudly protects artists by researching emote submissions and ensuring that personal or commissioned artwork is not being stolen." Footer reads "2025 Dan Salvato LLC". Source: https://www.frankerfacez.com/ Plan impact: Benchmark items: per-channel mod-tool profiles, keyword filters with two actions (highlight or remove), and configurable mod buttons and keyboard shortcuts. The page text was read from a tab already open in the shared Chrome; it was not re-fetched with a fresh navigation.

**7TV Help articles Highlights, Extension Onboarding, New Extension FAQ.** All three were opened and read. Highlights is a 3-article collection (General, Built-In, Style). "General - Highlights" says "Customize how messages stand out using phrases, usernames, and badges." and lists Pattern, Label, Flash Title, RegExp, Case Sensitive, Color and Sound per phrase. "Built-In - Highlights" lists "Show Monitored Suspicious User Highlights (Moderator only)", "Show Restricted Suspicious User Highlights (Moderator only)", "Show Returning Chatters (Moderator only)", "Show Raiders (Moderator only)" and "Show First-Time Chatter Highlights". "Extension Onboarding" has one line on mod tools: "Mod Tools - Enable the mod slider for easy moderation". The New Extension FAQ (updated "April 3, 2026") covers emote priority and display bugs only and has no moderation text; it says deleted-message styles follow FFZ when both run: "FFZ chat behavior takes priority." Source: https://help.7tv.app/en/articles/11095320-general-highlights ; https://help.7tv.app/en/articles/11095340-built-in-highlights ; https://help.7tv.app/en/articles/11095353-style-highlights ; https://help.7tv.app/en/articles/11072866-extension-onboarding ; https://help.7tv.app/en/articles/14425435-frequently-asked-questions Plan impact: A mod-side viewer extension already marks monitored and restricted suspicious users, raiders, returning chatters and first-time chatters with colour and sound. The bot dashboard should show the same signals (suspicious, raider, first-time, returning) in its own mod feed and not depend on the extension. Per-phrase highlight with regex, case flag, colour and sound is the benchmark for a bot "watch phrase" list.

**Helix Block User effect on the follow.** The Helix reference is silent. The Block User section says only "Blocks the specified user from interacting with or having contact with the broadcaster." and "The API ignores the request if the broadcaster has already blocked the user." (https://dev.twitch.tv/docs/api/reference/, section Block User). The Twitch help page documents the effect for blocking in general: "They are removed from your followers/following list" and "They are prevented from following you, and vice-versa" (https://help.twitch.tv/s/article/about-bans-blocking-and-suspensions). The help page does not mention the API. Plan impact: Do not claim the API removes the follow. Treat the help-page effect as likely but unproven for the API call. The docs do not settle it, so a live test on a throwaway account pair is the only proof, and the bot must read the follow state back after a block instead of assuming it.

**48 of 70 controllers not traced for hub refresh.** The tree has 116 controller classes under server/src/NomNomzBot.Api/Controllers (not 70). 101 have write actions (486 [HttpPost/Put/Patch/Delete] in total); 15 have none (OAuthRelay, ObsBridgeHost, OverlayHost, OverlaySdk, OverlayVueRuntime, VoiceListenerPage, DiscordOAuth, Overlay, PipelineExecutions, PipelineOptions, PlatformAnalytics, RenderManifest, Sdk, Templates, AdminSupport). Script result (service chain followed 2 levels deep, regex on source, not per action): 50 push (320 writes), 48 no push (158 writes), 3 overlay-only (8 writes), 0 unclear. Of the 50 pushers, 36 publish from the service the controller injects and 14 only through a service that service calls (weak, may be a different domain): AdminController, AdminSpamDefenseController, AdminTrustSafetyController, AutomationDataController, BundlesController, ChatController, CommunityController, CustomDataSourcesController, ImportController, MarketplaceController, PlatformAudioAssetsController, PlatformContentController, RewardsController, SupportersController. Limit: it says a controller can reach a hub-handled event, not that every write action does. Push mechanisms (all dashboard pushes end in IDashboardNotifier -> IHubContext<DashboardHub>): 1. Generic config event: a service calls IEventBus.PublishAsync(new ChannelConfigChangedEvent{Domain,EntityId,Action}) (event at Domain/Platform/Events/ChannelConfigChangedEvent.cs:21; example CommandService.cs:660), handler Api/Hubs/Broadcasters/ChannelConfigChangedBroadcastHandler.cs:21 calls DashboardNotifier.SendConfigChangedAsync (Api/Hubs/DashboardNotifier.cs:206); the app side is app/composeApp/src/commonMain/kotlin/bot/nomnomz/dashboard/core/realtime/ConfigChangeRefresh.kt. 24 Domain strings exist (quotes, webhooks, commands, builtins, timers, pipelines, catalog, features, tts-config, music-config, economy-config, savings-jar, channel-settings, picklists, widgets, ...). 2. Typed domain-event handlers: 81 IEventHandler<T> classes in Api/Hubs/Broadcasters/*.cs (role, ban, poll, prediction, shield, lockdown, savings jar, reward, integration, stream lifecycle, automod, ...) each forward one event through IDashboardNotifier (registered Program.cs:232; example RoleBroadcastHandlers.cs:64). 3. Direct notifier call from a controller or service: ChannelsController, DashboardController, LiveOpsController, NotificationsController, PlatformAdminController, StreamController inject IDashboardNotifier; services use IActionRequiredChangeNotifier (Api/Hubs/ActionRequiredChangeNotifier.cs:27, registered Infrastructure/DependencyInjection.cs:838) for the inbox. 4. Overlay/OBS side (not the dashboard): WidgetNotifier.cs:84 (IHubContext<OverlayHub>) and ObsBridgePusher.cs:21 (IHubContext<OBSRelayHub>). No-push controllers (48, writes in brackets): ActionDefaultsAdmin(1), ActionPermissions(2), AdminBilling(9), Analytics(1), Assets(2), AudioMix(1), Auth(18), AutomationPairing(2), AutomationTokens(3), Billing(7), BillingWebhook(1), BuiltinReplyDefaultsAdmin(2), ChannelFederation(2), ChatFilters(4), ChatTriggers(3), Compliance(2), Discord(13), DiscordInteractions(1), EconomyLeaderboards(4), EgressAllowlist(3), Engagement(1), EventResponseDefaultsAdmin(2), EventStore(5), EventSub(3), FeatureFlagAdmin(3), Federation(5), Games(5), Gdpr(4), GiveawayCodePools(3), Giveaways(7), InboundWebhook(1), IntegrationOAuth(2), IpcDevMode(2), Permits(3), PlatformTemplates(2), Pronouns(1), Roles(2), System(7), TrustPolicy(1), TtsNamePronunciation(1), TtsVoiceDefaultsAdmin(2), TwitchDiagnostics(1), Users(1), ViewerData(2), VoiceTriggers(3), Vts(4), WidgetGallery(3), WidgetPreviewAction(1). Overlay-only (3, no dashboard push): GameSessions(2), SoundClips(5), WidgetTestEvent(1). Checked in source: ChatFilterService, GiveawayCodePoolService, ChatTriggerService, TrustPolicyService, VtsConnectionService, ChannelAudioMixService, ChannelNamePronunciationService have no IEventBus at all. Some services publish an event that has NO hub handler: RolesController -> MembershipService publishes ManagementRoleChangedEvent, PermitsController -> PermitService publishes PermitGrantedEvent/PermitRevokedEvent, ChannelFederationController -> ChannelFederationOptInChangedEvent, DiscordController -> DiscordGuildLinked/Unlinked events; no Api/Hubs handler exists for them (grep of the event names finds only Domain, the contract and the service). GiveawayService, GameService, EngagementService, VoiceTriggerService inject IEventBus but never publish ChannelConfigChangedEvent or another hub-handled event. Plan impact: These no-push controllers back channel dashboard pages that must update live (a second open tab or a moderator's tab shows stale data until a manual refresh): ChatFilters, ChatTriggers, Giveaways, GiveawayCodePools, Games, Roles, Permits, VoiceTriggers, Vts, EgressAllowlist, TrustPolicy, AudioMix, TtsNamePronunciation, Engagement, Discord, EconomyLeaderboards, ChannelFederation, ActionPermissions, Pronouns, ViewerData, Assets, WidgetGallery, EventStore, EventSub, TwitchDiagnostics, IntegrationOAuth. Cheapest fix: add a ChannelConfigChangedEvent publish (new Domain string) in each service, plus the app screen subscribing in ConfigChangeRefresh.kt; for Roles/Permits/Federation/Discord add a typed broadcast handler or map the existing event. Platform-admin pages (ActionDefaultsAdmin, AdminBilling, BuiltinReplyDefaultsAdmin, EventResponseDefaultsAdmin, FeatureFlagAdmin, TtsVoiceDefaultsAdmin, PlatformTemplates, Federation, System, AutomationTokens/Pairing, Billing, IpcDevMode, Users) also push nothing to DashboardHub; AdminHub does not cover them either: its only senders are AdminBroadcastHandlers.cs (channel online, channel offline, tenant suspension), AdminHubStatusPublisher.cs and IamBreakGlassAlertHandler.cs (main-session grep, 2026-10-10). No UI needed: Auth, BillingWebhook, InboundWebhook, DiscordInteractions, WidgetPreviewAction, Gdpr/Compliance (user-facing one-off actions). The Games and Sound pages list sessions and clips (app GamesScreen.kt, SoundScreen.kt) and get no dashboard push, so they need one; WidgetTestEvent only fires a test into an overlay and holds no page state.

**benchmarks/moderation-tools.md user-hub tab list.** The benchmark has no tab list. It lists six user-hub features under S-USER-HUB: account age on the user card, per-user chat, timeout and ban history, shared mod comments, delete one message without a timeout, and all mod settings on one page (today split over Community and Moderation). The ban list is S-MOD-BANLIST. Source: .claude/docs/design/benchmarks/moderation-tools.md, Features table. Plan impact: No document settles the tab list for the user hub. S-USER-HUB must define it in its spec and cover these six features.

**Whether chat filters fold lookalike letters with MessageNormalizer.** No for the Regex and Blocklist filter types. ChatFilterExecutionHandler matches the raw message with Regex.IsMatch plus IgnoreCase (Infrastructure/Moderation/EventHandlers/ChatFilterExecutionHandler.cs:438-447) and with string.Contains plus OrdinalIgnoreCase (:459-470), so no confusable or leet folding happens. Only the LinkPolicy filter type folds, because LinkPolicy.IsTrippedBy calls LinkDetector.Find (Domain/Moderation/ChatFilters/LinkPolicy.cs:101-103) which calls MessageNormalizer.ReadableForm (Domain/Moderation/SpamDefense/LinkDetector.cs:364). The dashboard test path in ChatFilterService.TestPattern has the same raw matching (Infrastructure/Moderation/ChatFilterService.cs:299-332). Plan impact: A new slice is needed: run Regex and Blocklist filters on the MessageNormalizer skeleton (or offer it as a per-filter option), in both ChatFilterExecutionHandler and ChatFilterService.TestPattern so the test matches live. No existing slice ID covers it (a main-session grep of the plan for normaliz, lookalike, confusable and homoglyph found only the unrelated "Delete all like this" slice).

**Escalation rung maths unit test.** A test exists. ModerationEscalationServiceTests.Offenses_climb_the_ladder_and_clamp_at_the_top_rung asserts warn at 1, timeout 60 at 2, ban at 3 and ban at 4 (tests/NomNomzBot.Infrastructure.Tests/Moderation/ModerationEscalationServiceTests.cs:56-83). The same file tests window lapse reset (:85-103) and 15 concurrent offenses reaching rung 16 (:202-283). The code under test picks the rung with ladder.Where(AtOffense <= newCount).MaxBy(AtOffense) (src/NomNomzBot.Infrastructure/Moderation/ModerationEscalationService.cs:116-118). ViolationEscalationServiceTests.cs covers warn, timeout and ban across offenses 1 to 3 (:194, :234). Plan impact: none. The paper's "no unit test found" statement is wrong and should be corrected.

**Discord bot token storage (vault or config).** The code reads the platform-wide Discord bot token, not a per-channel one, through ISystemCredentialsProvider.GetValueAsync("discord", "bot_token") (src/NomNomzBot.Infrastructure/Discord/Gateway/DiscordRestBotGateway.cs:872-901, call at 892). That method reads the system Configuration row with key discord.bot_token first (a sealed SecureValue is opened under the row's AAD, otherwise the plain Value), then falls back to the config key Discord:<PascalCase(field)> (src/NomNomzBot.Infrastructure/Platform/Configuration/SystemCredentialsProvider.cs:75-80, 109-124). No code path writes discord.bot_token: the setup endpoint saves only discord.client_id and discord.client_secret (src/NomNomzBot.Api/Controllers/V1/SystemController.cs:426-433), so the token comes from config or env today. Defect found: PascalCase only upper-cases the first letter (SystemCredentialsProvider.cs:148-149), so the fallback key is Discord:Bot_token, while docker-compose.yml:101 sets Discord__BotToken and .env.example:84 uses DISCORD_BOT_TOKEN; I read no value and did not check the live box. Plan impact: new slice needed (no ID yet): fix the field-to-config-key mapping for underscore fields (bot_token, and youtube api_key at src/NomNomzBot.Infrastructure/Music/YouTubeApiKeyResolver.cs:24) with a test that Discord__BotToken resolves, and add a vaulted write endpoint for discord.bot_token. Token-custody paper section 8 should say: DB vault first, config fallback, no writer yet.

**Discord OAuth scope list.** Discord requests exactly two scopes, `bot guilds` (NomNomzBot.Api/Controllers/V1/DiscordOAuthController.cs:38), sent as the authorize `scope` (DiscordOAuthController.cs:110) with `integration_type=0` (guild install, :114); no `permissions=` parameter is in that URL. The same two scopes are stored on the vault connection by `DiscordGuildService` (NomNomzBot.Infrastructure/Discord/DiscordGuildService.cs:156 and :173, both pass `oauth.Scopes`, built from `DiscordScopes.Split(' ')` at DiscordOAuthController.cs:256). Discord is not in `OAuthProviderRegistry` (grep for "discord" in NomNomzBot.Infrastructure/Integrations/OAuthProviderRegistry.cs finds nothing); status comes from the guild table instead (NomNomzBot.Infrastructure/Integrations/IntegrationOAuthService.cs:451-456). Plan impact: none. The Discord row in 10.1 can state "two scopes, bot and guilds, no user-identity scope"; no slice needed.

**Seal on write for CustomDataSource, SupporterConnection, VtsConnection, webhook and TTS secret columns.** Every secret column named is sealed on write through `ITokenProtector.ProtectAsync` or `ISubjectKeyService.ProtectAsync`: CustomDataSource.AuthSecretCipher (NomNomzBot.Infrastructure/CustomEvents/CustomDataSourceService.cs:196 and :378), SupporterConnection.AuthSecretCipher (NomNomzBot.Infrastructure/Supporters/SupporterConnectionService.cs:185), VtsConnection.PluginTokenCipher (NomNomzBot.Infrastructure/Vts/VtsConnectionService.cs:121), InboundWebhookEndpoint.VerificationSecretEnvelope (NomNomzBot.Infrastructure/Webhooks/InboundWebhookEndpointService.cs:236), OutboundWebhookEndpoint.SigningSecretEnvelope (NomNomzBot.Infrastructure/Webhooks/OutboundWebhookEndpointService.cs:497; the secondary column is only a copy of an already sealed value, :280), and the TTS Azure and ElevenLabs keys (NomNomzBot.Infrastructure/Tts/TtsConfigService.cs:203-230, via the subject key service). Exception found: `VtsConnection.BridgeToken` and the OBS bridge token are plain `Guid.NewGuid().ToString()` values stored unsealed (NomNomzBot.Infrastructure/Vts/VtsConnectionService.cs:85, NomNomzBot.Infrastructure/Obs/ObsConnectionService.cs:101 and :120, column NomNomzBot.Domain/Vts/Entities/VtsConnection.cs:50). The OBS token is also put in a URL query string (ObsConnectionService.cs:148) and matched in plaintext (NomNomzBot.Api/Hubs/OBSRelayHub.cs:76). Plan impact: New gap, not in the plan from this lane: seal or hash the VTS and OBS bridge tokens (an equality lookup needs a hash, not a cipher), and take the token out of the URL query string if the bridge client allows it. Add as one slice under 10.6 (for example S-TOKEN-BRIDGE-SEAL). The sealing rows for the other five columns need no slice.

**`Service.ClientSecret` sealing and other `Service` writers.** `MusicProviderTokenMirror` is the only code in server/src that creates or writes a `Service` row (`_db.Services.Add` at NomNomzBot.Infrastructure/Music/MusicProviderTokenMirror.cs:117; no `new Service` and no other `ClientSecret =` assignment exists in Infrastructure or Api). It seals AccessToken, RefreshToken, ClientId and ClientSecret with `ITokenProtector` (MusicProviderTokenMirror.cs:91, :96, :103, :108). Other code only reads, removes or backfills: IntegrationsController.cs:225-288 removes rows, the two backfill seeders unprotect and move tokens into the vault (NomNomzBot.Infrastructure/Content/Identity/YouTubeServiceConnectionBackfillSeeder.cs:132-156). No code in server/src writes a `twitch_bot` Service row; the only mention of who writes it is a comment (NomNomzBot.Api/Controllers/V1/ChannelBotController.cs:128, "written by the bot OAuth flow"), and the grep found no such writer. Plan impact: none for sealing. Small doc fix: the `twitch_bot` writer named in that comment does not exist in this tree, so the 10.1 "old Service rows" text should say rows are only legacy data plus the music mirror.

**Exact list of Twitch scopes we request (about 79).** The catalogue is 70 distinct scopes, not about 79: 57 distinct scopes come from 93 `[RequiresTwitchScope(TwitchScopes.X)]` uses in the Helix sub-clients (all use named constants; counted by script over NomNomzBot.Infrastructure/Platform/Transport/Helix/SubClients and resolved against NomNomzBot.Application/Contracts/Twitch/TwitchScopes.cs), plus 13 hand-kept residual EventSub scopes (NomNomzBot.Infrastructure/Identity/TwitchScopeRegistry.cs:45-80), with no overlap, built into `FullCatalogue` at TwitchScopeRegistry.cs:82. The catalogue is not what a login requests: login asks for 4 scopes (`user:read:email`, `user:read:chat`, `user:write:chat`, `user:read:moderated_channels`, NomNomzBot.Infrastructure/Identity/AuthService.cs:89-98); the rest are asked for on demand (AuthService.cs:84-88). The bot account asks for 7 (AuthService.cs:102-115). The "~79" figure is a stale comment (AuthService.cs:183). Plan impact: S-TOKEN-LEAST-SCOPE (10.6): the registry script already exists as `TwitchScopeRegistry`; the slice only needs to emit its list and a per-scope reason, not build the registry. Use 70 (57 + 13) in the paper, and correct the stale "~79" comment in the same slice.

**Whether `IamAuditLog` covers token use.** No. `IamAuditLog` rows are written by `PlatformIamService.AuthorizePlatformAsync` (NomNomzBot.Infrastructure/Identity/PlatformIamService.cs:69) and by about a dozen named staff actions (for example webhook:replay at NomNomzBot.Infrastructure/Identity/AdminService.cs:420, scheduled_job:retry :593, eventstore:admin-replay :1000, invoice:refund, entitlement_grant:issue, feature_flag:set, platform_default:*, content publish). `IntegrationTokenVault` (NomNomzBot.Infrastructure/Identity/IntegrationTokenVault.cs) has no reference to `IamAuditLogs`; it only logs and publishes connection events, and `GetAccessTokenAsync` and `GetRefreshTokenAsync` (:230, :235) write no audit row. In self-host mode `AuthorizePlatformAsync` returns true with no audit (PlatformIamService.cs:55-56). Plan impact: Confirms the gap in 10.3 F5: token reads are not audited. A new slice is needed if the paper wants token-use audit (for example S-TOKEN-USE-AUDIT, a vault read record that names principal, connection and reason); it cannot reuse `IamAuditLog` as is, because that table is keyed on a platform principal and permission key.

**Hidden validate call inside a library.** No package that talks to Twitch is referenced. Directory.Packages.props declares 56 PackageVersion entries, and none is Twitch-named (no TwitchLib, no OAuth handler package); the closest network clients are SocketIOClient (src/NomNomzBot.Infrastructure/NomNomzBot.Infrastructure.csproj:101, used only in src/NomNomzBot.Infrastructure/Supporters/Sockets/SocketIoFrameSource.cs, which has no Twitch reference) and Stripe.net. Our own code calls id.twitch.tv only for authorize, token, revoke and device (src/NomNomzBot.Infrastructure/Identity/AuthService.cs:1302; src/NomNomzBot.Infrastructure/Platform/Auth/TwitchAuthService.cs:42-43; src/NomNomzBot.Infrastructure/Platform/Auth/TwitchAppTokenProvider.cs:32; src/NomNomzBot.Infrastructure/Platform/Auth/TwitchDeviceCodeService.cs:36-37), and a search of every .cs file in server/src for oauth2/validate finds nothing. Limit: I judged the packages by name and usage; I did not open package binaries (the NuGet cache exists on disk at the user's .nuget/packages folder). Plan impact: none. The paper's F6 premise that a library validates hourly does not hold for this repo; there is no validate call at all, so any hourly validate must be built, not found.

**EventSub type name for authorization revoke (`user.authorization.revoke` came from a forum post).** The official subscription-types page lists the type as "The user.authorization.revoke subscription type sends a notification when a user's authorization has been revoked for your client id." (https://dev.twitch.tv/docs/eventsub/eventsub-subscription-types/). The same section says "NOTE This subscription type is only supported by webhooks and conduits, and cannot be used with WebSockets." and "Provided client_id must match the client id in the application access token." (same URL). The name from the forum post is correct. Plan impact: S-TOKEN-AUDIT can use the name `user.authorization.revoke` (version 1). The type needs a webhook or conduit transport, so it works only when EventSub conduit mode is on. The WebSocket-only path cannot receive it. The condition holds only our client ID, and the subscription needs an app access token.

**Which framework features use the Data Protection ring.** The ring is registered once (NomNomzBot.Infrastructure/DependencyInjection.cs:928, NomNomzBot.Infrastructure/Platform/Security/DataProtectionRegistration.cs, files in `dataprotection-keys`, NomNomzBot.Infrastructure/Platform/SelfHostDataPaths.cs:46), but a search of all of server/src finds no `IDataProtector`, `IDataProtectionProvider`, cookie scheme, antiforgery, session, TempData or Identity use. The only authentication schemes are JwtBearer and the custom API-token scheme (NomNomzBot.Api/Program.cs:334-348). So no first-party code and no registered framework feature in this tree uses the ring; envelope encryption for secrets goes through `TokenProtector` and the DEK/KEK path instead (DependencyInjection.cs:915-927). The only limit: this is a source search, so a framework feature that creates a protector inside a package is not seen; not run against a live process. Plan impact: none for F7 as a risk: the ring protects nothing today, so it is a low-value key store. A new small slice is optional: remove the registration, or document it as reserved (for example S-TOKEN-DP-RING-REMOVE), so the paper does not count it as a custody surface.

**Federation outbound transport and peer key storage.** There is no outbound federation transport in this tree. `IFederationEventSigner.SignAsync` has no caller outside its own class (grep over server/src), the Federation folder has no `HttpClient`, and the inbound gateway only calls `VerifyAsync` (NomNomzBot.Infrastructure/Federation/FederationInboundGateway.cs:51). Peer keys are stored as public keys only, in plaintext, in `FederationPeerKey.PublicKey` (NomNomzBot.Domain/Federation/Entities/FederationPeerKey.cs, config at NomNomzBot.Infrastructure/Platform/Persistence/Configurations/FederationPeerKeyConfiguration.cs:23); verify uses RSA-SHA256 and fails closed on unknown, inactive or expired keys (NomNomzBot.Infrastructure/Federation/FederationEventSigner.cs:74-99). The local private signing key comes from configuration keys `Federation:SigningKeyId` and `Federation:SigningKeyPem`, read as a plain string by `FederationSigningKeyProvider` (FederationEventSigner.cs:139-140), so it is not in the OS vault or the DEK/KEK path. Plan impact: 10.5 federation fix slice: move the private signing key from configuration into the vault/KEK custody, or document the config seam as the current limit (the class comment already calls it a seam, FederationEventSigner.cs:129-132). Peer public keys need no sealing. No outbound-transport slice is needed until an outbound sender exists; if one is planned, its design should require the vault key first.

**Whether `ModerationActionTakenEvent` is journaled.** Yes. JournalingEventBusDecorator writes every published domain event to the event journal, this one included (section 10). Plan impact: none

**OWASP Secrets Management cheat sheet.** The sheet says "engineers should not have access to all secrets in the secrets management system, and the Least Privilege principle should be applied" (section 2.3). It lists what to audit, including "Who requested a secret and for what system and role" and "When the secret was used and by whom/what" (section 2.6). For encryption it prefers "AES-256 using GCM (Galois Counter Mode)" or ChaCha20 with Poly1305 (section 7.1). Revocation: "When secrets are no longer required or potentially compromised, you must securely revoke them to restrict access" (section 2.7.3). Source: https://cheatsheetseries.owasp.org/cheatsheets/Secrets_Management_Cheat_Sheet.html Plan impact: Token custody needs an authenticated-encryption cipher (AES-256-GCM) for stored tokens. It needs per-access audit records (who, which channel's token, when). Operators and engineers must not read all streamers' tokens by default. A compromised token needs a defined revoke path.

**AWS KMS concepts.** The page defines a key hierarchy where a "Customer data key" is a "User-defined symmetric or asymmetric key exported from HSM in plaintext and ciphertext" with "Rotation and use controlled by application". On KMS key rotation, "The older HBKs are preserved and can be used to decrypt and verify previously protected data. But only the active cryptographic key can be used to protect new information." Customer managed keys give "full control" and can be audited: "You can use your customer managed key in cryptographic operations and audit usage in AWS CloudTrail logs." Source: https://docs.aws.amazon.com/kms/latest/developerguide/concepts.html (this concepts page does not use the words "envelope encryption" or "encryption context"; it was read in full and those two topics are not on it). Plan impact: Supports a key hierarchy: a master key wraps per-record data keys, and rotation keeps old key versions for decrypt while new writes use the active version. This matches a key-id column on each stored token so ENCRYPTION_KEY rotation re-wraps lazily instead of forcing re-auth. Encryption context (AAD binding a token to its channel) is not covered by this page and cannot be claimed from it.

**RFC 9700 (OAuth security best practice).** Section 2.2.2 says "Refresh tokens for public clients MUST be sender-constrained or use refresh token rotation", and section 4.14 describes rotation: "The previous refresh token is invalidated, but information about the relationship is retained by the authorization server." For confidential clients (a bot server), RFC 6749 already binds refresh tokens to the client. On storage, section 4.9.3 says "The resource server MUST treat access tokens like other sensitive secrets and not store or transfer them in plaintext." Source: https://www.rfc-editor.org/rfc/rfc9700.txt (also https://www.rfc-editor.org/rfc/rfc9700.html) Plan impact: A stored token must not sit in plaintext. The bot must save the new refresh token atomically each time a provider rotates it, or the grant is lost. Federated bots must not forward a streamer's raw access tokens to other bots.

**RFC 7009 (token revocation).** Section 2.1 says "If the particular token is a refresh token and the authorization server supports the revocation of access tokens, then the authorization server SHOULD also invalidate all access tokens based on the same authorization grant." Section 2.2 says "The authorization server responds with HTTP status code 200 if the token has been revoked successfully or if the client submitted an invalid token." Section 2.1 also requires client authentication: "The authorization server first validates the client credentials (in case of a confidential client) and then verifies whether the token was issued to the client making the revocation request." Source: https://www.rfc-editor.org/rfc/rfc7009.txt Plan impact: On streamer disconnect or account deletion, revoke the refresh token at the provider, then delete the local copy. Treat HTTP 200 as success even for an already-dead token, so revocation is safe to retry. Revoke with the bot's own client credentials. Not every provider implements RFC 7009; support is per provider; the row "Which of our providers support token revoke" settles it.

**ActivityPub (W3C).** Appendix B.1 says "Unfortunately at the time of standardization, there are no strongly agreed upon mechanisms for authentication", so the spec mandates no signature scheme. Appendix B.2 says "federated servers also should not trust content received from a server other than the content's origin without some form of verification." Appendix B.7 says "Servers should implement protections against denial-of-service attacks from other, federated servers" and names rate limiting. Source: https://www.w3.org/TR/activitypub/ Plan impact: Bot-to-bot federation must pick and document its own signing scheme; ActivityPub does not supply one. Verify the signer and the signer's permission for every inbound request, and rate-limit inbound federated calls per peer, especially on actions with side effects.

**Mastodon HTTP signatures.** The guide says "Typically, you will want to include the request target, as well as the host and the date" in the signed headers. For POST it says "you must calculate the RSA-SHA256 digest hash of your request's body and include this hash (in base64 encoding) within the Digest: header." The receiver must "Use the Date: header to check that the signed request was made within the past 12 hours", and the keyId is "a URI for the publicKey object associated with the actor", resolved to the actor's public key. Source: https://docs.joinmastodon.org/spec/security/ Plan impact: Signed bot-to-bot requests should sign method plus path, host, date and a body digest. A 12-hour replay window is wide, so use a much tighter window plus a nonce or one-time id for actions with side effects. Peers look up the public key by a keyId URL, so publish each bot's public key at a stable URL and support key rotation.

**Matrix server-server API.** The spec says "Every HTTP request made by a homeserver is authenticated using public key digital signatures. The request method, target and body are signed by wrapping them in a JSON object", sent in an Authorization header with scheme X-Matrix, and the signed object carries both "origin" and "destination" server names. Keys expire: "Servers MUST use the lesser of this field and 7 days into the future when determining if a key is valid." Transport rule: "Server-server communication must take place over HTTPS. The destination server must provide a TLS certificate signed by a known Certificate Authority." Source: https://spec.matrix.org/latest/server-server-api/ Plan impact: Sign the full request (method, path, body) and include origin and destination names in the signed object so a captured request cannot be replayed to a third bot. Give peer signing keys a short validity cap and re-fetch them, so a stolen key stops working. Require HTTPS with a CA-signed certificate between bots.

**Handler order for one chat message.** Partly read. EventBus runs handlers one after another, in the order DI returns them (Infrastructure/Platform/Eventing/EventBus.cs:53-56,64-69). DI order is the order the assembly scan registers them: Infrastructure handlers first (Infrastructure/DependencyInjection.cs:244-248; scan loop Platform/AssemblyScanExtensions.cs:174-180 uses Assembly.GetTypes order), then Api handlers (Api/Program.cs:124 then 271). IEventHandler has no order member (Domain/Platform/Interfaces/IEventHandler.cs:18-22) and no order attribute exists in src. So the code states no order between ChatMessageHandler (Infrastructure/Chat/EventHandlers/ChatMessageHandler.cs:56) and ChatFilterExecutionHandler (Infrastructure/Moderation/EventHandlers/ChatFilterExecutionHandler.cs:62); the order depends on reflection, not on a declared rule. Test check (main session, 2026-10-10): no test file under server/tests that names ChatFilterExecutionHandler also names ChatMessageHandler or AddInfrastructure (grep returned no file). No test pins the order. Plan impact: S-MOD-ECHO-AFTER-FILTER stays needed: make the order explicit instead of relying on GetTypes order.

**Discord Safety guide.** The Discord Safety Center and Safety Library were opened. The Library lists a server moderator track and "Four steps to a safer server". That article says "Setting a high verification level is a great way to protect your server from spammers or raids." with levels None, Low, Medium, High ("a member of the server for at least 10 minutes... a good way to stall raids") and Highest ("verified phone number"). "Auto Moderation in Discord" says "Auto moderation isn't a replacement for manual moderation; rather, it serves to enrich it." and that AutoMod "applies keyword filters" with "keyword and spam filters" that can "automatically trigger moderation actions such as blocking messages". Discord also has a "Twitch X Discord" article for streamers. Source: https://discord.com/safety-library ; https://discord.com/safety/360043653152-four-steps-to-a-super-safe-server ; https://discord.com/safety/auto-moderation-in-discord ; https://discord.com/safety/twitch-x-discord Plan impact: For the Discord side of a multi-platform safety plan: a join-age/verification gate to slow raids, keyword and spam filters, and a mod team with 2FA. These map to our Twitch features (follower-only, chat verification, AutoMod). One shared "raid defence" setting per community could drive both Twitch and Discord. The bot must treat auto-moderation as support for human mods and not a replacement.

**VoiceBuiltin echo of the query.** Two replies echo viewer text unfiltered into bot chat. "No voices found for '{voice.language}'" uses the raw trimmed query when no locale matches (Infrastructure/Tts/Builtins/VoiceBuiltin.cs:182,193-197). "Voice '{query}' not found" uses the raw voice query when the search returns nothing (VoiceBuiltin.cs:498-521). Both go out through the response composer (VoiceBuiltin.cs:647-662), which the file does not pass through any outbound filter. Plan impact: S-MOD-OUTBOUND-FILTER: both echo sites are the concrete cases to cover. Not yet read: whether the composer or chat sender filters the final text further downstream.

**Chat HTML fragment allows img and marquee.** Confirmed by the allow-list. img and marquee are in AllowedTags (Infrastructure/Chat/ChatHtmlSanitizer.cs:94-95), src is an allowed attribute (ChatHtmlSanitizer.cs:118), and only the https scheme is checked, with no host allow-list (ChatHtmlSanitizer.cs:191-192). So any https image URL a permitted sender posts renders. The path is opt-in per channel (use_chat_html, default off) and limited to senders with standing (Infrastructure/Chat/Adapters/HtmlFragmentAdapter.cs:21-24,34-38). Still not run as a live test: only the code path is read. Plan impact: none to existing slices. A new slice is needed if the paper wants an image-host allow-list or a hate-image check on this path.

**UpdateUserInfo, Game and Lurk builtins.** All three were read line by line. Game parses the bet with long.TryParse and echoes only numbers and the fixed trigger word (Infrastructure/Commands/Builtins/GameBuiltins.cs:111-120, :168-176). Lurk echoes only the chatter's own display name (LurkBuiltins.cs:78-82, :113-119). UpdateUserInfo echoes the raw argument in one place: the not-found reply uses login in the fallback text and the user variable (UpdateUserInfoBuiltin.cs:143-152); the argument is only trimmed and stripped of one leading @ (Application/Commands/Builtin/MentionParser.cs:29-30), and naming another user needs moderator level (UpdateUserInfoBuiltin.cs:63-70). Plan impact: none for viewers. Section 11.1 may say these builtins read no free viewer text, with one exception: !update echoes a moderator-typed argument on a not-found reply.

**Whether moderation records store the operator behind an act-as call.** No. Under act-as the access token's sub is the impersonated subject and the operator is only in the ambient Impersonation context (src/NomNomzBot.Application/Abstractions/Auth/ICurrentUserService.cs:23-28). The dashboard ban passes actorId from the NameIdentifier claim (src/NomNomzBot.Api/Controllers/V1/ModerationController.cs:139, 150-156), which is that subject, and the writer stores it as Record.UserId with a JSON payload of action, target, reason and duration only (src/NomNomzBot.Infrastructure/Moderation/ModerationActionRecord.cs:24-31, 79; src/NomNomzBot.Domain/Platform/Entities/Record.cs:17-33). ModerationService.BanAsync does not read the Impersonation context (src/NomNomzBot.Infrastructure/Moderation/ModerationService.cs:131-184); the operator reaches only the event journal, which writes ActorUserId = operator and OnBehalfOfUserId = subject (src/NomNomzBot.Infrastructure/EventStore/EventJournalService.cs:284-301), and soft-delete DeletedBy (src/NomNomzBot.Infrastructure/Platform/Persistence/Interceptors/SoftDeleteInterceptor.cs:200-208). Plan impact: S-MOD-ACTOR-AUDIT: confirmed gap. The moderation_action row needs the operator (ICurrentUserService.Impersonation.OperatorUserId) added to the payload or a column; the test should ban under an act-as token and assert the row names both people.

**Whether a validator forces ApiKey ExpiresAt.** No validator forces it. The create request has only data-annotation attributes and ExpiresAt is a nullable property with no attribute (src/NomNomzBot.Application/AutomationApi/Dtos/AutomationTokenDtos.cs:35-49), and no file under server/src declares an AbstractValidator. The create service copies request.ExpiresAt as is (src/NomNomzBot.Infrastructure/AutomationApi/AutomationApiTokenService.cs:120) and the entity column is nullable (src/NomNomzBot.Domain/Automation/Entities/AutomationApiToken.cs:53), so a dashboard-made token can live forever; only paired device tokens get a fixed 30 days (src/NomNomzBot.Infrastructure/AutomationApi/AutomationPairingService.cs:46-50, 380-389). The IPC dev-mode key is the one place that checks a supplied expiry, and only that it lies in the future (src/NomNomzBot.Infrastructure/Services/Ipc/IpcDevModeService.cs:80-84). Plan impact: S-MOD-APIKEY-LIMITS: the test must assert that a create with ExpiresAt null succeeds today; forcing or capping expiry is new behaviour that the slice must add, with a migration path for existing no-expiry tokens.

**AutomationPairingService.cs and the rest of BanAction.cs.** BanAction is 69 lines and takes no actor: it resolves user_id from the action field, then target.id, then user.id (src/NomNomzBot.Infrastructure/Moderation/PipelineActions/BanAction.cs:49-53), and calls _moderation.BanAsync(ctx.BroadcasterId, userId, reason, token) (BanAction.cs:59-64). The pipeline service behind it passes moderatorId: null on both the non-Twitch path and the Twitch path (src/NomNomzBot.Infrastructure/Moderation/PipelineModerationService.cs:67, 81) and signs the Twitch call as the channel owner (PipelineModerationService.cs:22-27, 76-83). The pairing service has a 5-minute code, default scopes invoke/events/read with chat only when the operator asked, 5 redeems per client and 30 globally per minute, and a 30-day paired token (src/NomNomzBot.Infrastructure/AutomationApi/AutomationPairingService.cs:30-31, 38-44, 50). Plan impact: S-MOD-APIKEY-LIMITS: a pipeline ban is attributed to the broadcaster id, never to the token or device that fired the pipeline; the test should pin that, and any per-key audit must add the token id on this path.

**How our integration behaves when a channel is banned by Twitch and returns.** No code models a Twitch-side ban; the only channel states are active, suspended, churned and platform_banned, and those are our own platform's (src/NomNomzBot.Domain/Identity/Enums/AuthEnums.cs:45-51). The nearest Twitch signal is an EventSub revocation with status user_removed or authorization_revoked (src/NomNomzBot.Infrastructure/Platform/Eventing/WebSocketEventSubTransport.cs:962-973), which flips the channel's Twitch connection to needs_reauth and posts a lost-access notice in chat (src/NomNomzBot.Infrastructure/Platform/Eventing/EventHandlers/EventSubRevokedEventHandler.cs:39-40, 48, 59-63, 77-80). Only a fresh grant clears needs_reauth (AuthEnums.cs:116-118), so on return the streamer must reconnect from the dashboard. Meanwhile BotLifecycleService keeps the channel in its working set while Channel.Status is active, whatever the connection status, and retries the subscription set every tick (src/NomNomzBot.Infrastructure/BackgroundServices/BotLifecycleService.cs:212-220, 262-278). Not read: how Helix errors for a suspended user are surfaced per call, outside the revocation path. Plan impact: S-MOD-OWNER-WATCH: design for a reconnect on return, not an automatic resume; the test should deliver a user_removed revocation and assert needs_reauth plus the chat notice, then assert that a routine token refresh does not clear it.

**YouTube liveChatMessages.streamList scope.** The docs are silent on the scope. The streamList reference page has no Authorization or scope section (full page text read, no match for "Authorization" or "scope"; https://developers.google.com/youtube/v3/live/docs/liveChatMessages/streamList), and the liveChatMessages.list page has none either (https://developers.google.com/youtube/v3/live/docs/liveChatMessages/list). The streaming guide says only "You can use an OAuth 2.0 access token or an API key to run the demo." (https://developers.google.com/youtube/v3/live/streaming-live-chat). The REST discovery document lists scopes for the sibling method list: youtube, youtube.force-ssl, youtube.readonly (https://www.googleapis.com/discovery/v1/apis/youtube/v3/rest), and has no streamList entry. Plan impact: Request the scope set already used for liveChatMessages.list (youtube.readonly is the least privilege that list allows; our bot also needs youtube.force-ssl for insert and delete). Treat the streamList scope as unproven until a live call with a token limited to youtube.readonly succeeds. Quota cost is "5 units" per call (streamList page).

**Whether TwitchHelixTransport hands Ratelimit headers to callers.** No. SendAsync returns only Result and disposes the response (Infrastructure/Platform/Transport/Helix/TwitchHelixTransport.cs:175-185); MapErrorAsync builds Result.Failure from status, code and body, with no headers (:374-404). The headers are read in the delegating handler TwitchRateLimitHandler (Infrastructure/Platform/Transport/Helix/TwitchRateLimitHandler.cs:56-59, :79-96), which passes them to rateLimiter.Observe and publishes TwitchHelixRateLimitedEvent only on a 429 (:61-73). Plan impact: S-MOD-RATE-HEADERS must read the remaining count and reset time from ITwitchRateLimiter state or the TwitchRateLimitHandler event, not from the transport. Its cited lines TwitchHelixTransport.cs:341 (a BuildMessage options line) and :384 (the 429 code map) should become TwitchRateLimitHandler.cs:56-59 and :79-96.

**Whether the outbound webhook dispatcher forwards ModerationActionTakenEvent.** Yes, with no per-type exclusion. ChannelModerateTranslator publishes the event (server/src/NomNomzBot.Infrastructure/Platform/Eventing/Translators/ChannelModerateTranslator.cs:49,58). The journal decorator captures every published event by type name and calls every post-commit hook (Infrastructure/EventStore/EventStoreSubscriber.cs:42-58; EventStore/JournalingEventBusDecorator.cs:117-120). OutboundWebhookFanoutHandler enqueues for any endpoint whose subscribed list holds "*" or the exact type (Infrastructure/Webhooks/OutboundWebhookFanoutHandler.cs:72-84; OutboundWebhookDispatcher.cs:424-429), and the type is subscribable via OutboundWebhookEventCatalogue.cs:118. Limit: the fanout skips events with no BroadcasterId (OutboundWebhookFanoutHandler.cs:47-48). Plan impact: none.

**Where the youtube.manage scope is requested at connect time.** The scope set is chosen by the client, not the server. The dashboard sends the constant "youtube.manage" (app/composeApp/src/commonMain/kotlin/bot/nomnomz/dashboard/feature/integrations/ui/IntegrationsScreen.kt:148, 204, 248) as ConnectIntegrationRequest.ScopeSetKey (src/NomNomzBot.Application/Integrations/Dtos/OAuthDtos.cs:17-21); the controller passes it on (src/NomNomzBot.Api/Controllers/V1/IntegrationOAuthController.cs:94), StartConnectAsync resolves it through ResolveScopes (src/NomNomzBot.Infrastructure/Integrations/IntegrationOAuthService.cs:109, 482-507, lookup at 497), and the registry maps it to the youtube and youtube.force-ssl Google scopes (src/NomNomzBot.Infrastructure/Integrations/OAuthProviderRegistry.cs:231-235). The server accepts "+"-joined keys, so a reconnect can add youtube.readonly in the same consent (IntegrationOAuthService.cs:480-481, 487). Plan impact: none. No server default exists: a client that omits "youtube.manage" never asks for it, so any new connect surface must send that key.

**Whether any ModerationProjectionService caller mixes platforms.** Yes, the callers are six event handlers and they are inconsistent. They are the only callers of IModerationProjectionService in src (grep). Ban and timeout pass @event.Provider (Infrastructure/Moderation/EventHandlers/ModerationProjectionHandlers.cs:35 and :54), because UserBannedEvent and UserTimedOutEvent carry Provider (Domain/Moderation/Events/UserBannedEvent.cs:17,20; UserTimedOutEvent.cs:17,20). Unban, warn, warning-acknowledged and automod_denied pass no provider (ModerationProjectionHandlers.cs:64-72, :80-89, :97-102, :116-124) and their events have no Provider field (UserUnbannedEvent.cs:16; no Provider in WarningEvents.cs or AutoModEvents.cs), so the service defaults them to Twitch (Infrastructure/Moderation/ModerationProjectionService.cs:77 and :144). Today only Twitch sources publish these four events: the EventSub translators (Platform/Eventing/Translators/ModerationTranslators.cs, AutoModTranslators.cs, WarningSuspiciousShieldTranslators.cs) and ModerationQueueService.cs (main-session grep, 2026-10-10). So the Twitch default is right today, and it breaks the day Kick or YouTube publish them. Plan impact: S-MOD-PROJECTION-PROVIDER also needs a Provider field on UserUnbannedEvent, WarningSentEvent, WarningAcknowledgedEvent and AutoModMessageUpdatedEvent, passed through the four handlers, plus a test that a Kick unban resolves the Kick user.

**Whether the admin support page lists every journal event type.** No. The history reader (Infrastructure/Identity/AdminSupportService.cs:289-352) queries the journal with ActorUserId = the person and no type filter (AdminSupportService.cs:318-326; EventJournalService.cs:433). So it lists only rows that carry that actor id. The bus capture path that journals domain events never sets ActorUserId (Infrastructure/EventStore/EventStoreSubscriber.cs:47-56), so moderation and chat events from the bus do not show on that page. The row shows only EventId, tenant, type, source and time (Application/Identity/Dtos/AdminSupportDtos.cs:122-129). Plan impact: none to existing slices. A new slice is needed if the paper wants moderation events on the support page: stamp ActorUserId (or a subject id) in EventStoreSubscriber.CaptureAsync, or query by payload subject.

**Full channel.moderate action list in live Twitch docs.** The V2 event table says "The action performed. Possible values are: ban timeout unban untimeout clear emoteonly emoteonlyoff followers followersoff uniquechat uniquechatoff slow slowoff subscribers subscribersoff unraid delete unvip vip raid add_blocked_term add_permitted_term remove_blocked_term remove_permitted_term mod unmod approve_unban_request deny_unban_request warn shared_chat_ban shared_chat_timeout shared_chat_unban shared_chat_untimeout shared_chat_delete" (https://dev.twitch.tv/docs/eventsub/eventsub-reference/, section "Channel Moderate Event V2"). The V1 list is the same minus "warn" ("The type of action: Possible values are: ban timeout unban ... deny_unban_request shared_chat_ban shared_chat_timeout shared_chat_untimeout shared_chat_unban shared_chat_delete", same URL, section "Channel Moderate Event"). The subscription-types page says V2 is "A moderator performs a moderation action in a channel. Includes warnings." and needs "moderator:read:warnings OR moderator:manage:warnings" plus "moderator:read:moderators" and "moderator:read:vips" (https://dev.twitch.tv/docs/eventsub/eventsub-subscription-types/). Plan impact: Use version 2 so the bot sees `warn`. The list holds 33 values in V2 (32 in V1). Each action has its own optional object (for example `followers`, `slow`, `vip`, `warn`). The paper and our enum must cover all 33 values, and the token must carry all eight scope groups of the V2 page.

**Who emits OutgoingRaidStartedEvent.** One emitter in src: ChannelModerateTranslator builds and publishes it when the channel.moderate action is "raid" (Infrastructure/Platform/Eventing/Translators/ChannelModerateTranslator.cs:70-81). It is a Twitch EventSub translator, so only Twitch raids emit it. The only consumer is OutgoingRaidStartedAlertHandler (Infrastructure/Stream/EventHandlers/OutgoingRaidStartedAlertHandler.cs:32), and a test reads it at tests/NomNomzBot.Infrastructure.Tests/Platform/Eventing/Translators/ChannelModerateTranslatorTests.cs:249. Plan impact: none.

**Twitch rate limits for endpoints other than add and remove moderator.** The general rule is "Your app is given a bucket of points. Each endpoint is assigned a points value (the default points value per request for an endpoint is 1)." and "the limits are applied per client ID per user per minute" for user tokens (https://dev.twitch.tv/docs/api/guide/); the example header is "Ratelimit-Limit: 800" (same URL). The Helix reference lists these endpoint-specific limits (https://dev.twitch.tv/docs/api/reference/): Send Chat Announcement "One announcement may be sent every 2 seconds."; Send a Shoutout "once every 2 minutes. They may send the same broadcaster a Shoutout once every 60 minutes."; Add and Remove Channel VIP "a maximum of 10 VIPs within a 10-second window."; Start a raid and Cancel a raid "10 requests within a 10-minute window."; Send Whisper "a maximum of 40 unique recipients per day ... a maximum of 3 whispers per second and a maximum of 100 whispers per minute."; Check AutoMod Status "Rates are limited per channel based on the account type rather than per access token."; Get Clips Download "Limited to 100 requests per minute." Block User, Unblock User and Ban User carry no endpoint-specific limit line, so only the general bucket applies. Plan impact: Queue sizing per endpoint, not one global rate. Hard per-endpoint caps to enforce: shoutout 2 min and 60 min per target, announcement 2 s, VIP add/remove 10 per 10 s, raid 10 per 10 min, whisper 40 recipients/day with 3 per s and 100 per min. Everything else uses the 800-point-per-minute bucket per user token. Extension endpoints have their own limits (20 or 100 per minute) and are not used by the bot.

**What the suspicious-user, unban-request and moderator handlers show on screen.** Suspicious user: the update handler stores the flag and pushes the config domain "suspicious-users" so an open dashboard refetches (Infrastructure/Moderation/EventHandlers/SuspiciousUserHandlers.cs:23-37; Moderation/LowTrustStatusService.cs:78-87). The message handler also adds a non-"none" flagged message to the moderation review queue with source SuspiciousUser (SuspiciousUserHandlers.cs:60-72). Unban request: the created and resolved handlers only publish a ChannelConfigChangedEvent for domain "unban-requests" (action created or updated) so the page refetches; nothing is stored locally (Moderation/EventHandlers/UnbanRequestConfigPushHandlers.cs:17-71). Moderator add and remove: these are pipeline trigger sources (event responses) with variables user and user.id, not a dashboard view (Moderation/EventHandlers/ModerationAlertHandlers.cs:152-224; base class Platform/Eventing/TwitchAlertHandlerBase.cs:23-30). Plan impact: none.

**StreamElements Twitch Moderation Guide.** The real title is "Moderation Guide for the StreamElements Twitch Chatbot" (updated "2 years ago"). It is about chat commands for mods without dashboard access, not about automatic filters. It says "Knowing how to moderate using the Chatbot is a must if you're a moderator in a channel that uses StreamElements as their main chatbot." It lists !cmd add/edit/del/show/options/alias, !settitle and !setgame. It also warns "these commands need to have the correct permissions so if some of these don't work, make sure that the streamer sets all relevant commands to the correct permission level so all moderators can use them." Source: https://support.streamelements.com/hc/en-us/articles/18486260707602-Moderation-Guide-for-the-StreamElements-Twitch-Chatbot Plan impact: A competitor's "moderation" guide for mods is command and title management through chat, with permission levels per command. Our plan should let a mod do these tasks in the dashboard and in chat with per-command permission, and should keep automatic chat safety (filters, timeouts) as a separate item. The guide has no safety-filter content to copy.

**Twitch page "Filing a Report".** The page was opened in a real browser. It says "Reporting is safe and anonymous - your identity will not be revealed to the reported party." and "File one detailed report, rather than several generic reports. Mass or repeatedly reporting the same content will not increase the speed in which a report is investigated or increase the likelihood of enforcement." It lists what to include in "Tell Us More": date and time with time zone, detailed description, channel name, target username, game name. It also describes a "Batch Reporting Tool" widget in Mod View: streamers and mods "can easily view a list of recently banned or timed out users and submit reports to Twitch in bulk", with a reason per user and a "Tell Us More" field. Source: https://safety.twitch.tv/s/article/Filing-a-Report?language=en_US Plan impact: If the bot offers "report to Twitch" help, it should prepare one detailed report per case (time with zone, channel, target, reason) and never mass-report, because Twitch says mass reports do not speed up action. The Batch Reporting Tool is a benchmark for a ban/timeout review list with a reason picker. Abuse of the report tool is itself a violation, so the bot must not auto-report.

**Twitch page "Navigating our Policy on Discussions and Depictions of Self-Harm".** The page was opened in a real browser (post dated "JUN 22, 2022"). It says "sharing suicide notes or graphic details of suicide intent are not appropriate for Twitch" and "This does not mean you cannot share your prior experiences with self-harm or suicide, but it does mean that you should not describe them in vivid or graphic detail, glorify these experiences, or otherwise encourage others to engage in them." It also says "content that promotes weight loss strategies that either explicitly or implicitly encourage eating disorders is prohibited". It points to "Twitch Cares and Crisis Prevention pages" and to local law enforcement for immediate danger. Source: https://safety.twitch.tv/s/article/Navigating-our-Policy-on-Discussions-and-Depictions-of-Self-Harm?language=en_US Plan impact: The line is between supportive talk (allowed) and graphic detail, glorifying or encouraging (prohibited). A bot filter for self-harm terms must not auto-ban people who talk about their struggles. Better plan: flag for a human mod, and offer a configurable crisis-resources reply that points to Twitch Cares. Eating-disorder promotion (weight-loss tips) is a separate blocked-content category.

**Dedicated Twitch IRL-streaming safety page.** Twitch has no dedicated IRL safety page that covers physical safety. The help article help.twitch.tv/s/article/irl-category rendered no body (a script-only page). The Twitch blog post "The IRL Takeover is here" (May 11, 2026) has a "Guidelines + Safety" section that lists chat settings only: "AutoMod set to Level 2", "Email and phone chat verification turned on", "Followers only mode for at least 10 minutes" and "Shield Mode setup". It says nothing on location, doxxing, stream delay or bystanders. Source: https://blog.twitch.tv/en/2026/05/11/the-irl-takeover-is-here/ Plan impact: Twitch leaves physical IRL safety to the streamer. Our IRL preset can apply the four chat settings Twitch names, and S-SAFETY-DOXX covers the address and phone-number gap that Twitch does not.

**AutoMod on a clip title edited later in the editor.** The docs are silent on title edits. The clips help page says only about captions: "When you edit caption text, the updated captions are reviewed by AutoMod." (https://help.twitch.tv/s/article/how-to-use-clips). It lets you "re-edit the title, trim, or portrait layout of any clips of your channel at any time" and names no AutoMod review for that title edit (whole page text read; the word AutoMod appears three times and all are about captions, plus a link to the "How to Use AutoMod" article). Plan impact: Do not promise that AutoMod screens an edited clip title. The bot must run its own banned-term and spam check on clip titles it reads or writes. State in the safety paper that Twitch documents AutoMod review only for edited captions.

**Fulfill or cancel endpoint for Custom Power-ups.** The Helix reference has one Power-up endpoint: "Gets a list of custom Power-ups that the specified broadcaster created." (Get Custom Power-up, GET /helix/bits/custom_power_ups, scope bits:read, https://dev.twitch.tv/docs/api/reference/). The word Power-up appears in no other endpoint section. "Update Redemption Status" is for Channel Points only: "Updates a redemption's status. You may update a redemption only if its status is UNFULFILLED." with PATCH /helix/channel_points/custom_rewards/redemptions (same URL). The EventSub type channel.custom_power_up_redemption.add "sends a notification when a viewer has redeemed a custom Power-up on the specified channel", needs "bits:read scope", and its example payload shows "status" : "unfulfilled" (https://dev.twitch.tv/docs/eventsub/eventsub-subscription-types/). Plan impact: No fulfill or cancel call exists today for Power-ups, so the bot cannot change the status of a Power-up redemption. Build the receive side (EventSub add event plus Get Custom Power-up) and the handler, and mark the status as read-only in the dashboard. Re-check the reference at each release, because the endpoint is marked NEW and may gain a status call.

**Teen whisper and DM default restrictions in detail.** ConnectSafely states: "Private messages ("whispers") from people you're not connected to (someone you don't follow or subscribe to) are blocked by default. We recommend teens keep this setting. Go to Settings > Security and Privacy to view." Twitch's Whispers help page says "New accounts may have restricted whisper capabilities until they verify their phone number. Additionally, users can choose to not receive whispers from users they do not know." and "If you have blocked whispers from strangers, before sending a whisper to a user, you must acknowledge that this will allow the user to whisper you for the next 48 hours." The Parental Controls help page adds an optional "Disable DMs: Teens can be blocked from sending or receiving Whispers (direct messages)." The Twitch page that names "default restrictions for direct messaging" gives no more detail than that phrase. Source: https://connectsafely.org/twitch/ ; https://help.twitch.tv/s/article/how-to-use-whispers?language=en_US ; https://help.twitch.tv/s/article/family-center?language=en_US ; https://safety.twitch.tv/s/article/Introducing-Parental-Controls-for-Teen-Accounts?language=en_US Plan impact: Whispers from strangers are blocked by default, a stranger-whisper opens a 48-hour window once the receiver whispers first, and a parent can switch whispers off for a teen. A bot that sends whispers (for example for ban notices or timeout reasons) can silently fail for strangers and for teens with DMs off. The plan needs a chat or reply fallback, and a "whisper not delivered" state shown to the streamer. Limit of this finding: the Whispers page "Privacy Settings" and FAQ section (the exact default wording) was not read; the default is sourced from the ConnectSafely page.

**ConnectSafely Parent's Guide to Twitch.** The page was opened (title "Parent's Guide to Twitch - ConnectSafely"). It says "Parents should help their teens set up AutoMod settings on any channels where they stream." and "Teens might want to start with the most aggressive AutoMod setting (Level 4)". It says "Private messages ("whispers") from people you're not connected to (someone you don't follow or subscribe to) are blocked by default." and "new channels, by default, have additional safety settings like tools that require new chatters to undertake additional email or phone verification". On privacy it says young streamers "have revealed their age, school name, home address - even whether they were home alone". Source: https://connectsafely.org/twitch/ Plan impact: Under-18 streamers are a real user group. The bot onboarding could offer a "teen streamer" safe preset: strictest AutoMod, verified-chat required, and a filter for personal-data sharing (address, school, age) in the streamer's own chat messages and in viewers' messages. Defaults should start strict for new channels.

**Which of our providers support token revoke.** Twitch: "you can revoke it by sending an HTTP POST request to `https://id.twitch.tv/oauth2/revoke`", with `client_id` and `token`. Discord: `https://discord.com/api/oauth2/token/revoke` does RFC 7009 token revocation, and revoking one token revokes every token of that authorization. Google (YouTube): `https://oauth2.googleapis.com/revoke`. Spotify: the Web API docs read name no revoke endpoint. Source: https://dev.twitch.tv/docs/authentication/revoke-tokens, https://docs.discord.com/developers/topics/oauth2, https://developers.google.com/identity/protocols/oauth2/web-server, https://developer.spotify.com/documentation/web-api Plan impact: S-TOKEN-REVOKE-ON-DISCONNECT revokes at Twitch, Discord and Google on disconnect. For Spotify it can only delete the local copy, and the disconnect screen tells the streamer to remove the app in their Spotify account settings.

**New slices from this pass.** S-TOKEN-BRIDGE-SEAL, S-TOKEN-USE-AUDIT, S-TOKEN-DP-RING-REMOVE, S-MOD-FILTER-LOOKALIKE, S-MOD-MODERATE-DETAIL, S-SUPPORT-EVENT-SUBJECT, S-CHAT-HTML-IMAGE-ALLOW, S-MOD-BLOCK-READBACK, S-POWERUP-STATUS, S-MOD-CLIP-TITLE-CHECK, S-MOD-WHISPER-FALLBACK, S-SAFETY-SHARED-RAID-DEFENCE. Each is in the plan with its done-when.

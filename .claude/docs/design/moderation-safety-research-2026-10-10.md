# Moderation and safety: research paper and one plan (2026-10-10)

Owner request, verbatim: "i like you to do deep research on moderation tooling that help streamers, moderators and viewers to keep the platform safe and fun, and cover all shortcommings of twith lacking proper oversight on its users. i want this to be a dedicated research paper in plain english terms that have all the other work folded in, and every already settled on things reevaluated and pulled out of the other docs and reorganized into one plan. leave no stone unturned"

How to read this paper:

- Short sentences. One idea per line.
- "PLAN" means `.claude/docs/design/SHORTCOMINGS-EXECUTION-PLAN.md`.
- "R: name" means a research note from this session (session scratchpad, `research/name.md`). Each note lists its own URLs. Section 9 lists every URL.
- "via R: internal-settled" means a doc line that a research worker read and quoted. This writer did not open that doc.
- "not checked" means nobody read the source this session. Check it before you build on it.

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

### 1.1 The ten most important findings

1. **A ban across channels must be a request, not a command.** A mass ban was misused for self-gain on 2026-10-10 (PLAN:103-104). Kick tells streamers that "Mass-banning followers, attacking other channels, or organising counter-action puts your account at risk" (R: third-party-2). Bluesky saw shared block lists become vendetta tools (R: bluesky-lists).
2. **Twitch gives us the "silence first" tool.** A bot can set a user to Restrict or Monitor with Helix "Add Suspicious Status to Chat User" (scope `moderator:manage:suspicious_users`) (R: twitch-native). This answers the open "not checked" in guard (h) (PLAN:137-142). The troll gets no airtime while each channel decides.
3. **Twitch has no list of restricted users.** No endpoint returns the suspicious-user list. We must keep our own record from the EventSub `channel.suspicious_user.*` events (R: twitch-native).
4. **Twitch shared bans have no API.** Shared Ban Info lives only in the Twitch UI. It allows at most 50 partner channels, needs consent per pair, and shares every ban, joke bans included (R: twitch-native-help). Our bot cannot read it or add to it.
5. **The streamer needs to see why.** thecodedrift: "What's missing is me knowing why someone was banned from stoney's channel, seeing the last N messages, and with one click going "yeah, fuck that guy"" (PLAN:160-162). No tool we read does this across channels (R: third-party, third-party-3, ffz-7tv-mod).
6. **Twitch hides key settings from bots.** No API for: verified chat (phone or email), Block Hyperlinks, permitted terms, private blocked terms, ban-evasion level, the Shield Mode bundle, mod comments, the Lead Moderator role (R: twitch-native, twitch-shortcomings-3). Our dashboard must say "set this on Twitch" with a link. It must never pretend to control these.
7. **Hate raids are identity attacks.** In a 2021 sample of 9,664 channels, 98% of hate-raid messages were identity attacks (R: twitch-shortcomings, arXiv 2301.03946). Incoming-raid settings are owner-only and static. The raid event names only the raiding channel and a viewer count, not the raiders (R: twitch-native-help-2; PLAN:762-768).
8. **Scam links and impersonation are old and still open.** Block Hyperlinks is all or nothing. Mods and VIPs are exempt, so a stolen VIP account posts links freely (R: twitch-shortcomings-3). No Twitch feature compares a new name with the streamer, the mods or "Twitch Support" (R: twitch-shortcomings-3).
9. **Mature tools warn first and can undo.** Nightbot and Moobot warn on a first offence (R: third-party). Fossabot has a nuke rollback (R: third-party). Discord ships a "Report Issues" button on its machine spam decisions (R: third-party-2). Every automatic action in our bot needs a one-click "this was wrong".
10. **Our own docs disagree.** Fifteen contradictions (K1-K15) sit between the 2026-06-16 design, the user-flow spec, the spam spec and the 2026-10-10 decisions (via R: internal-settled-2). Fifteen more (C1-C15) came from a second read (via R: internal-settled). Section 6 settles each one.

### 1.2 The plan on one screen

| Order | Goal | Main slices |
|---|---|---|
| 1 | Prevent misuse of our own tools (the incident) | S-MOD-TOOLS-SPEC, S-MASSBAN-ABUSE-GUARDS, S-MASSBAN-GRANT-GATE, S-MASSBAN-ENTRUST, S-MASSBAN-RESTRICT-FIRST, S-MASSBAN-DECISION-CARD, S-MASSBAN-RETRY |
| 2 | Oversight and audit | S-MASSBAN-HISTORY, S-MASSBAN-REQUESTER-RECORD, S-MASSBAN-OWNER-OVERRIDE, S-MOD-WRONG-ACTION-REPORT, S-SAFETY-MONITOR |
| 3 | Protect the stream live | S-MOD-LOCKDOWN, S-SPAM-LOCKDOWN-WIRE, S-RAID-VET, S-SPAM-CONTEXT-TRUST, S-UF-M7a |
| 4 | Help moderators decide | S-SUSPICIOUS-USERS, S-MOD-SUSPICIOUS-LEDGER, S-UF-M1a, S-UF-M6a, S-MOD-EVIDENCE-PACK |
| 5 | Protect viewers | S-FILTER-LINKPOLICY (wider), S-SAFETY-FAKE-NAME, S-SAFETY-DOXX, S-MOD-FILTER-TEST |
| 6 | One user hub, live | S-USER-HUB, S-LIVE-EVERYWHERE, S-MOD-BANLIST, S-MOD-NUKE-SCREEN, S-MOD-TWITCH-ONLY-SETTINGS |

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
| 7 | Follow-bot and view-bot waves | Thousands of fake follows, or fake viewers. The 2025 viewbot crackdown cut some channels' viewership by up to 22%. | Streamer's stats, the streamer's standing | Detect the spike and block the bot accounts. Show the batch so the streamer can restore real followers. | Stop the follows at the source. See view bots. Whether a block removes the follow: not checked. | R: twitch-shortcomings; via R: internal-settled-2 (spam-defense s.7) |
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
| FrankerFaceZ | Browser extension | GitHub change log only. The website was not reachable (not checked). | R: ffz-7tv-mod |
| 7TV | Browser extension | GitHub change log only. The website was not reachable (not checked). | R: ffz-7tv-mod |
| Discord AutoMod and raid protection | Platform | Help articles | R: third-party, third-party-2 |
| Dyno, Wick | Discord bots | Docs pages | R: third-party-2 |
| Kick | Platform | Help articles | R: third-party-2 |
| YouTube | Platform | Help article | R: third-party, twitch-shortcomings-2 |
| Mastodon | Platform | Admin docs | R: third-party |
| Bluesky (lists, labels, Ozone) | Platform | Docs, blog, GitHub issue, news (real browser) | R: bluesky-lists |

Correction: an earlier note (R: third-party-3) said "7TV and FrankerFaceZ are emote platforms only, not moderation tools". That claim is wrong. FrankerFaceZ has mod cards with chat history, name history, user stats with ban count, notes count and inline mod icons. 7TV has Mod View, Mod Logs and Shield Mode buttons, a warn icon, and Lead Moderator support (change log v3.1.17) (R: ffz-7tv-mod). The same note also merged "Firebot/Moobot" and cited firebot.tv. Its other rows are not used here.

### 5.2 Feature table: others against NomNomzBot

Status: **have** (built), **partial** (built in part, or built but not reachable), **missing** (a gap to build), **not doing** (rejected, with the reason).

| # | Feature | Seen in | NomNomzBot status | Source for our status |
|---|---|---|---|---|
| 1 | Nuke: delete, timeout or ban everyone who said a phrase or regex in the last N seconds | Fossabot (1-600 s, radiation), StreamElements | partial: nuke batches and reversal exist; no dashboard screen | via R: internal-settled-2 (FEATURES-BY-SIDEBAR:68-77); PLAN:81 |
| 2 | Nuke rollback and a report of who was hit | Fossabot (ROLLBACK, COPY JSON) | partial: reversal exists; the report screen is missing | PLAN:81 |
| 3 | Nuke by username pattern, with a warning about innocent users | StreamElements | missing | R: third-party |
| 4 | Ban one account in many channels | Moobot batch tool (own channel only), none across channels | partial: API and executor exist; no screen; no restrict-first | PLAN:38-44, 53 |
| 5 | Decision card with the reason and last messages, per channel | none found | missing | PLAN:165-166 |
| 6 | Lookalike letters folded before matching | Fossabot, Moobot | have in the spec (normalizer test corpus); code not checked | via R: internal-settled-2 (spam-defense s.8) |
| 7 | Phrase groups and per-phrase severity | Fossabot, Moobot | partial: chat filters exist; filter kinds not fully wired | PLAN:1174-1181 |
| 8 | Test a filter before saving | none found directly; Fossabot silently ignores a bad regex | missing | PLAN:83 |
| 9 | Link filter with an allow-list | Nightbot, StreamElements, Moobot (hidden links, IP links) | partial: the policy is stored and never read | PLAN:745-747 |
| 10 | Temporary link pass for one viewer | Fossabot permit (60 s default, max 3600 s), Nightbot | not checked | none read |
| 11 | Warn first, then act | Nightbot, Moobot, Twitch Warn | partial: Twitch Warn is reachable; pipeline `warn` action missing | PLAN:1182 |
| 12 | Escalating timeouts on repeat offences | Wick multiplier, Dyno ladder | partial: an escalation reset exists in the user panel; the ladder is not checked | via R: internal-settled-2 (FEATURES s.8) |
| 13 | Caps, emote, symbol, repeat, length, zalgo and language filters | Nightbot, StreamElements | partial: chat filter kinds exist; full set not checked | PLAN:1178 |
| 14 | Protection presets (low, medium, high) | StreamElements | partial: spam defence defaults with dry run | via R: internal-settled-2 (spam-defense s.6) |
| 15 | Account-age and follow-age gate | Kick, Twitch verification (UI only), Dyno | partial: trust tiers (Newcomer, Known, Regular) | via R: internal-settled (spam-defense tiers) |
| 16 | Raid shield: one switch tightens the room, then restores it | Discord raid protection (1 hour), Wick panic mode, Twitch Shield | partial: manual lockdown exists; automatic trigger missing | PLAN:734-736; via R: internal-settled (C12) |
| 17 | Incoming-raid check of the raiding channel | Twitch (UI only) | missing | PLAN:762-768 |
| 18 | Silent block of follow bots, with restore | Sery_Bot, CommanderRoot | partial: spec built; S-SPAM-FOLLOWBOT-WIRE named in the spec header but not in PLAN | via R: internal-settled (C14) |
| 19 | Hate-raid detection | Sery_Bot (no thresholds published) | partial: spam defence L0-L5; lockdown wire missing | PLAN:727, 734 |
| 20 | Mod action audit log | Fossabot, Dyno, Ozone | have: History page mod log | via R: internal-settled-2 (FEATURES-BY-SIDEBAR:68-77) |
| 21 | User card with history, notes and stats | FrankerFaceZ, Chatterino, Twitch Mod View | partial: per-user panel exists; two notes stores | PLAN:2066 |
| 22 | Shared notes between mods | Twitch mod comments, FrankerFaceZ notes | partial: two stores; S-UF-M1a merges them | PLAN:2066 |
| 23 | Unified mod queue | Twitch Mod View, Ozone | partial: S-UF-M6a | PLAN:2143 |
| 24 | Custom roles and a separate nuke permission | Moobot, Fossabot, Wick | partial: Critical tier exists; mass-ban endpoint checks `moderation:ban` only | via R: internal-settled (C11) |
| 25 | Restrict or monitor a user | Twitch, Twitch shared bans, Kick | partial: event handlers exist; UI and ledger missing | PLAN:769-772; via R: internal-settled (C12) |
| 26 | Hide a user from others without telling them | YouTube hide user, bot-side shadowban | partial: built per platform identity, not per person | PLAN:2438 |
| 27 | One moderation action on Twitch, YouTube and Kick | StreamElements nuke on all three | partial: room modes are Twitch only | PLAN:737-739 |
| 28 | "Report Issues" on every automatic decision | Discord | missing | R: third-party-2 |
| 29 | Rate brake on staff who ban too fast | Wick anti-nuke | missing | R: third-party-2 |
| 30 | Shared ban lists with consent | Twitch Shared Ban Info, Mastodon CSV import | partial: shared-chat propagation built; publish-out missing | PLAN:751 |
| 31 | Import a ban list from a paste or a file | Moobot, Mastodon | not doing: a central list becomes a vendetta tool (guard l) | PLAN:166-168; R: bluesky-lists |
| 32 | Subscribe to someone else's label or block list | Bluesky | not doing: same reason as row 31 | R: bluesky-lists |
| 33 | Vote-kick by viewers | StreamElements (51% yes), Fossabot | not doing: a crowd can ban the wrong person | R: third-party |
| 34 | CAPTCHA for new chatters | Discord | not doing: no Twitch API; point to Twitch verification instead | R: twitch-native |
| 35 | Live updates on every page | Ozone event stream | partial: S-LIVE-EVERYWHERE | PLAN:173 |
| 36 | Doxx pattern detection | none found | missing | none read |
| 37 | Name impersonation check | none found (Fossabot lookalikes match words, not names) | missing | R: third-party |

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

"DOC" means `.claude/docs/design/`. "SPEC" means `.claude/docs/design/spec/`. Rows marked "via R" were quoted by a research worker; this writer did not open the doc.

### 6.1 Decisions

| # | Source (doc:line) | Decision | Still right? | Reason |
|---|---|---|---|---|
| D01 | DOC/2026-06-16-moderation.md:6 (via R) | One mod dashboard, Moderator and up; what a mod sees scales with level. | Yes | Every product read has a mod surface; FrankerFaceZ and 7TV add one on top of Twitch (R: ffz-7tv-mod). |
| D02 | DOC/2026-06-16-moderation.md:9-12 (via R) | One action queue, user context, opt-in filters per channel, mod audit log. | Yes | Twitch Mod View, Ozone and Fossabot all have these (R: twitch-native-help, bluesky-lists, third-party). |
| D03 | DOC/2026-06-16-moderation.md:15 (via R) | No cross-channel ban by default. | Yes, stronger | Restrict-first makes the default "silence, then ask" (PLAN:137-142). |
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
| D14 | PLAN:95-96 (D-MB1) | A lead moderator and up releases a hold with `!allow massban`. | Changed | The hold is replaced by restrict-first. `!allow massban` becomes the chat form of "Ban here too". Who may accept: question Q2. |
| D15 | PLAN:96-98 (D-MB2) | The requester picks channels with toggles; the broadcaster's opt-out wins. | Changed | See C10. The toggles stay, but list only channels that entrusted this mod. Opt-out becomes "withdraw trust". |
| D16 | PLAN:99-101 (D-MB3) | Community and Moderation fold into one user hub. | Yes | Owner answer 3 (PLAN:88-93). |
| D17 | PLAN:122-125 guard (a) | Own channels and auto-accept channels ban at once. | Changed | Auto-accept was dropped by guard (j) (PLAN:149-150). See C4. |
| D18 | PLAN:125-126 guard (b) | Every request carries evidence. | Yes | Core of the decision card (PLAN:165-166). |
| D19 | PLAN:126-127 guard (c) | Mass-ban power is a named grant, never a role. | Yes | The code checks the wrong permission today. See C11. |
| D20 | PLAN:127-128 guard (d) | A reversed disputed ban counts against the requester; repeats revoke. | Yes | Fossabot bans misusers of its nuke (L7). The count: question Q5. |
| D21 | PLAN:128-130 guard (e) | Every batch reverses in one action. Auto-ban only for hate raids, follow bots and "best viewers" spam. | Yes, narrowed | S-SPAM-CONTEXT-TRUST keeps regulars safe (PLAN:169-172). Follow bots: see K9 and Q1. |
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
| D33 | SD9 (via R) | Presence is never an offence. | Yes, one open point | Follow bots: Q1. |
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
| K1 | User-flow M5 bans directly in every channel. | Restrict-first and entrust (PLAN:137-150). | DOC/user-flow-behavioral-spec-2026-10-03.md:573-589 (M5) and :1037 (X2 Confirm ladder row). |
| K2 | 2026-06-16: LeadModerator and up bans everywhere at once. | Named grant plus entrust plus restrict-first. | DOC/2026-06-16-moderation.md:20. |
| K3 | Shared-chat ban propagates directly. | Restrict-first in the partner channel, plus a decision card. A partner auto-accept for one live session: Q4. | DOC/2026-06-16-moderation.md:16. |
| K4 | `moderation:nuke` granted by the origin broadcaster only. | Two grants: the origin grant (who may ask) and entrust per target (who is trusted there). | docs/bot-capabilities.md:72-80; SPEC/moderation.md:390. |
| K5 | SuperMod vs LeadModerator. | LeadModerator. | docs/bot-capabilities.md:33; FEATURES.md:49; FEATURES-BY-SIDEBAR.md:138; run S-LEADMOD-APP-RENAME (PLAN:3022). |
| K6 | History treats bans as per-channel records only. | S-MASSBAN-HISTORY shows requests, per-channel decisions and overrides. | DOC/owner-punch-list-2026-09-08.md s.12. |
| K7 | Ban here: 10 s Undo only. | Keep for local bans. A cross-channel request is undone by withdrawing it, which lifts the restriction. | DOC/user-flow-behavioral-spec-2026-10-03.md:578 (add the withdraw rule). |
| K8 | Reason chips include Spam, Bot, Off-topic. | Local actions keep all chips. The mass-ban picker offers only hate, insults, harm. | DOC/user-flow-behavioral-spec-2026-10-03.md:546 (add the mass-ban rule). |
| K9 | SD0 test asserts zero bans; owner wants auto-ban. | Auto-ban only at high confidence, only for hate raids and "best viewers" spam, never for Trusted and up. The SD0 test stays for medium confidence. Follow bots: Q1. | SPEC/spam-defense.md:735-738 (name the confidence level); :757 SD9 after Q1. |
| K10 | Trust is positive-only vs "looks at your ban status on all channels". | A ban elsewhere may raise the watch level (Monitor). It never bans alone. | SPEC/spam-defense.md s.6.1 (Semi-Trusted, SD11 text). |
| K11 | "Approve and trust for this stream" is a third trust idea. | No conflict. Keep three names: trust tier, stream trust, entrust. | None. |
| K12 | Punch list: bans are a text blob. | Stale; not checked in code. | DOC/owner-punch-list-2026-09-08.md s.3 (after a code check). |
| K13 | bot-capabilities says network nuke and shared bans are "Specced-only". | Stale; FEATURES.md lists them as built. | docs/bot-capabilities.md:151-156. |
| K14 | M7 spike banner vs automatic lockdown. | One feature: S-MOD-LOCKDOWN owns both the banner and the automatic trigger. | DOC/user-flow-behavioral-spec-2026-10-03.md:613-622 (point to S-MOD-LOCKDOWN). |
| K15 | Dry-run auto-flip and replay: spec target vs "document as shipped". | Open: Q11. | SPEC/spam-defense.md:645-668 after Q11. |

### 6.3 Contradictions C1-C15 settled

C1, C2, C5, C8 and C15 are the same as K9, K2/K4, K10, K5 and K1.

| # | Conflict (via R: internal-settled) | Winner | Doc line that must change |
|---|---|---|---|
| C3 | "Hold" has three meanings. | Mass ban: "waiting for a decision" (restricted). Chat filters: "queue". Spam spec SD12: "cannot hold a message" stays as plain English. | PLAN:60 (S-MASSBAN-HOLD-RELEASE text), PLAN:1174 (S-CHATFILTER-HOLD text), SPEC/moderation.md. |
| C4 | Guard (a) keeps auto-accept; guard (j) drops it. | Guard (j). | PLAN:123-125 (delete the auto-accept clause). |
| C6 | Trust weights fixed in the spec; editable in the shipped code. | Editable with defaults, as every spam knob is (SPEC/spam-defense.md:597-609, via R). | SPEC trust weights text (OWNER-QUESTIONS s.19 line 190, via R). |
| C7 | Nuke reversal: 24 h in the spec, no limit in the code. | Open: Q6. | PLAN:3260 after Q6. |
| C9 | 10-05: only live channels hold. 10-10: every other channel waits for a decision. | 10-10 (newer). Restriction silences at once, so waiting costs nothing in a live chat. | Memory feedback-massban-bans-happen-live-only-holds.md; `MassBanChannelPlanner.cs:218-223` (via R). |
| C10 | Three consent models: opt-in (10-06), opt-out (10-10 answer 2), entrust (10-10 guard j). | Entrust (newest, most specific). The requester's toggles pick among entrusting channels. "Opt-out" = withdraw trust. | PLAN:57-59 (S-MASSBAN-OPTIN-TOGGLES); PLAN:96-98; memory feedback-massban-opt-in-only.md. |
| C11 | The mass-ban endpoint checks `moderation:ban` (Moderator floor). | The named `moderation:nuke` grant. New slice S-MASSBAN-GRANT-GATE. | `ModerationController.cs:337` (via R). |
| C12 | PLAN says nothing consumes suspicious-user events; handlers exist. Lockdown is half built. | The code. | PLAN:769-772 (rewrite the S-SUSPICIOUS-USERS gap to the UI and ledger); PLAN:734-736 (automatic trigger only). |
| C13 | Floors per tab in the user hub are not stated. | Each S-USER-HUB tab names its floor. Money tiles stay Editor and up (PLAN S075, via R). | PLAN:76-80 (add floors to S-USER-HUB done-when). |
| C14 | The spam spec names two open slices that PLAN does not list. | Add S-SPAM-SEED-CORPUS and S-SPAM-FOLLOWBOT-WIRE to PLAN. | PLAN S-SPAM block (:727). |

---

## 7. The one plan

Build order runs top to bottom, number 1 first. It follows PRODUCT-ALIGNMENT D8: fix our own tool before adding. "New" marks a slice ID minted here for a real gap; every other ID already exists in PLAN.

### Goal A: prevent misuse of our own tools

| Order | Slice | What it does | Done-when | Rests on |
|---|---|---|---|---|
| 1 | S-MOD-TOOLS-SPEC (PLAN:48) | One spec for the moderator-of-many surface, built from this paper. | The spec is committed; every section 8 answer is in it; every gap maps to a slice. | Owner request 2026-10-10 |
| 2 | S-MASSBAN-ABUSE-GUARDS (PLAN:121) | Settle guards (a)-(l) as amended in section 6. | As in PLAN:129-130: a request to a channel that has not accepted bans nobody. | PLAN:104 |
| 3 | S-MASSBAN-GRANT-GATE (new) | The mass-ban endpoint checks the named `moderation:nuke` grant. | A test: a Moderator without the grant gets refused; with the grant, the request goes out. | Guard (c); C11 |
| 4 | S-MASSBAN-ENTRUST (new) | A streamer entrusts or withdraws named mods for their channel. | Entrust, reload, read back from the server. A request never reaches a channel that did not entrust the mod. | Guard (j) |
| 5 | S-MASSBAN-OPTIN-TOGGLES (PLAN:57, changed) | The requester's per-channel toggles list only entrusting channels. | As in PLAN:58-59, plus: a withdrawn trust removes the toggle live. | Owner answer 2; C10 |
| 6 | S-MASSBAN-RESTRICT-FIRST (new) | A request restricts the account in each entrusting channel. Accept bans. Reject lifts the restriction. The requester's own channel bans at once. | A test: channel B shows the account restricted, no ban, until a mod in B accepts. | Guards (a), (h), (i) |
| 7 | S-MASSBAN-SKIP-BANNED (PLAN:65) | Skip a channel where the account is already banned. | As in PLAN:65-66. | Incident fault 2 |
| 8 | S-MASSBAN-DECISION-CARD (new) | The card in each channel: requester, ground, origin channel, last N messages, "Ban here too" (primary) and "Reject". Shown in the dashboard inbox; `!allow massban` is its chat form. | E2E on the dev box: a request appears live on channel B's card; "Ban here too" bans on Twitch; the history row shows who decided. | Guard (k); thecodedrift (PLAN:157-162) |
| 9 | S-MASSBAN-HOLD-RELEASE (PLAN:60, folded) | Folded into order 8: a channel without our bot decides on the dashboard card. | Order 8 passes for a channel where the bot is not in chat. | Incident fault 1 |
| 10 | S-MASSBAN-SCREEN (PLAN:53, changed) | Username, ground (hate, insults, harm only), evidence, the channel preview, then one primary "Ban here and ask N channels". | As in PLAN:54-55, with the counts split into banned, restricted and waiting. | Guard (g); K8 |
| 11 | S-MASSBAN-RETRY (PLAN:748) | Retry a failed ban with backoff and show the result. | As in PLAN:749-750. | Dyno lesson L11 |

### Goal B: oversight and audit

| Order | Slice | What it does | Done-when | Rests on |
|---|---|---|---|---|
| 12 | S-MASSBAN-HISTORY (PLAN:68, wider) | Batches, per-channel decisions, who decided, overrides, reversal. | As in PLAN:68-69, plus each decision row names the deciding mod. | K6 |
| 13 | S-MASSBAN-REQUESTER-RECORD (new) | Counts rejects and reversals per requester. The streamer sees the record before entrusting. Repeats revoke the grant. | A test: the set number of reversals revokes the grant; the record shows on the entrust screen. | Guard (d); Q5 |
| 14 | S-MASSBAN-OWNER-OVERRIDE (new) | The system owner skips the accept step for one batch. The override is named, recorded and shown to every affected channel. | A test: an override batch bans at once and every affected channel sees the override note. | Guard (f) |
| 15 | S-MOD-WRONG-ACTION-REPORT (new) | A "this was wrong" button on every automatic action. It undoes the action and logs a false positive for tuning. | Pressing it on a test auto-timeout lifts the timeout on Twitch and adds a row to the false-positive log. | L15; owner rule PLAN:104 |
| 16 | S-SAFETY-MONITOR (PLAN:740) with S-MOD-PROTECTION-STATUS (PLAN:72) | One live protection-status card. | As in PLAN:741-744 and PLAN:72-73. | Goal 2026-10-08 |
| 17 | S-NETBLOCK-LEGS (PLAN:753), S-NETBLOCK-REACH (PLAN:758) | Store failed legs, back off retries, never lift a ban the streamer placed. | As in PLAN:753-761. | Owner rule PLAN:104 |

### Goal C: protect the stream live

| Order | Slice | What it does | Done-when | Rests on |
|---|---|---|---|---|
| 18 | S-SPAM-LOCKDOWN-WIRE (PLAN:734) | A raid or burst triggers lockdown, then restores. | As in PLAN:735-736. | Finding 7 |
| 19 | S-MOD-LOCKDOWN (PLAN:74, wider) | Manual start and end with a timer. Owns the M7 spike banner (S-UF-M7a, PLAN:2702) and turns Shield Mode on. | As in PLAN:74-75, plus the banner starts the same lockdown. | K14 |
| 20 | S-RAID-VET (PLAN:762) | Raider card and a hold-in-lockdown setting for unknown or banned raiders. | As in PLAN:765-768. | Finding 7 |
| 21 | S-SPAM-CONTEXT-TRUST (PLAN:169) | Trust and context weigh every auto-ban. | As in PLAN:170-172. | PLAN:118-119 |
| 22 | S-SPAM-FOLLOWBOT-WIRE (new to PLAN; named in the spam spec header) | Wire the follow-bot track: detect, block, restore per batch. | A follow spike test blocks the bots, bans nobody (unless Q1 says ban), and a batch restore works. | SD9; Q1 |
| 23 | S-SPAM-SEED-CORPUS (new to PLAN; named in the spam spec header) | Seed the spam signature store so near-duplicate checks fire from day one. | Not checked: read the spam spec header for its done-when before building. | C14 |
| 24 | S-MOD-ANNOUNCE-RATELIMIT (new) | Rate-limit the bot's ban and attack notices in chat during a raid. | A test raid of 50 bans posts at most the set number of notices. | L6 |
| 25 | S-MOD-ZERO-MOD-PRESET (new) | A safe default for a channel with no mods: stricter filters, auto-lockdown on, and a nudge to add a mod. | A channel with no mods shows the preset on first setup and the protection card names it. | Shortcoming 9 |
| 26 | S-SAFETY-ROOM-MODES (PLAN:737) | Lockdown room controls on YouTube and Kick. | As in PLAN:738-739. | PRODUCT-ALIGNMENT D1 |

### Goal D: help moderators decide

| Order | Slice | What it does | Done-when | Rests on |
|---|---|---|---|---|
| 27 | S-SUSPICIOUS-USERS (PLAN:769, rewritten; absorbs S-MOD-SUSPICIOUS-LEDGER) | Show restricted and monitored users in chat, the card and the queue. Keep our own list from events, since Twitch has no list endpoint. | As in PLAN:770-772, plus: a user restricted in the Twitch UI appears in our list, and the list survives a restart. | Finding 3; C12 |
| 28 | S-UF-M1a (PLAN:2066) | One shared notes store. | As in PLAN. | D45 |
| 29 | S-UF-M6a (PLAN:2143) | One oldest-first queue. | As in PLAN. | D46 |
| 30 | S-MOD-PIPELINE-ACTIONS (PLAN:1182) | `warn`, filter-hit and heat actions in the pipeline. | As in PLAN. | Warn first (section 5.2 row 11) |
| 31 | S-MOD-EVIDENCE-PACK (new) | One-click evidence for Twitch's own report flow: messages, times, channel. Not checked whether any part is built. | A mod opens the pack from a user card and it holds the last messages with times. | D07 |
| 32 | S-MOD-TEAM-HANDOFF (new) | A short shift note mods leave for the next mod. | A note left by one mod shows on the next mod's home tab. | R: twitch-shortcomings-2 (mods coordinate off-tool) |

### Goal E: protect viewers

| Order | Slice | What it does | Done-when | Rests on |
|---|---|---|---|---|
| 33 | S-FILTER-LINKPOLICY (PLAN:745, wider) | Apply the link policy. Add short-link and lookalike-domain checks, "new account plus link", and a VIP option, since Twitch exempts VIPs. | As in PLAN:746-747, plus a test: a link from a VIP account with the VIP option on is queued. | Shortcoming 4 |
| 34 | S-MOD-FILTER-TEST (PLAN:83) | Test a filter before saving; refuse an invalid regex with a clear error. | As in PLAN:83-84, plus an invalid regex cannot be saved. | L9 |
| 35 | S-SAFETY-FAKE-NAME (new) | Flag a new chatter whose name copies the streamer, a mod or "support" words. It flags, never bans. | A test: a fresh account named like the streamer is flagged to mods; a regular with a similar name is not. | Shortcoming 5; L5 |
| 36 | S-SAFETY-DOXX (new) | Queue a message that looks like an address or phone number for a mod. It never auto-bans. | A test message with a phone number is queued; a stream schedule time is not. | Shortcoming 6 |
| 37 | S-CHATFILTER-HOLD (PLAN:1174) | Queue = delete plus a queue entry; flag = queue entry only. Rename "hold" to "queue" in the text. | As in PLAN. | C3 |
| 38 | S-VIEWER-RESTRICTION-D1 (PLAN:2438) | Bot-side restriction per person, not per platform account. | As in PLAN. | D39 |

### Goal F: one user hub, live

| Order | Slice | What it does | Done-when | Rests on |
|---|---|---|---|---|
| 39 | S-USER-HUB (PLAN:76) | One users area with tabs; each tab names its floor. | As in PLAN:78-80, plus a floor per tab. | Owner answer 3; C13 |
| 40 | S-LIVE-EVERYWHERE (PLAN:173) | Every page updates live. | As in PLAN:173-175. | Owner answer 3 |
| 41 | S-MOD-BANLIST (PLAN:70) | Ban list with search and unban. | As in PLAN:70-71. | Incident fault 3 |
| 42 | S-MOD-NUKE-SCREEN (PLAN:81) | Nuke from the dashboard with rollback. | As in PLAN:81-82. | Section 5.2 rows 1-2 |
| 43 | S-MOD-TWITCH-ONLY-SETTINGS (new) | One card that lists every Twitch setting our bot cannot set, each with a link and a one-line why. | The card lists all settings in section 3.2 and each link opens the right Twitch page. | Finding 6 |
| 44 | S-MOD-AUTOMOD-MAP (new) | The AutoMod screen writes the full set every time and shows Twitch's 5 categories mapped to the 8 fields. | A test: changing one category keeps every other field. | Section 3.3 |
| 45 | S-LEADMOD-APP-RENAME (PLAN:3022) | Rename SuperMod to the LeadModerator display name. | As in PLAN; needs Q8. | K5 |

Kept blocked or changed outside the order:

- S-SPAM-NETWORK (PLAN:729): stays blocked. It shares signatures, never bans.
- S-SHAREDBAN-FEDERATE-OUT (PLAN:751): changed. A published ban arrives in a subscriber channel as a request (restrict plus decision card), never as a ban.
- S-UF-M5b (PLAN:1685): replaced by orders 6, 8 and 10. Delete it with `scripts/close-slice.ps1` once order 10 ships.

### Doc lines to change once the owner settles section 8

| Doc | Line | Change |
|---|---|---|
| DOC/user-flow-behavioral-spec-2026-10-03.md | 546, 573-589, 578, 613-622, 1037 | K1, K7, K8, K14 |
| DOC/2026-06-16-moderation.md | 16, 17, 20 | K2, K3, D05 |
| docs/bot-capabilities.md | 33, 72-80, 151-156 | K4, K5, K13 |
| SPEC/moderation.md | 390-411, ~425-445 | C2, D05 |
| SPEC/spam-defense.md | 645-668, 735-738, 757, s.6.1 | K9, K10, K15 |
| PLAN | 57-60, 95-98, 123-125, 727, 734-736, 751, 769-772, 1174, 1685 | C3, C4, C10, C12, C14, C15 |
| Memory | feedback-massban-bans-happen-live-only-holds.md, feedback-massban-opt-in-only.md | C9, C10 |
| Code | `ModerationController.cs:337`, `MassBanChannelPlanner.cs:218-223` | C11, C9 |

---

## 8. Open questions only the owner can answer

| # | Question | Recommendation |
|---|---|---|
| Q1 | Follow bots: auto-ban (your words, PLAN:116-117) or block only (SD9)? | Block only. Kick warns that mass-banning followers puts the account at risk (L4). |
| Q2 | Who in a target channel may press "Ban here too"? LeadModerator and up (your answer 1), or any mod? | LeadModerator and up, plus the streamer. |
| Q3 | A channel without our bot: may a mod still vouch for the streamer (your 10-06 words), or must the streamer entrust with their own click? | The streamer's own click. |
| Q4 | In a live Shared Chat session, may a partner streamer choose "accept this session's bans at once"? | Yes, as the partner's own choice, for that session only. |
| Q5 | How many reversed bans revoke a mass-ban grant, and over what time? | A channel setting with a default; the number is yours. |
| Q6 | Nuke reversal: 24 hours (spec) or no limit (code)? | No limit. A wrong ban should always be undoable. |
| Q7 | A request nobody decides: how long does the restriction stay? | A default of 7 days, then it lifts and the requester is told. |
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

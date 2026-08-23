# Spam & Bot Defense

Status: **design settled, not implemented** (2026-08-23)
Sibling spec: `moderation.md` (this spec extends it; it does not replace it)

Defends every channel against the automated-spam economy that plagues Twitch and its
siblings: chat-promo bots, follow bots, view bots, and hate raids. The reference bar is
`Sery_Bot` — matched on capability, exceeded on transparency and operator control.

---

## 1. Problem

Three distinct attacks, commonly conflated:

| Attack | Shape | Correct response |
|---|---|---|
| **Chat promo spam** | An account joins, posts one promo message ("best viewers on the stream — TG @…"), leaves | Delete + ban. It will never chat again. |
| **Follow / view bots** | Thousands of accounts follow or lurk; they never chat | **Block**, not ban. A ban on an account that never speaks is wasted API budget and pollutes the ban list. |
| **Hate raid** | 50–500 accounts arrive in seconds, each posting slur/harassment content | Mass-ban the cohort + emergency channel lockdown. |

### 1.1 Why a word list loses

The observed real message that motivated this spec rendered as:

```
VI EWERS ON THE STREAM  STREAM_PROMOTION_BOT  TG
```

Pure ASCII. A blocklist entry for `viewers` does not match `VI EWERS`. The evasion families
in active use:

| Family | Example | Beats |
|---|---|---|
| Word splitting / injection | `VI EWERS`, `v.i.e.w.e.r.s` | substring match |
| Combining diacriticals | `B̟est`, `vie̟wers` (U+0300–U+036F) | substring match, most regex |
| Zero-width / invisible | `vie<ZWSP>wers` (U+200B–200D, U+FEFF, U+2060, U+E0000–E007F tag chars) | everything naive |
| Homoglyph substitution | Cyrillic `о`/`а`/`е`, Greek `ο`, fullwidth `ｖｉｅｗｅｒｓ` | substring match |
| Alt alphabets | Unicode math bold/script/monospace ranges | substring match |
| Leetspeak | `v13w3r5` | substring match |
| Link mutation | `t.me∕x`, `bit␣ly/x`, unicode dot U+2024 | link regex |

Each is cheap for the attacker and each defeats a filter written for the previous one. The
answer is therefore **not a longer blocklist**. It is normalization plus signal fusion plus
earned capability.

---

## 2. Decisions

| # | Decision |
|---|---|
| **SD1** | **Split enforcement by confidence.** High-confidence signals act immediately (ban/timeout/block), logged with one-click mod undo. Trust-gated capabilities route to a silent review queue instead of an action. |
| **SD2** | **Three charset tiers, not one gate.** Cosmetic-abuse characters are blocked at every tier for everyone; homoglyph script-mixing is detected within a token; whole non-Latin scripts are trust-gated per-channel, **default OFF**. |
| **SD3** | **Curated + earned-trust signature network.** Every instance may subscribe read-only. Contribution requires an earned reporter-trust score or NoMercy curation. Poisoning the feed must not be a viable attack. |
| **SD4** | **One subsystem.** Chat spam, follow/view-bot detection, and hate-raid burst detection ship in the same spec — they share the trust model, the account-risk scorer, the signature feed, and the action pipeline. |
| **SD5** | **Local-only is fully functional.** Every layer except the network feed works with zero NoMercy infrastructure. A self-hosted instance that never phones home is still protected. The network is an accelerator, never a dependency. |
| **SD6** | **Default-deny on capabilities, default-allow on chatting.** A brand-new anonymous account may always speak plain text. It may not, until it earns it, post links, mention crowds, or use exotic character classes. |
| **SD7** | **Every action is explainable.** Each enforcement records the normalized text, the signals that fired, their weights, and the resulting score. No black-box bans. |
| **SD8** | **Established regulars are immune — absolutely.** A long-term active participant is **never** auto-actioned by this subsystem, at any confidence, by any signal, from any source. The engine may flag for a human; it may not delete, hold, time out, ban, or block. This is a hard invariant, not a high threshold. |
| **SD9** | **Presence is never an offence.** No account is ever actioned for being silent, for being new, or for arriving at the same moment as an attack. Every action requires a signal *that account itself* produced. Bursts and spikes select a **window to scrutinise**, never a set to punish. |
| **SD10** | **Account risk multiplies, it never adds.** Score = content signal × account-risk coefficient, so zero content signal is zero score whatever the account looks like. Nobody is ever actioned for *what they are* — only for *what they said*. The two marks that describe silence are pinned at ×1.0 and move nothing (§L1.1). |

---

## 3. Architecture

Six layers. Each is independently testable and independently disableable.

```
chat.message ──> L0 Normalizer ──> L1 Account Risk ──> L2 Content Signals
                                                             │
                            L3 Correlation <─────────────────┤
                                    │                        │
                                    └──> L4 Scorer ──> L5 Enforcement
                                              ▲
follow / raid / join events ──> Burst Detector┘
```

### L0 — Normalizer

The foundation. Every layer above it consumes normalized text, so every layer gets evasion
resistance for free. Produces a `NormalizedMessage` carrying both forms plus the audit of
what was stripped.

Pipeline, in order:

1. **Unicode NFKD decompose** — separates base characters from combining marks; folds
   fullwidth, math-alphanumeric, and compatibility forms to ASCII.
2. **Strip category Mn** (non-spacing marks) — kills `B̟est` → `Best`.
3. **Strip invisibles** — Cf (format) category, ZWSP/ZWNJ/ZWJ/BOM/word-joiner, RTL/LTR
   overrides, U+E0000–E007F tag block, and Unicode whitespace that is not U+0020.
4. **Homoglyph fold** — map confusable non-Latin codepoints to their Latin skeleton using the
   Unicode `confusables.txt` skeleton algorithm (UTS #39). Record *which* tokens were mixed-script
   before folding — that record is itself a signal (§L2).
5. **Casefold + de-leet** — lowercase; `0→o 1→i 3→e 4→a 5→s 7→t @→a $→s`.
6. **Collapse** — runs of the same character to two (`heeeeey` → `heey`); then strip all
   non-alphanumerics and all whitespace to produce the **match skeleton**.

`VI EWERS ON THE STREAM` and `vie̟wers оn thе ѕtream` both reduce to `viewersonthestream`.
One corpus entry now covers both, and every future respacing of it.

**The normalizer never mutates the message shown in chat.** It exists only to decide.

### L1 — Account Risk

Account properties, cached per user per channel.

| Mark | Multiplier |
|---|---|
| Account age < 7d / < 30d / < 6mo | ×1.6 / ×1.3 / ×1.1 |
| Not following, or following < 24h | ×1.2 |
| Default profile (no avatar, empty bio, zero streams) | ×1.15 |
| Username matches generated-handle pattern (`word` + 4–8 digits, or high-entropy) | ×1.4 |
| First message ever in this channel | **×1.0 — a mark, never a multiplier** |
| Zero chat history across this instance's channels | **×1.0 — a mark, never a multiplier** |

#### L1.1 — L1 multiplies, it never adds (the lurker's-first-word rule)

**Final score = ContentSignalScore × AccountRiskMultiplier.** L1 is a *coefficient*, and there is
no additive path from it into the score. The consequence is the point:

> **Content signal of zero × any multiplier = zero.** An account with every risk mark on it,
> saying something ordinary, scores zero and is not evaluated further.

An account cannot be actioned for *what it is*. Only for *what it said*. L1 decides how hard a
suspicious message is judged; it can never make an unsuspicious one suspicious.

The two marks pinned at ×1.0 are the ones that describe **silence**, and they are deliberately
inert. "First message ever" and "no chat history" are the definition of a lurker finally speaking
— the single most sympathetic person in the channel, and under any additive scheme the one who
stacks two mediums for the crime of having been quiet. They are recorded, they show in the mod's
explanation, and they move nothing. A ten-year lurker's first word is scored exactly like a
regular's thousandth.

They earn their keep in one place only: **corroboration**. Where a content signal has *already*
fired, they can raise a Medium to High — which per SD1 means a `Hold` into the review queue, not
an action. Silence never bans anyone.

### L2 — Content Signals

Evaluated against the L0 skeleton.

- **Cosmetic-abuse presence** — any codepoint stripped in L0 steps 2–3. Per **SD2** this is a
  standalone high-confidence signal at every trust tier. There is no legitimate reason to put a
  zero-width joiner in a chat message.
- **Intra-token script mixing** — a single token containing codepoints from two scripts
  (`ѕtream`: Cyrillic ѕ + Latin). Near-zero false-positive rate; explicitly **not** the same as
  a message being wholly in another script.
- **Corpus match** — exact skeleton hit against the local + subscribed signature corpus.
- **Near-duplicate** — SimHash over character 4-shingles of the skeleton; Hamming distance ≤ 3
  against any corpus entry. Catches the next mutation of a known campaign before anyone reports it.
- **Link policy** — links extracted from the *skeleton* (so `t.me∕x` and `bit␣ly/x` are seen),
  checked against per-channel allow/deny plus the network's malicious-domain set.
- **Promo shape** — contact-handle patterns (`@handle`, `t.me/`, `discord.gg/`), price/offer
  vocabulary, and imperative CTAs.
- **Caps / emote-only / wall-of-text** — retained from the existing `AutoModerationEngine`.

### L3 — Correlation

Cross-message and cross-channel, over sliding windows.

- **Campaign** — N distinct accounts posting messages within SimHash distance ≤ 3 of each other
  inside M seconds. Actions the whole cohort at once, not one at a time.
- **Cross-channel campaign** — the same, observed across channels on this instance (and, when
  subscribed, across the network). This is what turns one channel's catch into everyone's
  immunity.
- **Join burst** — abnormal chatter-join rate vs. the channel's own rolling baseline.
- **Follow spike** — follow rate exceeding the channel's baseline by a configurable factor.
  Feeds the follow-bot track, which **blocks** rather than bans (SD4, and Sery_Bot's rationale:
  a ban on a silent account is wasted work).

Baselines are per-channel and self-calibrating. A 50-viewer channel and a 50 000-viewer channel
must not share a threshold.

#### L3.1 — Lurker protection (SD9)

The lurker is the easiest person in this system to hurt by accident, because they generate no
evidence of being real. Getting raided, going viral, being hosted, or landing on the front page
all produce exactly the follow spike and join burst this layer watches for — and the people
arriving are real viewers who will never type a word.

Therefore, per **SD9**:

- **A burst or spike is a trigger to scrutinise, never a set to action.** Detecting one raises
  the channel's evaluation sensitivity for its window. It does not, by itself, action a single
  account. There is no path in this design from "you were in the window" to "you were blocked".
- **Every block or ban needs that account's own evidence.** For the follow-bot track that means
  a per-account risk finding — a known-bot id, a generated-handle pattern, a zero-history
  profile created hours ago, a follow-and-unfollow oscillation. Not "followed at 14:03:07".
- **Being silent is not a signal.** Absence of chat history feeds only the *newcomer* side of
  L1; it never accumulates toward an action on its own. A lurker who never speaks can sit in a
  channel for years and never be evaluated for enforcement, because there is no message to
  evaluate.
- **Hate-raid lockdown is channel-scoped, and reversible.** It restricts *posting* for the
  cooldown window; it does not remove, block, or ban anyone for being present. Watching is never
  restricted. The window auto-expires.
- **Cohort actions require per-member confirmation.** An L3 campaign actions only the accounts
  that individually matched the campaign skeleton. Accounts merely present in the same window
  are not members.
- **Follow-spike blocks are reviewable and bulk-reversible.** Each spike batch is retained as a
  `FollowBotBlock` set with its per-account reason, and the operator can restore the whole batch
  in one action if a viral moment was misread. Nothing is silently unrecoverable.

### L4 — Scorer & Trust Tiers

Signals fuse into a confidence score. Trust tier determines which capabilities are even available.

**Trust tiers** (ladder-valued, consistent with the existing permission ladder; users never see
numbers — see the role-name rule):

| Tier | Earned by |
|---|---|
| Untrusted | default for every unknown account |
| Newcomer | account ≥ 7d **and** following ≥ 24h |
| Known | account ≥ 30d, following ≥ 7d, ≥ 5 messages |
| Regular | account ≥ 6mo, following ≥ 30d, ≥ 50 messages, no strikes in 90d |
| Trusted | sub / VIP / mod, or operator-granted |
| **Established** | **immune** — see §4.1 |

#### 4.1 Established — the immunity invariant (SD8)

A viewer reaches **Established** in a channel by being a real, sustained participant there:

- ≥ 90 days since their first message **in this channel**, and
- ≥ 300 messages in this channel, and
- active in ≥ 30 distinct days (not 300 messages in one night), and
- zero mod strikes upheld in the last 180 days.

Operators may also grant it by hand, and it is granted implicitly to moderators and VIPs.
Thresholds are per-channel tunable; the **invariant is not**.

**What immunity means, precisely:** L5 is hard-wired to reduce every outcome for an Established
viewer to **Flag** — visible to mods in the chat feed, with the full signal explanation, and
nothing else. Not a lower score, not a higher threshold, not a heavy negative weight that a
sufficiently loud stack of signals could still overcome. A short-circuit **before** the scorer
runs, so no future signal, no network signature, and no correlation cohort can ever reach them.

This is the one part of the design that is not a tunable. The failure it prevents — a channel's
most loyal regular of three years banned by an automated system for pasting a zalgo meme, or for
being swept up in a campaign cohort because they quoted the spam to complain about it — is worse
than every spam message the system will ever catch.

**Consequences elsewhere in this spec:**

- The "cosmetic-abuse characters: never, at any tier" row in the capability table means *no
  Untrusted-through-Trusted tier earns the capability*. It does not override SD8. For an
  Established viewer, cosmetic-abuse characters flag and nothing more.
- L3 campaign cohorts are filtered for Established members **before** the cohort action is
  applied — quoting spam must never make someone a member of it.
- Network signatures act on the local channel's tiers. A signature contributed elsewhere can
  never reach an Established viewer here.
- Established status is **per channel**, and it is earned by participation, not by account age.
  A ten-year-old Twitch account that has never spoken here is Untrusted, correctly.

**Capability table** — the operator-editable heart of the system. Defaults:

| Capability | Minimum tier |
|---|---|
| Post plain text | Untrusted (always allowed) |
| Post a link | Known |
| Mention 3+ users in one message | Newcomer |
| Paste > 200 characters | Newcomer |
| Emote-only message | Newcomer |
| Whole non-Latin script | Regular — **channel toggle, default OFF** (SD2) |
| Cosmetic-abuse characters | **never, at any tier** (SD2) |

Per **SD2**, the whole-script gate defaults off because Japanese, Korean, Cyrillic and Arabic
chat is written by real viewers. A streamer with an international audience must never be
protected into silencing them. It exists for channels actively under attack, and it is a single
switch away.

### L5 — Enforcement

Per **SD1**:

| Confidence | Response |
|---|---|
| **High** — cosmetic-abuse chars, corpus hit, confirmed campaign cohort, malicious link | Act immediately: delete + timeout/ban per the channel's escalation policy. Logged, explainable, one-click undo. |
| **Medium** — capability not yet earned (a Newcomer's first link), promo shape without a corpus hit | **Hold**: the message does not post, and lands in the mod review queue. Approving it also credits the sender's trust. |
| **Low** — a single weak signal | Flag only. Visible to mods in the chat feed, no action. |
| **Zero** — no content signal fired | Nothing. No record beyond the routine trust-counter update. Per SD10 this is where every silent, new, or odd-looking account saying something ordinary lands, regardless of its marks. |

`ChatFilterAction` already carries `Delete / Timeout / Hold / Flag / Escalate`, and
`ModerationEscalationService` already owns the strike ladder — L5 routes into both rather than
inventing a parallel action path.

**Follow/view-bot track** issues **block**, never ban, and strips the follow.

**Hate-raid track** additionally trips a channel lockdown (followers-only + slow mode) for a
cooldown window, and alerts the operator.

---

## 4. The Signature Network

Per **SD3** and **SD5**.

- **Subscribe** — any instance, free, read-only. Pulls the signature set: skeletons, SimHash
  values, malicious domains, known-bot account ids. Delta-synced.
- **Contribute** — gated. A channel earns a **reporter-trust** score from its submission history
  (confirmed by independent corroboration or NoMercy curation). Low-trust submissions enter a
  quarantine tier that only *flags*, never auto-acts, until corroborated by K independent
  reporters. This is the anti-poisoning property: a single malicious contributor cannot cause a
  mass-ban anywhere.
- **Curation** — NoMercy-published entries carry a curator signature and skip quarantine.
- **Never sent** — message text from non-matching messages, viewer identities, or channel
  analytics. A signature is a skeleton hash plus metadata, nothing more.
- **Offline** — an instance that never subscribes loses only L2 corpus and L3 cross-network
  correlation. L0, L1, L2 heuristics, L3 in-instance correlation, L4 and L5 all still work.

### 4.1 Seeding the corpus

Sery_Bot's own match set is **closed** — it is a hosted, closed-source service, and its corpus is
its product. There is no export, no API, and no repository. We cannot and should not take it.

Public seed material that *is* available, verified 2026-08-23:

| Source | Content | Usable? |
|---|---|---|
| [WolfwithSword gist](https://gist.github.com/WolfwithSword/364927fdddfdf6ede19111d4d373863b) | ~37 curated Twitch blocked terms, including live unicode-evasion variants | Yes — small, high-signal, ideal normalizer fixture |
| [dakkafex/streamer-spam-blacklist](https://github.com/dakkafex/streamer-spam-blacklist) | Thin phrase/regex lists (~270 bytes) plus a **2 481-entry known-bot account list** | Account list is the valuable half. **No LICENSE file** — treat as reference, do not vendor |
| [Stop The Bots](https://transparent-aluminium.net/2026/05/12/stop-the-bots-twitch-scam-spam-block-list-april-2026-update/) | ~200 terms across 7 categories: scam domains, viewer-bot promo, design-scammer phrases, Discord bait, a dedicated unicode-spam section, plus ~20 accounts to ban | Community-maintained, no stated licence — reference for building our own, not a redistributable asset |

**These lists validate the L0 design directly.** The WolfwithSword set contains
`bigfollows`, `igfollows`, `ͧ(ͧbigfollows`, `B͟est Viewers`, `*p viewers` and `*st viewers` as
six separate entries — six hand-written entries chasing mutations of two words, because the tool
consuming them has no normalizer. Under L0 all six collapse to two skeletons, and every *future*
mutation of them is covered without anyone adding a line. That is the whole argument for
normalizing before matching, written out by the people doing it the hard way.

**The seed corpus is built** — `data/spam-seed-corpus.md`. All 176 raw terms from the three
sources were pushed through the L0 algorithm and deduplicated to **119 phrase skeletons + 16
malicious domains**; 41 entries (23%) fell away as duplicates-under-normalization or artefacts. No file is
vendored; the entries are our own normalized derivations, with sources attributed per skeleton.

Measured against the motivating case: `VI EWERS ON THE STREAM` plus four mutations no list
contains (combining marks + Cyrillic, leetspeak, fullwidth, plain) → **one skeleton**.

Measured limit, also recorded there: exact-skeleton match does not unify `bestviewers` with
`bestviewerson`. SimHash near-duplicate matching is what closes that, which is why L2 carries both
rather than either alone.

The known-bot **account** ids in the `dak` list are a starting hint for L1, never a standing
auto-ban list (SD9: every block needs that account's own evidence).

Commercially this is the honest SaaS hook: the hosted tier's value is *the network*, not the
software. `saas` deployment mode remains restricted to NoMercy Labs per license.

---

## 5. Data Model

New entities under `Domain/Moderation/Entities/`:

| Entity | Purpose |
|---|---|
| `SpamSignature` | Skeleton, SimHash, kind, source (local / network / curated), quarantine state, hit count |
| `SpamDetection` | One evaluation: message ref, normalized skeleton, signals fired + weights, score, action taken, reviewer verdict |
| `TrustTierPolicy` | Per-channel capability → minimum-tier table + channel toggles |
| `SpamCampaign` | A correlated cohort: window, member accounts, representative skeleton, action outcome |
| `FollowBotBlock` | Blocked account, **per-account** detection reason (required, non-null — SD9), spike batch ref, restored-at |
| `ReporterTrust` | Per-channel contribution history and earned reporter score |

Extended: `UserTrustScore` gains the L1 component breakdown so a tier is explainable, not just a
number, plus the per-channel participation counters that earn **Established** (first-message-at,
message count, distinct-active-days, last-upheld-strike-at).

---

## 6. Frontend

Under Moderation in the dashboard:

- **Spam Defense** — master toggle, per-layer toggles, the capability/tier table as an editable
  grid, channel toggles (non-Latin gate, lockdown behaviour), network subscription state.
- **Review Queue** — held messages with the normalized skeleton and the signals that fired
  shown inline; approve (credits trust) / delete / ban.
- **Campaigns** — detected cohorts, their representative message, member count, bulk undo.
- **Detections** — the audit log, filterable by signal, with per-detection explanation (SD7).
- **Follow-bot blocks** — spike batches with each block's own reason shown, and a bulk-restore
  action per batch (SD9). Immune/flagged-only entries are labelled as such, so a mod can see the
  system *chose not to act* on a regular and why.

Every surface is role-gated per `frontend-ia.md`; the capability table is manage-floor.

---

## 7. Testing

The bar (`CLAUDE.md` testing standard) is behaviour, not surface.

- **Normalizer corpus test** — a fixture table of real evasion strings, each asserted to reduce
  to the expected skeleton. `VI EWERS ON THE STREAM`, `B̟est vie̟wers`, `ѕtreаm`, `v13w3r5`,
  `ｖｉｅｗｅｒｓ` all collapse to the same skeleton as the plain form.
- **False-positive corpus** — Japanese, Korean, Russian, Arabic, emoji, kaomoji, and ASCII-art
  messages asserted to trip **no** high-confidence signal with the non-Latin gate off.
- **Tier progression** — an account crossing an age/follow/message threshold gains the
  capability, asserted against persisted state.
- **Campaign correlation** — N synthetic accounts posting mutations of one skeleton produce one
  `SpamCampaign` and N actions, not N campaigns.
- **Quarantine** — a signature from an untrusted reporter flags but never auto-acts until K
  corroborations land; asserted on the resulting `SpamDetection.ActionTaken`.
- **Follow spike** — a burst against a calibrated baseline produces blocks, and asserts that
  **zero bans** were issued.

### 7.1 Invariant tests — these two may never be allowed to rot

**SD8 — Established immunity.** For *every* signal the engine can produce, an Established viewer
emitting it asserts `ActionTaken == Flag` and asserts the message still posted. Written as a
table-driven test over the full signal enum, so **adding a new signal without covering it fails
the build** — the immunity cannot decay as the system grows. Plus: an Established viewer who
quotes a live campaign's exact spam text is asserted absent from the resulting `SpamCampaign`
member set, and unactioned.

**SD10 — L1 cannot act alone.** Construct the worst-looking account the risk table can describe —
2 days old, not following, default profile, generated-handle username, zero history anywhere,
first message ever — and have it say `hi`. Assert final score `0`, `ActionTaken == None`, and that
the message posted. Then assert the *stored explanation* still lists all six marks, so a mod can
see the system looked and chose not to act. Repeat for a plain greeting in Japanese with the
non-Latin gate off, and for a greeting containing an emote.

**SD9 — Lurker safety.** A synthetic viral moment: 500 accounts follow inside 60 seconds, 480 of
them ordinary profiles with real history, 20 matching known-bot patterns. Assert exactly 20
blocks, each carrying its own per-account reason, and assert the other 480 have **no**
`FollowBotBlock`, no ban, and no strike. Separately: a hate raid triggers lockdown, and a silent
lurker present for its entire window is asserted to have zero moderation records of any kind
afterwards.

---

## 8. Build Order

1. L0 Normalizer + its corpus tests. Standalone, zero dependencies, immediately useful — wiring
   it into the existing `ChatFilterService` alone kills most current evasion.
2. L1 Account Risk onto `UserTrustScore`.
3. L4 tiers + capability table + `TrustTierPolicy`, with L5 `Hold` into the review queue.
   **The SD8 short-circuit and its table-driven invariant test land here, in the same slice as
   the scorer** — never as a follow-up. An engine that can act before it can be immune has a
   window in which it will hurt someone.
4. L2 content signals + local corpus.
5. L3 correlation + burst detection; follow-bot block track — with the SD9 per-account-evidence
   requirement and its viral-moment test in the same slice, for the same reason.
6. L5 hate-raid lockdown.
7. Signature network: subscribe first, contribute + quarantine second.
8. Frontend surfaces, per step.

Steps 1–3 alone would have stopped the message that motivated this spec.

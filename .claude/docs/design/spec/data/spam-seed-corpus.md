# Spam Defense — Seed Corpus

Companion data for `spam-defense.md` §4.1. Built 2026-08-23.

**What this is:** 176 raw blocked-terms from three public community lists, pushed through the L0
normalizer and deduplicated into **119 phrase skeletons + 16 malicious domains** (135 entries). This is
the seed for `SpamSignature`, not a finished corpus — the network feed (SD3) grows it.

**Provenance.** Compiled *from* the sources below; no file is vendored, and none of the three
states a licence. Entries are our own normalized derivations.

- `wol` — [WolfwithSword gist](https://gist.github.com/WolfwithSword/364927fdddfdf6ede19111d4d373863b) (74 lines)
- `dak` — [dakkafex/streamer-spam-blacklist](https://github.com/dakkafex/streamer-spam-blacklist) (nightbot/spam)
- `sto` — [Stop The Bots, April 2026](https://transparent-aluminium.net/2026/05/12/stop-the-bots-twitch-scam-spam-block-list-april-2026-update/)

## Measured result

| Metric | Value |
|---|---|
| Raw terms in | 176 |
| Skeletons out | 119 phrases + 16 domains = 135 |
| Collapsed away as duplicates-under-normalization | 37 by normalization + 4 excluded artefacts = 41 (23%) |
| Skeletons that absorbed more than one hand-written variant | 8 |

Nearly a quarter of the combined hand-maintained effort of three separate communities was people
writing the same entry twice because their tool could not normalize. The worst cases:

| Skeleton | Hand-written variants it replaces |
|---|---|
| `cheapviewers` | 6 — `Cheap Viewers`, `Cheap viewers`, `Che̢ap vie̮wers`, `Ch̍eap Viewers`, `Ch͟eap viewers`, `Ĉheap ͖Viewers` |
| `bestviewers` | 4 — `Best Viewers`, `B̟est viewers`, `B͟est Viewers`, `Bͦest vie̟wers` |
| `bigfollowscom` | 4 — `(bigfollows . com)`, `(bigfollows .com)!`, `bigfollows . com`, `bigfollows*com` |
| `wannabecomefamous` | 3 — across all three lists independently |
| `bigfollows` | 3 — incl. `ͧ(ͧbigfollows` |

## L0 verification against the motivating case

The message from the screenshot, plus four mutations of it that no list contains:

```
VI EWERS ON THE STREAM
viewers on the stream
vie̟wers оn thе ѕtream      (combining marks + Cyrillic о/е/ѕ)
v13w3r5 0n th3 5tr34m       (leetspeak)
ｖｉｅｗｅｒｓ on the stream        (fullwidth)
```

**5 variants → 1 skeleton** (`viewersonthestream`). One corpus entry covers all five and every
future respacing. Verified by running the L0 algorithm as specified in §L0.

**Honest limit found while measuring:** exact-skeleton matching does *not* unify `bestviewers`
with `bestviewerson` — they are genuinely different strings. Substring and near-duplicate
matching are what close that gap, which is why L2 carries SimHash (Hamming ≤ 3) alongside exact
match rather than instead of it. The probe confirms the layer is load-bearing, not decorative.

## Excluded during dedupe

- `pviewers`, `stviewers`, `igfollows`, `viewersonon` — artefacts of Twitch wildcard entries
  (`*p viewers`, `*st viewers`). Already covered by `bestviewers` / `cheapviewers` / `bigfollows`
  under normalization; kept out to avoid short-skeleton false positives.
- Bare TLDs, `dot com`, `remove the space` — L0 already strips the evasion these targeted.
- Single generic words (`promotion`, `collab`, `logos`, `overlays`, `primes`, `pog`) — far too
  broad; they belong to L2's promo-shape signal with corroboration, never to exact match.
- Slurs and political names present in the `sto` list — those are `moderation.md`'s blocked-terms
  surface, a different feature with different operator consent. Not spam signatures.
- Anything with a skeleton under 8 characters.

## Domains (link-policy signal, not phrase corpus)

Classed separately: a domain match is a link-policy input, not a message-text match. `tinyurl.com`
and `cutt.ly` are deliberately **not** included — they are general-purpose shorteners used by real
people, and belong to per-channel link policy rather than a malicious-domain set.

```
boostmap.ru
botrush.ru
botsister.com
eliteviewers.ru
fastfanfollowers.ru
mrgadget.net
mystrm.store
nezhna.com
popularviewers.ru
punktru.ru
skintrevor.fun
smmdex.ru
smmgen.online
smmmega.online
streamboo.com
streamfollowers.online
```

## Phrase skeletons

```
addmeondiscord
addmeoninsta
addmeupondiscord
addviewers
aiviewersstreamboocom
artworkforstreamers
attractmorefollowersandviewers
banishthetumbleweeds
banneremotes
becomethemainevent
bestfollowers
bestfollowersandviewers
bestviewers
bestviewersandfollowers
bestviewerson
bigfollows
bigfollowscom
buyfollower
buyfollowers
buyviewers
canirunwyou
canyoudropyourdisscord
chatexplosions
chatssoempty
cheapfollow
cheapfollowers
cheapviewers
cheapviewerson
cheapviews
checkmyportfolio
connectingondiscord
connectondiscord
creativeandeyecatchystuff
customart
customartist
customgraphics
didyouseethegametwitch
discordcord
discordusername
dogehype
dogehypedotcom
dontforgetourveryown
everythingisinyourhands
feelfreetoaddme
feelfreetoreachouttomeondiscord
fixitwithbotsister
followergains
followonbot
freepackageforstreamers
getafreetestof
gotostreamrise
hiupgradeyourstreamon
howmuchyoupayme
iamprofessionallogodesigner
idlikeforustochatondiscord
idoartisticthings
idocustomartanimation
idoworkforstreamersandyoutubers
imnotarobotatall
improveryourchannel
insearchoffollowersprimesandviews
itsgivingnosignalvibes
iwanttoofferpromotion
iwanttoofferpromotionofyourchannel
justforafeedback
kindlychatmeondiscord
letslinkondiscord
linkupondiscord
logobanneremotes
logosbannersoverlays
lovetocollab
massivefollower
mountviewers
myinstaprofile
myportfolio
offerpromotion
offerpromotionofyourchannel
pleasesentmeamessage
priceislower
primesandviewers
professionalcustomartist
promotionofyourchannel
reachouttome
realviewers
resultinmassivefollower
seemylast
seemywork
sendmeawhisper
showyoumywork
streamboo
streamisreallyentertaining
streamrise
streamviewers
thebestserviceispromoting
topviewers
tumbleweedsfromyour
twitchfollowers
uncontrollablegrinning
upgradeyourstream
upgradeyourstreamon
usepromocode
usernameondiscord
viewersfollowersandprimes
viewersonthestream
wannabecomefamous
wannacheckmyportfolio
wanttobuyfeetpics
wanttoofferpromotion
workondiscord
wouldyouliketoseemywork
youcanbeagreatstreamer
youcanimproveyourstreamon
yourchatssoempty
yourfollows
yourfollowscom
yourfollowz
youseethegametwitch
youseethegametwitchannouncedtoday
youstreamprettycoolthatswhyifollowedyou
```

## Use

Seeded at first run as `SpamSignature` rows with `Source = curated`. Per SD9, the ~2 500 known-bot
**account ids** in the `dak` list are deliberately **not** seeded as bans — they are an L1 risk
hint only, because every block must carry that account's own evidence.

Per SD8, none of these signatures can reach an Established viewer at any confidence.

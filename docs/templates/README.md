# Template helpers

A template is the text the bot says. A helper is a word in curly braces that the bot fills in.

Write a helper like this: `{user}`. The bot swaps it for the real value when it speaks.

One full example:

```
Template:  {name} has been here for {viewer.days} days and {verb:has|have} sent {viewer.messages} messages.
Output:    Chris has been here for 12 days and has sent 348 messages.
```

Where a helper works is listed under it:
**commands**, **event responses**, **timers**, **pipelines**, **Discord** notifications, **webhooks**.

Some helpers need a trigger (a chat message or an event) to know who "the user" is. Those do not work in timers.

Every example below uses the same people:
- the streamer is **Stoney_Eagle**
- the viewer who typed the command is **Stoney_Eagle** too, unless the example says otherwise
- the @mentioned person is **Chris** (pronouns he/him)

---

## Names and links

`{user}`
The login name of the person who triggered it.
Works in: commands, event responses, pipelines, webhooks.
```
Thanks for the command, {user}!
Thanks for the command, stoney_eagle!
```

`{name}`
The name to show: the @mentioned person if there is one, otherwise the person who typed.
Works in: commands, event responses, pipelines, webhooks.
```
!hug @Chris  →  {user} gives {name} a big hug.
stoney_eagle gives Chris a big hug.
```

`{target}` and `{target.name}`
The login name of the @mentioned person.
Works in: commands, event responses, pipelines, webhooks.
```
Go follow {target}!
Go follow chris!
```

`{target.displayname}`
The @mentioned person's display name. If the bot does not know them, it shows the text as typed.
```
Everyone say hi to {target.displayname}.
Everyone say hi to Chris.
```

`{target.known}`
`true` when the bot knows the @mentioned person, otherwise `false`. Useful in a pipeline condition.

`{link}`, `{user.link}`, `{target.link}`
A profile link. `{link}` picks the @mentioned person if there is one, otherwise the person who typed.
```
Check out {target.link}
Check out https://twitch.tv/chris
```

`{user.id}`, `{target.id}`
The platform user id. Mostly for webhooks and pipelines.

`{user.provider}`
The platform the person came from, like `twitch`.

`{streamer}`
The streamer's display name.
Works everywhere.
```
Welcome to {streamer}'s stream!
Welcome to Stoney_Eagle's stream!
```

`{botname}`
The bot's display name.
```
I am {botname}. Type !help to see what I can do.
I am NomNomz. Type !help to see what I can do.
```

`{channel}`, `{channel.display}`, `{channel.id}`
The channel login name, display name and platform id.
```
You are watching {channel.display}.
You are watching Stoney_Eagle.
```

---

## Pronouns and grammar

These helpers use the pronouns a viewer has set. When none are set, the bot uses they/them.

`{subject}`, `{user.subject}`, `{target.subject}`
he / she / they.
```
{name} is here, and {subject} brought snacks.
Chris is here, and he brought snacks.
```

`{object}`, `{user.object}`, `{target.object}`
him / her / them.
```
Give {name} a warm welcome and say hi to {object}.
Give Chris a warm welcome and say hi to him.
```

`{possessive}`, `{user.possessive}`, `{target.possessive}`
his / her / their. This is the pronoun word, not the name with 's.
```
{name} lost {possessive} keys again.
Chris lost his keys again.
```

`{presentTense}`, `{user.presentTense}`, `{target.presentTense}`
is / are, matching the pronoun. "they" takes "are".
```
{name} {presentTense} the best.
Chris is the best.
```
With they/them pronouns the same template gives: `Sam are the best.` See Known gaps.

`{verb:singular|plural}`, `{user.verb:...}`, `{target.verb:...}`
Pick the verb form that matches the person. Write the singular form first, then a pipe, then the plural form.
```
{name} {verb:plays|play} a lot of Minecraft.
Chris plays a lot of Minecraft.
```
With they/them pronouns: `Sam play a lot of Minecraft.`

`{genderedTerm}`, `{user.genderedTerm}`, `{target.genderedTerm}`
guy / gal / person, matching the pronoun.
```
{name} is a good {genderedTerm}.
Chris is a good guy.
```

`{tense}`, `{user.tense}`, `{target.tense}`
"is" while the stream is live, "was" after it ends.
```
The stream {tense} brought to you by coffee.
The stream is brought to you by coffee.
```

`{status}`
`live` or `offline`.
```
{streamer} is {status} right now.
Stoney_Eagle is live right now.
```

`{user.pronouns}`
The pronouns badge the viewer set, like `he/him`.
```
{user} goes by {user.pronouns}.
stoney_eagle goes by he/him.
```

---

## Stream

Work everywhere unless noted.

`{stream.title}` — the current title.
```
Now playing: {stream.title}
Now playing: Chill building night
```

`{stream.game}` — the current game or category.
```
We are playing {stream.game} tonight.
We are playing Minecraft tonight.
```

`{stream.uptime}` — how long the stream has been live.
```
Live for {stream.uptime} already!
Live for 2 hours 15 minutes already!
```

`{stream.viewers}` — the viewer count.
```
{stream.viewers} people are watching right now.
143 people are watching right now.
```

`{stream.isLive}` — `true` or `false`. For conditions.

`{stream.startedAt}` — the time the stream went live.

`{tts.audioconnected}` — `true` when an Audio Source page is open, so TTS can be heard.

`{broadcaster}`, `{title}`, `{game}`
Short names used by the stream online/offline events and Discord go-live posts.
Works in: event responses, pipelines, Discord.
```
{broadcaster} just went live with {game}: {title}
Stoney_Eagle just went live with Minecraft: Chill building night
```

---

## Time

Work everywhere.

`{time}` — the current local time (the channel's timezone).
```
It is {time} here.
It is 22:15 here.
```

`{time.utc}` — the current UTC time.

`{date}` — today's date.
```
Today is {date}.
Today is 5 October 2026.
```

---

## Viewer stats

For the person who typed: `{viewer.*}`. For the @mentioned person: `{target.*}`.
Works in: commands, event responses, pipelines, webhooks.

`{viewer.messages}` / `{target.messages}` — total chat messages.
```
{name} has sent {target.messages} messages here.
Chris has sent 348 messages here.
```

`{viewer.watchtime}` / `{target.watchtime}` — total watch time.
```
{name} has watched for {target.watchtime}.
Chris has watched for 3 days 4 hours.
```

`{viewer.firstseen}` / `{target.firstseen}` — when first seen in chat.
```
{name} first showed up on {target.firstseen}.
Chris first showed up on 12 March 2026.
```

`{viewer.days}` / `{target.days}` — whole days since first seen (at least 1).
```
{name} has been around for {target.days} days.
Chris has been around for 207 days.
```

`{viewer.avgperday}` / `{target.avgperday}` — average messages per day.

`{viewer.redemptions}` / `{target.redemptions}` — channel point redemptions.

`{viewer.songrequests}` / `{target.songrequests}` — song requests made.

`{viewer.commands}` / `{target.commands}` — commands used.

`{viewer.botpercent}` / `{target.botpercent}` — the share of messages that were commands, as a whole percent.
```
{target.botpercent}% of {name}'s messages are commands.
14% of Chris's messages are commands.
```

`{user.messageCount}` — same as `{viewer.messages}`.

`{user.accountAge}` — how long ago the account was made.
```
Your account is {user.accountAge} old.
Your account is 4 years old.
```

`{user.followAge}` / `{target.followAge}` — how long they have followed.
```
{name} has followed for {target.followAge}.
Chris has followed for 7 months.
```

`{user.lastmessage}` / `{target.lastmessage}` — the most recent chat message (shortened if long).

`{target.lastmessage.full}` — the most recent message, never shortened.

`{target.randommessage}` — a random older message of theirs, fit to quote.
```
{name} once said: "{target.randommessage}"
Chris once said: "creeper behind you"
```

`{viewer.data.<key>}` / `{target.data.<key>}` — a saved per-viewer field.
```
{name}'s favorite snack is {target.data.snack}.
Chris's favorite snack is popcorn.
```

---

## Random, counters and lists

Work everywhere.

`{random.user}` — a random person in chat right now.
```
{random.user} wins a free hug!
Chris wins a free hug!
```

`{random.number.N}` — a whole number from 1 to N.
```
I rate this {random.number.10} out of 10.
I rate this 7 out of 10.
```

`{random.number.MIN.MAX}` and `{random.number.MIN.MAX.STEP}` — between a minimum and a maximum, in steps.
```
You are {random.number.50.100} percent awesome.
You are 83 percent awesome.
```

`{roll.NAME.MIN.MAX}` — a named roll, drawn once per message. Reuse it with `{roll.NAME}`.
`{roll.NAME.complement}` — 100 minus the roll.
```
{name} is {roll.love.0.100}% in love. The other {roll.love.complement}% is hunger.
Chris is 64% in love. The other 36% is hunger.
```

`{random.pick.a.b.c}` — one item from a dot-separated list.
```
Today's mood: {random.pick.sleepy.hyped.cozy}
Today's mood: cozy
```

`{count.KEY}` — a named counter.
```
Deaths so far: {count.deaths}
Deaths so far: 23
```

`{list.pick.NAME}` — a random entry from a saved pick-list.
```
{list.pick.jokes}
Why did the creeper cross the road? To blow up the other side.
```

`{custom.SOURCE.FIELD}` — a field from a custom data source you set up.

---

## Command arguments

`{args.N}` — the Nth word after the command.
Works in: commands, pipelines.
```
!shout hello world  →  {user} shouts: {args.1} {args.2}!
stoney_eagle shouts: hello world!
```

---

## Text transforms

`{transform.NAME:text}` — change the text inside. Works everywhere. You can put other helpers inside.

- `upper` — `{transform.upper:{name}}` → `CHRIS`
- `lower` — `{transform.lower:Hello}` → `hello`
- `title` — `{transform.title:good night}` → `Good Night`
- `reverse` — `{transform.reverse:abc}` → `cba`
- `spaced` — `{transform.spaced:hey}` → `h e y`
- `alternating` — `{transform.alternating:hello}` → `hElLo`
- `mocking` — `{transform.mocking:sure}` → `sUrE`
- `yell` — `{transform.yell:no way}` → `NO WAY!`
- `trim` — removes spaces at both ends
- `truncate` — shortens long text
- `input` — the whole text typed after the command
- `argument` — one argument by position

---

## Events

These need an event. Works in: event responses, pipelines.

**Follow**
`{followed_at}` — when the follow happened.
```
Thanks for the follow, {user}!
Thanks for the follow, chris!
```

**Subscriptions**
`{tier}` — "Tier 1", "Tier 2" or "Tier 3".
`{months}` — total months subscribed.
`{streak}` — months in a row.
`{message}` — the resub message.
`{also_said}` — the resub message with a lead-in and quotes, or nothing when there is none.
```
{user} resubbed for {months} months at {tier}! {also_said}
chris resubbed for 5 months at Tier 1! They also said: "love this place"
```

**Gift subs**
`{count}` — how many were gifted.
`{gifter}` — who gifted, or "Anonymous".
`{gifter.id}` — the gifter's id, empty when anonymous.
`{anonymous}` — `true` or `false`.
```
{gifter} just gifted {count} subs!
Chris just gifted 5 subs!
```

**Cheers**
`{bits}` — how many bits.
```
{user} cheered {bits} bits! {message}
chris cheered 500 bits! great run
```

**Raids**
`{viewers}` — how many came with the raid.
```
{user} raided with {viewers} viewers!
chris raided with 42 viewers!
```

**Channel points**
`{reward}` — the reward title.
`{reward.id}`, `{redemption.id}` — ids.
`{cost}` — the cost.
`{input}` — what the viewer typed, if the reward asks for text.
```
{user} redeemed {reward} for {cost} points: {input}
chris redeemed Hydrate for 500 points: drink some water
```

**Moderation**
`{moderator}` — who did it.
`{reason}` — the reason, if given.
`{duration}` — "permanent" for a ban, "10 minutes" for a timeout.
```
{moderator} timed out {user} for {duration}. Reason: {reason}
Stoney_Eagle timed out chris for 10 minutes. Reason: spoilers
```

**Ads**
`{ad.duration}` — "3 minutes".
`{ad.seconds}` — "90".
`{ad.when}` — "in ~3 minutes".
`{ad.automatic}` — `true` or `false`.
```
An ad break of {ad.duration} starts {ad.when}.
An ad break of 3 minutes starts in ~3 minutes.
```

**Polls**
`{poll.title}`, `{poll.status}`, `{poll.winner}`, `{poll.winner.votes}`, `{poll.winner.percentage}`, `{poll.results}`.
```
Poll over! "{poll.title}" — winner: {poll.winner} {poll.winner.percentage}
Poll over! "Next game?" — winner: Minecraft (68%)
```

**Stream online / offline**
`{broadcaster}`, `{title}`, `{game}` (see Stream). On offline, `{duration}` is the stream length.
```
Stream over after {duration}. Thanks for watching!
Stream over after 3 hours 20 minutes. Thanks for watching!
```

**Viewer engagement**
`{viewer.name}` — the viewer's display name.
`{engagement.daysSinceLastSeen}` — days since last seen.
`{engagement.streak}` — current watch streak.
`{engagement.months}`, `{engagement.years}` — how long a moderator has moderated.
```
Welcome back {viewer.name}, it has been {engagement.daysSinceLastSeen} days!
Welcome back Chris, it has been 14 days!
```

**Supporters** (tips, memberships, merch, charity)
`{supporter.name}`, `{supporter.kind}`, `{supporter.amount}`, `{supporter.currency}`, `{supporter.tier}`, `{supporter.quantity}`, `{supporter.message}`.
```
{supporter.name} sent a {supporter.kind} of {supporter.amount} {supporter.currency}. {supporter.message}
Chris sent a tip of 12.34 EUR. keep it up!
```

**OBS and VTube Studio**
`{obs.event.FIELD}`, `{vts.event.FIELD}` — a field from the event itself.
```
Scene changed to {obs.event.sceneName}.
Scene changed to Gameplay.
```

---

## Pipeline only

`{playlist_id}`, `{track_name}` — after a playlist add (the `!banger` action).
```
Added {track_name} to the bangers playlist.
Added Never Gonna Give You Up to the bangers playlist.
```

`{music.favorite.track}`, `{music.favorite.artist}`, `{music.favorite.count}`, `{music.favorite.outcome}` — after a "song request favorite" step.
```
{name}'s favorite is {music.favorite.track} by {music.favorite.artist}, requested {music.favorite.count} times.
Chris's favorite is Never Gonna Give You Up by Rick Astley, requested 9 times.
```

---

## Discord only

`{channel.name}`, `{channel.title}`, `{channel.game}`, `{raw.message}` — short names for Discord posts.
```
{channel.name} is live: {channel.title} ({channel.game})
Stoney_Eagle is live: Chill building night (Minecraft)
```

---

## Known gaps

These are things the bot cannot do yet. They are listed here so you do not get surprised.

- **No plural helper for numbers.** `{months} months` gives `1 months` when it is one month. Only `{verb:...}` handles agreement, and only for people.
- **No name possessive helper.** Write `{name}'s` yourself. For a name ending in s, like Chris, that gives `Chris's`.
- **No a/an helper.** Write the article yourself.
- **`{presentTense}` with they/them gives "are".** `{name} {presentTense} here` gives `Sam are here.` Use `{verb:is|are}` only when the sentence reads well both ways, or rewrite the sentence.

These four get helpers next. This page is checked by a test against the real bot, so it stays correct when helpers change.

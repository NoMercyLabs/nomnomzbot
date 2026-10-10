# Community safety in NomNomzBot: how we keep your stream safe and fun

**Audience:** streamers and moderators on Twitch, Kick, YouTube and X Live.
**Owner:** NoMercy Labs.
**Date:** 2026-10-10. Version 2. The Today lists match the bot on this date.

## Contents

- [1. Never punish the wrong person](#1-never-punish-the-wrong-person)
- [2. Your channel, your call](#2-your-channel-your-call)
- [3. Strong tools, firm limits, a clear trail](#3-strong-tools-firm-limits-a-clear-trail)
- [4. Stop attacks fast while you are live](#4-stop-attacks-fast-while-you-are-live)
- [5. The bot never repeats the harm](#5-the-bot-never-repeats-the-harm)
- [6. Your accounts stay yours](#6-your-accounts-stay-yours)
- [7. Honest status, on every platform, updated live](#7-honest-status-on-every-platform-updated-live)
- [8. Care for the people behind the screen](#8-care-for-the-people-behind-the-screen)
- [9. Safety that keeps the stream fun](#9-safety-that-keeps-the-stream-fun)
- [10. The gaps platforms leave open, and our answer](#10-the-gaps-platforms-leave-open-and-our-answer)
- [Glossary](#glossary)

## Why we wrote this

A live stream is a public room. Most people come to have a good time.
A few come to hurt people.

Platforms give streamers some tools. But the tools have gaps.
Hate raids still happen. Scam links still spread. Many moderators work alone, late at night, without pay.

NomNomzBot is an open-source bot. It helps streamers, moderators and viewers keep a channel safe and fun.
This document explains how we think about that job.

### What this document is

- It states our design goals and the ideals behind them.
- It explains the reason for each goal in plain words.
- It tells you what each goal means for you and your channel.

### What this document is not

- It is not a manual. It does not explain buttons or settings.
- It is not a full progress report. Each chapter starts with a short Today list instead.
- It is not a promise about dates.

### How to read it

The chapters go from the most important goal to the least important.
Chapter 1 is the rule that wins over every other rule.
The last chapter lists the gaps that platforms leave open, and our answer to each one.
The glossary at the end explains the special words.

Each chapter starts with a Today list for 2026-10-10.
It says what works now, and the main parts that are not built yet.
The rest of the chapter describes our goal.
A goal that the Today list does not name as working is not proven yet.

## 1. Never punish the wrong person

The ideal: no viewer ever loses their place in a chat for something they did not do.
This is rule one. It comes before every other goal in this document.

**Today, 2026-10-10.**

Works now:

- A moderator can restrict an account on Twitch through the bot. The bot does not restrict an account by itself yet.
- The spam check never bans. It deletes a message, warns, or times out an account.
- The spam check never acts on a regular by itself. It only flags their message for a moderator.
- Good standing from other channels already counts in the spam check. The part on standing below explains how.
- The spam check starts in trial mode. It acts on nobody until you switch trial mode off.
- The spam check records the reason for each decision.
- The warn-first ladder is built. It is off until you turn it on.
- A mass ban reaches only the channels where it is switched on. Chapter 2 explains how.

Not built yet:

- Automatic bans for hate raids, spam waves and follow bot waves.
- Chat filters that spare regulars. Today chat filters spare only the streamer and moderators.
- Trial mode and a test screen for chat filters.
- Ban requests and decision cards.
- Your refusal winning over a moderator who switched mass bans on for your channel.
- Lower standing for accounts that other channels banned.

### 1.1 Why it matters

A wrong ban hurts a real person. It also breaks the trust between viewers, streamers and moderators.

Safety tools are strong. A strong tool that hits the wrong person can do more harm than the attack.

Other products teach the same lessons:

- A broad clean-up tool can remove real viewers together with the attackers.
- A blunt answer to a raid, such as removing every new account, drives away real newcomers.
- A name that looks like a banned name is not proof. Names can match by chance.
- A shared ban list can turn into a tool for personal revenge.
- Automatic filters make mistakes. Good tools give a simple way to report a wrong decision.

So we design every safety feature to protect the innocent viewer first.
We do that with a reason, a record and an undo on every strong tool. We do not take power away from your team.

### 1.2 What this means for you

#### Silence first, decide second

When the bot is not sure about an account, it first makes the account quiet. Then a person decides what happens next.

On Twitch, the bot can restrict the account. Its messages then show only to moderators.

- The attacker gets no audience while you look at the facts.
- You decide with a clear head, not in the middle of an attack.
- If the account is innocent, you lift the restriction. Nothing else changes.

#### Act on the room, not on the person

During an attack, the bot changes the room first. A lockdown makes the chat stricter for everyone for a short time.

A room change protects everyone and punishes nobody. When the attack ends, the room goes back to normal.

#### Being present is never an offence

An account does not break a rule by watching the stream or by being new.
The bot acts on what an account does, such as the messages it sends.

#### Regulars are safe

A regular has a long, good history in your channel. The bot never punishes a regular by itself.
At most, the bot puts a flag on a message so a moderator can look.

This matters most when a regular talks about an attack. A viewer who complains about spam can use the same words as the spam.

#### Good standing in other channels counts too

Many channels run NomNomzBot. So the bot can know a viewer from many chats, not only from yours.
A viewer with a long, good history in those channels has earned trust in the community.
The bot gives that viewer the benefit of the doubt, also on their first visit to your chat.
At most, the bot flags their message for a moderator.

Bans count the other way. A viewer banned in many of those channels has a weaker standing.
Then the bot looks more closely at what the account does in your chat.
A ban elsewhere never punishes the account in your chat by itself.

Today, watch time already counts.
A viewer with 25 hours of watch time on one NomNomzBot server is safe from the spam check.
That spam check then never times out or bans them. You can change the 25 hours for your channel.
Bans in other channels do not lower standing yet.

#### Automatic action only for clear attacks
The bot acts by itself only when the attack is clear and the bot is very sure.
A guess is never enough to punish a person.

| How sure the bot is | What the bot does |
|---|---|
| Very sure, and the attack is clear, such as a hate raid, a spam wave or a follow bot wave | Bans the attackers by itself and shows you why. |
| Less sure | Flags the message. A moderator decides. |

#### Warn before you punish

For a first small offence, the bot warns the viewer first. Many good bots work this way.
A stronger step comes only when the same thing happens again.

#### Every action shows its reason

Every action records why it happened. You see the rule, the message and who acted.

Some tools keep their rules secret, so nobody can predict or check them. We show our rules, so you can understand every decision.

#### Every action can be undone

A wrong action must be easy to fix. Every action has an undo.
Every automatic action also has a one-click "this was wrong" button.

- Undo puts things back the way they were.
- "This was wrong" also tells you which rule made the mistake, so you can fix it.

#### A ban elsewhere reaches you only by choice

A ban in another channel reaches your channel only by choice.
One of your own moderators can switch mass bans on for your channel. You can refuse, and your refusal wins.

When mass bans are off in your channel, another channel can only send a ban request.
You see a decision card with the reason and the last messages. You decide.

#### New rules start in a trial mode

A new safety rule starts in trial mode. In trial mode, the rule acts on nobody.
It shows you what it would do. You check the results before it acts on real viewers.

You can also test a rule on sample messages before you save it.

### 1.3 What we will not do

- We will not ban an account only because its name looks like another name.
- We will not punish a regular automatically.
- We will not let another channel ban in your channel. Only your own moderators can switch that on, and you can refuse.
- We will not keep a central ban list that others push into your channel.
- We will not let viewers vote to ban a person. A crowd can be wrong.
- We will not hide our rules or the reason for an action.

## 2. Your channel, your call

The ideal: you and the moderators you chose decide who gets banned in your channel. No other channel or list decides for you.

**Today, 2026-10-10.**

Works now:

- The mass ban works for Twitch through the bot's server. The dashboard has no mass ban screen yet.
- A mass ban reaches a channel in four cases only. It is the moderator's own channel, or it is under attack.
- The other two cases: you switched mass bans on, or the moderator recorded your yes with a note.
- Every switch starts off. It stays set until someone changes it.
- The moderator can leave out channels for one mass ban.
- A live channel waits. The streamer, a lead moderator or an editor can type "!allow massban" to let it go ahead.
- "!disallow massban" stops it. With no answer, the ban happens when the stream ends.
- In Twitch shared chat, a ban can reach a partner channel. The partner must switch this on and trust your channel.
- There is no central ban list to subscribe to or to import.

Not built yet:

- The limit to the people you entrust. Today any moderator can start a mass ban.
- Your refusal winning over a moderator's recorded yes.
- Ban requests and decision cards for channels that are off.
- The count of rejected or undone bans for each moderator.
- Mass bans on Kick and YouTube.

One more fact: the owner of a NomNomzBot server can ban one account in every channel on that server.
That step needs a written reason, and it can be lifted. Chapter 3 says more.

### 2.1 Why it matters

Many streamers fight the same trolls. A ban tool shared across channels looks like a fast answer. But that kind of tool is easy to misuse.

- One person with too much power can ban an innocent viewer in many channels at once.
- Shared ban lists turn into tools for grudges. Other platforms have seen this happen.
- A list name alone can hurt a person, even when nobody acts on it.
- A wrong ban is the worst result of a safety tool. **We build so that a viewer never gets banned for the wrong reason.**

So NomNomzBot keeps the final word in each channel. Your own moderators can switch mass bans on for you.
Any other help arrives as a question, never as an order.

### 2.2 What this means for you

#### No central ban list

NomNomzBot has no central ban list. You cannot subscribe to a list from another channel. You cannot import a list of names.

Each channel keeps its own bans. A mass ban reaches only the channels where it is switched on.

#### One click, in the channels your moderator picks

Many moderators help in a lot of channels. They should not ban the same troll in each channel by hand.

So a person with the mass ban power bans an account in many channels with one click.

- The mass ban reaches only the channels where the moderator switched it on.
- The moderator has one switch for each channel that they moderate.
- Every switch starts off. The moderator uses their own judgement for each channel.
- A switch saves at once. It stays set until the moderator changes it.
- If your channel does not use NomNomzBot, the moderator switches it on only with your word.

#### You can always say no

You can refuse mass bans for your channel. Your refusal wins over every moderator switch.
You can change your mind at any time.

#### Each channel shows its state

Before a mass ban, the moderator sees every channel and its state.

| State | What it means |
|---|---|
| On | The ban happens in this channel. |
| On, but live | The ban waits. The next part explains how. |
| Off | No ban here. A channel that uses NomNomzBot gets a ban request instead. |
| Refused | The streamer said no. No ban here. |
| No moderator rights | The bot cannot ban in this channel. |

The moderator can also leave out one channel for a single ban.

#### When the ban happens

| Channel | When the ban happens |
|---|---|
| The moderator's own channel | At once. |
| A channel under attack | At once. |
| An offline channel | At once. |
| A live channel | It waits. The bot posts one formal notice in the chat. |

In a live channel, a lead moderator or the streamer answers in chat.
The command "!allow massban" lets the ban go ahead. The command "!disallow massban" stops it.
With no answer, the ban happens when the stream ends.

If the bot is not in that chat, it joins only to listen for the answer. It leaves when the stream ends.

#### A ban request for channels that are off

When mass bans are off in your channel, a mass ban does not ban here.
If your channel uses NomNomzBot, it gets a ban request instead.

| What happens | Who does it |
|---|---|
| A moderator starts a mass ban. | A person with the mass ban power. |
| Your channel has mass bans off, so it gets a ban request. | NomNomzBot. |
| You see a decision card and choose. | You, or the team you trust. |
| You accept, so the account gets a real ban in your channel. | NomNomzBot, for you. |
| You reject, so nothing happens in your channel. | NomNomzBot, for you. |

#### The decision card

The decision card gives you the facts in one place. You do not need to ask anyone in another chat.

- Who asks: the moderator who sends the request, and their channel.
- Why: the reason they give.
- The evidence: the last messages of the account, so you can read what they said.
- One main choice: "Ban here too".
- A second choice: reject the request.

A ban request is only for hate, insults or harm. A personal quarrel on another stream is never a reason.

#### Your moderators carry your trust

When you make someone your moderator, you trust them. You trust them to ban only people you also do not want.
A mass ban rests on that same trust. We call this "entrust".

- You can refuse mass bans for your channel at any time.
- When you remove a moderator, their mass bans no longer reach your channel.
- The bot counts the bans of each moderator that channels reject or undo.
- You can see that count for each of your moderators.

#### Opt-in, off by default

Every safety feature that reaches across channels is opt-in. It is off until someone turns it on.

Nothing changes in your channel because another streamer turns a feature on.
Your refusal always wins over a moderator switch.

#### Real bans that the platform can see

When you accept a request, the bot places a real ban through the platform. It is not a private note inside NomNomzBot.

This matters. Each accepted request adds a ban that the platform itself records. When many channels ban the same account, the platform sees the pattern.

#### Good evidence, not mass reports

We do not mass-report accounts. On Twitch, many reports of one account do not speed up the review. Mass reports also become a second tool for grudges.

Instead, the bot helps you use the platform's own report flow well:

- It collects the messages of the account, with their times.
- It names the channel where they happened.
- You send that evidence through the platform's normal report form.

### 2.3 What we will not do

- We will not keep a central ban list.
- We will not ban in your channel after you refused mass bans.
- We will not let a mass ban reach your channel unless one of your own moderators switched it on.
- We will not switch on mass bans for every channel of a moderator at once.
- We will not mass-report accounts.
- We will not hide who asked for a ban, or why.

## 3. Strong tools, firm limits, a clear trail

The ideal: your team gets strong tools against hate and harm. Each strong tool has firm limits, a named owner and a full record.

**Today, 2026-10-10.**

Works now:

- One mass ban holds at most 1000 accounts.
- Each person can make at most 120 changes a minute.
- A network ban removes one account from every channel where you are a lead moderator or higher.
- Only a lead moderator or higher can use a network ban. One action undoes it.
- The system owner can ban one account in every channel on the server. It needs a written reason and can be lifted.
- When a ban in a mass ban fails, the moderator who started it gets a notice.

Not built yet:

- Reasons limited to hate, insults and harm. Today the moderator types any reason.
- One action that undoes a whole mass ban.
- Limits for each channel over an hour, and the second check with an authenticator app.
- The bot deciding if a channel is under attack. Today the moderator names the channel.
- A notice to the streamer for each strong action.
- A block on mass bans while the system owner is signed in to a channel for support.
- Ending every sign-in and every program key of one person at once.
- Program keys that always run out. Today a program key can have no end date.

### 3.1 Why it matters

A mass ban can remove an account from many channels in seconds. That power protects a community during an attack.

The same power can hurt innocent viewers. A ban tool shared across channels can be misused for a personal fight.

A moderator account can also be stolen. So we design every strong tool for the day that it falls into the wrong hands.

### 3.2 A mass ban is only for harm

A mass ban is for accounts that spread hate, insults or harm. **It is never a tool for a dispute or for drama.**

- The reasons you can pick for a mass ban are hate, insults and harm.
- Local actions in your own chat keep their full list of reasons.
- A disagreement between streamers, moderators or viewers is not a reason for a mass ban.

### 3.3 The power rests on the trust of each streamer

A streamer who makes someone a moderator trusts them with bans. The mass ban uses that trust, and no more.

- A moderator mass bans only an account that clearly harms the whole community.
- The streamer can refuse mass bans, or remove the moderator, at any time.

### 3.4 The person who asks owns the ban

Each mass ban carries the name of the person who starts it. That person owns the ban and its result.

- The ban shows who started it, why, and the last messages of the account.
- The history counts the bans of each person that channels reject or undo.
- The streamer sees that count for each moderator.

This keeps the power with people who use it with care.

### 3.5 Every batch can be undone

A mistake must never be permanent. You can undo a full batch of bans in one action.

- One action reverses every ban in the batch, in every channel.
- If a ban request is withdrawn, it leaves the inbox of each channel that has not decided yet.

### 3.6 A history shows everything

The dashboard keeps a clear history of every strong action. Nothing happens out of sight.

| The history shows | What you learn |
|---|---|
| Every request | Who asked, when, and for what reason. |
| Every decision | Which channel said yes or no, and who decided. |
| Every override | Who stepped in, and why. |
| Every undo | Who reversed a batch, and when. |

The history always names the real person who asked. This is also true when the bot runs the ban with the streamer's account.

### 3.7 Big actions have limits and a second check

A large action stops at a firm limit. Each person and each channel has a limit over a short time, such as one hour.

- When a person goes over the limit, the next batch waits for a decision by the streamer.
- A big action asks for a second check, such as a code from an authenticator app.
- The bot decides if a channel is under attack. The person who asks cannot declare it alone.

### 3.8 The streamer always knows

The streamer gets a notice for every strong action in the channel. This includes actions that the streamer did not start.

- The notice names the person who acted, the number of bans and the time.
- The streamer can see every batch in the channel, also those that moderators made.
- The streamer gets a warning when the moderator list changes.

### 3.9 The system owner steps in by name and in view

The system owner can step in during an emergency. The owner always acts by name and in full view.

- The step-in shows in the history, with the owner's name and the reason.
- The streamer gets a notice for each step-in.
- When the owner signs in to a channel to give support, that sign-in never starts a mass ban.

### 3.10 A stolen account does limited harm

We expect that an account can be stolen. So one stolen account, or one misused power, can do only limited harm.

- The limits and the second check slow a thief down.
- An admin can end every sign-in and every program key of one person at once.
- Program keys stop working after a set time.
- When a streamer account comes back after a theft, the bot asks for a new connection first.

### 3.11 What we will not do

- We will not give mass ban power to a whole role.
- We will not let one person ban across channels without a name on the request.
- We will not hide an action from the streamer.
- We will not make a mass ban that you cannot undo.
- We will not let anyone, the system owner included, act out of view.

## 4. Stop attacks fast while you are live

The ideal: an attack ends in seconds, and your stream stays safe even when no moderator is online. The bot acts fast, but it never stops being fair.

**Today, 2026-10-10.**

Works now:

- The spam check catches near-copies of spam pitches, also with look-alike letters.
- It deletes, warns or times out. A timeout lasts 10 minutes by default.
- An age gate can remove messages from brand new accounts. It is off by default.
- During a follow bot wave, the bot blocks the fake accounts. You can undo each block.
- A lockdown makes Twitch chat stricter. A moderator starts it through the bot's server. The dashboard has no lockdown button yet.
- The dashboard has a switch for Shield Mode, the safety mode of Twitch.
- You can list the sites whose links are always fine.
- Viewers can report a chatter from the dashboard chat. The report goes to a queue for moderators.
- Notes on an account are shared with the whole team.

Not built yet:

- A lockdown that starts by itself, or that covers alerts, TTS and the overlay.
- Lockdown on Kick and YouTube.
- Raid checks, copycat name checks and checks for personal details.
- The stricter setup for channels with no moderators, and the status card.
- One queue that also holds TTS and media requests, and the note for the next moderator.
- Checks for short links, look-alike sites and links from new accounts.

### 4.1 What we protect you from

These are the attacks we plan for first. The most harmful attack is at the top.

| Attack | What happens |
|---|---|
| Hate raid | Many accounts enter your chat at once and attack people for who they are. |
| Spam wave | Many new accounts post the same sales pitch again and again. |
| Follow bot | Thousands of fake accounts follow you in a short time. |
| Scam link | A link promises a gift but steals the account of the viewer who clicks it. |
| Copycat account | A name looks like yours, a moderator, or platform support, and asks for logins. |
| Leaked personal details | Someone posts a home address or a phone number in chat. |

### 4.2 You are safe with no moderator online

Many streamers go live alone, late at night, or with a small team. An attack does not wait for a moderator.

What this means for you:

- The bot watches for each attack above all the time you are live.
- A channel with no moderators gets a stricter safe setup from the start.
- That setup also asks you to add a moderator, because people still decide better than rules.
- One status card shows what protects your channel now, and what is off.

### 4.3 One lockdown protects everything

During a hate raid, a quiet chat is not enough. The attackers also use their names and words to reach your alerts, your overlay and the bot's voice.

So **one lockdown action protects chat and everything the bot shows and says**.

- Chat gets stricter at once, and the platform's own safety mode turns on where it exists.
- Alerts stop showing new names until the attack ends.
- TTS stops reading messages out loud.
- The chat on your overlay drops the lines that the chat filter removes.
- The bot posts fewer notices in chat, so the attackers get no stage.
- A sudden wave can start the lockdown by itself, and the lockdown ends by itself on a timer.

Each platform gives different controls. Where a platform does not let a bot lock chat, the dashboard says so plainly. It never shows a switch that does nothing.

### 4.4 Raids get checked as they arrive

A raid can be a gift from a friend or the start of an attack. The platform tells the bot only who sent the raid and how many came.

- The bot shows you a card for the raiding channel when the raid arrives.
- You can choose to start the lockdown when an unknown or banned channel raids you.
- Raiders become visible one by one as they talk, and the bot checks each one.

### 4.5 One queue for the whole team

Moderators often work alone and talk to each other in other apps. We want the team to work from one place.

- Every item that needs a person goes into one queue, oldest first.
- Flagged chat messages, TTS requests and media requests share that one queue.
- Each item shows the history of the account beside it, so a moderator decides with the facts.
- Notes on an account belong to the whole team. Every moderator sees what the last one wrote.
- A moderator can leave a short note for the next person on duty.

### 4.6 Links follow your policy

Blocking all links hurts your community. Allowing all links helps scammers. So links follow a policy that you set.

- You choose the sites that are always fine.
- The bot checks short links and web addresses made to look like a real site.
- A link from a brand new account gets a closer look than a link from a regular.
- You can also check links from trusted roles, because a stolen trusted account is a common trick.

### 4.7 Copycat accounts and leaked details get caught

A copycat account wins only if viewers believe it. Leaked details do harm only while people can see them.

- The bot compares each new name with your name, your moderators and words like "support".
- A match goes to a moderator as a flag. It is not an automatic ban.
- A regular with a similar name is not flagged.
- A message that looks like an address or a phone number goes to a moderator at once.
- The bot never repeats a leaked detail in an alert or a notice.

### 4.8 Fast, but fair

Chapter 1 sets our first rule: nobody gets banned for the wrong reason. Speed never sets that rule aside.

- The bot weighs trust and context before it acts on its own.
- A regular who quotes a spam phrase to complain about it stays safe.
- Every automatic action has a clear way to say "this was wrong" and undo it.
- Each "this was wrong" teaches us where a rule is too strict.

### 4.9 What we will not do

- We will not ban an account only because its name looks like yours.
- We will not ban an account only because a message looks like personal details.
- We will not show a lockdown control that a platform does not let us use.
- We will not let a lockdown leave your alerts, overlay or TTS open to attackers.

## 5. The bot never repeats the harm

**Today, 2026-10-10.**

Works now:

- TTS skips a short built-in list of 24 mild words.

Not built yet:

- The one safety check for every line the bot shows or speaks, and its word list.
- Keeping a removed message out of commands and the overlay.
- Full protection of viewer text in templates. Today viewer text can still use nine text tools, such as upper case.
- Those text tools change only the text. They cannot make the bot act.
- The viewer's own trust level in every place. Some TTS lines still give viewer text the streamer's level.
- A lockdown that makes the bot's output go quiet.

### 5.1 The ideal

The bot never becomes a tool for an attacker.
Nothing a troll types comes out of the bot as hate, in any place the bot speaks.

### 5.2 Why it matters

The bot talks on your stream in many places.
Most of these places show or speak words that come from viewers.

| Place | What viewers can put there |
|---|---|
| Alerts | Their display name. |
| TTS | Their message, read out loud. |
| Overlays | Their chat line, their name, their reward text. |
| Chat replies | Words they type after a command. |
| Reward text | Free text they type when they use a reward. |
| Poll and prediction titles | Text that a command or a reward fills in. |
| Discord posts | Text from a template that holds viewer words. |

A troll knows this.
The troll does not try to say the slur in chat.
The troll tries to make the bot say the slur for them.

When the bot repeats hate, the harm is worse.
**The hate now comes in your voice, from your own tool.**
Your community hears it from a source they trust.

### 5.3 One safety check for every line

Every line that the bot shows or speaks passes one safety check first.
It is the same check for every place in the table above.
There is no side door that skips it.

- **One check, everywhere.** A new feature that shows or speaks text uses the same check.
- **One word list per channel.** You set your words one time, and they apply to every output.
- **A clear action.** You choose the action: mask the word, drop the line, or skip the alert.
- **A safe default.** A new channel starts with a list that already blocks slurs and hate terms.

### 5.4 The check sees through tricks

Trolls hide bad words to get past a simple filter.
The check reads the word the way a person reads it.

| Trick | What the troll types | What the check sees |
|---|---|---|
| Spaced-out letters | Letters with spaces or dots between them | The whole word |
| Numbers for letters | A digit in place of a letter | The whole word |
| Look-alike characters | A letter from another alphabet that looks the same | The whole word |

So a trick that fools the eye for a moment does not fool the check.

### 5.5 A removed message stays removed

A filter can remove a message from chat.
That message must not come back through the bot.

- A command does not repeat a message that a filter removed.
- A removed message also leaves the overlay.
- The overlay waits for the filter before it shows a chat line.
- So a bad line never flashes on your stream, even for a moment.

### 5.6 Viewer text is only text

The bot uses templates to build its lines.
A template can hold words that a viewer typed.

**Viewer text is never run as a command inside the bot.**
The bot shows the viewer's words exactly as words.
A viewer cannot use special text to change what the bot does.

### 5.7 Viewer text keeps the viewer's trust level

Some bot actions run with the streamer's power.
Viewer text inside those actions does not borrow that power.

- Text from a viewer keeps that viewer's own trust level.
- A limit that you set for viewers applies to their words in every place.
- A moderator keeps the moderator's trust level for their own words.
- The bot never treats a viewer's words as if the streamer wrote them.

### 5.8 Lockdown makes the bot go quiet too

A hate raid does not stop at chat.
Attackers also use their names to fill your alerts and your TTS.

**During a lockdown, the bot's output goes quiet too.**
One action covers chat, alerts, TTS and the chat overlay together.
When the lockdown ends, the output comes back.

On a platform where we cannot lock a part, the dashboard tells you so in plain words.

### 5.9 What this means for you

- You set your safety words one time.
- They protect every place the bot speaks or shows text.
- Tricks with spaces, numbers and look-alike letters do not get past the check.
- A message your filter removes does not come back through a command or the overlay.
- One lockdown action makes chat and the bot's output safe together.

### 5.10 What we will not do

- We will not let any output skip the safety check.
- We will not run viewer text as a command.
- We will not give viewer text the streamer's trust level.
- We will not keep alerts and TTS running during a lockdown.
- We will not hide a part that a lockdown cannot cover.

## 6. Your accounts stay yours

The ideal: the bot acts for you, but your accounts stay under your control.
You decide what the bot may do, and you can stop it at any time.

**Today, 2026-10-10.**

Works now:

- For Twitch, the bot asks for a base set of access. It asks for more only when a feature needs it.
- For Spotify, YouTube and Kick, the bot asks for all the access it may need, in one step.
- The dashboard shows when a feature lacks access. It also shows when a connection ran out or was cut.
- We lock every key before we store it. Each account has its own lock.
- Disconnect and leaving delete our copy of the key for good.
- All three ways to run the bot use the same program. The desktop app is a dashboard only. It does not run the bot.

Not built yet:

- Asking the platform to cancel a key. Until then, cancel access in the platform's own settings too.
- The record of each key use, the frequent key check and the emergency stop.
- Signed requests out of your bot. Today your bot can receive signed requests, but not send them.

### 6.1 Why the bot holds a key

To act for you, a bot must keep an access key to your platform account.
The platform gives the bot this key when you sign in and agree.
The key lets the bot post in chat, ban an account or read your alerts.
The key keeps working until someone cancels it.
So **whoever holds the key can act as you**.

### 6.2 Why it matters

- One key opens one account.
- A place that keeps keys for many streamers keeps many accounts in one spot.
- Large channels make that spot a bigger prize for an attacker.
- Other services have lost the keys of their users before. The stolen keys then did the harm.

So we treat every key like a password.
We also plan for the day something goes wrong.

### 6.3 What this means for you

| Our ideal | What you see |
|---|---|
| We ask only for the access a feature needs. | The bot asks for more access only when you turn on a feature that needs it. |
| You can see what the bot can do. | The dashboard shows each account you connected and the access each one gives. |
| You can cut access. | Disconnect stops the bot from using the key at once. |
| A cut is real. | We also ask the platform to cancel the key, so a stolen copy stops working. |
| You get the truth. | If the platform does not cancel the key, we tell you. We show you where to cancel it yourself. |

You can always cancel access in the platform's own settings too.
The bot notices this and stops using that key.

### 6.4 How we guard every key

- We lock every key before we store it.
- Each account has its own lock. One open lock does not open the others.
- A key never goes into a web address, a log or a chat message.
- We record each use of a key: which part of the bot used it, why and when. The record never holds the key.
- We check each key with the platform often. So we learn fast when a key stops working.
- We keep an emergency stop. One action cancels every key the bot holds.

### 6.5 Three ways to run the bot

NomNomzBot runs in three ways.
Each way puts your keys in a different place.

| Way to run | Where it runs | Whose keys it keeps | What you win | What you take on |
|---|---|---|---|---|
| On your own computer | Your own computer | Only yours | Your keys never leave your computer. Nobody else can reach them. | You keep the computer safe. The bot runs only while your computer runs it. |
| Your own server | A server at home, on your network or at a friend's | A small group's | Good for a team. You choose who holds the keys. | Whoever runs the server can reach every key on it. Pick someone you trust. |
| Hosted service | Servers that NoMercy Labs runs | Every streamer who chose it | No setup for you. | One place keeps many keys, so it is the biggest target. |

**Self-hosting your own bot is always free.**
Running NomNomzBot as a hosted service for other people is reserved to NoMercy Labs under the license.

The more viewers a channel has, the more an attacker gains from its key.
Weigh this when you choose how to run the bot.

### 6.6 Shared safety without shared keys

The ideal: you run your own bot and still join the shared safety features.
Your keys stay on your own machine.

- Bots send each other signed requests.
- A request says what to do. It never says how to sign in.
- Your own bot shows the request to your channel first.
- Your channel decides. Then your own bot acts with your own key.

The hosted service does not need the key of a streamer who runs their own bot.
Running NomNomzBot as a hosted service for other people is reserved to NoMercy Labs under the license.

### 6.7 You can leave at any time

- When you leave, we delete your keys. Our copy can never be opened again.
- We also ask the platform to cancel those keys.
- Nothing locks you in.

### 6.8 What we will not do

- We will not ask for access that no feature of yours needs.
- We will not sell, share or pass on your keys.
- We will not put a key in a web address, a log or a message.
- We will not say that access is gone while the platform still accepts the key.
- We will not charge you to run your own bot.

## 7. Honest status, on every platform, updated live

The ideal: you manage your channel on every platform from one dashboard.
The dashboard always tells you the truth, and it is always up to date.

**Today, 2026-10-10.**

Works now:

- The bot connects to Twitch, Kick and YouTube. X Live does not connect yet.
- Bans and timeouts made in the Twitch tools show in the history.
- Kick bans and timeouts show too. Kick unbans do not show yet.
- The dashboard shows when its live link to the bot breaks, and when it comes back.
- 22 of the 70 areas of the bot send live updates. Other pages show new facts when you open them again.
- A mass ban runs at 25 bans every 5 seconds. A failed ban gets up to five tries.

Not built yet:

- The list of what each platform allows, on every screen.
- Progress for each platform during a large ban.
- The warning before a daily budget runs out.
- YouTube bans made outside the bot, and chat setting changes made on a platform.
- Linked accounts and account suggestions across platforms.

### 7.1 One dashboard for all your platforms

Many streamers go live on more than one platform at the same time.
NomNomzBot connects your channel to Twitch, Kick, YouTube and X Live together.
You and your moderators work from one dashboard, not from four.

- A ban, a timeout or a message delete works the same way on each platform that allows it.
- An attacker can have accounts on several of your platforms. An attacker never links those accounts.
- So one ban can hold several accounts on several platforms. Your moderator picks each account.
- Each account is banned for its own messages on its own platform. A matching name is never enough.
- The dashboard suggests accounts that look like the same person. A suggestion never bans anyone.
- Before the ban, the dashboard shows each account with its own last messages.

A viewer can link their own accounts by signing in to the bot with each one.
Good viewers do this. Their good name then protects them on every platform.

### 7.2 Why it matters

A safety tool that tells a lie is worse than no tool.
A switch that says "on" but does nothing makes you stop watching. Then the attack gets through.
Old or missing facts lead to wrong decisions in a hurry.
So every screen must show what is true right now.

### 7.3 Each platform allows different things

Each platform decides what a bot may do in its chat.
Twitch gives bots many safety tools. Kick and YouTube give bots fewer tools.
X Live gives bots no way to moderate its live chat.
The dashboard says plainly what each platform allows.

| When a platform... | The dashboard shows |
|---|---|
| lets the bot use a tool | The tool, ready to use. |
| lets you set a tool, but not the bot | "Set this on the platform", with a link to the right page. |
| has no such tool | "Not available on this platform", with the reason. |
| has no such tool, but the bot can do the same job | The bot's own version, marked as the bot's own. |

The dashboard never shows a switch that does nothing.
It never pretends to control a setting that only the platform controls.

### 7.4 On means it works

- A protection shows as on only when it truly works on that platform.
- A protection can work on one platform and not on another. Then the dashboard shows each platform on its own.
- A lockdown reports, for each platform, what it changed and what it could not change.
- If the bot loses access to a platform, its protections show as off. The dashboard gives the reason and the fix.
- A list that fails to load shows an error. It never shows as an empty list.
- An empty list means the platform confirms that the list is empty.

An empty ban list after a failed load looks like a clean channel.
That false calm is the danger we design against.

### 7.5 Changes made outside the bot still show up

You and your moderators also act on the platform itself.
The dashboard still shows those changes.

- A ban made in the platform's own tools appears in the dashboard.
- A change to the chat settings on the platform appears too.
- The moderation history shows who acted, on which platform, and when.
- Some platforms do not tell bots about every change. Then the dashboard says that it can miss a change made there.
- The bot records each of its own actions at once. So the history never misses what the bot did.

### 7.6 Live and fast

- The dashboard updates by itself the moment something changes. You never reload the page.
- Every moderator sees the same state at the same time.
- If the live connection to the bot breaks, the dashboard says so at once.
- The dashboard never shows old facts as current.
- When the connection comes back, the dashboard gets the latest state.

### 7.7 Platform limits are respected

Each platform limits how often a bot may act.
Some platforms also give each bot a fixed daily budget of actions.

- The bot paces its actions to stay inside the limits of each platform.
- The bot slows down when a platform asks it to slow down.
- During a large ban, the dashboard shows the progress on each platform.
- When a platform refuses an action, the bot tries again later and tells you.
- **A ban never fails without a notice.** The dashboard names each ban that did not go through.
- When a daily budget runs low, the dashboard warns you before it runs out.

### 7.8 What we will not do

- We will not show a protection as on when it does not work.
- We will not show an empty list when the list failed to load.
- We will not hide what each platform allows.
- We will not pretend to control a setting that only the platform controls.
- We will not ban an account on another platform because its name matches.
- We will not let a ban fail without telling you.

## 8. Care for the people behind the screen

The ideal: every chat has real people, and some get hurt in ways a ban cannot fix.
The bot helps people find real help fast, and it brings a human in.

**Today, 2026-10-10.**

Works now:

- Twitch unban requests show in the dashboard. A moderator decides each one.

Not built yet:

- Everything else in this chapter.

### 8.1 Why it matters

Some harms hurt a person more than a channel.
A viewer in crisis, a leaked home address or a child in chat needs care, not a quick ban.
A bot is fast, but it has no authority in the real world.
So **the bot adds speed and privacy, and the platform and real services give the help**.

Our rule for every harm in this chapter:

- The bot points to the platform's own tools and help pages.
- The bot alerts a human first.
- The dashboard says plainly what the bot cannot do, and it links the right page.

### 8.2 A viewer in crisis

Sometimes a viewer or the streamer writes about self-harm or suicide.
Chat can react badly with jokes or a pile-on.
A strict filter can even punish the person in distress.

| Our ideal | What you see |
|---|---|
| The person gets pointed to real help. | A support command posts your own kind words and a link to the platform's crisis help page. |
| Help fits the place where the person lives. | The link goes to a page that asks for the location. The bot does not show one country's number to everyone. |
| A human takes over. | The bot sends a private alert to you and your moderators, with a link to the message. |
| Nobody gets punished for asking for help. | The bot never bans, times out or shames a person for a crisis message. |

Word lists make mistakes, so a match is only an alert.
A joke can trigger an alert too, and that is fine.
A moderator reads the message and decides what to do.

### 8.3 Swatting and danger in the real world

Someone can find a streamer's home and share the address.
Then someone can make a false emergency call, so armed police go there.
Streamers who stream outdoors can also show where they are by accident.

A bot cannot call the police, and the platform cannot call them for you either.
So we help you prepare, and we keep the plan close at hand.

- A help page in the dashboard explains what the platform can and cannot do.
- The page shows how to reach your local police yourself.
- The page reminds you that a stream delay makes it harder to follow you in real time.
- The page links the platform's own guide on this harm.

### 8.4 Leaked personal details

A leak of a home address or a real name can follow a streamer for years.
So **the bot catches leaked details and removes them fast**.

- You can store your private details in the bot, such as your street or family names.
- The bot locks these details. Only the check uses them.
- The bot checks chat for these details, also with odd spacing or swapped letters.
- On a match, the bot deletes the message and alerts you in private.
- The alert does not repeat the details. The logs do not keep them.

A word filter is a good shield, but it is not a wall.
We never promise that a filter stops every leak.

### 8.5 Young viewers and their parents

The platform has its own rules for young people, and its own guides for parents.
The bot does not try to replace them.

- The dashboard links the platform's guide for parents and teachers.
- Moderators get a card with the platform's report path for harm to a child.
- You can turn on a private alert for some messages to young viewers.
- The alert looks for requests to move to another app or to send a photo.
- The same alert can catch a viewer who says they are below the platform's minimum age.

We treat the age of every person as unknown.
The bot never asks for an age or for proof of who you are.

### 8.6 Moderators are people too

Most moderators help for free.
Many of them work alone, late at night, and read abuse for hours.
When they get tired and leave, the channel has no cover.

| Our ideal | What you see |
|---|---|
| You see who is on duty. | A view shows which moderators are active now, and which hours have no cover. |
| Nobody stays alone too long. | The view marks the hours when one moderator works alone. |
| Help is one click away. | A moderator can ask for cover and leave a short note for the next person. |
| Moderators know what comes. | Moderators get a heads-up before a planned raid or a big stream. |
| A moderator's own data stays private. | Each moderator sees their own workload. You see the team totals only. |

Every one of these tools is opt-in.

### 8.7 A fair way back for a banned viewer

People make mistakes, and tools make mistakes too.
A banned viewer deserves a fair way to ask for a second chance.

- Where the platform has an unban request, the bot shows the request to your moderators.
- The bot can show the ban and the last messages next to the request.
- A moderator always makes the final decision, not the bot.
- If the platform does not allow a request, we say so plainly.

### 8.8 What we will not do

- The bot does not give medical, legal or mental health advice.
- The bot does not claim that it calls the police or that help is on the way.
- The bot does not send a private message to a person in crisis.
- The bot does not label a person as a child or as a danger to children in public.
- The bot does not keep or forward sexual content that involves a child. Moderators delete it, ban and report it to the platform.
- The bot does not rank moderators or track their mood in secret.
- We do not claim that the bot prevents burnout.

## 9. Safety that keeps the stream fun

The ideal: safety exists so people can have fun together.
It protects the chat, but it does not police the people in it.

**Today, 2026-10-10.**

Works now:

- The spam check never acts on a regular by itself.
- The warn-first ladder: a warning, then timeouts of 1 minute, 10 minutes, 1 hour and 1 day, then a ban.
- The ladder is off until you turn it on.
- The spam check starts in trial mode.
- The spam check and the age gate have defaults that you can change.

Not built yet:

- Chat filters that spare regulars, and trial mode for chat filters.

### 9.1 Why it matters

A chat full of fear is not a community.
If every word can cost a viewer a timeout, people stop talking.
A safety tool that hurts the mood does the attacker's job for them.

### 9.2 What this means for you

**Regulars and newcomers chat freely.**
A regular with a long, good history is never punished by an automatic rule.
At most, the bot flags a message for a moderator to look at.
A newcomer gets the same warm welcome, and the bot watches more closely only when a real signal shows up.

**A good joke stays a good joke.**
One word in a message does not tell the whole story.
The bot looks at who speaks and how the message reads, not only at single words.

**Warnings come before punishment.**
A first mistake gets a warning, not a ban.
A repeat gets a short timeout, then a longer one.
A ban comes last, for people who keep going.

**The bot does not make a show of bans.**
A public ban notice gives attackers a stage.
A stage draws more attackers.
So the bot acts quietly during an attack, and your team sees the full record on the dashboard.

**Blunt tools stay in the box.**
A tool that removes every new account also removes real new viewers.
A filter on part of a name also hits innocent people with a similar name.
The bot picks the narrow action that stops the harm and spares everyone else.

**You set how strict the bot is.**
Every limit is a setting with a sensible default.
You can make a rule stricter for a busy channel or softer for a small one.
The bot explains each action in plain words, so you can tune it with confidence.

**Safety stays quiet until it is needed.**
Most of the time, viewers do not notice the safety tools at all.
The strong tools wake up when an attack starts, and they step back when it ends.
New protection rules start in a test mode, so you see what they would do before they act.

### 9.3 What we will not do

- We will not punish a viewer for one word out of context.
- We will not post every ban in chat for the room to see.
- We will not use a tool that hurts real newcomers to catch a few attackers.
- We will not hide our rules, so you can always see why the bot acted.
- We will not let a crowd vote someone out of the chat.

## 10. The gaps platforms leave open, and our answer

Every platform leaves gaps in its safety tools. A bot can close some of these gaps, but not all of them.

The table lists every gap we know. The most harmful gap is at the top. Where the bot cannot help, we say so and point you to the platform.

| Gap | What it means for you | Our answer | Today |
|---|---|---|---|
| Hate raid | A wave of accounts enters your chat to attack people for who they are. | One lockdown makes your chat and the bot's output safe at once. The bot bans the accounts that clearly take part. The bot cannot stop a raid at the door, so set raid filters on the platform. | A person starts a lockdown of Twitch chat. No automatic bans yet. |
| No reason comes with a ban from another channel | Each channel bans the same attacker by hand. The platform shows where an account is banned, but not why. | A mass ban carries its reason and the last messages. Channels with mass bans off get a decision card with the reason. The bot cannot read the platform's own shared ban list. | The Twitch mass ban works, with no dashboard screen yet. Decision cards are not built. |
| Moderation power can be misused | A moderator can ban someone over a private dispute. The platform puts no brake on this. | Each mass ban rests on your trust in your moderators, and each ban needs a reason. You can refuse mass bans. Actions taken on the platform's own site reach your log only afterward. | Only switched-on channels get a mass ban. Any moderator can start one. Refusal is not built. |
| Scam links | Fake gifts and harmful links steal the accounts of your viewers. The platform's link block is all or nothing, and trusted chatters skip it. | Our own link filter checks short links and lookalike sites, even from trusted chatters. A new account that posts a link gets a closer look. | Your list of safe sites works. The other link checks are not built. |
| Copycat account | A name copies you, a moderator or platform support, and asks viewers for their logins. | The bot spots names made to look like yours or your team's. Report fake channels outside your chat on the platform. | Not built yet. |
| Personal details in chat | Someone posts a home address or a phone number. It can spread far beyond your chat. | A moderator reviews any message that looks like an address or a phone number. The bot never bans on this guess alone. | Not built yet. |
| Follow bots and fake viewers | Thousands of fake accounts follow you, or fake viewers fill your count. This can hurt your standing. | The bot bans the fake accounts of a follow wave. It leaves out followers with a chat history. You undo any other ban with one click. Fake viewers stay out of its sight. | The bot blocks follow bots, and you can undo each block. It does not ban them. |
| Spam campaign | Many new accounts post the same sales pitch again and again. | The bot matches near-copies of known pitches and bans the spam accounts. A regular who quotes the phrase stays safe, because trust counts. | Works, in trial mode first. It deletes, warns or times out, but never bans. Regulars stay safe. |
| Channel with no moderators | A small channel often has no moderator and weak default filters. | A channel with no moderators starts with a stricter safe setup. Only you can add moderators, so the bot asks you to. | Not built yet. |
| Reports go into a black box | You report an account and never learn the result. | The bot collects the evidence and opens the platform's own report form for you. It cannot send the report or show the result. | Not built yet. |
| Weak way back after a wrong ban | A viewer gets one unban request per channel, and a denial is final. | Unban requests arrive in one queue. Every automatic action has an undo button and names who acted. | Twitch unban requests show in the dashboard. A moderator decides each one. |
| Moderators work alone | Moderators plan in other apps, and one person often covers a whole night. | Shared notes on each account and one review queue keep the team together. A hand-over note passes on each shift. | Shared notes work. One queue and hand-over notes are not built. |
| Abuse in private messages | Scammers send private messages to your viewers. | Viewers can report to the bot in a private message, and the bot posts a short safety notice. It cannot see messages between other people. | Viewers can report a chatter from the dashboard. Reports by private message are not built. |
| Settings the bot cannot reach | Phone checks for chatters, link blocking and raid filters exist only on the platform's own site. | The dashboard lists each one as set on the platform. It gives a direct link and a short reason. | Not built yet. |
| Youth safety | Harm to a minor needs the platform's own safety team at once. | The bot sends a moderator straight to the platform's report form. It cannot check ages, and we never handle these cases ourselves. | Not built yet. |

### 10.1 What we will not do

- We will not claim to close a gap that only the platform can close.
- We will not hide a setting the bot cannot reach. You always see it, with a link to it.
- We will not ban on a guess, such as a message that only looks like an address.

## Glossary

| Word | Meaning here |
|---|---|
| Access key | What a platform gives the bot when you sign in and agree. It lets the bot act for your account. |
| Alert | A message on the stream when something happens, such as a new follower. |
| Authenticator app | A phone app that shows a short code to prove that you are you. |
| AutoMod | The Twitch tool that holds back a chat message for a moderator to review. |
| Ban request | A request to ban an account in a channel where mass bans are off. That channel decides. |
| Copycat account | An account with a name made to look like the streamer, a moderator or platform support. |
| Dashboard | The NomNomzBot web page where a streamer and the team manage the channel. |
| Decision card | The card a streamer sees for a ban request: who asks, why, and the last messages of the account. |
| Editor | A Twitch role that the streamer gives to someone who helps run the channel. |
| Entrust | The trust a streamer gives by making someone a moderator. A mass ban uses that trust. |
| Follow bot | A fake account that follows a channel in a large wave. |
| Hate raid | A wave of accounts that enters a chat to attack people for who they are. |
| Hosted service | NomNomzBot run on servers of NoMercy Labs for other streamers. Only NoMercy Labs may run it. |
| Lead moderator | A moderator with extra powers that the streamer gives. |
| Lockdown | One action that makes a chat and the bot's output safe during an attack. |
| Mass ban | A ban of one account across many channels at the same time. |
| Opt-in | Off until the streamer or the person turns it on. |
| Overlay | A layer on top of the stream picture, such as the chat box or an alert. |
| Program key | A secret key that lets another program act for a person on the bot. |
| Regular | A viewer with a long, good history in the channel. |
| Restrict | A Twitch status. The account's messages show only to moderators until someone decides. |
| Self-hosting | Running your own copy of the bot on your own computer or server. It is always free. |
| Shield Mode | The Twitch safety mode. It makes the chat stricter in one step. |
| Signed request | A message from one bot to another with a mark that proves which bot sent it. |
| Standing | How much the bot trusts a viewer, from their history in your channel and in other channels. |
| Stream delay | A setting that shows the stream to viewers a few seconds or minutes late. |
| Swatting | A false emergency call that sends armed police to a person's home. |
| System owner | The person who runs the NomNomzBot server that many channels use. |
| TTS | Text to speech: the bot reads a message out loud on the stream. |

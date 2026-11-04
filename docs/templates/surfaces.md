# Template helpers by surface

This page is generated from the helper registry by `TemplateSurfaceAvailability.RenderMarkdown`. The `Works in:` lines in [README.md](README.md) come from the same registry. Do not edit this page by hand: a test fails when it differs from the registry. `Y` means the helper works on that surface. A code means it does not, and the legend at the end gives the reason.

## Keys without a dot

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `also_said` | N | Y | N | Y | N | N | V |
| `anonymous` | N | Y | N | Y | N | N | V |
| `args` | Y | A | A | Y | A | A | A |
| `bits` | N | Y | N | Y | Y | N | V |
| `botname` | Y | Y | Y | Y | Y | Y | V |
| `broadcaster` | N | Y | N | Y | Y | N | V |
| `chance` | Y | Y | Y | Y | Y | Y | Y |
| `channel` | Y | Y | Y | Y | Y | Y | Y |
| `coalesce` | Y | Y | Y | Y | Y | Y | Y |
| `cost` | N | Y | N | Y | N | N | V |
| `count` | N | Y | N | Y | Y | N | V |
| `date` | Y | Y | Y | Y | Y | Y | V |
| `date:<source>` | Y | Y | Y | Y | Y | Y | V |
| `date:<source>\|<+/-amount><unit>` | Y | Y | Y | Y | Y | Y | V |
| `date:<source>\|diff:<date>[:<unit>]` | Y | Y | Y | Y | Y | Y | V |
| `date:<source>\|end:<unit>` | Y | Y | Y | Y | Y | Y | V |
| `date:<source>\|format:<pattern>` | Y | Y | Y | Y | Y | Y | V |
| `date:<source>\|lang:<language>` | Y | Y | Y | Y | Y | Y | V |
| `date:<source>\|next:<weekday>` | Y | Y | Y | Y | Y | Y | V |
| `date:<source>\|ordinal` | Y | Y | Y | Y | Y | Y | V |
| `date:<source>\|prev:<weekday>` | Y | Y | Y | Y | Y | Y | V |
| `date:<source>\|relative` | Y | Y | Y | Y | Y | Y | V |
| `date:<source>\|since[:<unit>]` | Y | Y | Y | Y | Y | Y | V |
| `date:<source>\|start:<unit>` | Y | Y | Y | Y | Y | Y | V |
| `date:<source>\|tz:<zone>` | Y | Y | Y | Y | Y | Y | V |
| `date:<source>\|until[:<unit>]` | Y | Y | Y | Y | Y | Y | V |
| `default` | Y | Y | Y | Y | Y | Y | Y |
| `duration` | N | Y | N | Y | Y | N | V |
| `emote` | Y | Y | Y | Y | Y | Y | V |
| `followed_at` | N | Y | N | Y | N | N | V |
| `game` | N | Y | N | Y | Y | N | V |
| `genderedTerm` | Y | Y | N | Y | N | Y | V |
| `gifter` | N | Y | N | Y | N | N | V |
| `if` | Y | Y | Y | Y | Y | Y | Y |
| `input` | N | Y | N | Y | N | N | V |
| `link` | Y | Y | N | Y | N | Y | V |
| `literal` | Y | Y | Y | Y | Y | Y | Y |
| `math` | Y | Y | Y | Y | Y | Y | Y |
| `message` | N | Y | N | Y | Y | N | V |
| `meter` | Y | Y | Y | Y | Y | Y | Y |
| `moderator` | N | Y | N | Y | N | N | V |
| `months` | N | Y | N | Y | Y | N | V |
| `name` | Y | Y | N | Y | N | Y | V |
| `object` | Y | Y | N | Y | N | Y | V |
| `pick` | Y | Y | Y | Y | Y | Y | Y |
| `playlist_id` | N | N | N | Y | N | N | V |
| `possessive` | Y | Y | N | Y | N | Y | V |
| `presentTense` | Y | Y | N | Y | N | Y | V |
| `provider` | N | Y | N | N | N | N | V |
| `quote` | Y | Y | Y | Y | Y | Y | V |
| `reason` | N | Y | N | Y | N | N | V |
| `reward` | N | Y | N | Y | N | N | V |
| `set` | Y | Y | Y | Y | Y | Y | V |
| `status` | Y | Y | Y | Y | Y | Y | V |
| `streak` | N | Y | N | Y | Y | N | V |
| `streamer` | Y | Y | Y | Y | Y | Y | Y |
| `subject` | Y | Y | N | Y | N | Y | V |
| `switch` | Y | Y | Y | Y | Y | Y | Y |
| `target` | Y | Y | N | Y | N | Y | V |
| `tense` | Y | Y | Y | Y | Y | Y | V |
| `tier` | N | Y | N | Y | Y | N | V |
| `time` | Y | Y | Y | Y | Y | Y | V |
| `title` | N | Y | N | Y | Y | N | V |
| `track_name` | N | N | N | Y | N | N | V |
| `user` | Y | Y | N | Y | Y | Y | Y |
| `verb:<sing\|plur>` | Y | Y | N | Y | N | Y | V |
| `viewers` | N | Y | N | Y | Y | N | V |

## activity

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `activity.busiestDay` | Y | Y | Y | Y | Y | Y | V |
| `activity.busiestHour` | Y | Y | Y | Y | Y | Y | V |
| `activity.chatters.active` | Y | Y | Y | Y | Y | Y | V |
| `activity.chatters.lurkers` | Y | Y | Y | Y | Y | Y | V |
| `activity.chatters.now` | Y | Y | Y | Y | Y | Y | V |
| `activity.chatters.unique` | Y | Y | Y | Y | Y | Y | V |
| `activity.commands.stream` | Y | Y | Y | Y | Y | Y | V |
| `activity.last.chatter` | Y | Y | Y | Y | Y | Y | V |
| `activity.last.message.ago` | Y | Y | Y | Y | Y | Y | V |
| `activity.messages.hour` | Y | Y | Y | Y | Y | Y | V |
| `activity.messages.stream` | Y | Y | Y | Y | Y | Y | V |
| `activity.messages.today` | Y | Y | Y | Y | Y | Y | V |
| `activity.mpm` | Y | Y | Y | Y | Y | Y | V |
| `activity.random.recent` | Y | Y | Y | Y | Y | Y | V |
| `activity.top.hour` | Y | Y | Y | Y | Y | Y | V |
| `activity.top.list` | Y | Y | Y | Y | Y | Y | V |
| `activity.top.stream` | Y | Y | Y | Y | Y | Y | V |
| `activity.top.today` | Y | Y | Y | Y | Y | Y | V |
| `activity.top.today.count` | Y | Y | Y | Y | Y | Y | V |

## ad

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `ad.automatic` | N | Y | N | Y | N | N | V |
| `ad.duration` | N | Y | N | Y | N | N | V |
| `ad.isScheduled` | Y | Y | Y | Y | Y | Y | V |
| `ad.last.ago` | Y | Y | Y | Y | Y | Y | V |
| `ad.last.at` | Y | Y | Y | Y | Y | Y | V |
| `ad.next.at` | Y | Y | Y | Y | Y | Y | V |
| `ad.next.duration` | Y | Y | Y | Y | Y | Y | V |
| `ad.next.in` | Y | Y | Y | Y | Y | Y | V |
| `ad.next.in.long` | Y | Y | Y | Y | Y | Y | V |
| `ad.next.seconds` | Y | Y | Y | Y | Y | Y | V |
| `ad.prerollFree` | Y | Y | Y | Y | Y | Y | V |
| `ad.seconds` | N | Y | N | Y | N | N | V |
| `ad.snoozes.left` | Y | Y | Y | Y | Y | Y | V |
| `ad.snoozes.refreshIn` | Y | Y | Y | Y | Y | Y | V |
| `ad.when` | N | Y | N | Y | N | N | V |

## args

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `args.<a>-<b>` | Y | A | A | Y | A | A | A |
| `args.<n>` | Y | A | A | Y | A | A | A |
| `args.<n>+` | Y | A | A | Y | A | A | A |
| `args.<n>.clean` | Y | A | A | Y | A | A | A |
| `args.<n>.isMention` | Y | A | A | Y | A | A | A |
| `args.<n>.isNumber` | Y | A | A | Y | A | A | A |
| `args.<n>.length` | Y | A | A | Y | A | A | A |
| `args.<n>.number` | Y | A | A | Y | A | A | A |
| `args.<n>.upper` | Y | A | A | Y | A | A | A |
| `args.<n>.urlEncoded` | Y | A | A | Y | A | A | A |
| `args.<n>.user.accountAge` | Y | A | A | Y | A | A | A |
| `args.<n>.user.accountAge.days` | Y | A | A | Y | A | A | A |
| `args.<n>.user.activity.level` | Y | A | A | Y | A | A | A |
| `args.<n>.user.avatar` | Y | A | A | Y | A | A | A |
| `args.<n>.user.bio` | Y | A | A | Y | A | A | A |
| `args.<n>.user.color` | Y | A | A | Y | A | A | A |
| `args.<n>.user.createdAt` | Y | A | A | Y | A | A | A |
| `args.<n>.user.data.<key>` | Y | A | A | Y | A | A | A |
| `args.<n>.user.display` | Y | A | A | Y | A | A | A |
| `args.<n>.user.followAge` | Y | A | A | Y | A | A | A |
| `args.<n>.user.followAge.days` | Y | A | A | Y | A | A | A |
| `args.<n>.user.followedAt` | Y | A | A | Y | A | A | A |
| `args.<n>.user.game` | Y | A | A | Y | A | A | A |
| `args.<n>.user.isFollower` | Y | A | A | Y | A | A | A |
| `args.<n>.user.isLive` | Y | A | A | Y | A | A | A |
| `args.<n>.user.isSub` | Y | A | A | Y | A | A | A |
| `args.<n>.user.login` | Y | A | A | Y | A | A | A |
| `args.<n>.user.mention` | Y | A | A | Y | A | A | A |
| `args.<n>.user.platform` | Y | A | A | Y | A | A | A |
| `args.<n>.user.platform.name` | Y | A | A | Y | A | A | A |
| `args.<n>.user.points` | Y | A | A | Y | A | A | A |
| `args.<n>.user.points.name` | Y | A | A | Y | A | A | A |
| `args.<n>.user.points.nextRank.gap` | Y | A | A | Y | A | A | A |
| `args.<n>.user.points.nextRank.name` | Y | A | A | Y | A | A | A |
| `args.<n>.user.points.rank` | Y | A | A | Y | A | A | A |
| `args.<n>.user.points.rank.ordinal` | Y | A | A | Y | A | A | A |
| `args.<n>.user.points.raw` | Y | A | A | Y | A | A | A |
| `args.<n>.user.quotes.count` | Y | A | A | Y | A | A | A |
| `args.<n>.user.shoutoutLink` | Y | A | A | Y | A | A | A |
| `args.<n>.user.stats.bits` | Y | A | A | Y | A | A | A |
| `args.<n>.user.stats.firstSeen.relative` | Y | A | A | Y | A | A | A |
| `args.<n>.user.stats.lastSeen` | Y | A | A | Y | A | A | A |
| `args.<n>.user.stats.lastSeen.relative` | Y | A | A | Y | A | A | A |
| `args.<n>.user.stats.streak` | Y | A | A | Y | A | A | A |
| `args.<n>.user.stats.streak.best` | Y | A | A | Y | A | A | A |
| `args.<n>.user.stats.watchTime.hours` | Y | A | A | Y | A | A | A |
| `args.<n>.user.stats.watchTime.long` | Y | A | A | Y | A | A | A |
| `args.<n>.user.subTier` | Y | A | A | Y | A | A | A |
| `args.<n>.user.subTier.name` | Y | A | A | Y | A | A | A |
| `args.<n>.user.title` | Y | A | A | Y | A | A | A |
| `args.<n>.user.viewers` | Y | A | A | Y | A | A | A |
| `args.<n>:<default>` | Y | A | A | Y | A | A | A |
| `args.all.quoted` | Y | A | A | Y | A | A | A |
| `args.all.urlEncoded` | Y | A | A | Y | A | A | A |
| `args.count` | Y | A | A | Y | A | A | A |
| `args.duration` | Y | A | A | Y | A | A | A |
| `args.first` | Y | A | A | Y | A | A | A |
| `args.isEmpty` | Y | A | A | Y | A | A | A |
| `args.last` | Y | A | A | Y | A | A | A |
| `args.mentions` | Y | A | A | Y | A | A | A |
| `args.mentions.count` | Y | A | A | Y | A | A | A |
| `args.numbers` | Y | A | A | Y | A | A | A |
| `args.rest` | Y | A | A | Y | A | A | A |

## bot

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `bot.channel.loadedAgo` | Y | Y | Y | Y | Y | Y | V |
| `bot.commands.count` | Y | Y | Y | Y | Y | Y | V |
| `bot.commands.list` | Y | Y | Y | Y | Y | Y | V |
| `bot.commands.list.builtin` | Y | Y | Y | Y | Y | Y | V |
| `bot.commands.list.custom` | Y | Y | Y | Y | Y | Y | V |
| `bot.commands.list.forUser` | Y | Y | Y | Y | Y | Y | V |
| `bot.dashboard` | Y | Y | Y | Y | Y | Y | V |
| `bot.isMod` | Y | Y | Y | Y | Y | Y | V |
| `bot.language` | Y | Y | Y | Y | Y | Y | V |
| `bot.lastRestart.relative` | Y | Y | Y | Y | Y | Y | V |
| `bot.license` | Y | Y | Y | Y | Y | Y | V |
| `bot.mention` | Y | Y | Y | Y | Y | Y | V |
| `bot.name` | Y | Y | Y | Y | Y | Y | V |
| `bot.personality` | Y | Y | Y | Y | Y | Y | V |
| `bot.prefix` | Y | Y | Y | Y | Y | Y | V |
| `bot.sourceCode` | Y | Y | Y | Y | Y | Y | V |
| `bot.startedAt` | Y | Y | Y | Y | Y | Y | V |
| `bot.timers.count` | Y | Y | Y | Y | Y | Y | V |
| `bot.uptime` | Y | Y | Y | Y | Y | Y | V |
| `bot.uptime.long` | Y | Y | Y | Y | Y | Y | V |
| `bot.version` | Y | Y | Y | Y | Y | Y | V |

## channel

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `channel.age` | Y | Y | Y | Y | Y | Y | V |
| `channel.avatar` | N | N | N | N | Y | N | V |
| `channel.ccls` | Y | Y | Y | Y | Y | Y | V |
| `channel.display` | Y | Y | Y | Y | Y | Y | Y |
| `channel.editors.count` | Y | Y | Y | Y | Y | Y | V |
| `channel.followers` | Y | Y | Y | Y | Y | Y | V |
| `channel.followers.compact` | Y | Y | Y | Y | Y | Y | V |
| `channel.game` | N | N | N | N | Y | N | V |
| `channel.id` | Y | Y | Y | Y | Y | Y | V |
| `channel.isBranded` | Y | Y | Y | Y | Y | Y | V |
| `channel.language` | Y | Y | Y | Y | Y | Y | V |
| `channel.localTime` | Y | Y | Y | Y | Y | Y | V |
| `channel.login` | N | N | N | N | Y | N | V |
| `channel.mention` | Y | Y | Y | Y | Y | Y | V |
| `channel.mods.count` | Y | Y | Y | Y | Y | Y | V |
| `channel.name` | N | N | N | N | Y | N | V |
| `channel.personality` | Y | Y | Y | Y | Y | Y | V |
| `channel.prefix` | Y | Y | Y | Y | Y | Y | V |
| `channel.raids.inCount` | Y | Y | Y | Y | Y | Y | V |
| `channel.raids.lastIn.at` | Y | Y | Y | Y | Y | Y | V |
| `channel.raids.lastOut` | Y | Y | Y | Y | Y | Y | V |
| `channel.raids.outCount` | Y | Y | Y | Y | Y | Y | V |
| `channel.subs` | Y | Y | Y | Y | Y | Y | V |
| `channel.tags` | Y | Y | Y | Y | Y | Y | V |
| `channel.team` | Y | Y | Y | Y | Y | Y | V |
| `channel.timezone` | Y | Y | Y | Y | Y | Y | V |
| `channel.title` | N | N | N | N | Y | N | V |
| `channel.url` | N | N | N | N | Y | N | V |
| `channel.vips.count` | Y | Y | Y | Y | Y | Y | V |
| `channel.vod.latest` | Y | Y | Y | Y | Y | Y | V |
| `channel.vod.latest.title` | Y | Y | Y | Y | Y | Y | V |

## charity

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `charity.bar` | Y | Y | Y | Y | Y | Y | V |
| `charity.donations.count` | Y | Y | Y | Y | Y | Y | V |
| `charity.donations.latest` | Y | Y | Y | Y | Y | Y | V |
| `charity.link` | Y | Y | Y | Y | Y | Y | V |
| `charity.name` | Y | Y | Y | Y | Y | Y | V |
| `charity.percent` | Y | Y | Y | Y | Y | Y | V |
| `charity.raised` | Y | Y | Y | Y | Y | Y | V |
| `charity.target` | Y | Y | Y | Y | Y | Y | V |

## chars

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `chars.newline` | Y | Y | Y | Y | Y | Y | Y |
| `chars.nothing` | Y | Y | Y | Y | Y | Y | Y |
| `chars.pipe` | Y | Y | Y | Y | Y | Y | Y |
| `chars.space` | Y | Y | Y | Y | Y | Y | Y |
| `chars.zws` | Y | Y | Y | Y | Y | Y | Y |

## chat

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `chat.fit` | Y | Y | Y | Y | Y | Y | V |
| `chat.fit.words` | Y | Y | Y | Y | Y | Y | V |
| `chat.isShared` | Y | Y | Y | Y | Y | Y | V |
| `chat.limit` | Y | Y | Y | Y | Y | Y | V |
| `chat.me` | Y | Y | Y | Y | Y | Y | V |
| `chat.mention` | Y | Y | Y | Y | Y | Y | V |
| `chat.mention.off` | Y | Y | Y | Y | Y | Y | V |
| `chat.mention.soft` | Y | Y | Y | Y | Y | Y | V |
| `chat.mention.user` | Y | Y | U | Y | Y | Y | V |
| `chat.message` | Y | Y | U | Y | Y | Y | V |
| `chat.message.bits` | Y | Y | U | Y | U | Y | V |
| `chat.message.length` | Y | Y | U | Y | Y | Y | V |
| `chat.message.noCommand` | Y | Y | U | Y | Y | Y | V |
| `chat.pinned` | Y | Y | Y | Y | Y | Y | V |
| `chat.settings.emoteOnly` | Y | Y | Y | Y | Y | Y | V |
| `chat.settings.followersOnly` | Y | Y | Y | Y | Y | Y | V |
| `chat.settings.slow` | Y | Y | Y | Y | Y | Y | V |
| `chat.settings.subOnly` | Y | Y | Y | Y | Y | Y | V |
| `chat.shared.channels` | Y | Y | Y | Y | Y | Y | V |

## count

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `count.<key>` | Y | Y | Y | Y | Y | Y | V |

## custom

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `custom.<name>.<field>` | Y | Y | Y | Y | Y | Y | V |

## emote

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `emote.channel.top` | Y | Y | Y | Y | Y | Y | V |
| `emote.channel.top.stream` | Y | Y | Y | Y | Y | Y | V |
| `emote.cheermote` | Y | Y | Y | Y | Y | Y | V |
| `emote.count.7tv` | Y | Y | Y | Y | Y | Y | V |
| `emote.count.bttv` | Y | Y | Y | Y | Y | Y | V |
| `emote.count.channel` | Y | Y | Y | Y | Y | Y | V |
| `emote.count.ffz` | Y | Y | Y | Y | Y | Y | V |
| `emote.image` | Y | Y | Y | Y | Y | Y | V |
| `emote.list.7tv` | Y | Y | Y | Y | Y | Y | V |
| `emote.list.bttv` | Y | Y | Y | Y | Y | Y | V |
| `emote.list.channel` | Y | Y | Y | Y | Y | Y | V |
| `emote.list.ffz` | Y | Y | Y | Y | Y | Y | V |
| `emote.message.count` | Y | Y | M | Y | Y | Y | V |
| `emote.message.first` | Y | Y | M | Y | Y | Y | V |
| `emote.provider` | Y | Y | Y | Y | Y | Y | V |
| `emote.random` | Y | Y | Y | Y | Y | Y | V |
| `emote.random.7tv` | Y | Y | Y | Y | Y | Y | V |
| `emote.random.any` | Y | Y | Y | Y | Y | Y | V |
| `emote.random.bits` | Y | Y | Y | Y | Y | Y | V |
| `emote.random.bttv` | Y | Y | Y | Y | Y | Y | V |
| `emote.random.ffz` | Y | Y | Y | Y | Y | Y | V |
| `emote.random.follower` | Y | Y | Y | Y | Y | Y | V |
| `emote.random.global` | Y | Y | Y | Y | Y | Y | V |
| `emote.random.sub` | Y | Y | Y | Y | Y | Y | V |
| `emote.repeat` | Y | Y | Y | Y | Y | Y | Y |
| `emote.user.favorite` | Y | Y | Y | Y | Y | Y | V |
| `emote.user.top` | Y | Y | Vw | Y | Y | Y | V |
| `emote.user.top.count` | Y | Y | Vw | Y | Y | Y | V |
| `emote.wall` | Y | Y | Y | Y | Y | Y | V |

## engagement

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `engagement.daysSinceLastSeen` | N | Y | N | Y | N | N | V |
| `engagement.months` | N | Y | N | Y | N | N | V |
| `engagement.streak` | N | Y | N | Y | N | N | V |
| `engagement.years` | N | Y | N | Y | N | N | V |

## fun

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `fun.8ball` | Y | Y | Y | Y | Y | Y | V |
| `fun.card` | Y | Y | Y | Y | Y | Y | Y |
| `fun.coin` | Y | Y | Y | Y | Y | Y | Y |
| `fun.coin.emoji` | Y | Y | Y | Y | Y | Y | Y |
| `fun.countdown` | Y | Y | Y | Y | Y | Y | Y |
| `fun.dice` | Y | Y | Y | Y | Y | Y | Y |
| `fun.dice.detail` | Y | Y | Y | Y | Y | Y | Y |
| `fun.dice.emoji` | Y | Y | Y | Y | Y | Y | Y |
| `fun.flip` | Y | Y | Y | Y | Y | Y | Y |
| `fun.lenny` | Y | Y | Y | Y | Y | Y | Y |
| `fun.love` | Y | Y | Y | Y | Y | Y | Y |
| `fun.meter` | Y | Y | Y | Y | Y | Y | Y |
| `fun.percent` | Y | Y | Vw | Y | Vw | Y | Y |
| `fun.random.animal` | Y | Y | Y | Y | Y | Y | Y |
| `fun.random.color.name` | Y | Y | Y | Y | Y | Y | Y |
| `fun.random.emoji` | Y | Y | Y | Y | Y | Y | Y |
| `fun.random.food` | Y | Y | Y | Y | Y | Y | Y |
| `fun.rate` | Y | Y | Y | Y | Y | Y | Y |
| `fun.roulette` | Y | Y | Y | Y | Y | Y | Y |
| `fun.rps` | Y | Y | Y | Y | Y | Y | Y |
| `fun.rps.vs` | Y | Y | Y | Y | Y | Y | Y |
| `fun.ship` | Y | Y | Y | Y | Y | Y | Y |
| `fun.shrug` | Y | Y | Y | Y | Y | Y | Y |
| `fun.slots` | Y | Y | Y | Y | Y | Y | Y |
| `fun.slots.win` | Y | Y | Y | Y | Y | Y | Y |
| `fun.unflip` | Y | Y | Y | Y | Y | Y | Y |

## gifter

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `gifter.id` | N | Y | N | Y | N | N | V |

## goal

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `goal.followers.bar` | Y | Y | Y | Y | Y | Y | V |
| `goal.followers.current` | Y | Y | Y | Y | Y | Y | V |
| `goal.followers.description` | Y | Y | Y | Y | Y | Y | V |
| `goal.followers.percent` | Y | Y | Y | Y | Y | Y | V |
| `goal.followers.remaining` | Y | Y | Y | Y | Y | Y | V |
| `goal.followers.target` | Y | Y | Y | Y | Y | Y | V |
| `goal.list` | Y | Y | Y | Y | Y | Y | V |
| `goal.newSubs.current` | Y | Y | Y | Y | Y | Y | V |
| `goal.newSubs.percent` | Y | Y | Y | Y | Y | Y | V |
| `goal.subs.bar` | Y | Y | Y | Y | Y | Y | V |
| `goal.subs.current` | Y | Y | Y | Y | Y | Y | V |
| `goal.subs.percent` | Y | Y | Y | Y | Y | Y | V |
| `goal.subs.remaining` | Y | Y | Y | Y | Y | Y | V |
| `goal.subs.target` | Y | Y | Y | Y | Y | Y | V |

## hype_train

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `hype_train.goal` | N | Y | N | Y | N | N | V |
| `hype_train.id` | N | Y | N | Y | N | N | V |
| `hype_train.level` | N | Y | N | Y | N | N | V |
| `hype_train.total` | N | Y | N | Y | N | N | V |

## hypetrain

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `hypetrain.bar` | Y | Y | Y | Y | Y | Y | V |
| `hypetrain.goal` | Y | Y | Y | Y | Y | Y | V |
| `hypetrain.isActive` | Y | Y | Y | Y | Y | Y | V |
| `hypetrain.isGolden` | Y | Y | Y | Y | Y | Y | V |
| `hypetrain.isShared` | Y | Y | Y | Y | Y | Y | V |
| `hypetrain.level` | Y | Y | Y | Y | Y | Y | V |
| `hypetrain.percent` | Y | Y | Y | Y | Y | Y | V |
| `hypetrain.progress` | Y | Y | Y | Y | Y | Y | V |
| `hypetrain.record.level` | Y | Y | Y | Y | Y | Y | V |
| `hypetrain.remaining` | Y | Y | Y | Y | Y | Y | V |
| `hypetrain.timeLeft` | Y | Y | Y | Y | Y | Y | V |
| `hypetrain.topContributor` | Y | Y | Y | Y | Y | Y | V |
| `hypetrain.total` | Y | Y | Y | Y | Y | Y | V |

## if

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `if.<path>` | Y | Y | Y | Y | Y | Y | Y |
| `if.<path>.contains` | Y | Y | Y | Y | Y | Y | Y |
| `if.<path>.endswith` | Y | Y | Y | Y | Y | Y | Y |
| `if.<path>.equals` | Y | Y | Y | Y | Y | Y | Y |
| `if.<path>.gt` | Y | Y | Y | Y | Y | Y | Y |
| `if.<path>.gte` | Y | Y | Y | Y | Y | Y | Y |
| `if.<path>.iequals` | Y | Y | Y | Y | Y | Y | Y |
| `if.<path>.lt` | Y | Y | Y | Y | Y | Y | Y |
| `if.<path>.lte` | Y | Y | Y | Y | Y | Y | Y |
| `if.<path>.matches` | Y | Y | Y | Y | Y | Y | V |
| `if.<path>.startswith` | Y | Y | Y | Y | Y | Y | Y |
| `if.all` | Y | Y | Y | Y | Y | Y | Y |
| `if.any` | Y | Y | Y | Y | Y | Y | Y |
| `if.empty` | Y | Y | Y | Y | Y | Y | Y |
| `if.not` | Y | Y | Y | Y | Y | Y | Y |

## include

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `include.builtin` | Y | Y | Y | Y | Y | Y | V |
| `include.cmd` | Y | Y | Y | Y | Y | Y | V |
| `include.list` | Y | Y | Y | Y | Y | Y | V |

## link

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `link.bluesky` | Y | Y | Y | Y | Y | Y | V |
| `link.mastodon` | Y | Y | Y | Y | Y | Y | V |
| `link.threads` | Y | Y | Y | Y | Y | Y | V |
| `link.x` | Y | Y | Y | Y | Y | Y | V |

## list

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `list.contains` | Y | Y | Y | Y | Y | Y | V |
| `list.count` | Y | Y | Y | Y | Y | Y | V |
| `list.first` | Y | Y | Y | Y | Y | Y | V |
| `list.indexOf` | Y | Y | Y | Y | Y | Y | V |
| `list.inline.item` | Y | Y | Y | Y | Y | Y | V |
| `list.inline.join` | Y | Y | Y | Y | Y | Y | V |
| `list.inline.length` | Y | Y | Y | Y | Y | Y | V |
| `list.item` | Y | Y | Y | Y | Y | Y | V |
| `list.join` | Y | Y | Y | Y | Y | Y | V |
| `list.join.and` | Y | Y | Y | Y | Y | Y | V |
| `list.last` | Y | Y | Y | Y | Y | Y | V |
| `list.length` | Y | Y | Y | Y | Y | Y | V |
| `list.pick` | Y | Y | Y | Y | Y | Y | V |
| `list.pick.<name>` | Y | Y | Y | Y | Y | Y | V |
| `list.random.index` | Y | Y | Y | Y | Y | Y | V |
| `list.shuffle` | Y | Y | Y | Y | Y | Y | V |
| `list.sorted` | Y | Y | Y | Y | Y | Y | V |

## lookup

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `lookup.currency` | Y | Y | Y | Y | Y | Y | V |
| `lookup.currency.date` | Y | Y | Y | Y | Y | Y | V |
| `lookup.currency.rate` | Y | Y | Y | Y | Y | Y | V |
| `lookup.game` | Y | Y | Y | Y | Y | Y | V |
| `lookup.game.top` | Y | Y | Y | Y | Y | Y | V |
| `lookup.time` | Y | Y | Y | Y | Y | Y | V |
| `lookup.translate` | Y | Y | Y | Y | Y | Y | V |
| `lookup.translate.chat` | Y | Y | M | Y | Y | Y | V |
| `lookup.twitch` | Y | Y | Y | Y | Y | Y | V |
| `lookup.weather` | Y | Y | Y | Y | Y | Y | V |

## math

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `math.abs` | Y | Y | Y | Y | Y | Y | Y |
| `math.add` | Y | Y | Y | Y | Y | Y | Y |
| `math.avg` | Y | Y | Y | Y | Y | Y | Y |
| `math.ceil` | Y | Y | Y | Y | Y | Y | Y |
| `math.div` | Y | Y | Y | Y | Y | Y | Y |
| `math.div.int` | Y | Y | Y | Y | Y | Y | Y |
| `math.floor` | Y | Y | Y | Y | Y | Y | Y |
| `math.log` | Y | Y | Y | Y | Y | Y | Y |
| `math.median` | Y | Y | Y | Y | Y | Y | Y |
| `math.mod` | Y | Y | Y | Y | Y | Y | Y |
| `math.mul` | Y | Y | Y | Y | Y | Y | Y |
| `math.pow` | Y | Y | Y | Y | Y | Y | Y |
| `math.sqrt` | Y | Y | Y | Y | Y | Y | Y |
| `math.sub` | Y | Y | Y | Y | Y | Y | Y |
| `math.sum` | Y | Y | Y | Y | Y | Y | Y |

## meter

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `meter.hearts` | Y | Y | Y | Y | Y | Y | Y |
| `meter.plain` | Y | Y | Y | Y | Y | Y | Y |
| `meter.stars` | Y | Y | Y | Y | Y | Y | Y |

## music

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `music.favorite.artist` | N | N | N | Y | N | N | V |
| `music.favorite.count` | N | N | N | Y | N | N | V |
| `music.favorite.outcome` | N | N | N | Y | N | N | V |
| `music.favorite.track` | N | N | N | Y | N | N | V |
| `music.history.count.stream` | Y | Y | Y | Y | Y | Y | V |
| `music.history.last` | Y | Y | Y | Y | Y | Y | V |
| `music.history.list` | Y | Y | Y | Y | Y | Y | V |
| `music.history.topArtist.stream` | Y | Y | Y | Y | Y | Y | V |
| `music.history.topRequester.stream` | Y | Y | Y | Y | Y | Y | V |
| `music.now` | Y | Y | Y | Y | Y | Y | V |
| `music.now.album` | Y | Y | Y | Y | Y | Y | V |
| `music.now.artist` | Y | Y | Y | Y | Y | Y | V |
| `music.now.bar` | Y | Y | Y | Y | Y | Y | V |
| `music.now.cover` | Y | Y | Y | Y | Y | Y | V |
| `music.now.duration` | Y | Y | Y | Y | Y | Y | V |
| `music.now.isPlaying` | Y | Y | Y | Y | Y | Y | V |
| `music.now.link` | Y | Y | Y | Y | Y | Y | V |
| `music.now.percent` | Y | Y | Y | Y | Y | Y | V |
| `music.now.progress` | Y | Y | Y | Y | Y | Y | V |
| `music.now.remaining` | Y | Y | Y | Y | Y | Y | V |
| `music.now.requester` | Y | Y | Y | Y | Y | Y | V |
| `music.now.requester.mention` | Y | Y | Y | Y | Y | Y | V |
| `music.now.status` | Y | Y | Y | Y | Y | Y | V |
| `music.now.title` | Y | Y | Y | Y | Y | Y | V |
| `music.provider` | Y | Y | Y | Y | Y | Y | V |
| `music.queue.cost.total` | Y | Y | Y | Y | Y | Y | V |
| `music.queue.duration` | Y | Y | Y | Y | Y | Y | V |
| `music.queue.isEmpty` | Y | Y | Y | Y | Y | Y | V |
| `music.queue.item` | Y | Y | Y | Y | Y | Y | V |
| `music.queue.length` | Y | Y | Y | Y | Y | Y | V |
| `music.queue.list` | Y | Y | Y | Y | Y | Y | V |
| `music.queue.mine.code` | Y | Y | Vw | Y | Vw | Y | V |
| `music.queue.mine.count` | Y | Y | Vw | Y | Vw | Y | V |
| `music.queue.mine.next` | Y | Y | Vw | Y | Vw | Y | V |
| `music.queue.mine.titles` | Y | Y | Vw | Y | Vw | Y | V |
| `music.queue.next` | Y | Y | Y | Y | Y | Y | V |
| `music.queue.next.requester` | Y | Y | Y | Y | Y | Y | V |
| `music.queue.position` | Y | Y | Vw | Y | Vw | Y | V |
| `music.queue.position.ordinal` | Y | Y | Vw | Y | Vw | Y | V |
| `music.queue.requesters` | Y | Y | Y | Y | Y | Y | V |
| `music.queue.target.count` | Y | Y | N | Y | N | Y | V |
| `music.queue.wait` | Y | Y | Vw | Y | Vw | Y | V |
| `music.queue.wait.at` | Y | Y | Vw | Y | Vw | Y | V |
| `music.repeat` | Y | Y | Y | Y | Y | Y | V |
| `music.requests.maxDuration` | Y | Y | Y | Y | Y | Y | V |
| `music.requests.maxPerUser` | Y | Y | Y | Y | Y | Y | V |
| `music.requests.open` | Y | Y | Y | Y | Y | Y | V |
| `music.shuffle` | Y | Y | Y | Y | Y | Y | V |
| `music.user.lastRequest` | Y | Y | Vw | Y | Vw | Y | V |
| `music.user.requests.stream` | Y | Y | Vw | Y | Vw | Y | V |
| `music.user.requests.total` | Y | Y | Vw | Y | Vw | Y | V |
| `music.volume` | Y | Y | Y | Y | Y | Y | V |

## num

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `num.abs` | Y | Y | Y | Y | Y | Y | Y |
| `num.bar` | Y | Y | Y | Y | Y | Y | Y |
| `num.between` | Y | Y | Y | Y | Y | Y | Y |
| `num.binary` | Y | Y | Y | Y | Y | Y | Y |
| `num.bytes` | Y | Y | Y | Y | Y | Y | Y |
| `num.clamp` | Y | Y | Y | Y | Y | Y | Y |
| `num.compact` | Y | Y | Y | Y | Y | Y | Y |
| `num.convert` | Y | Y | Y | Y | Y | Y | Y |
| `num.currency` | Y | Y | Y | Y | Y | Y | Y |
| `num.currency.compact` | Y | Y | Y | Y | Y | Y | Y |
| `num.dec` | Y | Y | Y | Y | Y | Y | Y |
| `num.diff` | Y | Y | Y | Y | Y | Y | Y |
| `num.digitSum` | Y | Y | Y | Y | Y | Y | Y |
| `num.digits` | Y | Y | Y | Y | Y | Y | Y |
| `num.duration` | Y | Y | Y | Y | Y | Y | Y |
| `num.duration.clock` | Y | Y | Y | Y | Y | Y | Y |
| `num.duration.long` | Y | Y | Y | Y | Y | Y | Y |
| `num.format` | Y | Y | Y | Y | Y | Y | Y |
| `num.fromRoman` | Y | Y | Y | Y | Y | Y | Y |
| `num.hex` | Y | Y | Y | Y | Y | Y | Y |
| `num.inc` | Y | Y | Y | Y | Y | Y | Y |
| `num.isEven` | Y | Y | Y | Y | Y | Y | Y |
| `num.max` | Y | Y | Y | Y | Y | Y | Y |
| `num.min` | Y | Y | Y | Y | Y | Y | Y |
| `num.ordinal` | Y | Y | Y | Y | Y | Y | Y |
| `num.ordinal.words` | Y | Y | Y | Y | Y | Y | Y |
| `num.parity` | Y | Y | Y | Y | Y | Y | Y |
| `num.parse` | Y | Y | Y | Y | Y | Y | Y |
| `num.percent` | Y | Y | Y | Y | Y | Y | Y |
| `num.percentOf` | Y | Y | Y | Y | Y | Y | Y |
| `num.ratio` | Y | Y | Y | Y | Y | Y | Y |
| `num.roman` | Y | Y | Y | Y | Y | Y | Y |
| `num.round` | Y | Y | Y | Y | Y | Y | Y |
| `num.sign` | Y | Y | Y | Y | Y | Y | Y |
| `num.truncate` | Y | Y | Y | Y | Y | Y | Y |
| `num.words` | Y | Y | Y | Y | Y | Y | Y |

## obs

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `obs.event.<field>` | N | Y | N | Y | N | N | V |

## pick

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `pick.inline` | Y | Y | Y | Y | Y | Y | Y |
| `pick.n` | Y | Y | Y | Y | Y | Y | Y |
| `pick.shuffle` | Y | Y | Y | Y | Y | Y | Y |
| `pick.weighted` | Y | Y | Y | Y | Y | Y | Y |

## platform

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `platform.chatters.stream` | Y | Y | Y | Y | Y | Y | V |
| `platform.connected` | Y | Y | Y | Y | Y | Y | V |
| `platform.count` | Y | Y | Y | Y | Y | Y | V |
| `platform.event` | Y | Y | P | Y | Y | Y | V |
| `platform.followers.twitch` | Y | Y | Y | Y | Y | Y | V |
| `platform.isLive` | Y | Y | Y | Y | Y | Y | V |
| `platform.link.kick` | Y | Y | Y | Y | Y | Y | V |
| `platform.link.twitch` | Y | Y | Y | Y | Y | Y | V |
| `platform.link.youtube` | Y | Y | Y | Y | Y | Y | V |
| `platform.link.youtube.live` | Y | Y | Y | Y | Y | Y | V |
| `platform.links` | Y | Y | Y | Y | Y | Y | V |
| `platform.live.count` | Y | Y | Y | Y | Y | Y | V |
| `platform.live.list` | Y | Y | Y | Y | Y | Y | V |
| `platform.messages.stream` | Y | Y | Y | Y | Y | Y | V |
| `platform.title` | Y | Y | Y | Y | Y | Y | V |
| `platform.top` | Y | Y | Y | Y | Y | Y | V |
| `platform.user` | Y | Y | N | Y | N | Y | V |
| `platform.user.icon` | Y | Y | N | Y | N | Y | V |
| `platform.user.name` | Y | Y | N | Y | N | Y | V |
| `platform.viewers.breakdown` | Y | Y | Y | Y | Y | Y | V |
| `platform.viewers.kick` | Y | Y | Y | Y | Y | Y | V |
| `platform.viewers.share` | Y | Y | Y | Y | Y | Y | V |
| `platform.viewers.total` | Y | Y | Y | Y | Y | Y | V |
| `platform.viewers.twitch` | Y | Y | Y | Y | Y | Y | V |
| `platform.viewers.youtube` | Y | Y | Y | Y | Y | Y | V |

## poll

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `poll.choices` | N | Y | N | Y | N | N | V |
| `poll.duration` | N | Y | N | Y | N | N | V |
| `poll.endsAt` | Y | Y | Y | Y | Y | Y | V |
| `poll.id` | N | Y | N | Y | N | N | V |
| `poll.isActive` | Y | Y | Y | Y | Y | Y | V |
| `poll.isTie` | Y | Y | Y | Y | Y | Y | V |
| `poll.last.question` | Y | Y | Y | Y | Y | Y | V |
| `poll.last.winner` | Y | Y | Y | Y | Y | Y | V |
| `poll.leading` | Y | Y | Y | Y | Y | Y | V |
| `poll.leading.percent` | Y | Y | Y | Y | Y | Y | V |
| `poll.leading.votes` | Y | Y | Y | Y | Y | Y | V |
| `poll.option` | Y | Y | Y | Y | Y | Y | V |
| `poll.option.percent` | Y | Y | Y | Y | Y | Y | V |
| `poll.option.votes` | Y | Y | Y | Y | Y | Y | V |
| `poll.options` | Y | Y | Y | Y | Y | Y | V |
| `poll.options.numbered` | Y | Y | Y | Y | Y | Y | V |
| `poll.question` | Y | Y | Y | Y | Y | Y | V |
| `poll.results` | N | Y | N | Y | Y | N | V |
| `poll.results.bars` | Y | Y | Y | Y | Y | Y | V |
| `poll.status` | N | Y | N | Y | N | N | V |
| `poll.timeLeft` | Y | Y | Y | Y | Y | Y | V |
| `poll.title` | N | Y | N | Y | Y | N | V |
| `poll.votes.total` | Y | Y | Y | Y | Y | Y | V |
| `poll.winner` | N | Y | N | Y | Y | N | V |
| `poll.winner.percentage` | N | Y | N | Y | N | N | V |
| `poll.winner.votes` | N | Y | N | Y | Y | N | V |

## prediction

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `prediction.id` | N | Y | N | Y | N | N | V |
| `prediction.isActive` | Y | Y | Y | Y | Y | Y | V |
| `prediction.last.winner` | Y | Y | Y | Y | Y | Y | V |
| `prediction.leading` | Y | Y | Y | Y | Y | Y | V |
| `prediction.leading.percent` | Y | Y | Y | Y | Y | Y | V |
| `prediction.leading.points` | Y | Y | Y | Y | Y | Y | V |
| `prediction.outcome` | Y | Y | Y | Y | Y | Y | V |
| `prediction.outcome.odds` | Y | Y | Y | Y | Y | Y | V |
| `prediction.outcome.percent` | Y | Y | Y | Y | Y | Y | V |
| `prediction.outcome.points` | Y | Y | Y | Y | Y | Y | V |
| `prediction.outcomes` | N | Y | N | Y | N | N | V |
| `prediction.outcomes.list` | Y | Y | Y | Y | Y | Y | V |
| `prediction.points.total` | Y | Y | Y | Y | Y | Y | V |
| `prediction.question` | Y | Y | Y | Y | Y | Y | V |
| `prediction.status` | N | Y | N | Y | N | N | V |
| `prediction.status.now` | Y | Y | Y | Y | Y | Y | V |
| `prediction.timeLeft` | Y | Y | Y | Y | Y | Y | V |
| `prediction.title` | N | Y | N | Y | Y | N | V |
| `prediction.topPredictor` | Y | Y | Y | Y | Y | Y | V |
| `prediction.total_points` | N | Y | N | Y | N | N | V |
| `prediction.users.total` | Y | Y | Y | Y | Y | Y | V |
| `prediction.window` | N | Y | N | Y | N | N | V |
| `prediction.winner` | N | Y | N | Y | Y | N | V |
| `prediction.winner.points` | N | Y | N | Y | Y | N | V |
| `prediction.winner.users` | N | Y | N | Y | Y | N | V |

## quote

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `quote.author` | Y | Y | Y | Y | Y | Y | V |
| `quote.count` | Y | Y | Y | Y | Y | Y | V |
| `quote.date` | Y | Y | Y | Y | Y | Y | V |
| `quote.game` | Y | Y | Y | Y | Y | Y | V |
| `quote.latest` | Y | Y | Y | Y | Y | Y | V |
| `quote.text` | Y | Y | Y | Y | Y | Y | V |

## random

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `random.bool` | Y | Y | Y | Y | Y | Y | Y |
| `random.chatter` | Y | Y | Y | Y | Y | Y | V |
| `random.chatter.mention` | Y | Y | Y | Y | Y | Y | V |
| `random.chatter.notMe` | Y | Y | Y | Y | Y | Y | V |
| `random.color` | Y | Y | Y | Y | Y | Y | Y |
| `random.decimal` | Y | Y | Y | Y | Y | Y | Y |
| `random.letter` | Y | Y | Y | Y | Y | Y | Y |
| `random.number` | Y | Y | Y | Y | Y | Y | Y |
| `random.number.<min>.<max>[.<step>]` | Y | Y | Y | Y | Y | Y | V |
| `random.number.<n>` | Y | Y | Y | Y | Y | Y | V |
| `random.percent` | Y | Y | Y | Y | Y | Y | Y |
| `random.pick.<a.b.c>` | Y | Y | Y | Y | Y | Y | V |
| `random.quote` | Y | Y | Y | Y | Y | Y | V |
| `random.user` | Y | Y | Y | Y | Y | Y | V |

## raw

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `raw.message` | N | N | N | N | Y | N | V |

## redemption

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `redemption.id` | N | Y | N | Y | N | N | V |

## reward

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `reward.id` | N | Y | N | Y | N | N | V |

## roll

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `roll.<name>` | Y | Y | Y | Y | Y | Y | V |
| `roll.<name>.<min>.<max>[.<step>]` | Y | Y | Y | Y | Y | Y | V |
| `roll.<name>.complement` | Y | Y | Y | Y | Y | Y | V |

## schedule

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `schedule.ical` | Y | Y | Y | Y | Y | Y | V |
| `schedule.link` | Y | Y | Y | Y | Y | Y | V |
| `schedule.list` | Y | Y | Y | Y | Y | Y | V |
| `schedule.next.at` | Y | Y | Y | Y | Y | Y | V |
| `schedule.next.date` | Y | Y | Y | Y | Y | Y | V |
| `schedule.next.duration` | Y | Y | Y | Y | Y | Y | V |
| `schedule.next.full` | Y | Y | Y | Y | Y | Y | V |
| `schedule.next.game` | Y | Y | Y | Y | Y | Y | V |
| `schedule.next.in` | Y | Y | Y | Y | Y | Y | V |
| `schedule.next.isRecurring` | Y | Y | Y | Y | Y | Y | V |
| `schedule.next.relative` | Y | Y | Y | Y | Y | Y | V |
| `schedule.next.title` | Y | Y | Y | Y | Y | Y | V |
| `schedule.next.viewerTime` | Y | Y | Y | Y | Y | Y | V |
| `schedule.today` | Y | Y | Y | Y | Y | Y | V |
| `schedule.vacation` | Y | Y | Y | Y | Y | Y | V |
| `schedule.vacation.until` | Y | Y | Y | Y | Y | Y | V |
| `schedule.week` | Y | Y | Y | Y | Y | Y | V |

## stream

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `stream.bits.top` | Y | Y | Y | Y | Y | Y | V |
| `stream.bits.total` | Y | Y | Y | Y | Y | Y | V |
| `stream.chatters.unique` | Y | Y | Y | Y | Y | Y | V |
| `stream.commandsRun` | Y | Y | Y | Y | Y | Y | V |
| `stream.count` | Y | Y | Y | Y | Y | Y | V |
| `stream.count.month` | Y | Y | Y | Y | Y | Y | V |
| `stream.followers.latest` | Y | Y | Y | Y | Y | Y | V |
| `stream.followers.new` | Y | Y | Y | Y | Y | Y | V |
| `stream.followers.newList` | Y | Y | Y | Y | Y | Y | V |
| `stream.game` | Y | Y | Y | Y | Y | Y | Y |
| `stream.game.boxart` | Y | Y | Y | Y | Y | Y | V |
| `stream.game.link` | Y | Y | Y | Y | Y | Y | V |
| `stream.game.short` | Y | Y | Y | Y | Y | Y | V |
| `stream.games.history` | Y | Y | Y | Y | Y | Y | V |
| `stream.hours.month` | Y | Y | Y | Y | Y | Y | V |
| `stream.hypetrains` | Y | Y | Y | Y | Y | Y | V |
| `stream.hypetrains.topLevel` | Y | Y | Y | Y | Y | Y | V |
| `stream.id` | Y | Y | Y | Y | Y | Y | V |
| `stream.isLive` | Y | Y | Y | Y | Y | Y | Y |
| `stream.last.duration` | Y | Y | Y | Y | Y | Y | V |
| `stream.last.endedAt` | Y | Y | Y | Y | Y | Y | V |
| `stream.last.followers` | Y | Y | Y | Y | Y | Y | V |
| `stream.last.game` | Y | Y | Y | Y | Y | Y | V |
| `stream.last.messages` | Y | Y | Y | Y | Y | Y | V |
| `stream.last.peak` | Y | Y | Y | Y | Y | Y | V |
| `stream.last.relative` | Y | Y | Y | Y | Y | Y | V |
| `stream.last.since` | Y | Y | Y | Y | Y | Y | V |
| `stream.last.subs` | Y | Y | Y | Y | Y | Y | V |
| `stream.last.title` | Y | Y | Y | Y | Y | Y | V |
| `stream.messages` | Y | Y | Y | Y | Y | Y | V |
| `stream.number` | Y | Y | Y | Y | Y | Y | V |
| `stream.raids.in` | Y | Y | Y | Y | Y | Y | V |
| `stream.raids.lastRaider` | Y | Y | Y | Y | Y | Y | V |
| `stream.raids.lastRaiderViewers` | Y | Y | Y | Y | Y | Y | V |
| `stream.redemptions` | Y | Y | Y | Y | Y | Y | V |
| `stream.songRequesters` | Y | Y | Y | Y | Y | Y | V |
| `stream.startedAt` | Y | Y | Y | Y | Y | Y | V |
| `stream.startedAt.local` | Y | Y | Y | Y | Y | Y | V |
| `stream.startedAt.relative` | Y | Y | Y | Y | Y | Y | V |
| `stream.status` | Y | Y | Y | Y | Y | Y | V |
| `stream.subs.gifted` | Y | Y | Y | Y | Y | Y | V |
| `stream.subs.new` | Y | Y | Y | Y | Y | Y | V |
| `stream.subs.resubs` | Y | Y | Y | Y | Y | Y | V |
| `stream.subs.selfSubs` | Y | Y | Y | Y | Y | Y | V |
| `stream.subs.topGifter` | Y | Y | Y | Y | Y | Y | V |
| `stream.subs.total` | Y | Y | Y | Y | Y | Y | V |
| `stream.thumbnail` | N | N | N | N | Y | N | V |
| `stream.title` | Y | Y | Y | Y | Y | Y | Y |
| `stream.title.short` | Y | Y | Y | Y | Y | Y | V |
| `stream.topChatter` | Y | Y | Y | Y | Y | Y | V |
| `stream.topChatters` | Y | Y | Y | Y | Y | Y | V |
| `stream.topCommand` | Y | Y | Y | Y | Y | Y | V |
| `stream.uptime` | Y | Y | Y | Y | Y | Y | Y |
| `stream.uptime.clock` | Y | Y | Y | Y | Y | Y | V |
| `stream.uptime.hours` | Y | Y | Y | Y | Y | Y | V |
| `stream.uptime.long` | Y | Y | Y | Y | Y | Y | V |
| `stream.uptime.minutes` | Y | Y | Y | Y | Y | Y | V |
| `stream.uptime.since` | Y | Y | Y | Y | Y | Y | V |
| `stream.url` | N | N | N | N | Y | N | V |
| `stream.viewers` | Y | Y | Y | Y | Y | Y | V |
| `stream.viewers.compact` | Y | Y | Y | Y | Y | Y | V |
| `stream.viewers.peak` | Y | Y | Y | Y | Y | Y | V |

## supporter

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `supporter.amount` | N | Y | N | Y | N | N | V |
| `supporter.currency` | N | Y | N | Y | N | N | V |
| `supporter.kind` | N | Y | N | Y | N | N | V |
| `supporter.message` | N | Y | N | Y | N | N | V |
| `supporter.name` | N | Y | N | Y | N | N | V |
| `supporter.quantity` | N | Y | N | Y | N | N | V |
| `supporter.tier` | N | Y | N | Y | N | N | V |

## switch

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `switch.range` | Y | Y | Y | Y | Y | Y | Y |

## target

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `target.accountAge` | Y | Y | U | Y | U | Y | V |
| `target.activity.level` | Y | Y | U | Y | U | Y | V |
| `target.avatar` | Y | Y | U | Y | U | Y | V |
| `target.avgperday` | Y | Y | U | Y | U | Y | V |
| `target.bio` | Y | Y | U | Y | U | Y | V |
| `target.botpercent` | Y | Y | U | Y | U | Y | V |
| `target.color` | Y | Y | U | Y | U | Y | V |
| `target.commands` | Y | Y | U | Y | U | Y | V |
| `target.createdAt` | Y | Y | U | Y | U | Y | V |
| `target.data.<key>` | Y | Y | U | Y | U | Y | V |
| `target.days` | Y | Y | U | Y | U | Y | V |
| `target.displayname` | Y | Y | U | Y | U | Y | V |
| `target.firstseen` | Y | Y | U | Y | U | Y | V |
| `target.followAge` | Y | Y | U | Y | U | Y | V |
| `target.game` | Y | Y | U | Y | U | Y | V |
| `target.genderedTerm` | Y | Y | U | Y | U | Y | V |
| `target.id` | Y | Y | U | Y | U | Y | V |
| `target.isFollower` | Y | Y | U | Y | U | Y | V |
| `target.isLive` | Y | Y | U | Y | U | Y | V |
| `target.isSub` | Y | Y | U | Y | U | Y | V |
| `target.known` | Y | Y | U | Y | U | Y | V |
| `target.lastmessage` | Y | Y | U | Y | U | Y | V |
| `target.lastmessage.full` | Y | Y | U | Y | U | Y | V |
| `target.link` | Y | Y | U | Y | U | Y | V |
| `target.mention` | Y | Y | U | Y | U | Y | V |
| `target.messages` | Y | Y | U | Y | U | Y | V |
| `target.name` | Y | Y | U | Y | U | Y | V |
| `target.object` | Y | Y | U | Y | U | Y | V |
| `target.points` | Y | Y | U | Y | U | Y | V |
| `target.points.rank` | Y | Y | U | Y | U | Y | V |
| `target.possessive` | Y | Y | U | Y | U | Y | V |
| `target.presentTense` | Y | Y | U | Y | U | Y | V |
| `target.randommessage` | Y | Y | U | Y | U | Y | V |
| `target.redemptions` | Y | Y | U | Y | U | Y | V |
| `target.shoutoutLink` | Y | Y | U | Y | U | Y | V |
| `target.songrequests` | Y | Y | U | Y | U | Y | V |
| `target.stats.bits` | Y | Y | U | Y | U | Y | V |
| `target.stats.lastSeen` | Y | Y | U | Y | U | Y | V |
| `target.stats.streak` | Y | Y | U | Y | U | Y | V |
| `target.subject` | Y | Y | U | Y | U | Y | V |
| `target.tense` | Y | Y | U | Y | U | Y | V |
| `target.title` | Y | Y | U | Y | U | Y | V |
| `target.verb:<sing\|plur>` | Y | Y | U | Y | U | Y | V |
| `target.viewers` | Y | Y | U | Y | U | Y | V |
| `target.watchtime` | Y | Y | U | Y | U | Y | V |

## text

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `text.alternating` | Y | Y | Y | Y | Y | Y | Y |
| `text.article` | Y | Y | Y | Y | Y | Y | Y |
| `text.base64.decode` | Y | Y | Y | Y | Y | Y | Y |
| `text.base64.encode` | Y | Y | Y | Y | Y | Y | Y |
| `text.bold` | Y | Y | Y | Y | Y | Y | Y |
| `text.bubble` | Y | Y | Y | Y | Y | Y | Y |
| `text.camel` | Y | Y | Y | Y | Y | Y | Y |
| `text.capitalize` | Y | Y | Y | Y | Y | Y | Y |
| `text.censor` | Y | Y | Y | Y | Y | Y | Y |
| `text.center` | Y | Y | Y | Y | Y | Y | Y |
| `text.chars.count` | Y | Y | Y | Y | Y | Y | Y |
| `text.chars.shuffle` | Y | Y | Y | Y | Y | Y | Y |
| `text.clap` | Y | Y | Y | Y | Y | Y | Y |
| `text.count` | Y | Y | Y | Y | Y | Y | Y |
| `text.cut` | Y | Y | Y | Y | Y | Y | Y |
| `text.cut.words` | Y | Y | Y | Y | Y | Y | Y |
| `text.decapitalize` | Y | Y | Y | Y | Y | Y | Y |
| `text.digits` | Y | Y | Y | Y | Y | Y | Y |
| `text.escape.chat` | Y | Y | Y | Y | Y | Y | Y |
| `text.first` | Y | Y | Y | Y | Y | Y | Y |
| `text.fullwidth` | Y | Y | Y | Y | Y | Y | Y |
| `text.hashtag` | Y | Y | Y | Y | Y | Y | Y |
| `text.html.strip` | Y | Y | Y | Y | Y | Y | Y |
| `text.initials` | Y | Y | Y | Y | Y | Y | Y |
| `text.italic` | Y | Y | Y | Y | Y | Y | Y |
| `text.join.and` | Y | Y | Y | Y | Y | Y | Y |
| `text.join.or` | Y | Y | Y | Y | Y | Y | Y |
| `text.kebab` | Y | Y | Y | Y | Y | Y | Y |
| `text.last` | Y | Y | Y | Y | Y | Y | Y |
| `text.leet` | Y | Y | Y | Y | Y | Y | Y |
| `text.length.fit` | Y | Y | Y | Y | Y | Y | Y |
| `text.letters` | Y | Y | Y | Y | Y | Y | Y |
| `text.letters.count` | Y | Y | Y | Y | Y | Y | Y |
| `text.lines.count` | Y | Y | Y | Y | Y | Y | Y |
| `text.lines.first` | Y | Y | Y | Y | Y | Y | Y |
| `text.lines.join` | Y | Y | Y | Y | Y | Y | Y |
| `text.lines.wrap` | Y | Y | Y | Y | Y | Y | Y |
| `text.links.strip` | Y | Y | Y | Y | Y | Y | Y |
| `text.lower` | Y | Y | Y | Y | Y | Y | Y |
| `text.mask` | Y | Y | Y | Y | Y | Y | Y |
| `text.mentions.strip` | Y | Y | Y | Y | Y | Y | Y |
| `text.mocking` | Y | Y | Y | Y | Y | Y | Y |
| `text.monospace` | Y | Y | Y | Y | Y | Y | Y |
| `text.pad.left` | Y | Y | Y | Y | Y | Y | Y |
| `text.pad.right` | Y | Y | Y | Y | Y | Y | Y |
| `text.paren` | Y | Y | Y | Y | Y | Y | Y |
| `text.pascal` | Y | Y | Y | Y | Y | Y | Y |
| `text.path.encode` | Y | Y | Y | Y | Y | Y | Y |
| `text.plain` | Y | Y | Y | Y | Y | Y | Y |
| `text.plural` | Y | Y | Y | Y | Y | Y | Y |
| `text.plural.auto` | Y | Y | Y | Y | Y | Y | Y |
| `text.plural.count` | Y | Y | Y | Y | Y | Y | Y |
| `text.possessive` | Y | Y | Y | Y | Y | Y | Y |
| `text.query.encode` | Y | Y | Y | Y | Y | Y | Y |
| `text.quote` | Y | Y | Y | Y | Y | Y | Y |
| `text.remove` | Y | Y | Y | Y | Y | Y | Y |
| `text.repeat` | Y | Y | Y | Y | Y | Y | Y |
| `text.replace` | Y | Y | Y | Y | Y | Y | Y |
| `text.replace.first` | Y | Y | Y | Y | Y | Y | Y |
| `text.replace.icase` | Y | Y | Y | Y | Y | Y | Y |
| `text.reverse` | Y | Y | Y | Y | Y | Y | Y |
| `text.reverse.words` | Y | Y | Y | Y | Y | Y | Y |
| `text.rot13` | Y | Y | Y | Y | Y | Y | Y |
| `text.script` | Y | Y | Y | Y | Y | Y | Y |
| `text.sentence` | Y | Y | Y | Y | Y | Y | Y |
| `text.singular` | Y | Y | Y | Y | Y | Y | Y |
| `text.slug` | Y | Y | Y | Y | Y | Y | Y |
| `text.smallcaps` | Y | Y | Y | Y | Y | Y | Y |
| `text.snake` | Y | Y | Y | Y | Y | Y | Y |
| `text.spaced` | Y | Y | Y | Y | Y | Y | Y |
| `text.spongecase` | Y | Y | Y | Y | Y | Y | Y |
| `text.squared` | Y | Y | Y | Y | Y | Y | Y |
| `text.squish` | Y | Y | Y | Y | Y | Y | Y |
| `text.strike` | Y | Y | Y | Y | Y | Y | Y |
| `text.stutter` | Y | Y | Y | Y | Y | Y | Y |
| `text.substring` | Y | Y | Y | Y | Y | Y | Y |
| `text.superscript` | Y | Y | Y | Y | Y | Y | Y |
| `text.swapcase` | Y | Y | Y | Y | Y | Y | Y |
| `text.title` | Y | Y | Y | Y | Y | Y | Y |
| `text.trim` | Y | Y | Y | Y | Y | Y | Y |
| `text.truncate` | Y | Y | Y | Y | Y | Y | Y |
| `text.underline` | Y | Y | Y | Y | Y | Y | Y |
| `text.upper` | Y | Y | Y | Y | Y | Y | Y |
| `text.upsidedown` | Y | Y | Y | Y | Y | Y | Y |
| `text.url.decode` | Y | Y | Y | Y | Y | Y | Y |
| `text.url.encode` | Y | Y | Y | Y | Y | Y | Y |
| `text.uwu` | Y | Y | Y | Y | Y | Y | Y |
| `text.vowels.remove` | Y | Y | Y | Y | Y | Y | Y |
| `text.word` | Y | Y | Y | Y | Y | Y | Y |
| `text.word.first` | Y | Y | Y | Y | Y | Y | Y |
| `text.word.last` | Y | Y | Y | Y | Y | Y | Y |
| `text.word.longest` | Y | Y | Y | Y | Y | Y | Y |
| `text.word.random` | Y | Y | Y | Y | Y | Y | Y |
| `text.words.count` | Y | Y | Y | Y | Y | Y | Y |
| `text.words.first` | Y | Y | Y | Y | Y | Y | Y |
| `text.words.from` | Y | Y | Y | Y | Y | Y | Y |
| `text.words.last` | Y | Y | Y | Y | Y | Y | Y |
| `text.words.range` | Y | Y | Y | Y | Y | Y | Y |
| `text.words.shuffle` | Y | Y | Y | Y | Y | Y | Y |
| `text.words.sort` | Y | Y | Y | Y | Y | Y | Y |
| `text.words.unique` | Y | Y | Y | Y | Y | Y | Y |
| `text.wrap` | Y | Y | Y | Y | Y | Y | Y |
| `text.yell` | Y | Y | Y | Y | Y | Y | Y |

## time

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `time.utc` | Y | Y | Y | Y | Y | Y | V |

## transform

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `transform.<name>:<text>` | Y | Y | Y | Y | Y | Y | V |

## tts

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `tts.audioconnected` | Y | Y | Y | Y | Y | Y | V |
| `tts.lexicon` | Y | Y | Y | Y | Y | Y | V |
| `tts.numbers` | Y | Y | Y | Y | Y | Y | V |
| `tts.plain` | Y | Y | Y | Y | Y | Y | V |
| `tts.plain.keepEmotes` | Y | Y | Y | Y | Y | Y | V |

## user

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `user.accountAge` | Y | Y | U | Y | U | Y | Y |
| `user.accountAge.days` | Y | Y | U | Y | U | Y | V |
| `user.activity.level` | Y | Y | U | Y | U | Y | V |
| `user.avatar` | Y | Y | U | Y | U | Y | V |
| `user.bio` | Y | Y | U | Y | U | Y | V |
| `user.color` | Y | Y | U | Y | U | Y | V |
| `user.createdAt` | Y | Y | U | Y | U | Y | V |
| `user.data.<key>` | Y | Y | U | Y | U | Y | V |
| `user.display` | Y | Y | U | Y | U | Y | V |
| `user.followAge` | Y | Y | U | Y | U | Y | Y |
| `user.followAge.days` | Y | Y | U | Y | U | Y | V |
| `user.followedAt` | Y | Y | U | Y | U | Y | V |
| `user.genderedTerm` | Y | Y | U | Y | U | Y | Y |
| `user.id` | Y | Y | U | Y | Y | Y | Y |
| `user.isFollower` | Y | Y | U | Y | U | Y | V |
| `user.isSub` | Y | Y | U | Y | U | Y | V |
| `user.lastmessage` | Y | Y | U | Y | U | Y | V |
| `user.link` | Y | Y | U | Y | U | Y | Y |
| `user.login` | Y | Y | U | Y | U | Y | V |
| `user.mention` | Y | Y | U | Y | U | Y | V |
| `user.messageCount` | Y | Y | U | Y | U | Y | Y |
| `user.name` | Y | Y | U | Y | Y | Y | Y |
| `user.object` | Y | Y | U | Y | U | Y | Y |
| `user.platform` | Y | Y | U | Y | U | Y | V |
| `user.platform.name` | Y | Y | U | Y | U | Y | V |
| `user.points` | Y | Y | U | Y | U | Y | V |
| `user.points.name` | Y | Y | U | Y | U | Y | V |
| `user.points.nextRank.gap` | Y | Y | U | Y | U | Y | V |
| `user.points.nextRank.name` | Y | Y | U | Y | U | Y | V |
| `user.points.rank` | Y | Y | U | Y | U | Y | V |
| `user.points.rank.ordinal` | Y | Y | U | Y | U | Y | V |
| `user.points.raw` | Y | Y | U | Y | U | Y | V |
| `user.possessive` | Y | Y | U | Y | U | Y | Y |
| `user.presentTense` | Y | Y | U | Y | U | Y | Y |
| `user.pronouns` | Y | Y | U | Y | U | Y | Y |
| `user.provider` | Y | Y | U | Y | U | Y | V |
| `user.quotes.count` | Y | Y | U | Y | U | Y | V |
| `user.stats.bits` | Y | Y | U | Y | U | Y | V |
| `user.stats.firstSeen.relative` | Y | Y | U | Y | U | Y | V |
| `user.stats.lastSeen` | Y | Y | U | Y | U | Y | V |
| `user.stats.lastSeen.relative` | Y | Y | U | Y | U | Y | V |
| `user.stats.streak` | Y | Y | U | Y | U | Y | V |
| `user.stats.streak.best` | Y | Y | U | Y | U | Y | V |
| `user.stats.watchTime.hours` | Y | Y | U | Y | U | Y | V |
| `user.stats.watchTime.long` | Y | Y | U | Y | U | Y | V |
| `user.subTier` | Y | Y | U | Y | U | Y | V |
| `user.subTier.name` | Y | Y | U | Y | U | Y | V |
| `user.subject` | Y | Y | U | Y | U | Y | Y |
| `user.tense` | Y | Y | U | Y | U | Y | Y |
| `user.verb:<sing\|plur>` | Y | Y | U | Y | U | Y | V |

## var

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `var.<name>` | Y | Y | Y | Y | Y | Y | V |

## viewer

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `viewer.avgperday` | Y | Y | N | Y | N | Y | V |
| `viewer.botpercent` | Y | Y | N | Y | N | Y | V |
| `viewer.commands` | Y | Y | N | Y | N | Y | V |
| `viewer.data.<key>` | Y | Y | N | Y | N | Y | V |
| `viewer.days` | Y | Y | N | Y | N | Y | V |
| `viewer.firstseen` | Y | Y | N | Y | N | Y | V |
| `viewer.messages` | Y | Y | N | Y | N | Y | V |
| `viewer.name` | N | Y | N | Y | N | N | V |
| `viewer.redemptions` | Y | Y | N | Y | N | Y | V |
| `viewer.songrequests` | Y | Y | N | Y | N | Y | V |
| `viewer.watchtime` | Y | Y | N | Y | N | Y | V |

## vts

| Helper | Commands | Event responses | Timers | Pipelines | Discord notifications | Webhooks | Viewer text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `vts.event.<field>` | N | Y | N | Y | N | N | V |

## Legend

| Code | Why the helper is absent |
| --- | --- |
| `A` | it needs a chat command, and only commands and pipelines carry one |
| `U` | it needs a trigger that carries a viewer or a chat message, and timers and Discord notifications have none |
| `P` | it needs an originating platform event, and scheduled timers have none |
| `M` | it needs a triggering chat message, and scheduled timers have none |
| `Vw` | it needs a viewer identity, which scheduled timers and go-live posts do not carry |
| `V` | viewer text allows only the helpers whose registry row lists it, and this row does not |
| `N` | the registry row of this helper does not list this surface, and no general rule explains it |

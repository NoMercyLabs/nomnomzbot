// nnz.d.ts — generated from the NomNomzBot Event Catalog (dev-platform.md §2). DO NOT EDIT.
// context: widget
// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0

/** THE canonical chat fact — published for every chat message the bot ingests on any platform: Twitch (EventSub channel.chat.message) and YouTube (Live Chat poller); Provider names the source. This is the HOT PATH event — handlers must be fast, and Twitch-only consumers (command replies, auto-mod enforcement, pronouns, decoration) gate on Provider. */
interface NnzChatMessageReceived {
  /** The unique id of this chat message. */
  messageId: string;
  /** The platform this message arrived on — Platform key (the vocabulary shared with Channel.Provider / UserIdentity.Provider). Defaults to Twitch, the dominant source. */
  provider: string;
  /** The id of the channel owner on the platform the message came from (a Twitch user id is a number as text). */
  twitchBroadcasterId: string;
  /** The id of the viewer who wrote the message on that platform (a Twitch user id is a number as text). */
  userId: string;
  /** The name of the viewer as shown in chat. */
  userDisplayName: string;
  /** The login name of the viewer, in lowercase. */
  userLogin: string;
  /** Raw plain-text content (concatenation of all fragment texts). */
  message: string;
  /** Structured fragments from EventSub: text, emote, cheermote, mention. Enables inline emote rendering, colored mentions, animated cheermotes. */
  fragments: NnzChatMessageFragment[];
  /** User's chat color as #RRGGBB hex (or null if unset). */
  colorHex?: string | null;
  /** Message type from EventSub: "text" | "channel_points_highlighted" | "channel_points_sub_only" | "user_intro" | "power_ups_message_effect" | "power_ups_gigantified_emote" */
  messageType: string;
  /** Parsed badges with their set ID, badge ID, and info field. */
  badges: NnzChatBadge[];
  /** True when the viewer is a subscriber of the channel. */
  isSubscriber: boolean;
  /** True when the viewer is a VIP of the channel. */
  isVip: boolean;
  /** True when Twitch's artist-badge is on the message: the channel named the chatter an artist. */
  isArtist: boolean;
  /** True when the viewer is a moderator of the channel. */
  isModerator: boolean;
  /** True when the viewer is the channel owner. */
  isBroadcaster: boolean;
  /** True when YouTube marks the author's channel as verified (authorDetails.isVerified); false on every other platform. */
  isYouTubeChannelVerified: boolean;
  /** Bits cheered in this message, or 0. */
  bits: number;
  /** If this is a reply, the parent message ID. */
  replyParentMessageId?: string | null;
  /** The parent reply message text (for display in the UI thread). */
  replyParentMessageBody?: string | null;
  /** Display name of the user being replied to. */
  replyParentUserName?: string | null;
  /** Login of the user being replied to; Twitch prefixes a reply's text with "@" plus this or the display name. */
  replyParentUserLogin?: string | null;
  /** Display name of the user who started the reply thread (Twitch thread_user_name). For a reply to the bot's own answer, this is the viewer the bot answered. */
  replyThreadUserName?: string | null;
}

/** Published when a channel's stream goes offline (EventSub stream.offline). */
interface NnzChannelOffline {
  /** The platform the channel went offline on: twitch or kick. */
  provider: string;
  /** The name of the channel owner as shown in chat. */
  broadcasterDisplayName: string;
  /** How long the stream ran, from the moment the channel went live to this offline signal. Neither Twitch nor Kick sends a duration, so it is computed from the channel's recorded go-live time. It is zero when that time is unknown, for example when the stream started while the bot was down and no live check has run yet. */
  streamDuration: string;
  /** When the stream that just ended went live: the stream's own identity, the same instant the matching StartedAt carried. Null when the go-live time is unknown. */
  streamStartedAt?: string | null;
}

/** Published when a channel's stream goes online (EventSub stream.online). */
interface NnzChannelOnline {
  /** The platform the channel went live on: twitch or kick. */
  provider: string;
  /** The name of the channel owner as shown in chat. */
  broadcasterDisplayName: string;
  /** The title of the stream when it started. */
  streamTitle: string;
  /** The name of the game or category when the stream started. */
  gameName: string;
  /** When the stream started, in UTC time. */
  startedAt: string;
}

/** AvatarUrl/Pronouns/CommunityStanding are additive hub-broadcast-layer enrichment (IHubUserEnricher) — null when the viewer has no internal User row yet, no avatar/pronouns on file, or no recorded standing in this channel. */
interface NnzFollowAlertDto {
  userId: string;
  displayName: string;
  login: string;
  followedAt?: string | null;
  avatarUrl?: string | null;
  pronouns?: string | null;
  communityStanding?: string | null;
  /** Canonical subject name — see the vocabulary note above. */
  user: string;
}

interface NnzSubscriptionAlertDto {
  userId: string;
  displayName: string;
  tier: string;
  /** Canonical subject name — see the vocabulary note above. */
  user: string;
}

interface NnzResubAlertDto {
  userId: string;
  displayName: string;
  tier: string;
  months: number;
  streak: number;
  message?: string | null;
  /** Canonical subject name — see the vocabulary note above. */
  user: string;
}

interface NnzGiftSubAlertDto {
  gifterId?: string | null;
  gifterDisplayName: string;
  tier: string;
  count: number;
  anonymous: boolean;
  /** Canonical subject name — the GIFTER, the only identity a gift-sub event carries. */
  user: string;
  /** Canonical headline scalar — how many subs were gifted. */
  amount: number;
}

interface NnzCheerAlertDto {
  userId?: string | null;
  displayName: string;
  bits: number;
  message: string;
  anonymous: boolean;
  imageUrl?: string | null;
  /** Canonical subject name — see the vocabulary note above. */
  user: string;
  /** Canonical headline scalar — the bits cheered. */
  amount: number;
}

interface NnzRaidAlertDto {
  fromUserId: string;
  fromDisplayName: string;
  fromLogin: string;
  viewerCount: number;
  /** Canonical subject name — the raiding channel. */
  user: string;
  /** Canonical headline scalar — the size of the incoming raid party. */
  viewers: number;
}

/** The answer to stream-stats: the stream window plus one named property per requested metric key. A metric that was not requested (or has nothing to show, such as the peak of a stream that never recorded one) is omitted. A new metric adds a property here and a class implementing IStreamMetric; never an endpoint. */
interface NnzStreamStatsDto {
  /** The stream these numbers belong to. */
  stream: NnzStreamStatsWindowDto;
  /** Who streamed: the channel's display name and profile picture, for headers and watermarks. */
  channel?: NnzStreamStatsChannelDto | null;
  /** Seconds from the start of the stream to its end (or to now while live). */
  duration?: number | null;
  /** The highest viewer count the stream recorded. */
  peakViewers?: number | null;
  /** New followers during the stream. */
  followers?: number | null;
  /** Display names of the viewers who followed during the stream, oldest first. */
  newFollowers?: string[] | null;
  /** Subscriptions during the stream: new, resubscribed, gifted and the total. */
  subs?: NnzStreamStatsSubsDto | null;
  /** Gifters by gift count; anonymous gifts are merged as "Anonymous". */
  gifters?: NnzStreamStatsNamedCountDto[] | null;
  /** Viewers who subscribed or resubscribed themselves (gift recipients excluded), in order. */
  selfSubs?: string[] | null;
  /** Total bits and the top cheerers (count = bits). */
  bits?: NnzStreamStatsRankedDto | null;
  /** Raids that arrived during the stream: who raided and with how many viewers. */
  raidsIn?: NnzStreamStatsRaidDto[] | null;
  /** The hype trains that ended during the stream. Absent when there was none. */
  hypeTrain?: NnzStreamStatsHypeTrainDto | null;
  /** Redemption total and the top reward titles. */
  redemptions?: NnzStreamStatsRankedDto | null;
  /** Song request total and the top requesters; never song titles. */
  songRequesters?: NnzStreamStatsRankedDto | null;
  /** Chat message count and the number of different chatters. */
  chat?: NnzStreamStatsChatDto | null;
  /** The chatters with the most messages. */
  topChatters?: NnzStreamStatsNamedCountDto[] | null;
  /** Commands run in the window and the most-used ones. */
  commandsRun?: NnzStreamStatsRankedDto | null;
}

/** TargetDisplayName/TargetAvatarUrl/TargetPronouns/ TargetCommunityStanding are additive hub-broadcast-layer enrichment (IHubUserEnricher) for the moderated viewer — null when unavailable. ModeratorDisplayName comes straight off the originating domain event's own ModeratorDisplayName field (moderator_user_name on Twitch ingests) — no extra lookup. Timestamp is the originating event's OccurredAt. BroadcasterId is the channel the action happened in, so a client that watches several channels marks only that channel's lines; blank on payloads built without a channel (widget test samples). */
interface NnzModActionDto {
  action: string;
  moderatorId: string;
  targetUserId: string;
  reason?: string | null;
  durationSeconds?: number | null;
  targetDisplayName?: string | null;
  targetAvatarUrl?: string | null;
  targetPronouns?: string | null;
  targetCommunityStanding?: string | null;
  moderatorDisplayName?: string | null;
  timestamp: string;
  broadcasterId: string;
}

/** Rich chat message DTO sent to dashboard/overlay clients via SignalR. Includes structured fragments for inline emote, mention, and cheermote rendering; field names match the frontend ChatMessagePayload type exactly. Message is the raw plain-text fallback for clients that don't render fragments; Fragments are the structured text/emote/cheermote/mention pieces; UserType is the derived role (broadcaster | moderator | vip | subscriber | viewer); Color is the user's chat color (#RRGGBB); MessageType is text | channel_points_highlighted | channel_points_sub_only | user_intro; AvatarUrl and Pronouns are additive hub-broadcast-layer enrichment (IHubUserEnricher), null when unavailable. */
interface NnzDashboardChatMessageDto {
  id: string;
  channelId: string;
  userId: string;
  displayName: string;
  username: string;
  message: string;
  fragments: NnzChatFragmentDto[];
  userType: string;
  isSubscriber: boolean;
  isVip: boolean;
  isModerator: boolean;
  isBroadcaster: boolean;
  isCheer: boolean;
  isCommand: boolean;
  badges: NnzChatBadgeDto[];
  bitsAmount: number;
  color?: string | null;
  messageType: string;
  replyToMessageId?: string | null;
  replyParentMessageBody?: string | null;
  replyParentUserName?: string | null;
  timestamp: string;
  avatarUrl?: string | null;
  pronouns?: string | null;
  provider: string;
  paint?: NnzChatPaintDto | null;
  isArtist: boolean;
  overlayLine?: NnzOverlayLineDto | null;
}

interface NnzChatClearedDto {
  clearedByUserId: string;
}

interface NnzMessageDeletedDto {
  messageId: string;
  deletedByUserId: string;
  targetUserId: string;
  deletedByDisplayName: string;
}

interface NnzUserMessagesClearedDto {
  targetUserId: string;
  targetUserDisplayName: string;
  targetUserLogin: string;
}

/** ChatMessageEnriched: the link preview resolved for a chat message after it was shown. */
interface NnzChatMessageEnrichedWidgetPayload {
  messageId: string;
  linkUrl?: string | null;
  title?: string | null;
  description?: string | null;
  imageUrl?: string | null;
  provider?: string | null;
  userDisplayName?: string | null;
  userLogin?: string | null;
}

interface NnzHypeTrainBeganAlertDto {
  hypeTrainId: string;
  level: number;
  total: number;
  progress: number;
  goal: number;
  topContributions: NnzHypeTrainContributionDto[];
  expiresAt: string;
}

interface NnzHypeTrainProgressAlertDto {
  hypeTrainId: string;
  level: number;
  total: number;
  progress: number;
  goal: number;
  topContributions: NnzHypeTrainContributionDto[];
  expiresAt: string;
}

interface NnzHypeTrainEndedAlertDto {
  hypeTrainId: string;
  level: number;
  total: number;
  topContributions: NnzHypeTrainContributionDto[];
  endedAt: string;
}

interface NnzPollBeganAlertDto {
  pollId: string;
  title: string;
  choices: NnzPollChoiceDto[];
  durationSeconds: number;
  endsAt: string;
}

interface NnzPollProgressAlertDto {
  pollId: string;
  title: string;
  choices: NnzPollChoiceDto[];
  endsAt: string;
}

interface NnzPollEndedAlertDto {
  pollId: string;
  title: string;
  status: string;
  choices: NnzPollChoiceDto[];
  winningChoiceId?: string | null;
}

interface NnzPredictionBeganAlertDto {
  predictionId: string;
  title: string;
  outcomes: NnzPredictionOutcomeDto[];
  windowSeconds: number;
  locksAt: string;
}

interface NnzPredictionProgressAlertDto {
  predictionId: string;
  title: string;
  outcomes: NnzPredictionOutcomeDto[];
  locksAt: string;
}

interface NnzPredictionLockedAlertDto {
  predictionId: string;
  title: string;
  outcomes: NnzPredictionOutcomeDto[];
}

interface NnzPredictionEndedAlertDto {
  predictionId: string;
  title: string;
  status: string;
  outcomes: NnzPredictionOutcomeDto[];
  winningOutcomeId?: string | null;
}

/** AvatarUrl/Pronouns/CommunityStanding are additive hub-broadcast-layer enrichment (IHubUserEnricher) for the redeeming viewer — null when unavailable. */
interface NnzRewardRedeemedDto {
  broadcasterId: string;
  rewardId: string;
  rewardTitle: string;
  redemptionId: string;
  userId: string;
  userDisplayName: string;
  cost: number;
  userInput?: string | null;
  timestamp: string;
  avatarUrl?: string | null;
  pronouns?: string | null;
  communityStanding?: string | null;
  eventId?: string | null;
}

/** Shared shape for moderator and VIP role grants/revocations (identical fields on all four events). AvatarUrl/Pronouns/CommunityStanding are additive hub-broadcast-layer enrichment (IHubUserEnricher). */
interface NnzRoleChangedAlertDto {
  userId: string;
  userDisplayName: string;
  userLogin: string;
  avatarUrl?: string | null;
  pronouns?: string | null;
  communityStanding?: string | null;
}

interface NnzShoutoutSentAlertDto {
  toUserId: string;
  toDisplayName: string;
}

/** AvatarUrl/Pronouns/CommunityStanding are additive hub-broadcast-layer enrichment (IHubUserEnricher), keyed off the shouting-out broadcaster's Twitch id — usually null since that broadcaster is rarely also a recorded viewer of this channel. */
interface NnzShoutoutReceivedAlertDto {
  fromBroadcasterId: string;
  fromBroadcasterDisplayName: string;
  fromBroadcasterLogin: string;
  viewerCount: number;
  avatarUrl?: string | null;
  pronouns?: string | null;
  communityStanding?: string | null;
}

/** sr_queue: the whole song-request queue, in play order. */
interface NnzSrQueueWidgetPayload {
  items: NnzSongRequestQueueSnapshotItem[];
}

/** media_share_command: what the server tells the media-share player to do. Command is one of load | play | pause | stop | seek | volume; the item fields (RequestId … DurationSeconds) are set only for load, PositionSeconds for seek, Volume for volume (0-100). */
interface NnzMediaShareCommandWidgetPayload {
  command: string;
  requestId?: string | null;
  sourceType?: string | null;
  mediaRef?: string | null;
  sourceUrl?: string | null;
  title?: string | null;
  durationSeconds?: number | null;
  positionSeconds?: number | null;
  volume?: number | null;
}

/** now_playing: the standing music snapshot. RequestedBy is null when the streamer started the track themselves. */
interface NnzNowPlayingWidgetPayload {
  isPlaying: boolean;
  track?: string | null;
  artist?: string | null;
  artUrl?: string | null;
  provider?: string | null;
  trackUri?: string | null;
  durationMs: number;
  progressMs: number;
  observedAt: string;
  requestedBy?: string | null;
  requestedByColor?: string | null;
}

/** The wire form of the one now-playing shape (music-playback-determinism.md section 2.1), pushed as the hub event music.nowPlaying. Engine is "spotify" or "youtube"; every other field is filled the same way for both engines. State is the camelCase director state: idle, starting, playing, pausedByOwner or blocked. */
interface NnzNowPlayingShapeDto {
  engine: string;
  itemId?: string | null;
  title: string;
  artists: string[];
  albumArtUrl?: string | null;
  durationMs: number;
  progressMs: number;
  isPlaying: boolean;
  requestedBy?: string | null;
  volume: number;
  muted: boolean;
  state: string;
  blockedReason?: string | null;
}

/** ad_schedule: the channel's ad schedule, sent on every poll while live (times are UTC, null when none). */
interface NnzAdScheduleWidgetPayload {
  nextAdAt?: string | null;
  lastAdAt?: string | null;
  durationSeconds: number;
  prerollFreeTimeSeconds: number;
  snoozeCount: number;
  snoozeRefreshAt?: string | null;
  timeUntilNextAdSeconds?: number | null;
}

/** ad_upcoming: an ad is near; sent once per warn threshold (300, 120, 60, 30 and 10 seconds) for each next-ad slot. */
interface NnzAdUpcomingWidgetPayload {
  secondsUntilAd: number;
  thresholdSeconds: number;
  durationSeconds: number;
  nextAdAt: string;
}

/** youtube.play: the overlay player loads and plays this video now. */
interface NnzYouTubePlayWidgetPayload {
  videoId: string;
  trackUri: string;
}

/** youtube.pause / youtube.resume / youtube.stop: the overlay player pauses, resumes or stops (and reports ENDED); no fields. */
interface NnzYouTubeTransportWidgetPayload {
}

/** youtube.seek: the overlay player jumps to this position, in milliseconds, in the video it plays. */
interface NnzYouTubeSeekWidgetPayload {
  positionMs: number;
}

/** youtube.volume: the overlay player plays at this volume (0-100) from now on, for this video and the ones after it. */
interface NnzYouTubeVolumeWidgetPayload {
  percent: number;
}

/** track_saved_changed: the current track was liked or unliked. */
interface NnzTrackSavedWidgetPayload {
  trackUri: string;
  track?: string | null;
  artist?: string | null;
  isSaved: boolean;
}

/** tts_speak: one dispatched utterance. User is the requester's platform user id. */
interface NnzTtsSpeakWidgetPayload {
  text: string;
  voice: string;
  user: string;
  durationMs: number;
  audioUrl?: string | null;
}

/** stage_effect: one effect for the stage widget to play. Text is plain text the widget sets through a text node, never markup. Intensity and Color are null when the caller left them out, and the widget then uses its own channel settings. */
interface NnzStageEffectPayload {
  preset: string;
  text?: string | null;
  durationMs: number;
  intensity?: number | null;
  color?: string | null;
}

/** The wire shape `goal_bar.vue`/`labels.vue`'s nnz.on('goal', ...) handlers expect (widget-quality-audit §1): the authoritative absolute value for one metric, not a delta. */
interface NnzGoalWidgetEventPayload {
  metric: string;
  value: number;
  target: number;
}

/** The wire shape of the count widget event: the authoritative absolute total of one metric (followers or subs), sent as a seed after an overlay reload. It is not a goal, so it never moves a goal bar; labels.vue sets its running count to Value. */
interface NnzCountWidgetEventPayload {
  metric: string;
  value: number;
}

/** The real wire shape a supporter.tip/supporter.membership/supporter.merch/ supporter.charity widget event carries — every field SupporterEventReceived actually publishes, reduced to what an overlay needs (no internal ids). Amounts stay in minor units (cents) so the widget, not this payload, decides how to format them. */
interface NnzSupporterAlertPayload {
  kind: string;
  supporterDisplayName: string;
  amountMinor?: number | null;
  currency?: string | null;
  tier?: string | null;
  quantity?: number | null;
  messageText?: string | null;
  isRecurring: boolean;
  /** Canonical subject name, matching the widget-facing vocabulary every alert payload exposes (AlertDtos.cs) — so a widget reads data.user for a supporter event exactly as it does for a raid or a follow, instead of having to know this payload spells it supporterDisplayName. */
  user: string;
  /** Canonical headline scalar — the supported amount in major units, null when the kind carries none. */
  amount?: number | null;
}

/** The wire shape the Alert widget's voice_trigger card reads: word + live count + sticker image. */
interface NnzVoiceTriggerWidgetEventPayload {
  word: string;
  count: number;
  stickerImageUrl?: string | null;
}

/** The lobby opened. Each game adds the setting its widget draws: crash the cap, drop the target, heist the odds. */
interface NnzGameRoundOpenFrame {
  kind: 'round_open';
  lobbySeconds: number;
  maxMultiplier?: number | null;
  target?: number | null;
  radius?: number | null;
  successChance?: number | null;
}

/** A player bought in. Crash adds the multiplier they ride from; heist the crew so far and its odds; raffle the pot and the entrants so far. */
interface NnzGameJoinFrame {
  kind: 'join';
  player: string;
  stake: number;
  entry?: number | null;
  /** Crash only: lobby or running, the phase the player joined in. */
  phase?: string | null;
  crewSize?: number | null;
  successChance?: number | null;
  crew?: NnzGameRosterEntry[] | null;
  pot?: number | null;
  entrants?: NnzGameRosterEntry[] | null;
}

/** Crash's running multiplier: a tick (progress), the bust, or the cap being reached. */
interface NnzGameMultiplierFrame {
  kind: 'progress' | 'bust' | 'cap';
  multiplier: number;
}

/** A crash player cashed out at the current multiplier. */
interface NnzGameCashoutFrame {
  kind: 'cashout';
  player: string;
  multiplier: number;
  payout: number;
  crewSize: number;
}

/** A drop-game player landed their marker. */
interface NnzGameDropFrame {
  kind: 'drop';
  player: string;
  landed: number;
  distance: number;
  hit: boolean;
}

/** The round resolved. Each game adds its own summary next to results: crash where it crashed, drop the target and radius, heist the odds and crew size, raffle the pot and the winner. */
interface NnzGameResultsFrame {
  kind: 'results';
  results: NnzGameResultRow[];
  crashedAt?: number | null;
  capped?: boolean | null;
  target?: number | null;
  radius?: number | null;
  successChance?: number | null;
  crewSize?: number | null;
  pot?: number | null;
  winner?: string | null;
  payout?: number | null;
}

/** The round was cancelled and every stake refunded. cancelled stays on the wire for older widgets. */
interface NnzGameCancelledFrame {
  kind: 'cancelled';
  cancelled: boolean;
  reason: string;
}

/** Payload the overlay receives to start playback of a clip on its audio bus. */
interface NnzPlaySoundPayload {
  playbackUrl: string;
  volume: number;
  handle?: string | null;
  synced: boolean;
}

/** Payload the overlay receives to stop in-progress clip playback. */
interface NnzStopSoundPayload {
  handle?: string | null;
  all: boolean;
}

/** custom.<source>: one reading from a custom data source; every value arrives as text. */
interface NnzCustomDataWidgetPayload {
  fields: Record<string, string>;
}

/** A single fragment of a Twitch chat message. Corresponds to one element in the EventSub channel.chat.message `fragments[]` array. A record so the decoration pipeline can copy a fragment with with { } — a hand-written field-by-field clone silently drops whatever field was added to this type last, which is how native GIFs reached every renderer with no url and rendered as their caption text. */
interface NnzChatMessageFragment {
  /** Fragment type: "text" | "emote" | "cheermote" | "mention" | "gif" */
  type: string;
  /** The raw text of this fragment. */
  text: string;
  /** Twitch emote ID (e.g. "25" for Kappa). */
  emoteId?: string | null;
  /** The emote set this emote belongs to. */
  emoteSetId?: string | null;
  /** Channel that owns this emote (for channel-specific emotes). */
  emoteOwnerId?: string | null;
  /** Available formats for this emote: "static" | "animated". Use "animated" if available, fall back to "static". */
  emoteFormats: string[];
  /** The fully-resolved, render-ready emote for this fragment (Type == "emote"), unifying Twitch and third-party (BTTV/FFZ/7TV) emotes behind one shape — provider tag, scale-keyed urls, animated and zero-width flags (chat-decoration spec §4). Null until the decoration pipeline resolves it; the flat Twitch Emote* fields above carry the raw EventSub payload until then. Settable so a decoration adapter can enrich the fragment in place (the orchestrator seeds the pipeline with copies, not the event's own fragments). */
  emote?: NnzChatEmote | null;
  /** Cheermote prefix (e.g. "Cheer", "PogChamp"). */
  cheermotePrefix?: string | null;
  /** Number of bits in this cheermote fragment. */
  cheermoteBits?: number | null;
  /** Cheer tier (1, 2, 3, 4, or 5) — determines animation and color. */
  cheermoteTier?: number | null;
  /** The resolved cheermote image (scale-keyed urls + animated flag + tier colour), filled in place by the cheermote decoration step from the cached Helix cheermotes (chat-decoration spec §4). Null until resolved, or on a miss. */
  cheermoteImage?: NnzCheermoteImage | null;
  /** Twitch user ID of the mentioned user. */
  mentionUserId?: string | null;
  /** Login (lowercase) of the mentioned user. */
  mentionUserLogin?: string | null;
  /** Display name of the mentioned user. */
  mentionUserName?: string | null;
  /** The mentioned user's chat colour as #RRGGBB, filled in place by the mention decoration step from the per-channel colour memory (chat-decoration spec §3.1). Null until resolved, or when that user has not been seen recently. */
  mentionColorHex?: string | null;
  /** The shared url, when this fragment is a standalone link (chat-decoration spec §4). */
  linkUrl?: string | null;
  /** The OpenGraph preview for LinkUrl, filled by the (opt-in, gated) link step. Null until resolved, when previews are off, or when the page exposes no OpenGraph tags — the link still renders with its url. */
  linkPreview?: NnzLinkPreview | null;
  /** GIPHY-backed GIF id Twitch assigns this fragment (the EventSub payload's gif.id). */
  gifId?: string | null;
  /** The GIF's directly-fetchable image url (the EventSub payload's gif.url). */
  gifUrl?: string | null;
}

/** One chat badge a user wears, such as moderator or subscriber. */
interface NnzChatBadge {
  /** The badge family, for example moderator or subscriber. */
  setId: string;
  /** The version inside the family, for example the number of subscribed months. */
  id: string;
  /** Extra text for the badge, such as the exact subscribed months. Null when there is none. */
  info?: string | null;
  /** Whether this badge is Twitch's artist badge. */
  isArtist: boolean;
}

/** The window of one stream: its id, start, end, title and game. */
interface NnzStreamStatsWindowDto {
  /** The stream id. */
  id: string;
  /** When the stream started. */
  startedAt: string;
  /** When the stream ended. Null while it is live. */
  endedAt?: string | null;
  /** The stream title. */
  title?: string | null;
  /** The game or category name. */
  game?: string | null;
}

/** The channel a stream belongs to. */
interface NnzStreamStatsChannelDto {
  /** The channel display name. */
  name: string;
  /** The channel profile picture URL. Null when the platform gave none. */
  avatarUrl?: string | null;
}

/** Subscription counts of a stream. */
interface NnzStreamStatsSubsDto {
  /** First-time subscriptions. */
  new: number;
  /** Resubscriptions. */
  resub: number;
  /** Gifted subscriptions. */
  gift: number;
  /** All subscriptions together. */
  total: number;
}

/** A name with a count. */
interface NnzStreamStatsNamedCountDto {
  /** The viewer or reward name. */
  name: string;
  /** How many times it counted. */
  count: number;
}

/** A total and its top entries, ranked by count then name. */
interface NnzStreamStatsRankedDto {
  /** The total across all entries. */
  total: number;
  /** The top entries, highest first. */
  top: NnzStreamStatsNamedCountDto[];
}

/** One raid that arrived. */
interface NnzStreamStatsRaidDto {
  /** The raiding channel. */
  name: string;
  /** How many viewers came with the raid. */
  viewers: number;
}

/** The hype trains that ended during a stream. */
interface NnzStreamStatsHypeTrainDto {
  /** The highest level any hype train reached. */
  level: number;
  /** How many hype trains ended during the stream. */
  trains: number;
}

/** Chat activity of a stream. */
interface NnzStreamStatsChatDto {
  /** The number of chat messages. */
  messages: number;
  /** The number of different chatters. */
  uniqueChatters: number;
}

/** A single fragment of a chat message: text | emote | cheermote | mention | link | gif. */
interface NnzChatFragmentDto {
  type: string;
  text: string;
  emote?: NnzChatEmoteDto | null;
  cheermote?: NnzChatCheermoteDto | null;
  mention?: NnzChatMentionDto | null;
  linkUrl?: string | null;
  linkPreview?: NnzChatLinkPreviewDto | null;
  gif?: NnzChatGifDto | null;
}

/** A chat badge (subscriber, moderator, etc.) resolved to its scale-keyed image urls ("1"/"2"/"4") from the cached Helix badge sets (chat-decoration spec §3.3). Urls is empty when the badge set is not yet warmed in cache. */
interface NnzChatBadgeDto {
  setId: string;
  id: string;
  info?: string | null;
  urls: Record<string, string>;
}

/** A 7TV paint, already flattened to the CSS a widget applies directly — the wire mirror of ChatPaint (chat-decoration spec, 7TV paint theming). */
interface NnzChatPaintDto {
  id: string;
  name: string;
  backgroundImage?: string | null;
  color?: string | null;
  textShadow?: string | null;
  isImageOnly: boolean;
}

/** What a chat widget needs to draw a bot overlay line beyond its text: the viewer who asked for it (drawn as "requested by ...", like the song request card; the line is never shown under the viewer's name) and the look the channel chose. A null look value means the widget's default for that part. */
interface NnzOverlayLineDto {
  /** The display name of the viewer who triggered the reply; null when nobody did. */
  requestedBy?: string | null;
  /** A design token name (primary | success | warning | destructive); null = the widget accent. */
  highlight?: string | null;
  /** normal | large. */
  size: string;
  /** How long the line stays before it fades; null = the widget's own show time. */
  durationSeconds?: number | null;
}

interface NnzHypeTrainContributionDto {
  userId: string;
  userLogin: string;
  userDisplayName: string;
  type: string;
  total: number;
}

interface NnzPollChoiceDto {
  id: string;
  title: string;
  votes: number;
  channelPointsVotes: number;
}

interface NnzPredictionOutcomeDto {
  id: string;
  title: string;
  channelPoints: number;
  users: number;
  color: string;
}

/** One upcoming request in the snapshot — exactly the fields the sr_queue overlay renders. */
interface NnzSongRequestQueueSnapshotItem {
  /** The song title. */
  title: string;
  /** The display name of the viewer who requested the song. */
  requestedBy: string;
  /** The song length in seconds. */
  durationSec: number;
  /** The number viewers see for this entry: the same one !queue prints and !wrongsong N reads back (1 plays next). */
  number: number;
  /** The short handle a viewer says to name this request, for example K7QM. Empty only for a snapshot made before codes existed. */
  code: string;
}

/** One member of a heist crew or raffle entrant list. */
interface NnzGameRosterEntry {
  player: string;
  stake: number;
}

/** One player's outcome in a GameResultsFrame. Each game fills the fields it has: crash sends cashedAt (null when the player busted); drop sends landed, distance (both null when the player never dropped) and won, with no stake; heist sends escaped; raffle sends won. */
interface NnzGameResultRow {
  player: string;
  stake?: number | null;
  cashedAt?: number | null;
  landed?: number | null;
  distance?: number | null;
  won?: boolean | null;
  escaped?: boolean | null;
  payout: number;
}

/** The single emote shape for EVERY provider (Twitch emotes use it too). The decorator fills Urls/Animated/ZeroWidth so the client renders any emote identically (chat-decoration spec §4). Urls is keyed by scale "1".."4"; ZeroWidth (a 7TV overlay) lets the renderer stack this emote over the preceding one and is false for the rest. SetId/OwnerId/Formats are populated for Twitch only. */
interface NnzChatEmote {
  /** The service the emote comes from, such as Twitch or 7TV. */
  provider: 'Twitch' | 'Bttv' | 'Ffz' | 'SevenTv';
  /** The emote id at that service. */
  id: string;
  /** The text a viewer types to use the emote. */
  code: string;
  /** Image links keyed by scale, from "1" (small) to "4" (large). */
  urls: Record<string, string>;
  /** True when the emote image is animated. */
  animated: boolean;
  /** True when the emote draws on top of the emote before it (a 7TV overlay). */
  zeroWidth: boolean;
  /** The Twitch emote set id. Set for Twitch emotes only. */
  setId?: string | null;
  /** The user id of the channel that owns the emote. Set for Twitch emotes only. */
  ownerId?: string | null;
  /** The image formats Twitch offers, such as static or animated. Set for Twitch emotes only. */
  formats?: string[] | null;
  /** Twitch: the Helix emote_type (such as subscriptions, follower, bitstier, globals). Third party: "channel" or "global", the set the emote came from. Null when the cache does not know it. */
  type?: string | null;
  /** The subscription tier 1, 2 or 3 of a Twitch subscription emote. Null for every other emote. */
  tier?: number | null;
  /** The owning channel (Twitch) or the uploader (7TV, BTTV, FFZ). Null when the cache does not know it. */
  owner?: NnzChatEmoteOwner | null;
  /** The artist when the provider names one apart from the owner (FFZ). Null otherwise. */
  artist?: NnzChatEmoteOwner | null;
  /** Whose emote this is for the operator who asked for the catalogue. Set by the catalogue per request; null on a cached emote and on the live feed. */
  origin?: ('Own' | 'Channel' | 'Other' | 'Global') | null;
  /** The channel the picker groups this emote under: the owning channel of a Twitch emote, or the viewed channel for a 7TV, BTTV or FFZ channel set. Null for a global emote. Set by the catalogue per request. */
  ownerChannel?: NnzChatEmoteOwner | null;
  /** Still-image links with the same scales as Urls, for a viewer who asks for reduced motion. An emote that is not animated carries the same links as Urls. Null only on an entry cached before this field existed. */
  staticUrls?: Record<string, string> | null;
}

/** A cheermote fragment resolved to its render-ready image (chat-decoration spec §4): the scale-keyed CDN urls for the tier the cheer qualified for, whether that image is animated, and the tier's colour. Null on the fragment until the cheermote step resolves it from the cached Helix cheermotes; a miss leaves it null (the client falls back to text). */
interface NnzCheermoteImage {
  /** Image links keyed by scale, from "1" (small) to "4" (large). */
  urls: Record<string, string>;
  /** True when the cheermote image is animated. */
  animated: boolean;
  /** The colour of the tier the cheer reached, as a hex colour such as #979797. */
  colorHex: string;
}

/** An OpenGraph link preview for a url shared in chat (chat-decoration spec §4): the host plus the page's og:title, og:description and og:image when present. Null on the fragment until the (opt-in, gated) link step resolves it; any missing tag is simply null — the link still renders with its url. */
interface NnzLinkPreview {
  /** The site the link points to, for example example.com. */
  host: string;
  /** The page title (og:title). Null when the page has none. */
  title?: string | null;
  /** The page description (og:description). Null when the page has none. */
  description?: string | null;
  /** The preview image link (og:image). Null when the page has none. */
  imageUrl?: string | null;
}

/** Emote fragment data — the unified, render-ready shape covering Twitch and third-party (BTTV/FFZ/7TV) emotes (chat-decoration spec §4). Urls is scale-keyed ("1"/"2"/"3"); Provider is the source network. StaticUrls holds the still image per scale for reduced motion; a non-animated emote repeats Urls. */
interface NnzChatEmoteDto {
  id: string;
  setId?: string | null;
  format: string;
  provider: string;
  urls: Record<string, string>;
  animated: boolean;
  zeroWidth: boolean;
  code: string;
  type?: string | null;
  tier?: number | null;
  owner?: NnzChatEmoteOwnerDto | null;
  artist?: NnzChatEmoteOwnerDto | null;
  staticUrls?: Record<string, string> | null;
}

/** Cheermote fragment data — the raw prefix/bits/tier plus the resolved tier image (chat-decoration spec §3.4): scale-keyed Urls, whether animated, and the tier ColorHex. The image fields are null/empty until the channel's cheermotes are warmed in cache. */
interface NnzChatCheermoteDto {
  prefix: string;
  bits: number;
  tier: number;
  urls?: Record<string, string> | null;
  animated: boolean;
  colorHex?: string | null;
}

/** Mention fragment data (@user) plus the mentioned user's last-seen chat colour as #RRGGBB (chat-decoration spec §3.1), or null when that user has not been seen recently. */
interface NnzChatMentionDto {
  userId: string;
  username: string;
  displayName: string;
  color?: string | null;
}

/** OpenGraph link-preview data (chat-decoration spec §3.5); fields are null when the page exposes no tags. */
interface NnzChatLinkPreviewDto {
  host: string;
  title?: string | null;
  description?: string | null;
  imageUrl?: string | null;
}

/** Twitch's native chat GIF fragment data (GIPHY-backed, Tier 2+ subscriber feature): the wire payload already carries a directly-fetchable url, so — unlike emotes/cheermotes/badges — there is no separate resolve step. */
interface NnzChatGifDto {
  gifId: string;
  url: string;
}

/** Who owns an emote. For a Twitch emote this is the owning channel (platform user id, login, display name). For a 7TV, BTTV or FFZ emote it is the uploader as that provider returns it. A field the provider or cache does not have is null. */
interface NnzChatEmoteOwner {
  /** The owner id at the emote's service. */
  id?: string | null;
  /** The owner login name (lowercase), when known. */
  login?: string | null;
  /** The owner display name, when known. */
  displayName?: string | null;
  /** A public profile link for the owner, when the provider has one. */
  profileUrl?: string | null;
  /** The owner profile image link, when the platform has one (Twitch owners). */
  avatarUrl?: string | null;
}

/** Who owns an emote: the channel for a Twitch emote, the uploader for a 7TV, BTTV or FFZ emote. A field the provider or cache does not have is null. */
interface NnzChatEmoteOwnerDto {
  id?: string | null;
  login?: string | null;
  displayName?: string | null;
  profileUrl?: string | null;
}

/** The window of one stream: its id, start, end, title and game. */
interface NnzStreamWindow {
  /** The stream id. */
  id: string;
  /** When the stream started. */
  startedAt: string;
  /** When the stream ended. Null while it is live. */
  endedAt: string | null;
  /** The stream title. */
  title: string | null;
  /** The game or category name. */
  game: string | null;
}

/** A total and its top entries, ranked by count then name. */
interface NnzStreamRanked {
  /** The total across all entries. */
  total: number;
  /** The top entries, highest first. */
  top: NnzStreamNamedCount[];
}

/** A name with a count. */
interface NnzStreamNamedCount {
  /** The viewer or reward name. */
  name: string;
  /** How many times it counted. */
  count: number;
}

/** The channel a stream belongs to. */
interface NnzStreamChannel {
  /** The channel display name. */
  name: string;
  /** The channel profile picture URL. Null when the platform gave none. */
  avatarUrl: string | null;
}

/** Chat activity of a stream. */
interface NnzStreamChat {
  /** The number of chat messages. */
  messages: number;
  /** The number of different chatters. */
  uniqueChatters: number;
}

/** The hype trains that ended during a stream. */
interface NnzStreamHypeTrain {
  /** The highest level any hype train reached. */
  level: number;
  /** How many hype trains ended during the stream. */
  trains: number;
}

/** One raid that arrived. */
interface NnzStreamRaid {
  /** The raiding channel. */
  name: string;
  /** How many viewers came with the raid. */
  viewers: number;
}

/** Subscription counts of a stream. */
interface NnzStreamSubs {
  /** First-time subscriptions. */
  new: number;
  /** Resubscriptions. */
  resub: number;
  /** Gifted subscriptions. */
  gift: number;
  /** All subscriptions together. */
  total: number;
}

/** Each stream metric key and the value it returns. */
interface NnzStreamMetricMap {
  /** Total bits and the top cheerers (count = bits). */
  bits: NnzStreamRanked;
  /** Who streamed: the channel's display name and profile picture, for headers and watermarks. */
  channel: NnzStreamChannel;
  /** Chat message count and the number of different chatters. */
  chat: NnzStreamChat;
  /** Commands run in the window and the most-used ones. */
  commandsRun: NnzStreamRanked;
  /** Seconds from the start of the stream to its end (or to now while live). */
  duration: number;
  /** New followers during the stream. */
  followers: number;
  /** Gifters by gift count; anonymous gifts are merged as "Anonymous". */
  gifters: NnzStreamNamedCount[];
  /** The hype trains that ended during the stream. Absent when there was none. */
  hypeTrain: NnzStreamHypeTrain;
  /** Display names of the viewers who followed during the stream, oldest first. */
  newFollowers: string[];
  /** The highest viewer count the stream recorded. */
  peakViewers?: number;
  /** Raids that arrived during the stream: who raided and with how many viewers. */
  raidsIn: NnzStreamRaid[];
  /** Redemption total and the top reward titles. */
  redemptions: NnzStreamRanked;
  /** Viewers who subscribed or resubscribed themselves (gift recipients excluded), in order. */
  selfSubs: string[];
  /** Song request total and the top requesters; never song titles. */
  songRequesters: NnzStreamRanked;
  /** Subscriptions during the stream: new, resubscribed, gifted and the total. */
  subs: NnzStreamSubs;
  /** The chatters with the most messages. */
  topChatters: NnzStreamNamedCount[];
}

type NnzStreamMetricKey = keyof NnzStreamMetricMap;

/** The answer of streamStats: the stream window plus the requested metrics. An optional metric (?) is absent when it has nothing to show. */
type NnzStreamStats<K extends NnzStreamMetricKey> = Pick<NnzStreamMetricMap, K> & { stream: NnzStreamWindow };

/** What to read: the stream (the latest when left out) and the metric keys. */
interface NnzStreamStatsOptions<K extends NnzStreamMetricKey> {
  /** The stream id. Leave out for the latest stream, live or ended. */
  stream?: string;
  /** The metric keys to read. */
  metrics: readonly K[];
}


/** Every template helper name, as written in a template: {name} or {name:argument}. */
type TemplateHelperName =
  | 'ad.automatic'
  | 'ad.duration'
  | 'ad.seconds'
  | 'ad.when'
  | 'also_said'
  | 'anonymous'
  | 'args'
  | 'args.<n>'
  | 'bits'
  | 'botname'
  | 'broadcaster'
  | 'channel'
  | 'channel.avatar'
  | 'channel.display'
  | 'channel.game'
  | 'channel.id'
  | 'channel.login'
  | 'channel.name'
  | 'channel.title'
  | 'channel.url'
  | 'cost'
  | 'count'
  | 'count.<key>'
  | 'custom.<name>.<field>'
  | 'date'
  | 'date:<source>'
  | 'date:<source>|<+/-amount><unit>'
  | 'date:<source>|diff:<date>[:<unit>]'
  | 'date:<source>|end:<unit>'
  | 'date:<source>|format:<pattern>'
  | 'date:<source>|lang:<language>'
  | 'date:<source>|next:<weekday>'
  | 'date:<source>|ordinal'
  | 'date:<source>|prev:<weekday>'
  | 'date:<source>|relative'
  | 'date:<source>|since[:<unit>]'
  | 'date:<source>|start:<unit>'
  | 'date:<source>|tz:<zone>'
  | 'date:<source>|until[:<unit>]'
  | 'duration'
  | 'engagement.daysSinceLastSeen'
  | 'engagement.months'
  | 'engagement.streak'
  | 'engagement.years'
  | 'followed_at'
  | 'game'
  | 'genderedTerm'
  | 'gifter'
  | 'gifter.id'
  | 'hype_train.goal'
  | 'hype_train.id'
  | 'hype_train.level'
  | 'hype_train.total'
  | 'input'
  | 'link'
  | 'list.pick.<name>'
  | 'message'
  | 'moderator'
  | 'months'
  | 'music.favorite.artist'
  | 'music.favorite.count'
  | 'music.favorite.outcome'
  | 'music.favorite.track'
  | 'name'
  | 'object'
  | 'obs.event.<field>'
  | 'playlist_id'
  | 'poll.choices'
  | 'poll.duration'
  | 'poll.id'
  | 'poll.results'
  | 'poll.status'
  | 'poll.title'
  | 'poll.winner'
  | 'poll.winner.percentage'
  | 'poll.winner.votes'
  | 'possessive'
  | 'prediction.id'
  | 'prediction.outcomes'
  | 'prediction.status'
  | 'prediction.title'
  | 'prediction.total_points'
  | 'prediction.window'
  | 'prediction.winner'
  | 'prediction.winner.points'
  | 'prediction.winner.users'
  | 'presentTense'
  | 'provider'
  | 'random.number.<min>.<max>[.<step>]'
  | 'random.number.<n>'
  | 'random.pick.<a.b.c>'
  | 'random.user'
  | 'raw.message'
  | 'reason'
  | 'redemption.id'
  | 'reward'
  | 'reward.id'
  | 'roll.<name>'
  | 'roll.<name>.<min>.<max>[.<step>]'
  | 'roll.<name>.complement'
  | 'status'
  | 'streak'
  | 'stream.game'
  | 'stream.isLive'
  | 'stream.startedAt'
  | 'stream.thumbnail'
  | 'stream.title'
  | 'stream.uptime'
  | 'stream.url'
  | 'stream.viewers'
  | 'streamer'
  | 'subject'
  | 'supporter.amount'
  | 'supporter.currency'
  | 'supporter.kind'
  | 'supporter.message'
  | 'supporter.name'
  | 'supporter.quantity'
  | 'supporter.tier'
  | 'target'
  | 'target.avgperday'
  | 'target.botpercent'
  | 'target.commands'
  | 'target.data.<key>'
  | 'target.days'
  | 'target.displayname'
  | 'target.firstseen'
  | 'target.followAge'
  | 'target.genderedTerm'
  | 'target.id'
  | 'target.known'
  | 'target.lastmessage'
  | 'target.lastmessage.full'
  | 'target.link'
  | 'target.messages'
  | 'target.name'
  | 'target.object'
  | 'target.possessive'
  | 'target.presentTense'
  | 'target.randommessage'
  | 'target.redemptions'
  | 'target.songrequests'
  | 'target.subject'
  | 'target.tense'
  | 'target.verb:<sing|plur>'
  | 'target.watchtime'
  | 'tense'
  | 'tier'
  | 'time'
  | 'time.utc'
  | 'title'
  | 'track_name'
  | 'transform.<name>:<text>'
  | 'tts.audioconnected'
  | 'user'
  | 'user.accountAge'
  | 'user.followAge'
  | 'user.genderedTerm'
  | 'user.id'
  | 'user.lastmessage'
  | 'user.link'
  | 'user.messageCount'
  | 'user.name'
  | 'user.object'
  | 'user.possessive'
  | 'user.presentTense'
  | 'user.pronouns'
  | 'user.provider'
  | 'user.subject'
  | 'user.tense'
  | 'user.verb:<sing|plur>'
  | 'verb:<sing|plur>'
  | 'viewer.avgperday'
  | 'viewer.botpercent'
  | 'viewer.commands'
  | 'viewer.data.<key>'
  | 'viewer.days'
  | 'viewer.firstseen'
  | 'viewer.messages'
  | 'viewer.name'
  | 'viewer.redemptions'
  | 'viewer.songrequests'
  | 'viewer.watchtime'
  | 'viewers'
  | 'vts.event.<field>'
  ;

/** Each template helper name and what it gives, in one line. */
interface NnzTemplateHelperMap {
  'ad.automatic': 'For example true';
  'ad.duration': 'For example 3 minutes';
  'ad.seconds': 'For example 300';
  'ad.when': 'For example 5 minutes';
  'also_said': 'For example Great stream!';
  'anonymous': 'For example false';
  'args': 'For example hello world';
  'args.<n>': 'For example hello';
  'bits': 'For example 100';
  'botname': 'For example NomNomzBot';
  'broadcaster': 'For example Example_Channel';
  'channel': 'For example example_channel';
  'channel.avatar': 'For example https://static-cdn.jtvnw.net/jtv_user_pictures/example_channel.png';
  'channel.display': 'For example Example_Channel';
  'channel.game': 'For example Just Chatting';
  'channel.id': 'For example 123456789';
  'channel.login': 'For example example_channel';
  'channel.name': 'For example Example_Channel';
  'channel.title': 'For example Cozy coding night';
  'channel.url': 'For example https://www.twitch.tv/example_channel';
  'cost': 'For example 500';
  'count': 'For example 12';
  'count.<key>': 'For example 12';
  'custom.<name>.<field>': 'For example Blue';
  'date': 'For example 2026-10-06';
  'date:<source>': 'For example 2026-10-06 20:15';
  'date:<source>|<+/-amount><unit>': 'For example 2026-10-13 20:15';
  'date:<source>|diff:<date>[:<unit>]': 'For example 411 days';
  'date:<source>|end:<unit>': 'For example 2026-10-31 23:59';
  'date:<source>|format:<pattern>': 'For example Tuesday 6 October 2026';
  'date:<source>|lang:<language>': 'For example dinsdag 6 oktober 2026';
  'date:<source>|next:<weekday>': 'For example 2026-10-09 20:15';
  'date:<source>|ordinal': 'For example 6th';
  'date:<source>|prev:<weekday>': 'For example 2026-10-02 20:15';
  'date:<source>|relative': 'For example 3 days ago';
  'date:<source>|since[:<unit>]': 'For example 1 year, 1 month, 16 days';
  'date:<source>|start:<unit>': 'For example 2026-10-01 00:00';
  'date:<source>|tz:<zone>': 'For example 2026-10-06 14:15';
  'date:<source>|until[:<unit>]': 'For example 59 days';
  'duration': 'For example 10 minutes';
  'engagement.daysSinceLastSeen': 'For example 14';
  'engagement.months': 'For example 18';
  'engagement.streak': 'For example 5';
  'engagement.years': 'For example 1';
  'followed_at': 'For example 2026-10-06T18:30:00Z';
  'game': 'For example Just Chatting';
  'genderedTerm': 'For example person';
  'gifter': 'For example Example_Supporter';
  'gifter.id': 'For example 987654321';
  'hype_train.goal': 'For example 8000';
  'hype_train.id': 'For example 1b0AsbInCHZW2SQFQkCzqN07Ib2';
  'hype_train.level': 'For example 3';
  'hype_train.total': 'For example 6400';
  'input': 'For example Hello chat';
  'link': 'For example https://twitch.tv/example_viewer';
  'list.pick.<name>': 'One random entry of a pick list';
  'message': 'For example Great stream!';
  'moderator': 'For example Example_Moderator';
  'months': 'For example 12';
  'music.favorite.artist': 'For example The Weeknd';
  'music.favorite.count': 'For example 12';
  'music.favorite.outcome': 'For example added';
  'music.favorite.track': 'For example Blinding Lights';
  'name': 'For example Example_Viewer';
  'object': 'For example them';
  'obs.event.<field>': 'For example Scene 1';
  'playlist_id': 'For example 37i9dQZF1DX';
  'poll.choices': 'For example Pizza, Pasta';
  'poll.duration': 'For example 5 minutes';
  'poll.id': 'For example ed961efd-8a3f-4cf5-a9d0-e616c590cd2a';
  'poll.results': 'For example Pizza 28, Pasta 22';
  'poll.status': 'For example completed';
  'poll.title': 'For example What next?';
  'poll.winner': 'For example Pizza';
  'poll.winner.percentage': 'For example 56';
  'poll.winner.votes': 'For example 28';
  'possessive': 'For example their';
  'prediction.id': 'For example 4de1a8b0-c3a0-4ed0-8b6f-7d1f0d1c2a11';
  'prediction.outcomes': 'For example Yes, No';
  'prediction.status': 'For example resolved';
  'prediction.title': 'For example Will we win?';
  'prediction.total_points': 'For example 5000';
  'prediction.window': 'For example 120';
  'prediction.winner': 'For example Yes';
  'prediction.winner.points': 'For example 3200';
  'prediction.winner.users': 'For example 41';
  'presentTense': 'For example are';
  'provider': 'For example twitch';
  'random.number.<min>.<max>[.<step>]': 'For example 42';
  'random.number.<n>': 'For example 7';
  'random.pick.<a.b.c>': 'For example b';
  'random.user': 'For example example_viewer';
  'raw.message': 'For example !hello world';
  'reason': 'For example Spam';
  'redemption.id': 'For example e5f6a7b8';
  'reward': 'For example Hydrate';
  'reward.id': 'For example a1b2c3d4';
  'roll.<name>': 'For example 13';
  'roll.<name>.<min>.<max>[.<step>]': 'For example 13';
  'roll.<name>.complement': 'For example 87';
  'status': 'For example live';
  'streak': 'For example 6';
  'stream.game': 'For example Just Chatting';
  'stream.isLive': 'For example true';
  'stream.startedAt': 'For example 2026-10-06T18:00:00Z';
  'stream.thumbnail': 'For example https://static-cdn.jtvnw.net/previews-ttv/live_user_example_channel-1280x720.jpg';
  'stream.title': 'For example Cozy coding night';
  'stream.uptime': 'For example 2h 15m';
  'stream.url': 'For example https://www.twitch.tv/example_channel';
  'stream.viewers': 'For example 42';
  'streamer': 'For example Example_Channel';
  'subject': 'For example they';
  'supporter.amount': 'For example 5.00';
  'supporter.currency': 'For example EUR';
  'supporter.kind': 'For example tip';
  'supporter.message': 'For example Keep it up!';
  'supporter.name': 'For example Example_Supporter';
  'supporter.quantity': 'For example 1';
  'supporter.tier': 'For example Gold';
  'target': 'For example example_viewer';
  'target.avgperday': 'For example 8';
  'target.botpercent': 'For example 5';
  'target.commands': 'For example 40';
  'target.data.<key>': 'For example Blue';
  'target.days': 'For example 30';
  'target.displayname': 'For example Example_Viewer';
  'target.firstseen': 'For example 2025-03-01';
  'target.followAge': 'For example 1 year';
  'target.genderedTerm': 'For example person';
  'target.id': 'For example 123456789';
  'target.known': 'For example true';
  'target.lastmessage': 'For example see you tomorrow';
  'target.lastmessage.full': 'For example see you tomorrow';
  'target.link': 'For example twitch.tv/example_viewer';
  'target.messages': 'For example 250';
  'target.name': 'For example example_viewer';
  'target.object': 'For example them';
  'target.possessive': 'For example their';
  'target.presentTense': 'For example are';
  'target.randommessage': 'For example nice play';
  'target.redemptions': 'For example 8';
  'target.songrequests': 'For example 3';
  'target.subject': 'For example they';
  'target.tense': 'For example are';
  'target.verb:<sing|plur>': 'For example plays';
  'target.watchtime': 'For example 12h';
  'tense': 'For example is';
  'tier': 'For example Tier 1';
  'time': 'For example 20:15';
  'time.utc': 'For example 18:15';
  'title': 'For example Cozy coding night';
  'track_name': 'For example Blinding Lights';
  'transform.<name>:<text>': 'For example HELLO';
  'tts.audioconnected': 'For example true';
  'user': 'For example Example_Viewer';
  'user.accountAge': 'For example 3 years';
  'user.followAge': 'For example 1 year';
  'user.genderedTerm': 'For example person';
  'user.id': 'For example 123456789';
  'user.lastmessage': 'For example see you tomorrow';
  'user.link': 'For example twitch.tv/example_viewer';
  'user.messageCount': 'For example 250';
  'user.name': 'For example example_viewer';
  'user.object': 'For example them';
  'user.possessive': 'For example their';
  'user.presentTense': 'For example are';
  'user.pronouns': 'For example they/them';
  'user.provider': 'For example twitch';
  'user.subject': 'For example they';
  'user.tense': 'For example are';
  'user.verb:<sing|plur>': 'For example plays';
  'verb:<sing|plur>': 'For example plays';
  'viewer.avgperday': 'For example 8';
  'viewer.botpercent': 'For example 5';
  'viewer.commands': 'For example 40';
  'viewer.data.<key>': 'For example Blue';
  'viewer.days': 'For example 30';
  'viewer.firstseen': 'For example 2025-03-01';
  'viewer.messages': 'For example 250';
  'viewer.name': 'For example Example_Viewer';
  'viewer.redemptions': 'For example 8';
  'viewer.songrequests': 'For example 3';
  'viewer.watchtime': 'For example 12h';
  'viewers': 'For example 25';
  'vts.event.<field>': 'For example Model 1';
}

interface NnzEventMap {
  'chat.message': NnzChatMessageReceived;
  'stream.offline': NnzChannelOffline;
  'stream.online': NnzChannelOnline;
}

/** The parameters of every pipeline action, keyed by action type. */
interface NnzActionParams {
  'adjust_counter': {
    key: string;
    delta?: number;
  };
  'adjust_viewer_data': {
    key: string;
    delta?: number;
    target?: string;
  };
  'announce': {
    message: string;
    color?: 'primary' | 'purple' | 'blue' | 'green' | 'orange';
  };
  'ban': {
    user_id?: string;
    reason?: string;
  };
  'break': {
  };
  'cancel_live_game': {
  };
  'check_balance': {
    set_var?: string;
    min?: number;
  };
  'clear_viewer_data': {
    key: string;
  };
  'continue': {
  };
  'deduct_currency': {
    amount: number;
    reason?: string;
  };
  'delete_message': {
    message_id?: string;
  };
  'draw_giveaway': {
    giveaway_id?: string;
  };
  'enter_giveaway': {
    giveaway_id?: string;
  };
  'grant_currency': {
    amount: number;
    reason?: string;
  };
  'jar_contribute': {
    jar_id: string;
    amount: number;
  };
  'media_playback': {
    command: 'play' | 'pause' | 'stop' | 'skip' | 'seek' | 'volume' | 'replay' | 'play_now';
    value?: string;
  };
  'music_add_to_playlist': {
    playlistId: string;
    provider?: 'spotify' | 'youtube';
  };
  'music_cycle_repeat': {
  };
  'music_follow_artist': {
  };
  'music_next': {
  };
  'music_pause': {
  };
  'music_play': {
  };
  'music_play_pause': {
  };
  'music_previous': {
  };
  'music_remove_from_playlist': {
    playlistId: string;
    provider?: 'spotify' | 'youtube';
  };
  'music_save_track': {
  };
  'music_seek': {
    positionSeconds: number;
  };
  'music_set_repeat': {
    mode: 'off' | 'track' | 'context';
  };
  'music_set_shuffle': {
    enabled: boolean;
  };
  'music_set_volume': {
    volume: number;
  };
  'music_toggle_saved': {
  };
  'music_toggle_shuffle': {
  };
  'music_transfer_device': {
    deviceId: string;
  };
  'music_unfollow_artist': {
  };
  'music_unsave_track': {
  };
  'music_volume_down': {
    step?: number;
  };
  'music_volume_mute': {
    unmuteVolume?: number;
  };
  'music_volume_up': {
    step?: number;
  };
  'obs_call_vendor': {
    vendor: string;
    request_type: string;
    request_data?: string;
  };
  'obs_filter': {
    source: string;
    filter: string;
    enabled?: boolean;
  };
  'obs_hotkey': {
    hotkey_name: string;
  };
  'obs_input_mute': {
    input: string;
    muted?: boolean;
    toggle?: boolean;
  };
  'obs_input_volume': {
    input: string;
    volume_db?: number;
    volume_mul?: number;
  };
  'obs_media': {
    input: string;
    action?: 'play' | 'pause' | 'stop' | 'restart' | 'next' | 'previous';
  };
  'obs_recording': {
    action?: 'start' | 'stop' | 'toggle' | 'pause' | 'resume' | 'split';
  };
  'obs_refresh_browser': {
    input: string;
  };
  'obs_replay_buffer': {
    action?: 'start' | 'stop' | 'toggle';
  };
  'obs_request': {
    request_type: string;
    request_data?: string;
  };
  'obs_request_batch': {
    requests: string[];
    execution?: 'realtime' | 'frame' | 'parallel';
    halt_on_failure?: boolean;
  };
  'obs_save_replay': {
  };
  'obs_screenshot': {
    source: string;
    format?: 'png' | 'jpg' | 'jpeg';
  };
  'obs_set_preview_scene': {
    scene: string;
  };
  'obs_set_source': {
    scene: string;
    source: string;
    visible?: boolean;
  };
  'obs_streaming': {
    action?: 'start' | 'stop' | 'toggle';
  };
  'obs_switch_scene': {
    scene: string;
  };
  'obs_transition': {
    transition?: string;
    studio?: boolean;
    duration_ms?: number;
  };
  'obs_virtual_cam': {
    action?: 'start' | 'stop' | 'toggle';
  };
  'open_giveaway': {
    giveaway_id: string;
  };
  'permit': {
    target_variable?: string;
    role_or_capability: string;
    duration_minutes?: number;
  };
  'pick_from_list': {
    list: string;
    variable?: string;
  };
  'play_game': {
    game_type: string;
    bet: number;
  };
  'play_sound': {
    clip: string;
    volume?: number;
    wait_for_finish?: boolean;
    synced?: boolean;
    handle?: string;
  };
  'play_track_once': {
    track_uri: string;
  };
  'play_tts': {
    text?: string;
    segments?: string;
    voice?: string;
    as?: string;
    rate?: string;
  };
  'playlist_add': {
    playlist_id: string;
    provider?: 'spotify' | 'youtube';
    track_uri?: string;
    message?: string;
    already_message?: string;
  };
  'post_quote': {
    number?: number;
  };
  'redemption_fulfill': {
  };
  'redemption_refund': {
  };
  'require_tier': {
    min_tier: 'free' | 'base' | 'pro' | 'premium';
    denied_message?: string;
  };
  'return_value': {
    value?: string;
  };
  'run_code': {
    code_script_id: string;
  };
  'run_pipeline': {
    pipeline: string;
    mode?: 'inline' | 'detached';
    args?: string[];
    named_args?: Record<string, string>;
    wait?: boolean;
  };
  'schedule_pipeline': {
    pipeline?: string;
    command?: string;
    action_type?: string;
    action_params?: Record<string, string>;
    delay_seconds: number;
    dedupe_key?: string;
  };
  'send_discord_notification': {
    trigger_type: 'go_live' | 'follow' | 'subscription' | 'gift_subscription' | 'cheer' | 'raid' | 'stream_end' | 'resubscription' | 'poll_end' | 'prediction_resolved';
    dedupe_key?: string;
  };
  'send_message': {
    message: string;
    sender?: 'bot' | 'broadcaster';
    reply_target?: 'chat' | 'overlay' | 'both' | 'none';
    reply_html?: boolean;
  };
  'send_reply': {
    message: string;
  };
  'send_webhook': {
    endpoint: string;
    event_type?: string;
  };
  'set_counter': {
    key: string;
    value: number;
  };
  'set_pronoun': {
    username: string;
    pronoun: string;
  };
  'set_variable': {
    name: string;
    value?: string;
  };
  'set_viewer_data': {
    key: string;
    value?: string;
    target?: string;
  };
  'shoutout': {
    user_id: string;
    cooldown_minutes?: number;
    global_cooldown_minutes?: number;
    tts?: boolean;
    priority?: boolean;
    template?: string;
  };
  'show_raid_recap': {
  };
  'song_ban': {
    reason?: string;
  };
  'song_current': {
  };
  'song_pause': {
  };
  'song_previous': {
  };
  'song_queue': {
    max?: number;
  };
  'song_request': {
    query: string;
  };
  'song_request_favorite': {
    user_id?: string;
  };
  'song_resume': {
  };
  'song_skip': {
  };
  'song_volume': {
    volume: number;
  };
  'song_wrong': {
  };
  'stage_effect': {
    preset: 'confetti' | 'flash' | 'run_across' | 'shake';
    text?: string;
    duration_ms?: number;
    intensity?: number;
    color?: string;
  };
  'start_live_game': {
    game_type: string;
  };
  'start_raid': {
    target: string;
    delay_seconds?: number;
    raid_window_seconds?: number;
  };
  'stop': {
  };
  'stop_sound': {
    handle?: string;
    all?: boolean;
  };
  'submit_media': {
    url: string;
  };
  'timeout': {
    user_id?: string;
    duration: number;
    reason?: string;
  };
  'tts_synthesize': {
    text: string;
    voice?: string;
    rate?: number;
    pitch?: number;
    play_on_audio?: boolean;
  };
  'unpermit': {
    target_variable?: string;
    role_or_capability?: string;
  };
  'vts_color_tint': {
    r: number;
    g: number;
    b: number;
    a?: number;
    art_mesh_tag?: string;
  };
  'vts_load_model': {
    model: string;
  };
  'vts_move_model': {
    x?: number;
    y?: number;
    rotation?: number;
    size?: number;
    time_seconds?: number;
    relative?: boolean;
  };
  'vts_request': {
    request_type: string;
    payload_json?: string;
  };
  'vts_set_expression': {
    expression: string;
    active?: boolean;
  };
  'vts_trigger_hotkey': {
    hotkey: string;
  };
  'wait': {
    milliseconds?: number;
    seconds?: number;
  };
  'wait_for_event': {
    event_name: string;
    timeout_seconds?: number;
  };
  'wait_until_raid_fires': {
  };
  'widget_event': {
    widget_id: string;
    event_type: string;
    data?: string;
  };
}

/** The action types whose params object has no required field, so params may be left out. */
type NnzActionsWithOptionalParams = {
  [K in keyof NnzActionParams]: {} extends NnzActionParams[K] ? K : never;
}[keyof NnzActionParams];

/** Every event a widget can receive, by the name it passes to NomNomz.on(...). */
interface NnzWidgetEventMap {
  'follow': NnzFollowAlertDto;
  'subscription': NnzSubscriptionAlertDto;
  'resub': NnzResubAlertDto;
  'gift': NnzGiftSubAlertDto;
  'cheer': NnzCheerAlertDto;
  'raid': NnzRaidAlertDto;
  'raid_recap.show': NnzStreamStatsDto;
  'ban': NnzModActionDto;
  'timeout': NnzModActionDto;
  'unban': NnzModActionDto;
  'ChatMessage': NnzDashboardChatMessageDto;
  'ChatCleared': NnzChatClearedDto;
  'MessageDeleted': NnzMessageDeletedDto;
  'UserMessagesCleared': NnzUserMessagesClearedDto;
  'ChatMessageEnriched': NnzChatMessageEnrichedWidgetPayload;
  'hype_train_begin': NnzHypeTrainBeganAlertDto;
  'hype_train_progress': NnzHypeTrainProgressAlertDto;
  'hype_train_end': NnzHypeTrainEndedAlertDto;
  'poll_begin': NnzPollBeganAlertDto;
  'poll_progress': NnzPollProgressAlertDto;
  'poll_end': NnzPollEndedAlertDto;
  'prediction_begin': NnzPredictionBeganAlertDto;
  'prediction_progress': NnzPredictionProgressAlertDto;
  'prediction_lock': NnzPredictionLockedAlertDto;
  'prediction_end': NnzPredictionEndedAlertDto;
  'reward_redeemed': NnzRewardRedeemedDto;
  'moderator_added': NnzRoleChangedAlertDto;
  'moderator_removed': NnzRoleChangedAlertDto;
  'vip_added': NnzRoleChangedAlertDto;
  'vip_removed': NnzRoleChangedAlertDto;
  'shoutout_sent': NnzShoutoutSentAlertDto;
  'shoutout_received': NnzShoutoutReceivedAlertDto;
  'sr_queue': NnzSrQueueWidgetPayload;
  'media_share_command': NnzMediaShareCommandWidgetPayload;
  'now_playing': NnzNowPlayingWidgetPayload;
  'music.nowPlaying': NnzNowPlayingShapeDto;
  'ad_schedule': NnzAdScheduleWidgetPayload;
  'ad_upcoming': NnzAdUpcomingWidgetPayload;
  'youtube.play': NnzYouTubePlayWidgetPayload;
  'youtube.pause': NnzYouTubeTransportWidgetPayload;
  'youtube.resume': NnzYouTubeTransportWidgetPayload;
  'youtube.stop': NnzYouTubeTransportWidgetPayload;
  'youtube.seek': NnzYouTubeSeekWidgetPayload;
  'youtube.volume': NnzYouTubeVolumeWidgetPayload;
  'track_saved_changed': NnzTrackSavedWidgetPayload;
  'tts_speak': NnzTtsSpeakWidgetPayload;
  'stage_effect': NnzStageEffectPayload;
  'goal': NnzGoalWidgetEventPayload;
  'count': NnzCountWidgetEventPayload;
  'supporter.tip': NnzSupporterAlertPayload;
  'supporter.membership': NnzSupporterAlertPayload;
  'supporter.merch': NnzSupporterAlertPayload;
  'supporter.charity': NnzSupporterAlertPayload;
  'voice_trigger': NnzVoiceTriggerWidgetEventPayload;
  'game.lobby': NnzGameRoundOpenFrame | NnzGameJoinFrame | NnzGameMultiplierFrame | NnzGameCashoutFrame | NnzGameDropFrame;
  'game.running': NnzGameRoundOpenFrame | NnzGameJoinFrame | NnzGameMultiplierFrame | NnzGameCashoutFrame | NnzGameDropFrame;
  'game.resolved': NnzGameResultsFrame | NnzGameCancelledFrame;
  'play_sound': NnzPlaySoundPayload;
  'stop_sound': NnzStopSoundPayload;
  'test': Record<string, unknown>;
}

/** The arguments of an onAny handler: the event name, and the payload that name carries. */
type NnzWidgetAnyEvent =
  | [eventType: 'follow', data: NnzFollowAlertDto]
  | [eventType: 'subscription', data: NnzSubscriptionAlertDto]
  | [eventType: 'resub', data: NnzResubAlertDto]
  | [eventType: 'gift', data: NnzGiftSubAlertDto]
  | [eventType: 'cheer', data: NnzCheerAlertDto]
  | [eventType: 'raid', data: NnzRaidAlertDto]
  | [eventType: 'raid_recap.show', data: NnzStreamStatsDto]
  | [eventType: 'ban', data: NnzModActionDto]
  | [eventType: 'timeout', data: NnzModActionDto]
  | [eventType: 'unban', data: NnzModActionDto]
  | [eventType: 'ChatMessage', data: NnzDashboardChatMessageDto]
  | [eventType: 'ChatCleared', data: NnzChatClearedDto]
  | [eventType: 'MessageDeleted', data: NnzMessageDeletedDto]
  | [eventType: 'UserMessagesCleared', data: NnzUserMessagesClearedDto]
  | [eventType: 'ChatMessageEnriched', data: NnzChatMessageEnrichedWidgetPayload]
  | [eventType: 'hype_train_begin', data: NnzHypeTrainBeganAlertDto]
  | [eventType: 'hype_train_progress', data: NnzHypeTrainProgressAlertDto]
  | [eventType: 'hype_train_end', data: NnzHypeTrainEndedAlertDto]
  | [eventType: 'poll_begin', data: NnzPollBeganAlertDto]
  | [eventType: 'poll_progress', data: NnzPollProgressAlertDto]
  | [eventType: 'poll_end', data: NnzPollEndedAlertDto]
  | [eventType: 'prediction_begin', data: NnzPredictionBeganAlertDto]
  | [eventType: 'prediction_progress', data: NnzPredictionProgressAlertDto]
  | [eventType: 'prediction_lock', data: NnzPredictionLockedAlertDto]
  | [eventType: 'prediction_end', data: NnzPredictionEndedAlertDto]
  | [eventType: 'reward_redeemed', data: NnzRewardRedeemedDto]
  | [eventType: 'moderator_added', data: NnzRoleChangedAlertDto]
  | [eventType: 'moderator_removed', data: NnzRoleChangedAlertDto]
  | [eventType: 'vip_added', data: NnzRoleChangedAlertDto]
  | [eventType: 'vip_removed', data: NnzRoleChangedAlertDto]
  | [eventType: 'shoutout_sent', data: NnzShoutoutSentAlertDto]
  | [eventType: 'shoutout_received', data: NnzShoutoutReceivedAlertDto]
  | [eventType: 'sr_queue', data: NnzSrQueueWidgetPayload]
  | [eventType: 'media_share_command', data: NnzMediaShareCommandWidgetPayload]
  | [eventType: 'now_playing', data: NnzNowPlayingWidgetPayload]
  | [eventType: 'music.nowPlaying', data: NnzNowPlayingShapeDto]
  | [eventType: 'ad_schedule', data: NnzAdScheduleWidgetPayload]
  | [eventType: 'ad_upcoming', data: NnzAdUpcomingWidgetPayload]
  | [eventType: 'youtube.play', data: NnzYouTubePlayWidgetPayload]
  | [eventType: 'youtube.pause', data: NnzYouTubeTransportWidgetPayload]
  | [eventType: 'youtube.resume', data: NnzYouTubeTransportWidgetPayload]
  | [eventType: 'youtube.stop', data: NnzYouTubeTransportWidgetPayload]
  | [eventType: 'youtube.seek', data: NnzYouTubeSeekWidgetPayload]
  | [eventType: 'youtube.volume', data: NnzYouTubeVolumeWidgetPayload]
  | [eventType: 'track_saved_changed', data: NnzTrackSavedWidgetPayload]
  | [eventType: 'tts_speak', data: NnzTtsSpeakWidgetPayload]
  | [eventType: 'stage_effect', data: NnzStageEffectPayload]
  | [eventType: 'goal', data: NnzGoalWidgetEventPayload]
  | [eventType: 'count', data: NnzCountWidgetEventPayload]
  | [eventType: 'supporter.tip', data: NnzSupporterAlertPayload]
  | [eventType: 'supporter.membership', data: NnzSupporterAlertPayload]
  | [eventType: 'supporter.merch', data: NnzSupporterAlertPayload]
  | [eventType: 'supporter.charity', data: NnzSupporterAlertPayload]
  | [eventType: 'voice_trigger', data: NnzVoiceTriggerWidgetEventPayload]
  | [eventType: 'game.lobby', data: NnzGameRoundOpenFrame | NnzGameJoinFrame | NnzGameMultiplierFrame | NnzGameCashoutFrame | NnzGameDropFrame]
  | [eventType: 'game.running', data: NnzGameRoundOpenFrame | NnzGameJoinFrame | NnzGameMultiplierFrame | NnzGameCashoutFrame | NnzGameDropFrame]
  | [eventType: 'game.resolved', data: NnzGameResultsFrame | NnzGameCancelledFrame]
  | [eventType: 'play_sound', data: NnzPlaySoundPayload]
  | [eventType: 'stop_sound', data: NnzStopSoundPayload]
  | [eventType: 'test', data: Record<string, unknown>]
  | [eventType: `custom.${string}`, data: NnzCustomDataWidgetPayload];

/** The settings of this widget; it declares no schema, so any key is allowed. */
type NnzWidgetSettings = Record<string, unknown>;

/**
 * The overlay SDK global, served as /overlay/sdk.js and installed before the widget bundle
 * runs. Event names are this widget's OWN subscription keys (see
 * WIDGET_EVENT_SUBSCRIPTIONS) — e.g. 'follow', 'tts_speak' — not the NnzEventMap wire names.
 * Every registration returns the SDK, so calls chain.
 */
/** Says whether an event is a live one or a replay of a remembered one sent on join. */
interface NnzEventMeta {
  /** True for a seed frame the bot replays on join; false for a live event. */
  replay: boolean;
  /** ISO time the replayed event really happened. Set only when replay is true. */
  occurredAt?: string;
  /** True for a dashboard test fire: show it, but never keep it as the widget's value or count. */
  test?: boolean;
}

interface NnzOverlaySdk {
  on<K extends keyof NnzWidgetEventMap>(eventType: K, handler: (data: NnzWidgetEventMap[K], eventType: K, meta?: NnzEventMeta) => void): NnzOverlaySdk;
  on(eventType: `custom.${string}`, handler: (data: NnzCustomDataWidgetPayload, eventType: string, meta?: NnzEventMeta) => void): NnzOverlaySdk;
  on(eventType: string, handler: (data: unknown, eventType: string, meta?: NnzEventMeta) => void): NnzOverlaySdk;
  /** Removes a handler registered with on() — pass the SAME function reference. */
  off<K extends keyof NnzWidgetEventMap>(eventType: K, handler: (data: NnzWidgetEventMap[K], eventType: K, meta?: NnzEventMeta) => void): NnzOverlaySdk;
  off(eventType: `custom.${string}`, handler: (data: NnzCustomDataWidgetPayload, eventType: string, meta?: NnzEventMeta) => void): NnzOverlaySdk;
  off(eventType: string, handler: (data: unknown, eventType: string, meta?: NnzEventMeta) => void): NnzOverlaySdk;
  onAny(handler: (...event: NnzWidgetAnyEvent) => void): NnzOverlaySdk;
  /** Fires immediately with the injected settings, then again on every dashboard change. */
  onSettings(handler: (settings: Record<string, unknown>) => void): NnzOverlaySdk;
  /** Logs the message and reports it to the server as a widget runtime error. */
  reportError(message: string): void;
  /** A YouTube player page tells the bot what it plays: state is 'PLAYING', 'PAUSED' or 'ENDED', positionMs the
   *  playhead in milliseconds. Resolves true when the bot accepted it; rejects when the overlay is offline. */
  reportYouTubePlayerState(videoId: string, state: string, positionMs: number): Promise<boolean>;
  /** The media-share page tells the bot what its player does: state is 'idle', 'playing', 'paused', 'ended' or
   *  'error'; an ended or error report moves the queue on. Resolves true when the bot accepted it. */
  reportMediaSharePlayerState(requestId: string | null, state: string, positionSeconds: number, durationSeconds: number, volume: number): Promise<boolean>;
  readonly settings: Record<string, unknown>;
  readonly actions: NnzOverlayActions;
  /** Snapshots of the channel's state. The SDK sends the overlay token itself. */
  readonly data: NnzOverlayData;
  readonly spotify: NnzOverlaySpotify;
  /** Renders a template, with any of the template helpers, for this channel. */
  readonly template: NnzOverlayTemplate;
  /** Who this page is. */
  readonly widget: NnzOverlayWidget;
}

interface NnzMusicQueueItem {
  trackName: string;
  artist: string;
  imageUrl: string | null;
  durationMs: number;
  requestedBy: string | null;
  cost: number;
  code: string;
  inFlight: boolean;
}

/** Reads of the channel's current state. Each rejects when the request fails. */
interface NnzOverlayData {
  /** The track playing now, or null when nothing plays. Use it on mount, before the next music.nowPlaying event. */
  nowPlaying(): Promise<NnzWidgetEventMap['music.nowPlaying'] | null>;
  /** The playback queue, without the track playing now. */
  queue(): Promise<NnzMusicQueueItem[]>;
  /** One value from the channel's script storage, or null when the key has no value. */
  storage(key: string): Promise<string | null>;
  /** Any metrics of one stream, by key. Rejects on an unknown key or when the channel has no such stream. */
  streamStats<K extends NnzStreamMetricKey>(options: NnzStreamStatsOptions<K>): Promise<NnzStreamStats<K>>;
}

interface NnzOverlaySpotify {
  /** A short-lived Spotify token for the Web Playback SDK. Never rejects: 'blocked' means the bot may not play
   *  here (401/403), 'error' means the request failed. Ask again whenever the Spotify player wants a token. */
  playbackToken(): Promise<{ token: string } | { error: 'blocked' | 'error' }>;
}

interface NnzOverlayTemplate {
  /** Renders the template for this channel. Write helpers as {name:argument}; the names are in TemplateHelperName.
   *  values fill the plain names of the template. Rejects when the template is empty or over 4096 characters. */
  render(template: string, values?: Record<string, string | number | boolean>): Promise<string>;
}

interface NnzOverlayWidget {
  readonly id: string;
  readonly name: string;
}

/** Pipeline actions the widget runs as the channel owner, within the owner's own permissions. */
interface NnzOverlayActions {
  /** Runs one action (e.g. 'tts_synthesize', 'song_pause') with a pipeline step's parameters. Resolves with
   *  the outcome, also when the bot refuses or the action fails; rejects only when the overlay is offline. */
  invoke<T extends keyof NnzActionParams>(actionType: T, params: NnzActionParams[T], variables?: Record<string, string | number>): Promise<NnzActionResult>;
  /** Same call for an action with no required field: params may be left out. */
  invoke<T extends NnzActionsWithOptionalParams>(actionType: T, params?: NnzActionParams[T], variables?: Record<string, string | number>): Promise<NnzActionResult>;
  /** Resolves true only for the first open copy of this widget to claim the key (e.g. a redemption id), for
   *  ten minutes; false when another copy already claimed it. Claim an event before acting on it, so two OBS
   *  sources never both act.
   *  REJECTS (it never resolves false for these) when the overlay is not connected to the bot or the connection
   *  drops mid-call, and when the bot refuses the claim: not authenticated, a widget that is not this channel's
   *  (NOT_FOUND), or an empty key or one over 200 characters (VALIDATION_FAILED). Catch it, or an offline overlay
   *  surfaces as an unhandled rejection. */
  claim(key: string): Promise<boolean>;
}

interface NnzActionResult {
  success: boolean;
  output: string | null;
  error: string | null;
  /** Set when the bot refused to run it: AUTH_REQUIRED (the overlay connection is not signed in),
   *  FORBIDDEN (another channel's widget, or this widget is turned off), NOT_FOUND, VALIDATION_FAILED or
   *  RATE_LIMITED. A refusal is not exhaustive: treat an unknown code as a refusal too. */
  errorCode: string | null;
  /** The action's context after the run, e.g. tts.audioUrl and tts.durationMs. */
  variables: Record<string, string>;
}

declare const NomNomz: NnzOverlaySdk;

declare const WIDGET_SETTINGS: Record<string, unknown>;
declare const WIDGET_EVENT_SUBSCRIPTIONS: string[];

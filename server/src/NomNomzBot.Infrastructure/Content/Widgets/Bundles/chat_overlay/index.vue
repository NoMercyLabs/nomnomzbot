<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { reactive, ref, onMounted, onUnmounted } from 'vue'
import ChatMessage from './components/ChatMessage.vue'

// The overlay SDK global (window.NomNomz), injected before this bundle runs. Loose type by design.
const nnz = (window as any).NomNomz

// Port of the legacy "Twitch Chat Overlay" (01K0QVBQYEGAQKAX1CYTK1S61M): animated chat with a
// per-user colour banner, avatar bubble, staged enter sequence (banner → avatar → bubble → content),
// a one-shot shine sweep, badges, pronouns, emotes and link previews. Driven by "ChatMessage".
interface ChatOverlayConfig {
  maxMessages: number
  chatAnimationDuration: number
  chatShowDuration: number
  defaultColor: string
  width: number
  height: number
  hideCommands: boolean
  animations: {
    sequenceTiming: { bannerDelay: number; avatarDelay: number; bubbleDelay: number; contentDelay: number }
    durations: { banner: string; avatar: string; bubble: string; content: string; shine: string }
    easings: { default: string; entrance: string; exit: string }
  }
  features: {
    userBadges: boolean
    userPronouns: boolean
    userColors: boolean
    lightColorDetection: boolean
    shineEffect: boolean
    ogPreviews: boolean
    emoteSupport: boolean
  }
}

const cfg = reactive<ChatOverlayConfig>({
  maxMessages: 4,
  chatAnimationDuration: 0.75,
  chatShowDuration: 30,
  defaultColor: '#f72264',
  width: 520,
  height: 800,
  // Matches the legacy bot: a chat command line (e.g. "!sr <song>") never shows up itself — only its
  // reply does, so a viewer sees "Added <track> to the queue" in the spot where the raw command would
  // have gone, never the command text plus the reply as two separate lines.
  hideCommands: true,
  animations: {
    sequenceTiming: { bannerDelay: 0, avatarDelay: 0.3, bubbleDelay: 0.6, contentDelay: 0.9 },
    durations: { banner: '0.5s', avatar: '0.3s', bubble: '0.4s', content: '0.3s', shine: '12s' },
    easings: { default: 'ease-out', entrance: 'ease-in-out', exit: 'ease-in-out' },
  },
  features: {
    userBadges: true,
    userPronouns: true,
    userColors: true,
    lightColorDetection: true,
    shineEffect: true,
    ogPreviews: true,
    emoteSupport: true,
  },
})

interface ChatLine {
  id: string
  sourceId: string
  userId: string
  name: string
  color: string
  pronouns: string
  avatarUrl: string
  badgeUrls: string[]
  fragments: any[]
  message: string
  emoteOnly: boolean
  state: string
  // Set only on a song-request line: the resolved track the bot queued, drawn instead of a text body.
  card: any | null
  // The chatter's 7TV name-theme paint, already flattened to CSS by the server (ChatPaintDto) - null
  // when they wear none. Same shape and null-tolerant read as the built-in Chat Box widget.
  paint: any | null
}

const lines = ref<ChatLine[]>([])
let seq = 0
const timers: number[] = []

function later(fn: () => void, ms: number): void {
  timers.push(window.setTimeout(fn, ms))
}

function hexColor(c: any): string {
  return (typeof c === 'string' && /^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$/.test(c.trim())) ? c.trim() : ''
}

function firstUrl(urls: any, keys: string[]): string {
  if (!urls) return ''
  for (let i = 0; i < keys.length; i++) if (urls[keys[i]]) return urls[keys[i]]
  return ''
}

function isEmoteOnly(fragments: any[]): boolean {
  if (!fragments.length) return false
  return fragments.every((f: any) => f.type === 'emote' || (f.type === 'text' && String(f.text || '').trim() === ''))
}

// Same convention as the built-in Chat Box widget: the server-decorated payload carries `isCommand`
// when it knows for certain, but the raw '!' prefix is the reliable fallback for any command the
// server didn't tag (a viewer's own unrecognized "!whatever" still reads as a command visually).
function isCommandMessage(m: any): boolean {
  if (m.isCommand === true) return true
  const text: string = String(m.message || '').trim()
  return text.charAt(0) === '!'
}

// Who a viewer is, remembered from any line they send — including the "!sr ..." line that hideCommands is
// about to discard. A song-request card is drawn as the requester's OWN bubble, and the enrichment event
// carries only their name, so without this the bubble has no avatar, no colour and no badges. Capturing it
// above the command filter is what makes the requester's own command line the source of their identity.
interface Identity {
  color: string
  pronouns: string
  avatarUrl: string
  badgeUrls: string[]
  paint: any | null
  userId: string
}

const identities = new Map<string, Identity>()

function rememberIdentity(m: any): void {
  const login: string = String(m.username || m.displayName || '').toLowerCase()
  if (!login) return

  // Bounded like the line list: a channel's chatters are unbounded, this map must not be.
  if (identities.size >= 200 && !identities.has(login)) {
    const oldest: string | undefined = identities.keys().next().value
    if (oldest) identities.delete(oldest)
  }

  identities.set(login, {
    color: hexColor(m.color),
    pronouns: m.pronouns || '',
    avatarUrl: m.avatarUrl || m.profileImageUrl || '',
    badgeUrls: (m.badges || []).map((b: any) => firstUrl(b && b.urls, ['2', '1', '4'])).filter((u: string) => !!u),
    paint: m.paint && typeof m.paint === 'object' ? m.paint : null,
    userId: typeof m.userId === 'string' ? m.userId : '',
  })
}

function onChat(m: any): void {
  if (!m || typeof m !== 'object') return
  rememberIdentity(m)
  if (cfg.hideCommands && isCommandMessage(m)) return

  const fragments: any[] = Array.isArray(m.fragments) ? m.fragments : []
  const line: ChatLine = {
    id: (m.id || '') + '-' + (++seq),
    sourceId: String(m.id || ''),
    userId: typeof m.userId === 'string' ? m.userId : '',
    name: m.displayName || m.username || 'Someone',
    color: hexColor(m.color),
    pronouns: m.pronouns || '',
    avatarUrl: m.avatarUrl || m.profileImageUrl || '',
    badgeUrls: cfg.features.userBadges
      ? (m.badges || []).map((b: any) => firstUrl(b && b.urls, ['2', '1', '4'])).filter((u: string) => !!u)
      : [],
    fragments,
    message: m.message || '',
    emoteOnly: isEmoteOnly(fragments),
    state: 'entering',
    card: null,
    paint: m.paint && typeof m.paint === 'object' ? m.paint : null,
  }

  pushLine(line)
}

// The shared queue + enter/leave sequence. Both a chat line and a song card become a bubble the same
// way, so the animation timing can never drift between them.
function pushLine(line: ChatLine): void {
  // Legacy queue behavior: push out the oldest with its leave animation when full.
  if (lines.value.length >= Math.max(1, cfg.maxMessages)) {
    const oldest: ChatLine = lines.value[0]
    oldest.state = 'leaving'
    later(() => { lines.value = lines.value.filter((x) => x.id !== oldest.id) }, cfg.chatAnimationDuration * 1000)
  }
  lines.value = lines.value.concat([line])

  // State flips must go through the REACTIVE array entry — `line` is the raw object; the ref wraps
  // it in a proxy on insert, and mutating the raw target never notifies Vue.
  const setState = (id: string, state: string): void => {
    const entry = lines.value.find((x) => x.id === id)
    if (entry) entry.state = state
  }

  later(() => { setState(line.id, 'active') }, cfg.chatAnimationDuration * 1000)
  later(() => {
    setState(line.id, 'leaving')
    later(() => { lines.value = lines.value.filter((x) => x.id !== line.id) }, cfg.chatAnimationDuration * 1000)
  }, cfg.chatShowDuration * 1000)
}

// A song request the bot has finished processing. It arrives resolved (title, artist, artwork, provider)
// and names the viewer who asked, because this overlay hides command lines: the "!sr ..." line was never
// rendered, so there is no earlier bubble to read a name off or to swap a body into. Draw the requester's
// own bubble with the track card as its content instead.
function onEnriched(e: any): void {
  if (!e || typeof e !== 'object') return
  if (!e.title) return

  const login: string = String(e.userLogin || e.userDisplayName || '').toLowerCase()
  const who: Identity | undefined = identities.get(login)

  pushLine({
    id: (e.messageId || 'card') + '-' + (++seq),
    sourceId: String(e.messageId || ''),
    userId: who?.userId ?? '',
    name: e.userDisplayName || e.userLogin || 'Someone',
    color: who?.color ?? '',
    pronouns: who?.pronouns ?? '',
    avatarUrl: who?.avatarUrl ?? '',
    badgeUrls: cfg.features.userBadges ? (who?.badgeUrls ?? []) : [],
    fragments: [],
    message: '',
    emoteOnly: false,
    state: 'entering',
    paint: who?.paint ?? null,
    card: {
      title: e.title,
      description: e.description || '',
      imageUrl: e.imageUrl || '',
      linkUrl: e.linkUrl || '',
      provider: e.provider || '',
    },
  })
}

function mergeSettings(s: any): void {
  if (!s || typeof s !== 'object') return
  if (isFinite(Number(s.maxMessages)) && Number(s.maxMessages) > 0) cfg.maxMessages = Number(s.maxMessages)
  if (isFinite(Number(s.chatAnimationDuration)) && Number(s.chatAnimationDuration) > 0) cfg.chatAnimationDuration = Number(s.chatAnimationDuration)
  if (isFinite(Number(s.chatShowDuration)) && Number(s.chatShowDuration) > 0) cfg.chatShowDuration = Number(s.chatShowDuration)
  if (typeof s.defaultColor === 'string' && hexColor(s.defaultColor)) cfg.defaultColor = s.defaultColor
  if (isFinite(Number(s.width)) && Number(s.width) > 0) cfg.width = Number(s.width)
  if (isFinite(Number(s.height)) && Number(s.height) > 0) cfg.height = Number(s.height)
  if (typeof s.hideCommands === 'boolean') cfg.hideCommands = s.hideCommands
  if (s.animations && typeof s.animations === 'object') {
    if (s.animations.sequenceTiming && typeof s.animations.sequenceTiming === 'object')
      Object.assign(cfg.animations.sequenceTiming, s.animations.sequenceTiming)
    if (s.animations.durations && typeof s.animations.durations === 'object')
      Object.assign(cfg.animations.durations, s.animations.durations)
    if (s.animations.easings && typeof s.animations.easings === 'object')
      Object.assign(cfg.animations.easings, s.animations.easings)
  }
  if (s.features && typeof s.features === 'object') {
    const f: any = s.features
    if (typeof f.userBadges === 'boolean') cfg.features.userBadges = f.userBadges
    if (typeof f.userPronouns === 'boolean') cfg.features.userPronouns = f.userPronouns
    if (typeof f.userColors === 'boolean') cfg.features.userColors = f.userColors
    if (typeof f.lightColorDetection === 'boolean') cfg.features.lightColorDetection = f.lightColorDetection
    if (typeof f.shineEffect === 'boolean') cfg.features.shineEffect = f.shineEffect
    if (typeof f.ogPreviews === 'boolean') cfg.features.ogPreviews = f.ogPreviews
    if (typeof f.emoteSupport === 'boolean') cfg.features.emoteSupport = f.emoteSupport
  }
}

// A moderator or AutoMod deleted ONE message: drop the line(s) drawn from that raw message id.
function onMessageDeleted(d: any): void {
  const id: string = d && d.messageId ? String(d.messageId) : ''
  if (!id) return
  lines.value = lines.value.filter((l: ChatLine) => l.sourceId !== id)
}

// A whole-channel "Clear chat": every rendered line goes.
function onChatCleared(): void {
  lines.value = []
}

// A per-chatter purge: every line from that one person goes.
function onUserMessagesCleared(d: any): void {
  const uid: string = d && d.targetUserId ? String(d.targetUserId) : ''
  if (!uid) return
  lines.value = lines.value.filter((l: ChatLine) => l.userId !== uid)
}

onMounted(() => {
  if (!nnz) return
  nnz.onSettings(mergeSettings)
  nnz.on('ChatMessage', onChat)
  nnz.on('ChatMessageEnriched', onEnriched)
  nnz.on('MessageDeleted', onMessageDeleted)
  nnz.on('ChatCleared', onChatCleared)
  nnz.on('UserMessagesCleared', onUserMessagesCleared)
})

onUnmounted(() => {
  timers.forEach((t: number) => window.clearTimeout(t))
  if (!nnz) return
  nnz.off('ChatMessage', onChat)
  nnz.off('ChatMessageEnriched', onEnriched)
  nnz.off('MessageDeleted', onMessageDeleted)
  nnz.off('ChatCleared', onChatCleared)
  nnz.off('UserMessagesCleared', onUserMessagesCleared)
})
</script>

<template>
  <div
    class="nnz-stoney-chat"
    :style="{
      '--accent': cfg.defaultColor,
      '--anim-dur': cfg.chatAnimationDuration + 's',
      '--banner-dur': cfg.animations.durations.banner,
      '--avatar-dur': cfg.animations.durations.avatar,
      '--bubble-dur': cfg.animations.durations.bubble,
      '--content-dur': cfg.animations.durations.content,
      '--shine-dur': cfg.animations.durations.shine,
      '--banner-delay': cfg.animations.sequenceTiming.bannerDelay + 's',
      '--avatar-delay': cfg.animations.sequenceTiming.avatarDelay + 's',
      '--bubble-delay': cfg.animations.sequenceTiming.bubbleDelay + 's',
      '--content-delay': cfg.animations.sequenceTiming.contentDelay + 's',
      '--ease-default': cfg.animations.easings.default,
      '--ease-entrance': cfg.animations.easings.entrance,
      width: 'calc(' + cfg.width + 'px - 32px)',
      height: 'calc(' + cfg.height + 'px - 32px)',
    }"
  >
    <ChatMessage v-for="l in lines" :key="l.id" :line="l" :default-color="cfg.defaultColor" :features="cfg.features" />
  </div>
</template>

<style scoped>
.nnz-stoney-chat *,
.nnz-stoney-chat *::before,
.nnz-stoney-chat *::after {
  box-sizing: border-box;
}
.nnz-stoney-chat {
  position: fixed;
  top: 0;
  left: 0;
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 8px 4px 0 16px;
  margin: 16px;
  overflow: hidden;
  pointer-events: none;
  box-sizing: border-box;
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
}
</style>

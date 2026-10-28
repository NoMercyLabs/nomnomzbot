<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import MessageFragment from './MessageFragment.vue'
import OgPreview from './OgPreview.vue'
import SongCard from './SongCard.vue'
import type { ChatLine, Features } from '../types'

const props = defineProps<{ line: ChatLine; defaultColor: string; features: Features }>()

function lineVars(): Record<string, string> {
  const base: string = (props.features.userColors && props.line.color) ? props.line.color : props.defaultColor
  const vars: Record<string, string> = {
    '--user-300': `hsl(from ${base} h calc(s * .30) l)`,
    '--user-500': `hsl(from ${base} h calc(s * .50) l)`,
    '--user-700': `hsl(from ${base} h s calc(l * .70))`,
  }
  // The highlight a channel gave a bot overlay line, as its design token color.
  if (props.line.look?.color) vars['--hl'] = props.line.look.color
  return vars
}

// Legacy tooLight(): perceptual brightness > 140 flips the banner text to black.
function tooLight(color: string): boolean {
  const h: string = color.replace('#', '')
  const full: string = h.length === 3 ? h.split('').map((c) => c + c).join('') : h
  const r: number = parseInt(full.slice(0, 2), 16)
  const g: number = parseInt(full.slice(2, 4), 16)
  const b: number = parseInt(full.slice(4, 6), 16)
  return (0.299 * r + 0.587 * g + 0.114 * b) > 140
}

function bannerTextClass(): string {
  if (!props.features.lightColorDetection) return 'text-light'
  const base: string = (props.features.userColors && props.line.color) ? props.line.color : props.defaultColor
  return tooLight(base) ? 'text-dark' : 'text-light'
}

// The viewer's 7TV paint IS the name bar's background (gradient or picture, with the paint's own shadows
// as a glow). The name text on it is plain black or white by contrast. Without a paint the bar keeps the
// chat-colour gradient from the stylesheet.
function hasPaint(): boolean {
  const p: NnzChatPaintDto | null = props.line.paint
  return !!p && !!(p.backgroundImage || p.color)
}

function paintColors(p: NnzChatPaintDto): string[] {
  const stops: string[] = (p.backgroundImage || '').match(/#[0-9a-fA-F]{6,8}\b/g) || []
  return stops.length ? stops : p.color ? [p.color] : []
}

function bannerPaintStyle(): Record<string, string> {
  const p: NnzChatPaintDto | null = props.line.paint
  if (!p || !hasPaint()) return {}
  const style: Record<string, string> = {}
  if (p.backgroundImage) {
    style['background-image'] = p.backgroundImage
    if (p.backgroundImage.startsWith('url(')) {
      style['background-size'] = 'cover'
      style['background-position'] = 'center'
      style['background-repeat'] = 'no-repeat'
    }
  } else {
    style['background-image'] = 'none'
  }
  const colors: string[] = paintColors(p)
  if (colors.length) style['background-color'] = colors[0] ?? ''
  const dark: boolean = colors.length ? colors.filter((c: string) => tooLight(c)).length * 2 >= colors.length : false
  style.color = colors.length && dark ? '#000' : '#fff'
  if (!colors.length) style['text-shadow'] = '0 1px 3px rgba(0, 0, 0, 0.9)'
  if (p.textShadow) style['box-shadow'] = p.textShadow
  return style
}

// A link fragment's resolved OpenGraph preview (the wire field is `linkPreview`).
const ogPreviews: NnzChatLinkPreviewDto[] = props.line.fragments
  .map((fr: NnzChatFragmentDto) => (fr.type === 'link' ? fr.linkPreview : null))
  .filter((p): p is NnzChatLinkPreviewDto => !!p && !!(p.title || p.imageUrl))
</script>

<template>
  <div
    class="message-container"
    :class="[line.state]"
    :data-highlight="line.look?.highlight || undefined"
    :data-size="line.look?.large ? 'large' : undefined"
    :style="lineVars()"
  >
    <div class="head">
      <div class="shine-wrapper" :class="{ 'shine-on': features.shineEffect }">
        <div class="banner" :class="[bannerTextClass(), { 'banner-painted': hasPaint() }]" :style="bannerPaintStyle()">
          <template v-if="features.userBadges">
            <img v-for="(b, i) in line.badgeUrls" :key="i" class="badge" :src="b" alt="badge">
          </template>
          <span class="name">{{ line.name }}</span>
          <span v-if="features.userPronouns && line.pronouns" class="pron">({{ line.pronouns }})</span>
        </div>
      </div>
      <div v-if="line.avatarUrl" class="avatar">
        <img :src="line.avatarUrl" alt="profile picture">
      </div>
    </div>

    <div class="message-bubble">
      <div class="message-content" :class="{ 'emote-only': line.emoteOnly }">
        <!-- A processed song request: the bubble's whole body IS the track card. -->
        <SongCard v-if="line.card" :card="line.card" />
        <template v-else-if="line.fragments.length">
          <MessageFragment
            v-for="(fr, i) in line.fragments"
            :key="i"
            :fragment="fr"
            :emote-only="line.emoteOnly"
            :emote-support="features.emoteSupport"
          />
          <!-- OG link preview cards, appended after the body (legacy behavior). -->
          <template v-if="features.ogPreviews">
            <OgPreview
              v-for="(preview, i) in ogPreviews"
              :key="'og-' + i"
              :title="preview.title ?? ''"
              :description="preview.description ?? undefined"
              :host="preview.host"
              :image-url="preview.imageUrl ?? undefined"
            />
          </template>
        </template>
        <template v-else>{{ line.message }}</template>
        <!-- Drawn as text (never markup), like the song request mark: the line is the bot's, the viewer only asked. -->
        <span v-if="line.look && line.look.requestedBy" class="requested-by">requested by {{ line.look.requestedBy }}</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.message-container {
  display: flex;
  flex-direction: column;
  margin-top: 12px;
  opacity: 1;
  transform: translateX(120%);
  transition: transform var(--anim-dur) ease-in-out, opacity var(--anim-dur) ease-in-out;
  will-change: transform, opacity;
}
.message-container.active {
  transform: translateX(0);
}
.message-container.leaving {
  transform: translateX(-120%);
  opacity: 0;
}

.head {
  position: relative;
  padding-top: 12px;
}
.shine-wrapper {
  position: relative;
  z-index: 10;
  overflow: hidden;
  margin-top: -24px;
  transform: translateX(120%);
  transition: transform var(--banner-dur) var(--ease-default);
  transition-delay: var(--banner-delay);
}
.message-container.active .shine-wrapper {
  transform: translateX(0);
}
.banner {
  position: relative;
  display: flex;
  flex-direction: row;
  align-items: center;
  gap: 4px;
  height: 32px;
  padding: 0 48px 0 8px;
  border-radius: 8px;
  background-image: linear-gradient(45deg, var(--user-300), var(--user-500) 23%, var(--user-700));
}
.banner.text-light { color: #fff; }
.banner.text-dark { color: #000; }
.badge {
  width: 20px;
  height: 20px;
}
.name {
  font-weight: 700;
  font-size: 20px;
  line-height: 1;
}
.pron {
  font-size: 12.8px;
  font-weight: 600;
  font-family: ui-monospace, monospace;
  margin-left: 2px;
  white-space: nowrap;
  transform: translateY(2px);
}

.shine-wrapper.shine-on::after {
  content: '';
  position: absolute;
  inset: 0;
  pointer-events: none;
  z-index: 10;
  transform: translateX(100%);
  background: linear-gradient(
    65deg,
    rgba(255, 255, 255, 0) 0%,
    rgba(255, 255, 255, 0) 35%,
    rgba(255, 255, 255, 0.2) 50%,
    rgba(128, 186, 232, 0) 65%,
    rgba(128, 186, 232, 0) 99%,
    rgba(125, 185, 232, 0) 100%
  );
}
.message-container.active .shine-wrapper.shine-on::after {
  animation: shine var(--shine-dur) forwards;
  animation-iteration-count: 1;
}
@keyframes shine {
  1% { transform: translateX(100%); }
  15%, 100% { transform: translateX(-100%); }
}

.avatar {
  position: absolute;
  width: 56px;
  height: 56px;
  right: -4px;
  top: -20px;
  z-index: 20;
  border-radius: 50%;
  overflow: hidden;
  background: var(--user-700);
  border: 1px solid var(--user-700);
  transform: scale(0);
  transition: transform var(--avatar-dur) var(--ease-default);
  transition-delay: var(--avatar-delay);
}
.message-container.active .avatar {
  transform: scale(1);
}
.avatar img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.message-bubble {
  position: relative;
  overflow: clip;
  background: #171717;
  display: flex;
  padding: 20px 12px 12px 12px;
  border-radius: 8px;
  margin: -12px 16px 0 -16px;
  transform: scaleY(0);
  transform-origin: top;
  transition: transform var(--bubble-dur) var(--ease-default);
  transition-delay: var(--bubble-delay);
}
.message-container.active .message-bubble {
  transform: scaleY(1);
}

.message-content {
  position: relative;
  z-index: 10;
  width: 100%;
  max-height: 384px;
  overflow: clip;
  color: #fff;
  font-size: 1.1rem;
  line-height: 1.75rem;
  font-weight: 500;
  word-break: break-word;
  opacity: 0;
  transition: opacity var(--content-dur) var(--ease-default);
  transition-delay: var(--content-delay);
}
.message-container.active .message-content {
  opacity: 1;
}
/* The look a channel gave a bot overlay line: the highlight (--hl) as the bubble's left edge plus a soft wash, and a
   larger size. */
.message-container[data-highlight] .message-bubble {
  border-left: 4px solid var(--hl);
  background-image: linear-gradient(color-mix(in srgb, var(--hl) 14%, transparent), color-mix(in srgb, var(--hl) 14%, transparent));
}
.message-container[data-size='large'] .message-content {
  font-size: 1.5rem;
  line-height: 2.1rem;
}
/* Small muted mark under the reply, the way the song request card marks its source. */
.requested-by {
  display: block;
  margin-top: 4px;
  font-size: 0.8rem;
  line-height: 1;
  font-weight: 500;
  color: rgba(255, 255, 255, 0.55);
}
.message-content.emote-only {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  flex-wrap: wrap;
}
</style>

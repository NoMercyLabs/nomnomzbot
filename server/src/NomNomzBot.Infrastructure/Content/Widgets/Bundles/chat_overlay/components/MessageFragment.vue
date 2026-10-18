<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
const props = defineProps<{ fragment: any; emoteOnly: boolean; emoteSupport: boolean }>()

function firstUrl(urls: any, keys: string[]): string {
  if (!urls) return ''
  for (let i = 0; i < keys.length; i++) if (urls[keys[i]]) return urls[keys[i]]
  return ''
}

function emoteUrl(fr: any, emoteOnly: boolean): string {
  const keys: string[] = emoteOnly ? ['3', '2', '1'] : ['2', '1', '3']
  return firstUrl(fr && fr.emote && fr.emote.urls, keys)
}

function cheermoteUrl(fr: any): string {
  return firstUrl(fr && fr.cheermote && fr.cheermote.urls, ['2', '1', '3'])
}

// Twitch's native chat GIF (GIPHY-backed, Tier 2+ subscriber feature): the fragment already carries a
// real, directly-fetchable url — no lookup needed, just render it.
function gifUrl(fr: any): string {
  return (fr && fr.gif && typeof fr.gif.url === 'string') ? fr.gif.url : ''
}

function hexColor(c: any): string {
  return (typeof c === 'string' && /^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$/.test(c.trim())) ? c.trim() : ''
}

function fragColor(c: any): Record<string, string> {
  const hex: string = hexColor(c)
  return hex ? { color: hex } : {}
}
</script>

<template>
  <span v-if="fragment.type === 'html'" class="frag-html" v-html="fragment.text"></span>
  <img v-else-if="fragment.type === 'emote' && emoteSupport && fragment.emote"
    class="emote" :class="{ big: emoteOnly }" :src="emoteUrl(fragment, emoteOnly)" :alt="fragment.text">
  <!-- Twitch native chat GIF (GIPHY-backed, Tier 2+ sub feature): renders inline like an emote — just at
       GIF scale, since it IS the whole message, not decoration on top of one. -->
  <img v-else-if="fragment.type === 'gif' && gifUrl(fragment)" class="chat-gif" :src="gifUrl(fragment)" :alt="fragment.text || 'GIF'">
  <template v-else-if="fragment.type === 'cheermote' && fragment.cheermote">
    <img v-if="emoteSupport && cheermoteUrl(fragment)" class="emote" :src="cheermoteUrl(fragment)" :alt="fragment.text">
    <span class="cheer-bits" :style="fragColor(fragment.cheermote.colorHex)">{{ fragment.cheermote.bits }}</span>
  </template>
  <span v-else-if="fragment.type === 'mention' && fragment.mention" class="mention" :style="fragColor(fragment.mention.color)">{{ '@' + (fragment.mention.displayName || fragment.mention.username || '') }}</span>
  <a v-else-if="fragment.type === 'link' && fragment.linkUrl" class="link" :href="fragment.linkUrl" target="_blank" rel="noopener noreferrer">{{ fragment.text || fragment.linkUrl }}</a>
  <span v-else>{{ fragment.text || '' }}</span>
</template>

<style scoped>
.emote {
  height: 28px;
  width: auto;
  margin: 0 4px;
  vertical-align: middle;
  display: inline;
}
.emote.big {
  height: 72px;
}
.chat-gif {
  /* Content, not a glyph — big enough to actually read, capped so one GIF can't blow out the chat bubble. */
  display: block;
  max-width: 100%;
  max-height: 8em;
  width: auto;
  border-radius: 6px;
  margin: 4px 0;
}
.cheer-bits {
  font-weight: 700;
}
.mention {
  font-weight: 700;
  color: var(--accent, #f72264);
}
.link {
  color: var(--accent, #f72264);
  text-decoration: underline;
}
.frag-html {
  display: inline;
}
.frag-html :deep(img) {
  width: auto;
  vertical-align: middle;
}
.frag-html :deep(*) {
  max-width: 100%;
}
</style>

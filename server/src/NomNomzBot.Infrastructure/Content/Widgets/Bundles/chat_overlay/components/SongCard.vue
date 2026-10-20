<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
// The card a processed song request renders inside the REQUESTER's own bubble. Ported from the legacy chat
// overlay's SpotifyCard (widget 01K0QVBQYEGAQKAX1CYTK1S61M), which the owner keeps as the reference: the card
// is washed in the SOURCE's brand colour and wears its real logo, rather than sitting flat with a colour chip.
// YouTube gets the same treatment in its own brand, so the two read as one family instead of two designs.
import type { SongCardData } from '../types'

const props = defineProps<{ card: SongCardData }>()

interface Source {
  name: string
  color: string
  path: string
}

const SPOTIFY: Source = {
  name: 'Spotify',
  color: '29, 185, 84',
  path: 'M12 0C5.4 0 0 5.4 0 12s5.4 12 12 12 12-5.4 12-12S18.66 0 12 0zm5.521 17.34c-.24.359-.66.48-1.021.24-2.82-1.74-6.36-2.101-10.561-1.141-.418.122-.779-.179-.899-.539-.12-.421.18-.78.54-.9 4.56-1.021 8.52-.6 11.64 1.32.42.18.479.659.301 1.02zm1.44-3.3c-.301.42-.841.6-1.262.3-3.239-1.98-8.159-2.58-11.939-1.38-.479.12-1.02-.12-1.14-.6-.12-.48.12-1.021.6-1.141C9.6 9.9 15 10.561 18.72 12.84c.361.181.54.78.241 1.2zm.12-3.36C15.24 8.4 8.82 8.16 5.16 9.301c-.6.179-1.2-.181-1.38-.721-.18-.601.18-1.2.72-1.381 4.26-1.26 11.28-1.02 15.721 1.621.539.3.719 1.02.419 1.56-.299.421-1.02.599-1.559.3z',
}

const YOUTUBE: Source = {
  name: 'YouTube',
  color: '255, 0, 0',
  path: 'M23.498 6.186a3.016 3.016 0 0 0-2.122-2.136C19.505 3.545 12 3.545 12 3.545s-7.505 0-9.377.505A3.017 3.017 0 0 0 .502 6.186C0 8.07 0 12 0 12s0 3.93.502 5.814a3.016 3.016 0 0 0 2.122 2.136c1.871.505 9.376.505 9.376.505s7.505 0 9.377-.505a3.015 3.015 0 0 0 2.122-2.136C24 15.93 24 12 24 12s0-3.93-.502-5.814zM9.545 15.568V8.432L15.818 12l-6.273 3.568z',
}

// An unknown provider keeps the neutral treatment rather than borrowing a brand it is not.
const NEUTRAL: Source = { name: 'Song Request', color: '255, 255, 255', path: '' }

function source(): Source {
  const p: string = String(props.card.provider || '').toLowerCase()
  return p === 'spotify' ? SPOTIFY : p === 'youtube' ? YOUTUBE : NEUTRAL
}

function cardStyle(): Record<string, string> {
  const c: string = source().color
  return {
    background: `linear-gradient(135deg, rgba(${c}, 0.12) 0%, rgba(${c}, 0.04) 100%)`,
    border: `1px solid rgba(${c}, 0.2)`,
  }
}

function markStyle(): Record<string, string> {
  return { color: `rgb(${source().color})` }
}
</script>

<template>
  <div class="song-card" :style="cardStyle()">
    <img v-if="card.imageUrl" class="song-art" :src="card.imageUrl" :alt="card.title || 'Album art'">
    <div v-else class="song-art song-art-empty">
      <svg v-if="source().path" class="song-glyph-lg" viewBox="0 0 24 24" fill="currentColor" :style="markStyle()">
        <path :d="source().path" />
      </svg>
    </div>
    <div class="song-text">
      <span class="song-title">{{ card.title || 'Song Request' }}</span>
      <span v-if="card.description" class="song-artist">{{ card.description }}</span>
      <span class="song-mark" :style="markStyle()">
        <svg v-if="source().path" class="song-glyph" viewBox="0 0 24 24" fill="currentColor">
          <path :d="source().path" />
        </svg>
        Song Request
      </span>
    </div>
  </div>
</template>

<style scoped>
.song-card {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-top: 4px;
  padding: 10px;
  border-radius: 8px;
  overflow: hidden;
}
.song-art {
  /* Height is fixed; width follows the source. Spotify art is square (56x56), a YouTube thumbnail is 4:3, and
     the legacy card's fixed square cropped its left and right edges away — it only ever carried Spotify. */
  height: 56px;
  width: auto;
  max-width: 96px;
  flex: 0 0 auto;
  border-radius: 4px;
  object-fit: cover;
  box-shadow: 0 10px 15px -3px rgba(0, 0, 0, 0.35), 0 4px 6px -4px rgba(0, 0, 0, 0.35);
}
/* No artwork: the source's own glyph stands in, so the row keeps its shape instead of collapsing. */
.song-art-empty {
  width: 56px;
  display: flex;
  align-items: center;
  justify-content: center;
  background: #404040;
  box-shadow: none;
}
.song-glyph-lg {
  width: 24px;
  height: 24px;
}
.song-text {
  display: flex;
  flex-direction: column;
  min-width: 0;
  flex: 1;
}
.song-title {
  font-size: 14px;
  font-weight: 600;
  line-height: 1.25;
  color: #fff;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.song-artist {
  margin-top: 2px;
  font-size: 12px;
  line-height: 1.25;
  color: rgba(255, 255, 255, 0.5);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
/* The brand mark is the only full-chroma element on the card — the wash behind it is the same hue at 12%. */
.song-mark {
  display: flex;
  align-items: center;
  gap: 4px;
  margin-top: 4px;
  font-size: 12px;
  line-height: 1;
}
.song-glyph {
  width: 14px;
  height: 14px;
  flex: 0 0 auto;
}
</style>

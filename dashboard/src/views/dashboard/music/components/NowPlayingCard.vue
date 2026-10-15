<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import { useIntervalFn } from '@vueuse/core';
import Music from '~icons/nnz/note-eighth-pair';
import Next from '~icons/nnz/next';
import Pause from '~icons/nnz/pause';
import Play from '~icons/nnz/play';
import Previous from '~icons/nnz/previous';
import Repeat from '~icons/nnz/repeat';
import RepeatOne from '~icons/nnz/repeat-one';
import Shuffle from '~icons/nnz/shuffle';
import VolumeMuted from '~icons/nnz/volume-muted';
import VolumeTwo from '~icons/nnz/volume-two';
import { computed, ref, watch } from 'vue';

import type { NowPlaying } from '@/api/music';
import { Button } from '@/components/ui/button';
import { Slider } from '@/components/ui/slider';

import { formatClock } from './formatClock';

type RepeatMode = 'off' | 'context' | 'track';

const props = defineProps<{
  track: NowPlaying;
  // The moment the bot last reported the position; the bar moves on from there while the song plays.
  observedAt: number;
}>();

const emit = defineEmits<{
  pause: [];
  resume: [];
  skip: [];
  previous: [];
  seek: [positionMs: number];
  shuffle: [enabled: boolean];
  repeat: [mode: RepeatMode];
  volume: [percent: number];
}>();

const now = ref(Date.now());

useIntervalFn(() => {
  now.value = Date.now();
}, 1000);

const durationMs = computed<number>(() => Number(props.track.durationMs));
const positionMs = computed<number>(() => {
  const reported = Number(props.track.progressMs);
  const elapsed = props.track.isPlaying
    ? Math.max(0, now.value - props.observedAt)
    : 0;

  return Math.min(durationMs.value, reported + elapsed);
});

// While a thumb is held the slider shows the hand, not the clock; the release sends one request.
const seeking = ref<number[] | null>(null);
const shownPosition = computed<number>(() => seeking.value?.[0] ?? positionMs.value);

function commitSeek(value: number[] | undefined): void {
  seeking.value = null;

  if (value?.[0] !== undefined) {
    emit('seek', value[0]);
  }
}

const volume = ref<number[]>([Number(props.track.volume)]);

watch(
  () => props.track.volume,
  (next) => {
    volume.value = [Number(next)];
  },
);

function commitVolume(value: number[] | undefined): void {
  if (value?.[0] !== undefined) {
    emit('volume', value[0]);
  }
}

const repeatMode = computed(() => props.track.repeatState as RepeatMode);
const nextRepeat = computed<RepeatMode>(() => {
  const order: RepeatMode[] = ['off', 'context', 'track'];

  return order[(order.indexOf(repeatMode.value) + 1) % order.length] ?? 'off';
});
</script>

<template>
  <!-- Every control is always drawn, disabled when the player cannot do it, so nothing jumps between songs. -->
  <div data-player class="flex h-full flex-col gap-5 p-4">
    <div class="flex min-h-0 items-center gap-5">
      <img
        v-if="track.imageUrl"
        :src="track.imageUrl"
        alt=""
        class="size-44 flex-none rounded-lg object-cover shadow-md"
      />
      <div
        v-else
        class="flex size-44 flex-none items-center justify-center rounded-lg bg-muted text-muted-foreground"
      >
        <Music class="size-10" aria-hidden="true" />
      </div>
      <div class="grid min-w-0 flex-1 gap-1">
        <p data-player-title class="line-clamp-2 text-2xl font-semibold">{{ track.trackName }}</p>
        <p class="truncate text-base text-muted-foreground">{{ track.artist }}</p>
        <p data-player-requester class="h-5 truncate text-sm text-muted-foreground">
          <template v-if="track.requestedBy">
            {{ $t('dashboard.music.now.requestedBy', { name: track.requestedBy }) }}
          </template>
        </p>
      </div>
    </div>

    <div class="mt-auto grid gap-5">
      <div class="flex items-center gap-4 text-sm tabular-nums text-muted-foreground">
        <span class="w-10 text-right">{{ formatClock(shownPosition) }}</span>
        <Slider
          data-player-seek
          class="flex-1"
          :model-value="[shownPosition]"
          :max="Math.max(durationMs, 1)"
          :step="1000"
          :disabled="!track.canSeek"
          :aria-label="$t('dashboard.music.now.seek')"
          @update:model-value="seeking = $event ?? null"
          @value-commit="commitSeek"
        />
        <span class="w-10">{{ formatClock(durationMs) }}</span>
      </div>

      <!-- Three weights under the bar: play is the one filled key, skip keys are plain, modes are small. -->
      <div class="grid grid-cols-[1fr_auto_1fr] items-center gap-4">
        <span aria-hidden="true" />
        <div class="flex items-center gap-5">
          <Button
            variant="ghost"
            size="icon-lg"
            class="[&_svg]:size-6"
            :disabled="!track.canSetShuffle"
            :aria-pressed="track.shuffleState"
            :aria-label="$t('dashboard.music.now.shuffle')"
            :class="track.shuffleState ? 'text-primary' : 'text-muted-foreground'"
            @click="emit('shuffle', !track.shuffleState)"
          >
            <Shuffle aria-hidden="true" />
          </Button>
          <Button
            variant="secondary"
            size="icon-lg"
            class="size-12 [&_svg]:size-8"
            :disabled="!track.canSkipPrevious"
            :aria-label="$t('dashboard.music.now.previous')"
            @click="emit('previous')"
          >
            <Previous aria-hidden="true" />
          </Button>
          <Button
            v-if="track.isPlaying"
            size="icon-lg"
            class="size-16 rounded-full [&_svg]:size-9"
            :disabled="!track.canPause"
            :aria-label="$t('dashboard.music.now.pause')"
            @click="emit('pause')"
          >
            <Pause aria-hidden="true" />
          </Button>
          <Button
            v-else
            size="icon-lg"
            class="size-16 rounded-full [&_svg]:size-9"
            :disabled="!track.canResume"
            :aria-label="$t('dashboard.music.now.play')"
            @click="emit('resume')"
          >
            <Play aria-hidden="true" />
          </Button>
          <Button
            variant="secondary"
            size="icon-lg"
            class="size-12 [&_svg]:size-8"
            :disabled="!track.canSkipNext"
            :aria-label="$t('dashboard.music.now.skip')"
            @click="emit('skip')"
          >
            <Next aria-hidden="true" />
          </Button>
          <Button
            variant="ghost"
            size="icon-lg"
            class="[&_svg]:size-6"
            :disabled="!track.canSetRepeat"
            :aria-label="$t(`dashboard.music.now.repeat.${repeatMode}`)"
            :class="repeatMode === 'off' ? 'text-muted-foreground' : 'text-primary'"
            @click="emit('repeat', nextRepeat)"
          >
            <RepeatOne v-if="repeatMode === 'track'" aria-hidden="true" />
            <Repeat v-else aria-hidden="true" />
          </Button>
        </div>

        <div class="flex items-center justify-end gap-2">
          <VolumeMuted v-if="volume[0] === 0" class="size-5 flex-none text-muted-foreground" aria-hidden="true" />
          <VolumeTwo v-else class="size-5 flex-none text-muted-foreground" aria-hidden="true" />
          <Slider
            v-model="volume"
            data-player-volume
            class="w-36"
            :max="100"
            :step="1"
            :aria-label="$t('dashboard.music.now.volume')"
            @value-commit="commitVolume"
          />
        </div>
      </div>
    </div>
  </div>
</template>

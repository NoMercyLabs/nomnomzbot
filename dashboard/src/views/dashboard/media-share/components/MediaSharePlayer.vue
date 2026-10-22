<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The stream player: what plays on the media share overlay now, and the keys that drive it.
// Play or Pause is the one filled key; Skip and Stop are plain; volume sits beside them.
// Stoney 2026-10-20: "media share is still not showing any playback opportunities, it is not playing on stream
// either, i need full control on all fronts on this".
import Next from '~icons/nnz/next';
import Pause from '~icons/nnz/pause';
import Play from '~icons/nnz/play';
import Stop from '~icons/nnz/stop';
import VolumeMuted from '~icons/nnz/volume-muted';
import VolumeTwo from '~icons/nnz/volume-two';
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { useRouter } from 'vue-router';

import type { MediaPlaybackCommand, MediaSharePlayerState, MediaShareRequest } from '@/api/mediaShare';
import IconAction from '@/components/layout/IconAction.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Slider } from '@/components/ui/slider';
import { clockLength } from '@/lib/time';

const props = defineProps<{
  // The queue item the bot marks as playing; null when nothing plays.
  current: MediaShareRequest | null;
  // The last state the overlay reported; null until it reports this session.
  player: MediaSharePlayerState | null;
  // Approved items wait in the queue.
  hasNext: boolean;
  canControl: boolean;
  busy: boolean;
  // The media share browser source is loaded in OBS; nothing plays on stream without it.
  overlayLoaded: boolean;
}>();

const router = useRouter();

const emit = defineEmits<{
  command: [command: MediaPlaybackCommand, value?: number];
}>();

const paused = computed<boolean>(() => props.player?.state === 'paused');
const playing = computed<boolean>(() => props.current !== null && !paused.value);
const canPlay = computed<boolean>(() => props.current !== null || props.hasNext);

const volume = ref<number[]>([props.player?.volume ?? 100]);

watch(
  () => props.player?.volume,
  (next: number | undefined) => {
    if (next !== undefined) {
      volume.value = [next];
    }
  },
);

// The place in the item: the overlay's last report, moved on by the clock while it plays.
const position = ref<number[]>([props.player?.positionSeconds ?? 0]);
const duration = computed<number>(() => props.player?.durationSeconds ?? 0);
const showsTime = computed<boolean>(() => props.current !== null && duration.value > 0);

watch(
  () => props.player?.positionSeconds,
  (next: number | undefined) => {
    position.value = [next ?? 0];
  },
);

const ticker = setInterval(() => {
  if (playing.value && showsTime.value) {
    position.value = [Math.min(duration.value, (position.value[0] ?? 0) + 1)];
  }
}, 1000);

onBeforeUnmount(() => clearInterval(ticker));

const time = computed<string>(() =>
  `${clockLength(position.value[0] ?? 0)} / ${clockLength(duration.value)}`);

function commitSeek(value: number[]): void {
  emit('command', 'seek', value[0] ?? 0);
}

const title = computed<string>(() =>
  props.current?.title ?? props.current?.sourceUrl ?? '');

function togglePlay(): void {
  emit('command', playing.value
    ? 'pause'
    : 'play');
}

function commitVolume(value: number[]): void {
  emit('command', 'volume', value[0] ?? 0);
}
</script>

<template>
  <PanelCard
    tier="lead"
    data-media-share-player
    :title="$t('dashboard.mediaShare.player.title')"
    :description="$t('dashboard.mediaShare.player.description')"
  >
    <div class="flex flex-col gap-4">
      <div class="flex min-w-0 items-center gap-3">
        <img
          v-if="current?.thumbnailUrl"
          :src="current.thumbnailUrl"
          alt=""
          class="size-14 flex-none rounded-md object-cover"
        />
        <div class="min-w-0 flex-1">
          <p v-if="current" data-player-title class="truncate text-sm font-medium">
            {{ title }}
          </p>
          <p v-if="current" class="truncate text-sm text-muted-foreground">
            {{ $t("dashboard.mediaShare.queue.by", { name: current.requesterDisplayName }) }}
          </p>
          <p v-else data-player-empty class="text-sm text-muted-foreground">
            {{
              hasNext
                ? $t("dashboard.mediaShare.player.ready")
                : $t("dashboard.mediaShare.player.empty")
            }}
          </p>
        </div>
        <Badge v-if="current" data-player-state variant="secondary">
          {{
            paused
              ? $t("dashboard.mediaShare.player.paused")
              : $t("dashboard.mediaShare.player.playing")
          }}
        </Badge>
      </div>

      <div v-if="showsTime" class="flex items-center gap-3">
        <Slider
          v-if="canControl"
          v-model="position"
          data-player-seek
          class="flex-1"
          :max="duration"
          :step="1"
          :thumb-label="$t('dashboard.mediaShare.player.seek')"
          @value-commit="commitSeek"
        />
        <span data-player-time class="flex-none text-sm tabular-nums text-muted-foreground">
          {{ time }}
        </span>
      </div>

      <div v-if="canControl" data-player-controls class="flex flex-wrap items-center gap-2">
        <Button
          data-action="toggle-play"
          :disabled="busy || !canPlay"
          @click="togglePlay"
        >
          <Pause v-if="playing" aria-hidden="true" />
          <Play v-else aria-hidden="true" />
          {{
            playing
              ? $t("dashboard.mediaShare.player.pause")
              : $t("dashboard.mediaShare.player.play")
          }}
        </Button>
        <IconAction
          v-if="current"
          :label="$t('dashboard.mediaShare.player.skip')"
          data-action="player-skip"
          @click="emit('command', 'skip')"
        >
          <Next class="size-5" aria-hidden="true" />
        </IconAction>
        <IconAction
          v-if="current"
          :label="$t('dashboard.mediaShare.player.stop')"
          data-action="player-stop"
          @click="emit('command', 'stop')"
        >
          <Stop class="size-5" aria-hidden="true" />
        </IconAction>
        <div class="ml-auto flex items-center gap-2">
          <VolumeMuted
            v-if="volume[0] === 0"
            class="size-5 flex-none text-muted-foreground"
            aria-hidden="true"
          />
          <VolumeTwo v-else class="size-5 flex-none text-muted-foreground" aria-hidden="true" />
          <Slider
            v-model="volume"
            data-player-volume
            class="w-28"
            :max="100"
            :step="1"
            :thumb-label="$t('dashboard.mediaShare.player.volume')"
            @value-commit="commitVolume"
          />
        </div>
      </div>
      <p v-if="canControl && !canPlay" data-player-reason class="text-sm text-muted-foreground">
        {{ $t("dashboard.mediaShare.player.nothingToPlay") }}
      </p>
      <div
        v-if="!overlayLoaded"
        data-player-overlay-missing
        class="flex flex-wrap items-center gap-3 rounded-md border px-3 py-2"
      >
        <p class="min-w-0 flex-1 text-sm text-muted-foreground">
          {{ $t("dashboard.mediaShare.player.overlayMissing") }}
        </p>
        <Button
          variant="outline"
          data-action="setup-overlay"
          @click="router.push({ name: 'dashboard-obs', query: { view: 'overlays' }, hash: '#required-overlays' })"
        >
          {{ $t("dashboard.mediaShare.player.setupOverlay") }}
        </Button>
      </div>
    </div>
  </PanelCard>
</template>

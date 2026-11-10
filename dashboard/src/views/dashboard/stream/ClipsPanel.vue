<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->

<script setup lang="ts">
// The channel's Twitch clips, newest first. Each clip can go to the streamer's YouTube channel.
// Google allows the app 100 uploads a day in total, so the panel says how many are left.
import type { InfiniteData } from '@tanstack/vue-query';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useClips, type ChannelClip, type ChannelClips, type ClipUpload } from '@/api/clips';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { useTime } from '@/composables/useTime';

import ClipUploadDialog from './ClipUploadDialog.vue';

const { t, n } = useI18n();
const time = useTime();
const { can } = useCan();
const { channelId } = useCurrentChannel();
const page = 'dashboard.stream.clips';

const clips = useClips(channelId);
const rows = computed<ChannelClip[]>(() =>
  (clips.data.value?.pages ?? []).flatMap((answer) => answer?.clips ?? []));
// The newest page read holds the newest count.
const left = computed(() => Number(clips.data.value?.pages.at(-1)?.uploadsLeftToday ?? 0));
const state = usePanelState(
  clips,
  (answer: InfiniteData<ChannelClips | null, string | null>) =>
    (answer.pages[0]?.clips ?? []).length === 0,
);

const picked = ref<ChannelClip | null>(null);
const dialogOpen = ref(false);
const uploaded = ref<ClipUpload | null>(null);

function choose(clip: ChannelClip): void {
  picked.value = clip;
  dialogOpen.value = true;
}

const detail = (clip: ChannelClip): string =>
  t(`${page}.detail`, {
    creator: clip.creatorName,
    date: time.date(clip.createdAt),
    views: n(Number(clip.viewCount)),
  });
</script>

<template>
  <PanelCard
    v-if="can('live-ops:clips:write')"
    data-clips
    :title="t(`${page}.title`)"
    :description="state === 'ready' ? t(`${page}.left`, { count: left }, left) : t(`${page}.description`)"
    :state="state"
    :empty-text="t(`${page}.empty`)"
    :error-text="t(`${page}.error`)"
    @retry="clips.refetch()"
  >
    <p v-if="uploaded" data-clip-uploaded role="status" class="px-3 pb-3 text-sm text-foreground">
      {{ t(`${page}.uploaded`) }}
      <a
        v-if="uploaded.url"
        class="underline underline-offset-4"
        :href="uploaded.url"
        target="_blank"
        rel="noopener noreferrer"
      >{{ t(`${page}.openVideo`) }}</a>
    </p>
    <RowGroup>
      <SummaryRow
        v-for="clip in rows"
        :key="clip.id"
        data-clip
        truncate
        wrap
        :title="clip.title"
        :detail="detail(clip)"
      >
        <template #leading>
          <img
            class="h-9 w-16 shrink-0 rounded-sm object-cover"
            :src="clip.thumbnailUrl"
            alt=""
            loading="lazy"
          >
        </template>
        <template #end>
          <Button
            data-clip-upload
            variant="outline"
            size="sm"
            :disabled="left === 0"
            @click="choose(clip)"
          >
            {{ t(`${page}.upload.open`) }}
          </Button>
        </template>
      </SummaryRow>
    </RowGroup>
    <template v-if="clips.hasNextPage.value" #footer>
      <Button
        data-clips-more
        variant="ghost"
        size="sm"
        class="w-full"
        :disabled="clips.isFetchingNextPage.value"
        @click="clips.fetchNextPage()"
      >
        {{ t(`${page}.more`) }}
      </Button>
    </template>
    <ClipUploadDialog v-model:open="dialogOpen" :clip="picked" @uploaded="uploaded = $event" />
  </PanelCard>
</template>

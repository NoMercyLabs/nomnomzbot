<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The Blocked tab of the request rules: the songs viewers may not request, each with an Allow button.
// Allow takes the row out at once; the notice keeps the song for a short window so Undo can block it again.
import { useTimeoutFn } from '@vueuse/core';
import { computed, ref } from 'vue';

import {
  useBlockedTracks,
  useBlockTrack,
  useUnblockTrack,
  type BlockedTrack,
  type BlockTrack,
} from '@/api/music';
import RowGroup from '@/components/layout/RowGroup.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { DEFAULT_UNDO_MS } from '@/composables/useInlineFlow';

import RuleSection from './RuleSection.vue';

const { channelId } = useCurrentChannel();
const { can } = useCan();
const blockedQuery = useBlockedTracks(channelId);
const unblock = useUnblockTrack(channelId);
const block = useBlockTrack(channelId);

const blocked = computed<BlockedTrack[]>(() => blockedQuery.data.value ?? []);

const allowed = ref<BlockedTrack | null>(null);
const undoWindow = useTimeoutFn(
  () => {
    allowed.value = null;
  },
  DEFAULT_UNDO_MS,
  { immediate: false },
);

function allow(track: BlockedTrack): void {
  allowed.value = track;
  unblock.mutate({ blockedTrackId: track.id });
  undoWindow.start();
}

function undoAllow(): void {
  const track = allowed.value;

  if (track === null) return;

  undoWindow.stop();
  allowed.value = null;
  const body: BlockTrack = {
    provider: track.provider,
    trackUri: track.trackUri,
    title: track.title,
  };

  if (track.reason) {
    body.reason = track.reason;
  }

  if (track.blockedByUserId) {
    body.blockedByUserId = track.blockedByUserId;
  }

  block.mutate(body);
}
</script>

<template>
  <RuleSection :title="$t('dashboard.music.rules.blocked')">
    <p v-if="blockedQuery.isError.value" role="alert" class="text-sm text-destructive">
      {{ $t("dashboard.music.rules.blockedError") }}
    </p>
    <p
      v-else-if="!blockedQuery.isPending.value && blocked.length === 0"
      class="text-sm text-muted-foreground"
    >
      {{ $t("dashboard.music.rules.blockedEmpty") }}
    </p>
    <RowGroup v-if="blocked.length > 0">
      <SummaryRow
        v-for="track in blocked"
        :key="track.id"
        :title="track.title"
        :detail="track.reason ?? undefined"
        data-blocked-item
      >
        <template v-if="can('music:queue:moderate')" #trailing>
          <Button
            variant="outline"
            size="sm"
            class="border-border"
            :aria-label="$t('dashboard.music.rules.allowRow', { title: track.title })"
            @click="allow(track)"
          >
            {{ $t("dashboard.music.rules.allow") }}
          </Button>
        </template>
      </SummaryRow>
    </RowGroup>
    <p v-if="allowed" role="status" class="flex items-center gap-2 text-sm">
      <span class="min-w-0 flex-1">{{
        $t("dashboard.music.rules.allowed", { title: allowed.title })
      }}</span>
      <Button variant="outline" size="sm" @click="undoAllow">{{ $t("common.flow.undo") }}</Button>
    </p>
  </RuleSection>
</template>

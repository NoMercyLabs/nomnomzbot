<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One shared item, with the one primary action its state allows (sleak: one primary per group).
// Pending: Approve (primary) and Reject. Approved: Play now (not on the first item), Skip, Remove and a menu to
// move it one place. The playing item lives in the player card. A finished item (played, rejected, skipped) has no controls.
import ArrowDown from '~icons/nnz/arrow-forward-down';
import ArrowUp from '~icons/nnz/arrow-forward-up';
import Check from '~icons/nnz/check';
import Cross from '~icons/nnz/cross';
import MenuDotsHorizontal from '~icons/nnz/menu-dots-horizontal';
import Next from '~icons/nnz/next';
import Play from '~icons/nnz/play';
import Trash from '~icons/nnz/trash';
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { MediaShareAction, MediaShareRequest } from '@/api/mediaShare';
import IconAction from '@/components/layout/IconAction.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { useCan } from '@/composables/useCan';
import { formatLength } from '@/lib/time';

const props = defineProps<{
  row: MediaShareRequest;
  // The 1-based play position among the approved items; null for any other state.
  position: number | null;
  canMoveUp: boolean;
  canMoveDown: boolean;
}>();

defineEmits<{
  decide: [action: MediaShareAction];
  move: [step: number];
  playNow: [];
  remove: [];
}>();

const { can } = useCan();
const { locale, t, te } = useI18n();

const pending = computed<boolean>(() => props.row.status === 'pending');
const approved = computed<boolean>(() => props.row.status === 'approved');
const movable = computed<boolean>(() => props.canMoveUp || props.canMoveDown);

const meta = computed<string>(() => {
  const source = `dashboard.mediaShare.queue.source.${props.row.sourceType}`;
  const parts: string[] = [];

  if (props.position !== null) {
    parts.push(t('dashboard.mediaShare.queue.position', { position: props.position }));
  }

  const length = formatLength(Number(props.row.durationSeconds), locale.value);
  const sourceName = te(source)
    ? t(source)
    : props.row.sourceType;

  parts.push(length, sourceName);

  return parts.join(' · ');
});
</script>

<template>
  <SummaryRow
    wrap
    :title="row.title ?? row.sourceUrl"
    :detail="$t('dashboard.mediaShare.queue.by', { name: row.requesterDisplayName })"
    :meta="meta"
    data-media-share-row
    :data-status="row.status"
  >
    <template #leading>
      <img
        v-if="row.thumbnailUrl"
        :src="row.thumbnailUrl"
        alt=""
        class="size-14 flex-none rounded-md object-cover"
      />
    </template>
    <template v-if="can('media:moderate') && (pending || approved)" #trailing>
      <Button v-if="pending" data-action="approve" @click="$emit('decide', 'approve')">
        <Check aria-hidden="true" />
        {{ $t("dashboard.mediaShare.queue.approve") }}
      </Button>
      <Button
        v-if="pending"
        variant="outline"
        data-action="reject"
        @click="$emit('decide', 'reject')"
      >
        <Cross aria-hidden="true" />
        {{ $t("dashboard.mediaShare.queue.reject") }}
      </Button>
      <Button
        v-if="approved && canMoveUp"
        variant="outline"
        data-action="play-now"
        @click="$emit('playNow')"
      >
        <Play aria-hidden="true" />
        {{ $t("dashboard.mediaShare.queue.playNow") }}
      </Button>
      <IconAction
        v-if="approved"
        :label="$t('dashboard.mediaShare.queue.skip')"
        data-action="skip"
        @click="$emit('decide', 'skip')"
      >
        <Next class="size-5" aria-hidden="true" />
      </IconAction>
      <IconAction
        v-if="approved"
        :label="$t('dashboard.mediaShare.queue.remove')"
        data-action="remove"
        @click="$emit('remove')"
      >
        <Trash class="size-5" aria-hidden="true" />
      </IconAction>
      <DropdownMenu v-if="approved && movable">
        <DropdownMenuTrigger as-child>
          <Button
            variant="ghost"
            size="icon"
            :aria-label="$t('dashboard.mediaShare.queue.more')"
            data-action="more"
          >
            <MenuDotsHorizontal class="size-5" aria-hidden="true" />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          <DropdownMenuItem data-action="up" :disabled="!canMoveUp" @select="$emit('move', -1)">
            <ArrowUp aria-hidden="true" />
            {{ $t("dashboard.mediaShare.queue.moveUp") }}
          </DropdownMenuItem>
          <DropdownMenuItem data-action="down" :disabled="!canMoveDown" @select="$emit('move', 1)">
            <ArrowDown aria-hidden="true" />
            {{ $t("dashboard.mediaShare.queue.moveDown") }}
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
    </template>
  </SummaryRow>
</template>

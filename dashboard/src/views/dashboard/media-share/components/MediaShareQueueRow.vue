<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One shared item. A pending item offers Approve (the one primary action) and Reject; an approved or playing
// item offers Skip, and an approved one also moves up or down the play order.
import Check from '~icons/nnz/check';
import Cross from '~icons/nnz/cross';
import Next from '~icons/nnz/next';
import ArrowDown from '~icons/nnz/arrow-forward-down';
import ArrowUp from '~icons/nnz/arrow-forward-up';
import { computed } from 'vue';

import type { MediaShareAction, MediaShareRequest } from '@/api/mediaShare';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';

const props = defineProps<{
  row: MediaShareRequest;
  canMoveUp: boolean;
  canMoveDown: boolean;
}>();

defineEmits<{
  decide: [action: MediaShareAction];
  move: [step: number];
}>();

const pending = computed<boolean>(() => props.row.status === 'pending');
const skippable = computed<boolean>(
  () => props.row.status === 'approved' || props.row.status === 'playing',
);
const knownStatuses = ['pending', 'approved', 'playing', 'played', 'rejected', 'skipped'];
const statusKey = computed<string>(() =>
  knownStatuses.includes(props.row.status)
    ? `dashboard.mediaShare.queue.status.${props.row.status}`
    : '',
);
</script>

<template>
  <SummaryRow
    :title="row.title ?? row.sourceUrl"
    :detail="$t('dashboard.mediaShare.queue.by', { name: row.requesterDisplayName })"
    :meta="statusKey ? $t(statusKey) : row.status"
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
    <template #trailing>
      <Button
        v-if="pending"
        size="icon"
        :aria-label="$t('dashboard.mediaShare.queue.approve')"
        data-action="approve"
        @click="$emit('decide', 'approve')"
      >
        <Check class="size-5" aria-hidden="true" />
      </Button>
      <Button
        v-if="pending"
        variant="ghost"
        size="icon"
        :aria-label="$t('dashboard.mediaShare.queue.reject')"
        data-action="reject"
        @click="$emit('decide', 'reject')"
      >
        <Cross class="size-5" aria-hidden="true" />
      </Button>
      <Button
        v-if="canMoveUp"
        variant="ghost"
        size="icon"
        :aria-label="$t('dashboard.mediaShare.queue.moveUp')"
        data-action="up"
        @click="$emit('move', -1)"
      >
        <ArrowUp class="size-5" aria-hidden="true" />
      </Button>
      <Button
        v-if="canMoveDown"
        variant="ghost"
        size="icon"
        :aria-label="$t('dashboard.mediaShare.queue.moveDown')"
        data-action="down"
        @click="$emit('move', 1)"
      >
        <ArrowDown class="size-5" aria-hidden="true" />
      </Button>
      <Button
        v-if="skippable"
        variant="ghost"
        size="icon"
        :aria-label="$t('dashboard.mediaShare.queue.skip')"
        data-action="skip"
        @click="$emit('decide', 'skip')"
      >
        <Next class="size-5" aria-hidden="true" />
      </Button>
    </template>
  </SummaryRow>
</template>

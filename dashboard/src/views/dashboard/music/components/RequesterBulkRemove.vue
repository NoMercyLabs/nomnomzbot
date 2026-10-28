<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Picks one requester from the people who have songs in the queue now and removes all their songs at once.
// The list is built from the queue rows the page already holds. The confirm names the person and the count.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useRemoveRequesterSongs, type QueueItem } from '@/api/music';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import { requestersIn, type Requester } from './requesters';

const props = defineProps<{ rows: QueueItem[] }>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const removeAll = useRemoveRequesterSongs(channelId);

// A requester with one song is removed by that row's own button. Only someone with more songs needs this, so the
// picker lists only them and stays away while there is nobody (Stoney 2026-10-19: "extra useless dropdowns").
const requesters = computed<Requester[]>(() =>
  requestersIn(props.rows).filter((requester) => requester.count > 1));
const picked = ref<string>('');
const confirmOpen = ref<boolean>(false);
const chosen = computed<Requester | undefined>(() =>
  requesters.value.find((requester) => requester.name === picked.value),
);

function confirmRemove(): void {
  if (chosen.value === undefined) return;

  removeAll.mutate({ name: chosen.value.name });
  confirmOpen.value = false;
  picked.value = '';
}
</script>

<template>
  <div
    v-if="requesters.length > 0 && can('music:queue:moderate')"
    data-requester-remove
    class="flex items-center gap-2 px-4 py-3 sticky -top-2 -mt-2 bg-card z-10"
  >
    <Select v-model="picked">
      <SelectTrigger class="min-w-0 flex-1" :aria-label="$t('dashboard.music.queue.pickRequester')">
        <SelectValue :placeholder="$t('dashboard.music.queue.pickRequester')" />
      </SelectTrigger>
      <SelectContent>
        <SelectItem
          v-for="requester in requesters"
          :key="requester.name"
          :value="requester.name"
          data-requester-option
        >
          {{ $t("dashboard.music.queue.requesterOption", requester) }}
        </SelectItem>
      </SelectContent>
    </Select>
    <Button variant="destructive" :disabled="chosen === undefined" @click="confirmOpen = true">
      {{ $t("dashboard.music.queue.removeAll") }}
    </Button>
  </div>
  <ConfirmDialog
    v-model:open="confirmOpen"
    :title="
      t('dashboard.music.queue.removeAllTitle', {
        count: chosen?.count ?? 0,
        name: chosen?.name ?? '',
      })
    "
    :description="$t('dashboard.music.queue.removeAllBody')"
    :action="$t('dashboard.music.queue.removeAll')"
    @confirm="confirmRemove"
  />
</template>

<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
import ChevronDown from '~icons/nnz/chevron-down';
import { computed } from 'vue';

import { useMusicDevices, useTransferMusicDevice } from '@/api/music';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';

// startPlaying: nothing plays on any device, so choosing one must start the music there.
const props = defineProps<{ channelId: string | null; startPlaying: boolean }>();

const devices = useMusicDevices(() => props.channelId);
const transfer = useTransferMusicDevice(() => props.channelId);
const rows = computed(() => devices.data.value ?? []);
const active = computed(() => rows.value.find((row) => row.isActive));
const hasChoice = computed(() => rows.value.some((row) => !row.isActive));

function choose(deviceId: string): void {
  transfer.mutate({ deviceId, play: props.startPlaying });
}
</script>

<template>
  <DropdownMenu v-if="hasChoice">
    <DropdownMenuTrigger as-child>
      <Button variant="outline" size="sm" :disabled="transfer.isPending.value" data-device-picker>
        {{ active?.name ?? $t('dashboard.music.queue.chooseDevice') }}
        <ChevronDown class="size-4" aria-hidden="true" />
      </Button>
    </DropdownMenuTrigger>
    <DropdownMenuContent align="end">
      <DropdownMenuItem
        v-for="row in rows"
        :key="row.id"
        :disabled="row.isActive"
        data-device-row
        @select="choose(row.id)"
      >
        {{ row.name }}
      </DropdownMenuItem>
    </DropdownMenuContent>
  </DropdownMenu>
</template>

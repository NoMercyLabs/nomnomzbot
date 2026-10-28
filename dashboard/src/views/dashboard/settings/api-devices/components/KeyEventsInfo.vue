<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The info button of a key that has the listen scope: it opens the public events that key can stream, each
// with its description, so a tool maker knows what to listen for. The list is read once for all keys.
import InfoCircle from '~icons/nnz/info-circle';

import { useAutomationEvents } from '@/api/automationTokens';
import { Button } from '@/components/ui/button';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

defineProps<{ name: string }>();

const { channelId } = useCurrentChannel();
const events = useAutomationEvents(channelId);
</script>

<template>
  <Popover>
    <PopoverTrigger as-child>
      <Button
        data-events-info
        type="button"
        variant="ghost"
        size="icon"
        class="text-muted-foreground hover:text-foreground size-9! shrink-0 p-0!"
        :aria-label="$t('dashboard.settings.apiDevices.events.aria', { name })"
      >
        <InfoCircle class="size-5" aria-hidden="true" />
      </Button>
    </PopoverTrigger>
    <PopoverContent align="start" class="w-96 max-w-[calc(100vw-2rem)]">
      <p class="text-sm font-medium">{{ $t('dashboard.settings.apiDevices.events.title') }}</p>
      <p v-if="events.isError.value" role="alert" class="mt-2 text-sm text-destructive">
        {{ $t('dashboard.settings.apiDevices.events.error') }}
      </p>
      <p
        v-else-if="!events.isPending.value && (events.data.value ?? []).length === 0"
        class="mt-2 text-sm text-muted-foreground"
      >
        {{ $t('dashboard.settings.apiDevices.events.empty') }}
      </p>
      <ul class="mt-2 grid max-h-72 gap-2 overflow-y-auto">
        <li v-for="item in events.data.value" :key="item.publicName" data-event-row class="grid gap-0.5">
          <code class="font-mono text-sm">{{ item.publicName }}</code>
          <span class="text-sm text-muted-foreground">{{ item.description }}</span>
        </li>
      </ul>
    </PopoverContent>
  </Popover>
</template>

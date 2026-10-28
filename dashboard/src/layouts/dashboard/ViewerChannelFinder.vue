<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The viewer header's way to any channel: recent chats and a search of every channel the bot serves
// (chat-client.md 9a "Reaching any channel").
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute } from 'vue-router';
import ChevronDown from '~icons/nnz/chevron-down';

import { Button } from '@/components/ui/button';
import { Command, CommandInput, CommandList } from '@/components/ui/command';
import {
  ResponsivePopover,
  ResponsivePopoverContent,
  ResponsivePopoverTrigger,
} from '@/components/ui/responsive-popover';

import ChannelSearchEmpty from './ChannelSearchEmpty.vue';
import ViewerChannelGroups from './ViewerChannelGroups.vue';

const { t } = useI18n();
const route = useRoute();
const open = ref(false);
const label = computed(() => {
  const channel = route.params.channel;

  return typeof channel === 'string' && channel !== ''
    ? channel
    : t('frame.channel.find');
});
</script>

<template>
  <ResponsivePopover v-model:open="open">
    <ResponsivePopoverTrigger>
      <Button
        variant="outline"
        data-slot="viewer-channel-finder"
        :aria-label="t('frame.channel.switch')"
        class="group min-w-0 max-w-48 gap-2!"
      >
        <span class="truncate">{{ label }}</span>
        <ChevronDown
          class="size-4 shrink-0 text-muted-foreground transition-transform group-data-[state=open]:rotate-180 motion-reduce:transition-none"
          aria-hidden="true"
        />
      </Button>
    </ResponsivePopoverTrigger>
    <ResponsivePopoverContent align="end" class="w-72 p-0" :title="t('frame.channel.switch')">
      <Command>
        <CommandInput :placeholder="t('frame.channel.find')" />
        <CommandList class="max-md:max-h-none max-md:overflow-visible">
          <ChannelSearchEmpty />
          <ViewerChannelGroups @picked="open = false" />
        </CommandList>
      </Command>
    </ResponsivePopoverContent>
  </ResponsivePopover>
</template>

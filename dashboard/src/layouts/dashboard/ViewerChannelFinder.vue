<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The viewer header's way to any channel: recent chats and a search of every channel the bot serves
// (chat-client.md 9a "Reaching any channel").
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute } from 'vue-router';
import ChevronDown from '~icons/nnz/chevron-down';

import { Button } from '@/components/ui/button';
import { Command, CommandEmpty, CommandInput, CommandList } from '@/components/ui/command';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';

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
  <Popover v-model:open="open">
    <PopoverTrigger as-child>
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
    </PopoverTrigger>
    <PopoverContent align="end" class="w-72 p-0">
      <Command>
        <CommandInput :placeholder="t('frame.channel.find')" />
        <CommandList>
          <CommandEmpty>{{ t('frame.channel.noMatch') }}</CommandEmpty>
          <ViewerChannelGroups @picked="open = false" />
        </CommandList>
      </Command>
    </PopoverContent>
  </Popover>
</template>

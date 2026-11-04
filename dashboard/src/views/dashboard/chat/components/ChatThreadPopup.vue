<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
import Cross from '~icons/nnz/cross';

import type { ChatLine } from '@/api/chat';
import { Button } from '@/components/ui/button';
import { useTime } from '@/composables/useTime';

import { emoteVars, shownNameColor } from '../chatDisplay';
import { useChatDisplay } from '../chatDisplay';
import ChatFragments from './ChatFragments.vue';

defineProps<{
  lines: readonly ChatLine[];
  pending: boolean;
  failed: boolean;
}>();

const emit = defineEmits<{ close: [] }>();
const clock = useTime();
const display = useChatDisplay();
</script>

<template>
  <section
    data-thread-popup
    role="dialog"
    :aria-label="$t('dashboard.chat.reply.threadTitle')"
    class="absolute inset-s-0 bottom-full z-20 mb-2 w-full max-h-80 overflow-y-auto rounded-md border bg-popover p-1 text-popover-foreground shadow-md"
  >
    <div class="sticky top-0 flex items-center justify-between rounded-[calc(var(--radius-md)-4px)] bg-popover px-2 py-1">
      <h3 class="text-sm font-medium">{{ $t('dashboard.chat.reply.threadTitle') }}</h3>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        :aria-label="$t('dashboard.chat.reply.closeThread')"
        @click="emit('close')"
      >
        <Cross class="size-4" aria-hidden="true" />
      </Button>
    </div>
    <p v-if="pending" class="px-2 py-2 text-sm text-muted-foreground">{{ $t('dashboard.chat.reply.threadLoading') }}</p>
    <p v-else-if="failed" class="px-2 py-2 text-sm text-destructive">{{ $t('dashboard.chat.reply.threadError') }}</p>
    <p v-else-if="lines.length === 0" class="px-2 py-2 text-sm text-muted-foreground">{{ $t('dashboard.chat.reply.threadEmpty') }}</p>
    <ol v-else class="space-y-0.5">
      <li
        v-for="line in lines"
        :key="line.id"
        :data-thread-line="line.id"
        class="rounded-[calc(var(--radius-md)-4px)] px-2 py-1 text-sm leading-5"
        :style="emoteVars(line, display)"
      >
        <span class="font-medium" :style="{ color: shownNameColor(line.color, display.nameColors) }">{{ line.displayName }}</span>
        <time class="ms-2 text-xs text-muted-foreground tabular-nums" :datetime="line.timestamp">{{ display.timestamps === '12' ? clock.time12(line.timestamp) : clock.time24(line.timestamp) }}</time>
        <span class="block break-words">
          <ChatFragments
            v-if="line.fragments.length > 0"
            :fragments="line.fragments"
            :display="display"
            :me="null"
          />
          <template v-else>{{ line.message }}</template>
        </span>
      </li>
    </ol>
  </section>
</template>

<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One chat line. The actions appear on hover or focus. Timeout and Delete are a small step in
// place (InlineFlow); Ban is the one harm action and asks the page for its confirm dialog.
import { computed, type ComputedRef } from 'vue'
import Clock from '~icons/nnz/clock'
import StopSign from '~icons/nnz/stop-sign'
import Trash from '~icons/nnz/trash'

import { useDeleteChatMessage, useTimeoutUser, type ChatLine } from '@/api/chat'
import InlineFlow from '@/components/layout/InlineFlow.vue'
import UserName from '@/components/layout/UserName.vue'
import { Button } from '@/components/ui/button'

const props = defineProps<{
  line: ChatLine
  channelId: string | null
  timeoutSeconds: number
  timeoutName: string
}>()

const emit = defineEmits<{ ban: [line: ChatLine] }>()

const timeouts = useTimeoutUser(() => props.channelId)
const deletes = useDeleteChatMessage(() => props.channelId)

const time: ComputedRef<string> = computed(() =>
  new Date(props.line.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
)

async function timeout(): Promise<void> {
  await timeouts.mutateAsync({
    targetUserId: props.line.userId,
    durationSeconds: props.timeoutSeconds,
  })
}

async function remove(): Promise<void> {
  await deletes.mutateAsync(props.line.id)
}

function emoteUrl(urls: Record<string, string>): string | undefined {
  return urls['1'] ?? Object.values(urls)[0]
}
</script>

<template>
  <div
    :data-line-id="line.id"
    :data-removed="line.removed"
    class="group/line relative flex gap-2 rounded-lg px-3 py-2 text-sm hover:bg-muted/50 focus-within:bg-muted/50"
  >
    <time class="flex-none text-muted-foreground tabular-nums" :datetime="line.timestamp">{{
      time
    }}</time>
    <p
      class="min-w-0 flex-1 wrap-break-word"
      :class="{ 'text-muted-foreground line-through': line.removed }"
    >
      <UserName
        :user-id="line.userId"
        :name="line.displayName"
        :style="{ color: line.color ?? undefined }"
      />
      <template v-for="(fragment, index) in line.fragments" :key="index">
        <img
          v-if="fragment.emote && emoteUrl(fragment.emote.urls)"
          :src="emoteUrl(fragment.emote.urls)"
          :alt="fragment.text"
          class="mx-0.5 inline-block h-6 align-middle"
        />
        <template v-else>{{ ' ' + fragment.text }}</template>
      </template>
    </p>
    <div
      v-if="!line.removed"
      class="absolute top-1 right-2 flex items-center rounded-[10px] bg-card p-0.5 opacity-0 shadow-xs ring-1 ring-border group-hover/line:opacity-100 group-focus-within/line:opacity-100 **:data-[slot=inline-flow-box]:rounded-md **:data-[slot=inline-flow-box]:bg-card **:data-[slot=inline-flow-box]:px-2"
      role="group"
      :aria-label="$t('dashboard.chat.line.actions', { name: line.displayName })"
    >
      <InlineFlow
        right
        kind="confirm"
        :label="$t('dashboard.chat.timeout.flow')"
        :ask="$t('dashboard.chat.timeout.ask', { name: line.displayName, length: timeoutName })"
        :yes="$t('dashboard.chat.timeout.yes')"
        :result="$t('dashboard.chat.timeout.result', { name: line.displayName })"
        :failure="$t('dashboard.chat.timeout.failure')"
        :action="timeout"
      >
        <Button
          data-line-action="timeout"
          variant="ghost"
          size="icon-xs"
          :aria-label="$t('dashboard.chat.timeout.label', { name: line.displayName })"
        >
          <Clock aria-hidden="true" />
        </Button>
      </InlineFlow>
      <InlineFlow
        right
        kind="confirm"
        :label="$t('dashboard.chat.delete.flow')"
        :ask="$t('dashboard.chat.delete.ask')"
        :yes="$t('dashboard.chat.delete.yes')"
        :result="$t('dashboard.chat.delete.result')"
        :failure="$t('dashboard.chat.delete.failure')"
        :action="remove"
      >
        <Button
          data-line-action="delete"
          variant="ghost"
          size="icon-xs"
          :aria-label="$t('dashboard.chat.delete.label')"
        >
          <Trash aria-hidden="true" />
        </Button>
      </InlineFlow>
      <Button
        data-line-action="ban"
        variant="destructive"
        size="icon-xs"
        :aria-label="$t('dashboard.chat.ban.label', { name: line.displayName })"
        @click="emit('ban', line)"
      >
        <StopSign aria-hidden="true" />
      </Button>
    </div>
  </div>
</template>

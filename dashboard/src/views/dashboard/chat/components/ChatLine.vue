<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One chat line. The action tray floats over the line on hover or focus and never moves it.
// Timeout picks its length in a menu, Delete runs at once, Ban asks the page for its confirm
// dialog, and Profile opens the one profile dialog of the frame.
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import Clock from '~icons/nnz/clock'
import StopSign from '~icons/nnz/stop-sign'
import Trash from '~icons/nnz/trash'
import UserCircle from '~icons/nnz/user-circle'

import { useDeleteChatMessage, useTimeoutUser, type ChatLine } from '@/api/chat'
import UserName from '@/components/layout/UserName.vue'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { useProfileDialogStore } from '@/stores/profileDialog'

const LENGTHS: readonly number[] = [60, 600, 3600, 86400]

const props = defineProps<{
  line: ChatLine
  channelId: string | null
}>()

const emit = defineEmits<{ ban: [line: ChatLine] }>()

const { t } = useI18n()
const timeouts = useTimeoutUser(() => props.channelId)
const deletes = useDeleteChatMessage(() => props.channelId)
const profile = useProfileDialogStore()

const time: ComputedRef<string> = computed(() =>
  new Date(props.line.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
)

async function timeout(seconds: number): Promise<void> {
  try {
    await timeouts.mutateAsync({ targetUserId: props.line.userId, durationSeconds: seconds })
    toast.success(t('dashboard.chat.timeout.result', { name: props.line.displayName }))
  } catch {
    toast.error(t('dashboard.chat.timeout.failure'))
  }
}

async function remove(): Promise<void> {
  try {
    await deletes.mutateAsync(props.line.id)
    toast.success(t('dashboard.chat.delete.result'))
  } catch {
    toast.error(t('dashboard.chat.delete.failure'))
  }
}

function openProfile(event: MouseEvent): void {
  profile.open(
    props.line.userId,
    event.currentTarget instanceof HTMLElement ? event.currentTarget : null,
  )
}

function emoteUrl(urls: Record<string, string>): string | undefined {
  return urls['1'] ?? Object.values(urls)[0]
}
</script>

<template>
  <div
    :data-line-id="line.id"
    :data-removed="line.removed"
    class="group/line relative flex gap-2 rounded-lg px-3 py-2 text-sm hover:bg-muted focus-within:bg-muted"
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
      class="absolute top-1/2 right-2 flex -translate-y-1/2 items-center rounded-[12px] border p-0.5 opacity-0 group-hover/line:opacity-100 group-focus-within/line:opacity-100 has-data-[state=open]:opacity-100"
      data-tray
      role="group"
      :aria-label="$t('dashboard.chat.line.actions', { name: line.displayName })"
    >
      <DropdownMenu>
        <Tooltip>
          <TooltipTrigger as-child>
            <DropdownMenuTrigger as-child>
              <Button
                data-line-action="timeout"
                variant="ghost"
                size="icon"
                :aria-label="$t('dashboard.chat.timeout.label', { name: line.displayName })"
              >
                <Clock class="size-5" aria-hidden="true" />
              </Button>
            </DropdownMenuTrigger>
          </TooltipTrigger>
          <TooltipContent>{{ $t('dashboard.chat.timeout.tip') }}</TooltipContent>
        </Tooltip>
        <DropdownMenuContent align="end">
          <DropdownMenuLabel>{{ $t('dashboard.chat.timeout.menu') }}</DropdownMenuLabel>
          <DropdownMenuItem
            v-for="seconds in LENGTHS"
            :key="seconds"
            :data-timeout-length="seconds"
            @select="timeout(seconds)"
          >
            {{ $t(`dashboard.chat.lengths.${seconds}`) }}
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
      <Tooltip>
        <TooltipTrigger as-child>
          <Button
            data-line-action="delete"
            variant="ghost"
            size="icon"
            :aria-label="$t('dashboard.chat.delete.label')"
            @click="remove"
          >
            <Trash class="size-5" aria-hidden="true" />
          </Button>
        </TooltipTrigger>
        <TooltipContent>{{ $t('dashboard.chat.delete.label') }}</TooltipContent>
      </Tooltip>
      <Tooltip>
        <TooltipTrigger as-child>
          <Button
            data-line-action="ban"
            variant="ghost"
            size="icon"
            :aria-label="$t('dashboard.chat.ban.label', { name: line.displayName })"
            @click="emit('ban', line)"
          >
            <StopSign class="size-5" aria-hidden="true" />
          </Button>
        </TooltipTrigger>
        <TooltipContent>{{ $t('dashboard.chat.ban.tip') }}</TooltipContent>
      </Tooltip>
      <Tooltip>
        <TooltipTrigger as-child>
          <Button
            data-line-action="profile"
            variant="ghost"
            size="icon"
            :aria-label="$t('dashboard.chat.line.profile', { name: line.displayName })"
            @click="openProfile"
          >
            <UserCircle class="size-5" aria-hidden="true" />
          </Button>
        </TooltipTrigger>
        <TooltipContent>{{ $t('dashboard.chat.line.profileTip') }}</TooltipContent>
      </Tooltip>
    </div>
  </div>
</template>

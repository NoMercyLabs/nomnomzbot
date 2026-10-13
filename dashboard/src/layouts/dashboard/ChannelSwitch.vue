<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import ArrowsUpDown from '~icons/nnz/arrow-arrows-up-down'

import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from '@/components/ui/command'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { useCurrentChannel, type ChannelChoice } from '@/composables/useCurrentChannel'

// compact: the name only, for the narrow header where the sidebar is not shown.
defineProps<{ compact?: boolean }>()

const { t } = useI18n()
const { channels, current, isPending, choose } = useCurrentChannel()
const open = ref(false)

const own = computed<ChannelChoice[]>(() => channels.value.filter((row) => row.kind === 'own'))
// Live channels first: a moderator opens the one that needs them now.
const moderated = computed<ChannelChoice[]>(() =>
  channels.value
    .filter((row) => row.kind === 'moderated')
    .sort((a, b) => Number(b.isLive === true) - Number(a.isLive === true)),
)
const liveCount = computed<number>(
  () => moderated.value.filter((row) => row.isLive === true).length,
)

type ChannelGroup = { key: string; heading: string; rows: ChannelChoice[] }
const groups = computed<ChannelGroup[]>(() =>
  [
    { key: 'own', heading: t('frame.channel.yours'), rows: own.value },
    {
      key: 'moderated',
      heading: t('frame.channel.moderated', {
        live: liveCount.value,
        total: moderated.value.length,
      }),
      rows: moderated.value,
    },
  ].filter((group) => group.rows.length > 0),
)

function initials(name: string): string {
  return name
    .replace(/[^A-Za-z0-9]/g, '')
    .slice(0, 2)
    .toUpperCase()
}

function stateWord(row: ChannelChoice): string | null {
  if (row.isLive === null) return null
  return row.isLive ? t('frame.channel.live') : t('frame.channel.offline')
}

async function pick(row: ChannelChoice): Promise<void> {
  await choose(row)
  open.value = false
}
</script>

<template>
  <Popover v-model:open="open">
    <PopoverTrigger as-child>
      <button
        type="button"
        data-slot="channel-switch"
        :aria-label="t('frame.channel.switch')"
        class="flex min-h-11 w-full min-w-0 items-center gap-2 rounded-md p-2 text-left outline-none hover:bg-sidebar-accent focus-visible:ring-2 focus-visible:ring-ring"
      >
        <Avatar class="size-8 rounded-lg">
          <AvatarImage v-if="current?.profileImageUrl" :src="current.profileImageUrl" :alt="''" />
          <AvatarFallback class="rounded-lg">{{
            current ? initials(current.displayName) : ''
          }}</AvatarFallback>
        </Avatar>
        <span class="min-w-0 flex-1">
          <span class="block truncate text-sm font-medium">{{
            current?.displayName ?? t('frame.channel.none')
          }}</span>
          <span
            v-if="current && !compact && stateWord(current)"
            class="flex items-center gap-1.5 text-xs text-muted-foreground"
          >
            <span
              class="size-2 rounded-full"
              :class="current.isLive ? 'bg-success' : 'bg-muted-foreground'"
              aria-hidden="true"
            />
            {{ stateWord(current) }}
          </span>
        </span>
        <ArrowsUpDown class="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
      </button>
    </PopoverTrigger>
    <PopoverContent align="start" class="w-72 p-0">
      <Command>
        <CommandInput :placeholder="t('frame.channel.find')" />
        <CommandList>
          <CommandEmpty>{{ t('frame.channel.noMatch') }}</CommandEmpty>
          <CommandGroup v-for="group in groups" :key="group.key" :heading="group.heading">
            <CommandItem
              v-for="row in group.rows"
              :key="row.login"
              :value="row.displayName"
              :disabled="isPending"
              @select="pick(row)"
            >
              <Avatar class="size-6 rounded-md">
                <AvatarImage v-if="row.profileImageUrl" :src="row.profileImageUrl" :alt="''" />
                <AvatarFallback class="rounded-md text-xs">{{
                  initials(row.displayName)
                }}</AvatarFallback>
              </Avatar>
              <span class="min-w-0 flex-1 truncate">{{ row.displayName }}</span>
              <span
                v-if="stateWord(row)"
                class="flex items-center gap-1.5 text-xs text-muted-foreground"
              >
                <span
                  class="size-2 rounded-full"
                  :class="row.isLive ? 'bg-success' : 'bg-muted-foreground'"
                  aria-hidden="true"
                />
                {{ stateWord(row) }}
              </span>
            </CommandItem>
          </CommandGroup>
        </CommandList>
      </Command>
    </PopoverContent>
  </Popover>
</template>

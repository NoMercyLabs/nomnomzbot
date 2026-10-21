<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One member of the linked Discord server, found by name: the bot searches the server's members, so nobody
// types a Discord id. A saved member shows by name, and a server the bot cannot read says why.
import ChevronDown from '~icons/nnz/chevron-down';
import Check from '~icons/nnz/check';
import { refDebounced } from '@vueuse/core';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { usePipelineOption, usePipelineOptions } from '@/api/pipelines';
import CommandSearch from '@/components/layout/CommandSearch.vue';
import { Avatar, AvatarImage } from '@/components/ui/avatar';
import { Button } from '@/components/ui/button';
import {
  Command,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from '@/components/ui/command';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

const props = defineProps<{ disabled: boolean }>();

const memberId = defineModel<string>({ required: true });

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const open = ref(false);
const search = ref('');
const asked = refDebounced(search, 250);
const options = usePipelineOptions(channelId, 'discord_user', asked);

const list = computed(() =>
  options.data.value);
const items = computed(() =>
  list.value?.items ?? []);
const listed = computed(() =>
  items.value.find((option) =>
    option.value === memberId.value));
const named = usePipelineOption(channelId, 'discord_user', () =>
  memberId.value === '' || listed.value
    ? null
    : memberId.value);
const chosen = computed(() =>
  listed.value ?? named.data.value ?? undefined);
// A failed read and a source the bot cannot reach both say why the list is empty.
const unavailable = computed(() => {
  if (options.error.value)
    return t('dashboard.discord.liveRole.memberUnavailable');

  if (list.value && !list.value.sourceAvailable)
    return list.value.unavailableReason || t('dashboard.discord.liveRole.memberUnavailable');

  return undefined;
});
const noMatch = computed(() =>
  asked.value.trim() !== '' && !options.isFetching.value && items.value.length === 0);

function choose(value: string): void {
  open.value = false;
  search.value = '';
  memberId.value = value;
}
</script>

<template>
  <Popover v-model:open="open">
    <PopoverTrigger as-child>
      <Button
        type="button"
        variant="outline"
        role="combobox"
        data-field="live-member"
        :aria-expanded="open"
        :disabled="props.disabled"
        class="group w-full justify-between font-normal"
      >
        <span class="flex min-w-0 items-center gap-2">
          <Avatar v-if="chosen?.imageUrl" class="size-5">
            <AvatarImage :src="chosen.imageUrl" alt="" />
          </Avatar>
          <span class="truncate" :class="!chosen && 'text-muted-foreground'">
            {{ chosen?.label ?? t('dashboard.discord.liveRole.memberPlaceholder') }}
          </span>
        </span>
        <ChevronDown
          class="text-muted-foreground transition-transform group-data-[state=open]:rotate-180 motion-reduce:transition-none"
          aria-hidden="true"
        />
      </Button>
    </PopoverTrigger>
    <PopoverContent align="start" class="w-(--reka-popover-trigger-width) min-w-64 p-0">
      <Command>
        <CommandSearch v-model="search">
          <CommandInput :placeholder="t('dashboard.discord.liveRole.memberFind')" />
        </CommandSearch>
        <CommandList>
          <p v-if="noMatch" class="m-0 py-6 text-center text-sm">
            {{ t('dashboard.discord.liveRole.memberNoMatch') }}
          </p>
          <CommandGroup>
            <CommandItem
              v-for="option in items"
              :key="option.value"
              :data-member-option="option.value"
              :value="`${option.label} ${option.secondaryText ?? ''}`"
              @select="choose(option.value)"
            >
              <Avatar v-if="option.imageUrl" class="size-6">
                <AvatarImage :src="option.imageUrl" alt="" />
              </Avatar>
              <span class="flex min-w-0 flex-1 flex-col">
                <span class="truncate">{{ option.label }}</span>
                <span v-if="option.secondaryText" class="truncate text-xs text-muted-foreground">
                  {{ option.secondaryText }}
                </span>
              </span>
              <Check v-if="option.value === memberId" aria-hidden="true" />
            </CommandItem>
          </CommandGroup>
        </CommandList>
      </Command>
    </PopoverContent>
  </Popover>
  <p v-if="unavailable"
     role="alert"
     data-member-error
     data-picker-error
     class="text-sm text-destructive"
  >
    {{ unavailable }}
  </p>
</template>

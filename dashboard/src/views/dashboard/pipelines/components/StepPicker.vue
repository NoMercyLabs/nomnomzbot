<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// A step setting that names something the bot knows (a voice, a viewer, a sound, a reward): the bot lists and
// searches them, an item it cannot use now says why, and the typed text can be kept as is, so a setting can
// also be a word that fills in from the run. A source the bot cannot reach says so instead of an empty list.
import ChevronDown from '~icons/nnz/chevron-down';
import Check from '~icons/nnz/check';
import { refDebounced } from '@vueuse/core';
import { computed, ref } from 'vue';

import {
  usePipelineOption,
  usePipelineOptions,
  type PickerKind,
  type PipelineOption,
} from '@/api/pipelines';
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

const props = defineProps<{
  id: string;
  kind: PickerKind;
  value: string;
  required: boolean;
}>();

const emit = defineEmits<{ change: [value: string | undefined] }>();

const { channelId } = useCurrentChannel();
const open = ref(false);
const search = ref('');
const asked = refDebounced(search, 250);
const options = usePipelineOptions(channelId, () =>
  props.kind, asked);

const list = computed(() =>
  options.data.value);
const items = computed(() =>
  list.value?.items ?? []);
const listed = computed(() =>
  items.value.find((option: PipelineOption) =>
    option.value === props.value));
// A saved choice beyond the first page: the bot looks up that one item, so the button shows its name.
const named = usePipelineOption(channelId, () =>
  props.kind, () =>
  listed.value || options.isPending.value
    ? null
    : props.value);
const chosen = computed(() =>
  listed.value ?? named.data.value ?? undefined);
// The bot searches, so the answer decides "nothing matches": the Command only recounts when the text changes,
// before the bot's rows arrive.
const noMatch = computed(() =>
  asked.value.trim() !== '' && !options.isFetching.value && items.value.length === 0);
const typedIsOption = computed(() =>
  items.value.some((option: PipelineOption) =>
    option.value === search.value || option.label === search.value));

// The next open starts on the full list again, not on the last search.
function choose(value: string | undefined): void {
  open.value = false;
  search.value = '';
  emit('change', value);
}
</script>

<template>
  <Popover v-model:open="open">
    <PopoverTrigger as-child>
      <Button
        :id="id"
        type="button"
        variant="outline"
        role="combobox"
        :aria-expanded="open"
        class="group w-full justify-between font-normal"
      >
        <span class="flex min-w-0 items-center gap-2">
          <Avatar v-if="chosen?.imageUrl" class="size-5">
            <AvatarImage :src="chosen.imageUrl" alt="" />
          </Avatar>
          <span class="truncate" :class="value === '' && 'text-muted-foreground'">
            {{ chosen?.label ?? (value || $t('dashboard.pipelines.workbench.inspector.notSet')) }}
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
          <CommandInput :placeholder="$t('dashboard.pipelines.workbench.picker.find')" />
        </CommandSearch>
        <CommandList>
          <p v-if="list && !list.sourceAvailable" data-picker-unavailable class="m-0 px-3 py-2 text-sm text-muted-foreground">
            {{ list.unavailableReason || $t('dashboard.pipelines.workbench.picker.unavailable') }}
          </p>
          <p v-if="noMatch" data-picker-none class="m-0 py-6 text-center text-sm">
            {{ $t('dashboard.pipelines.workbench.picker.noMatch') }}
          </p>
          <CommandGroup>
            <CommandItem
              v-for="option in items"
              :key="option.value"
              :data-picker-option="option.value"
              :value="`${option.label} ${option.secondaryText ?? ''} ${option.value}`"
              :disabled="!!option.reason"
              @select="choose(option.value)"
            >
              <Avatar v-if="option.imageUrl" class="size-6">
                <AvatarImage :src="option.imageUrl" alt="" />
              </Avatar>
              <span class="flex min-w-0 flex-1 flex-col">
                <span class="truncate">{{ option.label }}</span>
                <span v-if="option.reason || option.secondaryText" class="truncate text-xs text-muted-foreground">
                  {{ option.reason || option.secondaryText }}
                </span>
              </span>
              <Check v-if="option.value === value" aria-hidden="true" />
            </CommandItem>
            <CommandItem
              v-if="search.trim() !== '' && !typedIsOption"
              data-picker-typed
              :value="search"
              @select="choose(search.trim())"
            >
              {{ $t('dashboard.pipelines.workbench.picker.useTyped', { text: search.trim() }) }}
            </CommandItem>
            <CommandItem
              v-if="!required && value !== '' && search === ''"
              data-picker-clear
              value="clear"
              @select="choose(undefined)"
            >
              {{ $t('dashboard.pipelines.workbench.picker.clear') }}
            </CommandItem>
          </CommandGroup>
        </CommandList>
      </Command>
    </PopoverContent>
  </Popover>
</template>

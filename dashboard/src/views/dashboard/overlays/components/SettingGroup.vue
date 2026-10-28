<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// A nested settings object edited in place: one labeled control per key, chosen by the kind of value it holds
// (switch, number, text, list of texts, or a nested group). A value of another kind says it is set in the code.
// The first level of nested groups opens one at a time, so a long group still fits one screen.
import ChevronDown from '~icons/nnz/chevron-down';
import ChevronRight from '~icons/nnz/chevron-right';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import LabeledSwitch from '@/components/layout/LabeledSwitch.vue';
import { Button } from '@/components/ui/button';
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible';
import { Input } from '@/components/ui/input';
import { InputGroup, InputGroupAddon, InputGroupInput } from '@/components/ui/input-group';
import { Switch } from '@/components/ui/switch';
import type { JsonObject, JsonValue } from '@/lib/json';
import { numberOrNull } from '@/lib/number';

import SettingGroup from './SettingGroup.vue';
import { plainKey } from './settingField';
import StringListEditor from './StringListEditor.vue';

const props = withDefaults(
  defineProps<{
    value: JsonObject;
    depth?: number;
    milliseconds?: boolean;
    skip?: string | null;
  }>(),
  {
    depth: 0,
    milliseconds: false,
    skip: null,
  },
);
const emit = defineEmits<{ update: [value: JsonObject] }>();

const { t, te } = useI18n();

type Kind = 'bool' | 'number' | 'text' | 'list' | 'group' | 'other';

// A group named for durations holds milliseconds, as does a number whose key ends in Ms; both show as seconds.
const DURATION_GROUP = /^(timings|durations|delays)$/i;
const MILLISECONDS_KEY = /[a-z]Ms$/;

function kindOf(value: JsonValue | undefined): Kind {
  if (typeof value === 'boolean')
    return 'bool';

  if (typeof value === 'number')
    return 'number';

  if (typeof value === 'string')
    return 'text';

  if (Array.isArray(value))
    return value.every((item) => typeof item === 'string')
      ? 'list'
      : 'other';

  return value !== null && typeof value === 'object'
    ? 'group'
    : 'other';
}

// The page's plain word for a key part, or null when it has none.
function wordFor(part: string): string | null {
  const path = `dashboard.overlays.detail.settings.words.${part}`;

  return te(path)
    ? t(path)
    : null;
}

function word(key: string): string {
  return plainKey(key, wordFor);
}

// A nested part's own on switch sits on its closed row, so a part can be switched without opening it.
const SWITCH_KEY = 'enabled';

function hasSwitch(value: JsonValue | undefined): boolean {
  return kindOf(value) === 'group' && typeof (value as JsonObject)[SWITCH_KEY] === 'boolean';
}

function setPart(key: string, part: string, value: JsonValue): void {
  set(key, { ...(props.value[key] as JsonObject), [part]: value });
}

const entries = computed(() =>
  Object.entries(props.value).filter(([key]) => key !== props.skip).map(([key, value]) => ({
    key,
    label: word(key),
    value,
    kind: kindOf(value),
    seconds: props.milliseconds || MILLISECONDS_KEY.test(key),
  })),
);

const opened = ref<Record<string, boolean>>({});

function set(key: string, value: JsonValue | undefined): void {
  emit('update', { ...props.value, [key]: value });
}

function setNumber(key: string, typed: string | number, seconds: boolean): void {
  const parsed = numberOrNull(String(typed));

  if (parsed === null) return;

  set(key, seconds
    ? Math.round(parsed * 1000)
    : parsed);
}

function shownNumber(value: JsonValue | undefined, seconds: boolean): string {
  return seconds
    ? String(Number(value) / 1000)
    : String(value);
}
</script>

<template>
  <!-- Plain values sit three to a line; a list or a nested group takes the whole line. -->
  <!-- The closed rows of the first level sit close together, as one list. -->
  <div
    class="grid w-full grid-cols-2 gap-x-3 sm:grid-cols-3"
    :class="depth === 0 ? 'gap-y-1' : 'gap-y-3'"
  >
    <div
      v-for="entry in entries"
      :key="entry.key"
      :data-group-key="entry.key"
      class="flex min-w-0 flex-col gap-1.5"
      :class="{ 'col-span-full': entry.kind === 'group' || entry.kind === 'list' }"
    >
      <LabeledSwitch
        v-if="entry.kind === 'bool'"
        :model-value="entry.value === true"
        :label="entry.label"
        @update:model-value="set(entry.key, $event)"
      />
      <Collapsible
        v-else-if="entry.kind === 'group' && depth === 0"
        v-model:open="opened[entry.key]"
      >
        <div data-group-head class="flex items-center gap-1">
          <CollapsibleTrigger as-child>
            <Button
              data-group-toggle
              variant="ghost"
              size="icon"
              :aria-label="$t('dashboard.overlays.detail.settings.open', { name: entry.label })"
              :aria-expanded="opened[entry.key] === true"
            >
              <ChevronDown v-if="opened[entry.key]" aria-hidden="true" />
              <ChevronRight v-else aria-hidden="true" />
            </Button>
          </CollapsibleTrigger>
          <span class="flex-1 text-sm font-medium">{{ entry.label }}</span>
          <Switch
            v-if="hasSwitch(entry.value)"
            :model-value="(entry.value as JsonObject)[SWITCH_KEY] === true"
            :aria-label="entry.label"
            @update:model-value="setPart(entry.key, SWITCH_KEY, $event)"
          />
        </div>
        <CollapsibleContent class="pb-2 pl-10">
          <SettingGroup
            :value="(entry.value as JsonObject)"
            :depth="depth + 1"
            :milliseconds="DURATION_GROUP.test(entry.key)"
            :skip="hasSwitch(entry.value) ? SWITCH_KEY : null"
            @update="set(entry.key, $event)"
          />
        </CollapsibleContent>
      </Collapsible>
      <span v-else class="text-sm font-medium">{{ entry.label }}</span>

      <InputGroup v-if="entry.kind === 'number' && entry.seconds">
        <InputGroupInput
          type="number"
          step="0.1"
          min="0"
          :model-value="shownNumber(entry.value, true)"
          :aria-label="entry.label"
          @update:model-value="setNumber(entry.key, $event, true)"
        />
        <InputGroupAddon align="inline-end">
          {{ $t('dashboard.overlays.detail.settings.seconds') }}
        </InputGroupAddon>
      </InputGroup>
      <Input
        v-else-if="entry.kind === 'number'"
        type="number"
        class="w-full"
        :model-value="shownNumber(entry.value, false)"
        :aria-label="entry.label"
        @update:model-value="setNumber(entry.key, $event, false)"
      />
      <Input
        v-else-if="entry.kind === 'text'"
        :model-value="String(entry.value)"
        :aria-label="entry.label"
        @update:model-value="set(entry.key, String($event))"
      />
      <StringListEditor
        v-else-if="entry.kind === 'list'"
        :items="(entry.value as string[])"
        @update="set(entry.key, $event)"
      />
      <div v-else-if="entry.kind === 'group' && depth > 0" class="border-l pl-3">
        <SettingGroup
          :value="(entry.value as JsonObject)"
          :depth="depth + 1"
          :milliseconds="DURATION_GROUP.test(entry.key)"
          @update="set(entry.key, $event)"
        />
      </div>
      <p v-else-if="entry.kind === 'other'" data-in-code class="text-sm text-muted-foreground">
        {{ $t('dashboard.overlays.detail.settings.inCode') }}
      </p>
    </div>
  </div>
</template>

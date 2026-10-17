<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// A nested settings object edited in place: one labeled control per key, chosen by the kind of value it holds
// (switch, number, text, list of texts, or a nested group). A value of another kind shows read-only.
import { computed } from 'vue';

import { Input } from '@/components/ui/input';
import { Switch } from '@/components/ui/switch';
import { Textarea } from '@/components/ui/textarea';
import type { JsonObject, JsonValue } from '@/lib/json';
import { numberOrNull } from '@/lib/number';
import { humanize } from '@/lib/text';

import SettingGroup from './SettingGroup.vue';
import StringListEditor from './StringListEditor.vue';

const props = defineProps<{ value: JsonObject }>();
const emit = defineEmits<{ update: [value: JsonObject] }>();

type Kind = 'bool' | 'number' | 'text' | 'list' | 'group' | 'other';

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

const entries = computed(() =>
  Object.entries(props.value).map(([key, value]) => ({
    key,
    label: humanize(key),
    value,
    kind: kindOf(value),
  })),
);

function set(key: string, value: JsonValue | undefined): void {
  emit('update', { ...props.value, [key]: value });
}

function setNumber(key: string, typed: string | number): void {
  const parsed = numberOrNull(String(typed));

  if (parsed !== null) {
    set(key, parsed);
  }
}
</script>

<template>
  <div class="flex w-full flex-col gap-3">
    <div
      v-for="entry in entries"
      :key="entry.key"
      :data-group-key="entry.key"
      class="flex flex-col gap-1.5"
    >
      <span class="text-sm font-medium">{{ entry.label }}</span>

      <Switch
        v-if="entry.kind === 'bool'"
        :model-value="entry.value === true"
        :aria-label="entry.label"
        @update:model-value="set(entry.key, $event)"
      />
      <Input
        v-else-if="entry.kind === 'number'"
        type="number"
        class="w-full sm:w-48"
        :model-value="String(entry.value)"
        :aria-label="entry.label"
        @update:model-value="setNumber(entry.key, $event)"
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
      <div v-else-if="entry.kind === 'group'" class="border-l pl-3">
        <SettingGroup :value="(entry.value as JsonObject)" @update="set(entry.key, $event)" />
      </div>
      <Textarea
        v-else
        readonly
        class="font-mono"
        :aria-label="entry.label"
        :model-value="JSON.stringify(entry.value ?? null, null, 2)"
      />
    </div>
  </div>
</template>

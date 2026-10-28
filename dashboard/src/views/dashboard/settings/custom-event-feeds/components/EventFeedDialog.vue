<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Makes or edits one feed: its name, how the data arrives (the bot polls an address, the bot listens on a socket,
// or the source pushes to a link the bot gives), the secret, the fields and the on/off switch.
// A saved secret is never shown. Leaving the secret empty keeps the saved one.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  eventFeedKinds,
  type EventFeed,
  type EventFeedDraft,
  type EventFeedPreset,
} from '@/api/customEventFeeds';
import FormDialog from '@/components/layout/FormDialog.vue';
import LabeledSwitch from '@/components/layout/LabeledSwitch.vue';
import SegmentedControl from '@/components/layout/SegmentedControl.vue';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';

import FieldMapEditor, { type FieldRow } from './FieldMapEditor.vue';

type Kind = (typeof eventFeedKinds)[number];

const props = defineProps<{
  feed: EventFeed | null;
  presets: EventFeedPreset[];
  pending: boolean;
  error?: string;
}>();

const emit = defineEmits<{ submit: [draft: EventFeedDraft] }>();
const open = defineModel<boolean>('open', { required: true });

const { t } = useI18n();

const noPreset = '__none__';
const defaultInterval = 30;

const displayName = ref('');
const name = ref('');
const kind = ref<Kind>('poll');
const presetKey = ref(noPreset);
const endpointUrl = ref('');
const authSecret = ref('');
const interval = ref(String(defaultInterval));
const fields = ref<FieldRow[]>([]);
const enabled = ref(false);

function asKind(value: string): Kind {
  return eventFeedKinds.find((candidate) => candidate === value) ?? 'poll';
}

watch(open, (shown) => {
  if (!shown) return;

  const feed = props.feed;

  displayName.value = feed?.displayName ?? '';
  name.value = feed?.name ?? '';
  kind.value = asKind(feed?.sourceKind ?? 'poll');
  presetKey.value = feed?.presetKey ?? noPreset;
  endpointUrl.value = feed?.endpointUrl ?? '';
  authSecret.value = '';
  interval.value = String(feed?.pollIntervalSeconds ?? defaultInterval);
  enabled.value = feed?.isEnabled ?? false;

  fields.value = Object.entries(feed?.fieldMap ?? {})
    .map(([field, path]) => ({ name: field, path }));
});

const kindOptions = computed(() =>
  eventFeedKinds.map((value) => ({
    value,
    label: t(`dashboard.settings.customEventFeeds.kind.${value}`),
  })));

const presetOptions = computed(() =>
  props.presets.filter((preset) => preset.sourceKind === kind.value));

const needsAddress = computed(() => kind.value !== 'push');
const saved = computed(() => props.feed !== null);
const hasSecret = computed(() => props.feed?.hasAuthSecret === true);

function pickPreset(key: string): void {
  presetKey.value = key;
}

function fieldMap(): Record<string, string> {
  const map: Record<string, string> = {};

  for (const row of fields.value) {
    if (row.name.trim() !== '' && row.path.trim() !== '') {
      map[row.name.trim()] = row.path.trim();
    }
  }

  return map;
}

function submit(): void {
  if (name.value.trim() === '' || displayName.value.trim() === '') return;

  emit('submit', {
    name: name.value.trim(),
    displayName: displayName.value.trim(),
    sourceKind: kind.value,
    presetKey: presetKey.value === noPreset
      ? null
      : presetKey.value,
    endpointUrl: needsAddress.value && endpointUrl.value.trim() !== ''
      ? endpointUrl.value.trim()
      : null,
    authSecret: authSecret.value === ''
      ? null
      : authSecret.value,
    fieldMap: fieldMap(),
    pollIntervalSeconds: kind.value === 'poll'
      ? Number(interval.value)
      : null,
    isEnabled: enabled.value,
  });
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    data-dialog="event-feed"
    :title="saved ? $t('dashboard.settings.customEventFeeds.edit.title') : $t('dashboard.settings.customEventFeeds.create.title')"
    :description="$t('dashboard.settings.customEventFeeds.create.description')"
    :action="saved ? $t('dashboard.settings.customEventFeeds.edit.action') : $t('dashboard.settings.customEventFeeds.create.action')"
    :pending="pending"
    :error="error"
    @submit="submit"
  >
    <template #header-action>
      <LabeledSwitch
        v-model="enabled"
        data-field="enabled"
        :label="$t('dashboard.settings.customEventFeeds.enabled')"
        :help="$t('dashboard.settings.customEventFeeds.enabledHelp')"
      />
    </template>

    <label class="grid gap-1.5 text-sm font-medium">
      {{ $t('dashboard.settings.customEventFeeds.displayName') }}
      <Input
        v-model="displayName"
        data-field="display-name"
        maxlength="100"
        :placeholder="$t('dashboard.settings.customEventFeeds.displayNamePlaceholder')"
      />
    </label>
    <label class="grid gap-1.5 text-sm font-medium">
      {{ $t('dashboard.settings.customEventFeeds.name') }}
      <Input
        v-model="name"
        data-field="name"
        class="font-mono"
        maxlength="50"
        :placeholder="$t('dashboard.settings.customEventFeeds.namePlaceholder')"
      />
      <span class="text-sm font-normal text-muted-foreground">
        {{ $t('dashboard.settings.customEventFeeds.nameHelp', { name: name || '...' }) }}
      </span>
    </label>

    <div class="grid gap-1.5">
      <span class="text-sm font-medium">{{ $t('dashboard.settings.customEventFeeds.kind.title') }}</span>
      <SegmentedControl
        v-model="kind"
        data-field="kind"
        :label="$t('dashboard.settings.customEventFeeds.kind.title')"
        :options="kindOptions"
      />
      <span class="text-sm text-muted-foreground">
        {{ $t(`dashboard.settings.customEventFeeds.kind.${kind}Help`) }}
      </span>
    </div>

    <div v-if="presetOptions.length > 0" class="grid gap-1.5">
      <span class="text-sm font-medium">{{ $t('dashboard.settings.customEventFeeds.preset.title') }}</span>
      <Select :model-value="presetKey" @update:model-value="pickPreset(String($event))">
        <SelectTrigger data-field="preset" :aria-label="$t('dashboard.settings.customEventFeeds.preset.title')">
          <SelectValue />
        </SelectTrigger>
        <SelectContent position="popper">
          <SelectGroup>
            <SelectItem :value="noPreset">{{ $t('dashboard.settings.customEventFeeds.preset.none') }}</SelectItem>
            <SelectItem v-for="preset in presetOptions" :key="preset.key" :value="preset.key">
              {{ preset.displayName }}
            </SelectItem>
          </SelectGroup>
        </SelectContent>
      </Select>
    </div>

    <label v-if="needsAddress" class="grid gap-1.5 text-sm font-medium">
      {{ $t('dashboard.settings.customEventFeeds.address') }}
      <Input
        v-model="endpointUrl"
        data-field="address"
        class="font-mono"
        maxlength="500"
        :placeholder="$t(`dashboard.settings.customEventFeeds.addressPlaceholder.${kind}`)"
      />
    </label>

    <label v-if="kind === 'poll'" class="grid gap-1.5 text-sm font-medium">
      {{ $t('dashboard.settings.customEventFeeds.interval') }}
      <Input v-model="interval"
             data-field="interval"
             type="number"
             min="1"
             class="w-32"
      />
      <span class="text-sm font-normal text-muted-foreground">
        {{ $t('dashboard.settings.customEventFeeds.intervalHelp') }}
      </span>
    </label>

    <label class="grid gap-1.5 text-sm font-medium">
      {{ $t('dashboard.settings.customEventFeeds.secret') }}
      <Input
        v-model="authSecret"
        data-field="secret"
        type="password"
        autocomplete="off"
        :placeholder="hasSecret ? $t('dashboard.settings.customEventFeeds.secretKept') : ''"
      />
      <span class="text-sm font-normal text-muted-foreground">
        {{ $t(`dashboard.settings.customEventFeeds.secretHelp.${kind}`) }}
      </span>
    </label>

    <div class="grid gap-1.5">
      <span class="text-sm font-medium">{{ $t('dashboard.settings.customEventFeeds.fields.title') }}</span>
      <FieldMapEditor v-model="fields" />
      <span class="text-sm text-muted-foreground">
        {{ $t('dashboard.settings.customEventFeeds.fields.help') }}
      </span>
    </div>
  </FormDialog>
</template>

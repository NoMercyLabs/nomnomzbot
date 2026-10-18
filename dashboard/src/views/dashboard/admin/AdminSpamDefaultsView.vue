<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The platform's spam-defense defaults: the values every new channel starts with. The bot sends the values
// together with the catalog (group, bounds, toggle or number) of every knob, so this page renders one control
// per catalog entry and holds no list of its own. Save sends the whole record; the bot checks the same ranges.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';
import Check from '~icons/nnz/check';

import {
  useAdminSpamDefaults,
  useSaveAdminSpamDefaults,
  type SpamPolicy,
  type SpamInvariant,
  type SpamSetting,
  type SpamSettings,
} from '@/api/adminSpamDefaults';
import { ApiError } from '@/api/errors';
import PageHeader from '@/components/layout/PageHeader.vue';
import GatedSwitch from '@/components/layout/GatedSwitch.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { usePanelState } from '@/composables/usePanelState';
import { usePlatformCan } from '@/composables/usePlatformCan';

import SpamSettingRow from './components/SpamSettingRow.vue';

type Value = number | boolean | '';
type Draft = Record<string, Value>;

const THRESHOLDS_KEY = 'trustThresholds';
const THRESHOLD_FIELDS = [
  'establishedDays',
  'establishedMessages',
  'establishedDistinctActiveDays',
  'establishedStrikeFreeDays',
] as const;

const { t, te } = useI18n();
const { can } = usePlatformCan();

const policy = useAdminSpamDefaults();
const save = useSaveAdminSpamDefaults();

const draft = ref<Draft>({});
const thresholds = ref<Draft>({});
const saved = ref('');

// The bot names a knob in PascalCase; its JSON and the copy keys are camelCase.
const camel = (key: string): string => key.charAt(0).toLowerCase() + key.slice(1);

function numbersOf(source: Record<string, number | string | boolean | undefined>): Draft {
  return Object.fromEntries(
    Object.entries(source).map(([key, value]): [string, Value] => [
      key,
      typeof value === 'boolean'
        ? value
        : Number(value),
    ]),
  );
}

// A number box shows its number, or nothing while it is empty; a typed blank stays '' so it reads as invalid.
const shown = (value: Value | undefined): number | '' =>
  typeof value === 'number'
    ? value
    : '';

const typed = (text: string | number): number | '' =>
  text === ''
    ? ''
    : Number(text);

function set(setting: SpamSetting, text: string | number): void {
  draft.value[camel(setting.key)] = typed(text);
}

function snapshot(): string {
  return JSON.stringify({ draft: draft.value, thresholds: thresholds.value });
}

function load(data: SpamPolicy): void {
  const { trustThresholds, ...rest } = data.settings;

  draft.value = numbersOf(rest);
  thresholds.value = numbersOf({ ...trustThresholds });
  saved.value = snapshot();
}

watch(() => policy.data.value, (data) => {
  if (data) {
    load(data);
  }
}, { immediate: true });

// cspell:ignore catalogue (the bot's wire name for the list)
const catalog = computed<SpamSetting[]>(() => policy.data.value?.catalogue ?? []);

const groups = computed<{ group: string; settings: SpamSetting[] }[]>(() => {
  const order: string[] = [];

  for (const setting of catalog.value) {
    if (!order.includes(setting.group)) {
      order.push(setting.group);
    }
  }

  return order.map((group: string) => ({
    group,
    settings: catalog.value.filter((setting: SpamSetting) => setting.group === group),
  }));
});

const dirty = computed<boolean>(() => snapshot() !== saved.value);

const number = (setting: SpamSetting, bound: 'minimum' | 'maximum'): number | undefined => {
  const value = setting[bound];

  return value === null || value === undefined
    ? undefined
    : Number(value);
};

function stepOf(setting: SpamSetting): number {
  const high = number(setting, 'maximum') ?? 1;
  const low = number(setting, 'minimum') ?? 0;

  if (high <= 1)
    return 0.01;

  return Number.isInteger(low)
    ? 1
    : 0.1;
}

function isToggleValue(setting: SpamSetting): boolean {
  return setting.isToggle && setting.key !== 'TrustThresholds';
}

function outOfRange(setting: SpamSetting): boolean {
  const value = draft.value[camel(setting.key)];
  const low = number(setting, 'minimum');
  const high = number(setting, 'maximum');

  if (typeof value !== 'number')
    return value === '';

  return (low !== undefined && value < low) || (high !== undefined && value > high);
}

const exonerationAboveCampaign = computed<boolean>(() => {
  const qualify = draft.value.qualifyNoStandingShare;
  const dequalify = draft.value.dequalifyNoStandingShare;

  return typeof qualify === 'number' && typeof dequalify === 'number' && dequalify >= qualify;
});

const thresholdBlank = computed<boolean>(() =>
  Object.values(thresholds.value).some((value: Value) => value === '' || Number(value) < 0));

const hasInvalid = computed<boolean>(() =>
  catalog.value.some((setting: SpamSetting) => outOfRange(setting)) ||
  exonerationAboveCampaign.value ||
  thresholdBlank.value);

const invariants = computed<string[]>(() =>
  (policy.data.value?.invariants ?? []).map((invariant: SpamInvariant): string =>
    hasCopy(`invariants.${invariant.decision.toLowerCase()}`)
      ? copy(`invariants.${invariant.decision.toLowerCase()}`)
      : invariant.guaranteeKey));

const panelState = usePanelState(policy);
const listError = computed<string>(() =>
  policy.error.value instanceof ApiError && policy.error.value.status === 403
    ? t('dashboard.admin.spamDefaults.forbidden')
    : t('dashboard.admin.spamDefaults.loadError'),
);

const copy = (key: string): string => t(`dashboard.admin.spamDefaults.${key}`);
const labelOf = (setting: SpamSetting): string => copy(`settings.${camel(setting.key)}.label`);
const hasCopy = (key: string): boolean => te(`dashboard.admin.spamDefaults.${key}`);

function invalidTextOf(setting: SpamSetting): string | undefined {
  if (setting.key === 'DequalifyNoStandingShare' && exonerationAboveCampaign.value)
    return copy('dequalifyBelowQualify');

  if (!outOfRange(setting))
    return undefined;

  return t('dashboard.admin.spamDefaults.range', {
    min: number(setting, 'minimum'),
    max: number(setting, 'maximum'),
  });
}

function inactiveReasonOf(setting: SpamSetting): string | undefined {
  if (!setting.pendingSlice)
    return undefined;

  return t('dashboard.admin.spamDefaults.inactiveArrives', { slice: setting.pendingSlice });
}

function body(): SpamSettings {
  const settings: Record<string, Value | Draft> = {
    ...draft.value,
    [THRESHOLDS_KEY]: { ...thresholds.value },
  };

  return settings as SpamSettings;
}

function submit(): void {
  save.mutate(body(), {
    onSuccess: () => toast.success(copy('saved')),
    onError: (error: Error) =>
      toast.error(t('dashboard.admin.spamDefaults.saveError', { reason: error.message })),
  });
}
</script>

<template>
  <PageHeader :title="$t('dashboard.admin.spamDefaults.title')" :crumb="$t('dashboard.admin.spamDefaults.crumb')">
    <template #status>
      <span v-if="dirty" class="text-sm text-muted-foreground" role="status">
        {{ $t('dashboard.admin.spamDefaults.unsaved') }}
      </span>
    </template>
    <template #actions>
      <Button
        v-if="can('featureflag:write')"
        data-save
        :disabled="!dirty || hasInvalid || save.isPending.value"
        @click="submit"
      >
        <Check aria-hidden="true" />
        {{ save.isPending.value ? $t('dashboard.admin.spamDefaults.saving') : $t('dashboard.admin.spamDefaults.save') }}
      </Button>
    </template>
  </PageHeader>
  <div data-page-body class="flex min-h-0 min-w-0 flex-1 flex-col gap-3 overflow-y-auto p-6 max-md:p-4">
    <p class="text-sm text-muted-foreground">{{ $t('dashboard.admin.spamDefaults.explain') }}</p>
    <p data-restricted class="text-sm text-muted-foreground">{{ $t('dashboard.admin.spamDefaults.restricted') }}</p>
    <PanelCard
      v-if="panelState !== 'ready'"
      tier="lead"
      class="min-h-40"
      :title="$t('dashboard.admin.spamDefaults.settingsTitle')"
      :state="panelState"
      :error-text="listError"
      @retry="policy.refetch()"
    />
    <template v-else>
      <PanelCard
        v-for="section in groups"
        :key="section.group"
        :tier="section.group === groups[0]?.group ? 'lead' : 'default'"
        :title="$t(`dashboard.admin.spamDefaults.groups.${section.group}`)"
        :data-group="section.group"
      >
        <div class="divide-y divide-border">
          <template v-for="setting in section.settings" :key="setting.key">
            <SpamSettingRow
              v-if="setting.key !== 'TrustThresholds'"
              :setting-key="camel(setting.key)"
              :label="labelOf(setting)"
              :explanation="copy(`settings.${camel(setting.key)}.explanation`)"
              :cost="copy(`settings.${camel(setting.key)}.cost`)"
              :inactive-badge="setting.pendingSlice ? copy('inactiveBadge') : undefined"
              :inactive-reason="inactiveReasonOf(setting)"
              :invalid-text="invalidTextOf(setting)"
            >
              <GatedSwitch
                v-if="isToggleValue(setting)"
                action="featureflag:write"
                platform
                :model-value="draft[camel(setting.key)] === true"
                :aria-label="labelOf(setting)"
                @update:model-value="(on: boolean) => (draft[camel(setting.key)] = on)"
              />
              <Input
                v-else
                :model-value="shown(draft[camel(setting.key)])"
                @update:model-value="(text: string | number) => set(setting, text)"
                type="number"
                :min="number(setting, 'minimum')"
                :max="number(setting, 'maximum')"
                :step="stepOf(setting)"
                :aria-label="labelOf(setting)"
                :aria-invalid="outOfRange(setting)"
              />
            </SpamSettingRow>
            <template v-else>
              <SpamSettingRow
                :setting-key="THRESHOLDS_KEY"
                :label="labelOf(setting)"
                :explanation="copy('settings.trustThresholds.explanation')"
                :cost="copy('settings.trustThresholds.cost')"
              />
              <SpamSettingRow
                v-for="field in THRESHOLD_FIELDS"
                :key="field"
                :setting-key="field"
                :label="copy(`trustFields.${field}`)"
                explanation=""
                cost=""
              >
                <Input
                  :model-value="shown(thresholds[field])"
                  @update:model-value="(text: string | number) => (thresholds[field] = typed(text))"
                  type="number"
                  min="0"
                  :aria-label="copy(`trustFields.${field}`)"
                  :aria-invalid="thresholds[field] === ''"
                />
              </SpamSettingRow>
            </template>
          </template>
        </div>
      </PanelCard>
      <PanelCard tier="quiet" :title="$t('dashboard.admin.spamDefaults.invariantsTitle')" data-invariants>
        <ul class="divide-y divide-border">
          <li
            v-for="text in invariants"
            :key="text"
            class="px-4 py-3 text-sm text-muted-foreground"
          >
            {{ text }}
          </li>
        </ul>
      </PanelCard>
    </template>
  </div>
</template>

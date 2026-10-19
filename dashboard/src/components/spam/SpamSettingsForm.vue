<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The spam-defense knobs as a form, for the platform defaults page and for a channel's own policy. The bot sends
// the values together with the catalog (group, bounds, toggle or number) of every knob, so the form renders one
// control per catalog entry and holds no list of its own. It keeps the draft, checks the same ranges the bot
// checks, and reports the whole record to its page, which owns the Save button. Copy lives under
// dashboard.admin.spamDefaults, so a page that shows this form loads the admin words beside its own.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import type { SpamPolicy, SpamSetting, SpamSettings } from '@/api/adminSpamDefaults';
import GatedSwitch from '@/components/layout/GatedSwitch.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Input } from '@/components/ui/input';
import { useCan } from '@/composables/useCan';
import { usePlatformCan } from '@/composables/usePlatformCan';
import { useRefusalLines } from '@/composables/useRefusalLines';
import { lookup, merge } from '@/lib/array';

import SpamSettingRow from './SpamSettingRow.vue';

type Value = number | boolean | '';
type Draft = Record<string, Value>;

export type SpamFormState = {
  body: SpamSettings;
  dirty: boolean;
  invalid: boolean;
};

const props = defineProps<{
  policy: SpamPolicy;
  // The permission that lets the person change the knobs; without it the form shows values only.
  action: string;
  // Reads the platform admin keys instead of the channel keys.
  platform?: boolean;
  // The error of the page's last refused save; the lines it names show next to their fields until the next edit.
  refusal?: Error | null;
}>();

const emit = defineEmits<{
  change: [state: SpamFormState];
}>();

const THRESHOLDS_KEY = 'trustThresholds';
const THRESHOLD_FIELDS = [
  'establishedDays',
  'establishedMessages',
  'establishedDistinctActiveDays',
  'establishedStrikeFreeDays',
] as const;

const { t, te } = useI18n();
const { can } = props.platform
  ? usePlatformCan()
  : useCan();

const writable = computed<boolean>(() => can(props.action));

const draft = ref<Draft>({});
const thresholds = ref<Draft>({});
const saved = ref('');

const { refuse, clear, linesFor } = useRefusalLines('dashboard.admin.spamDefaults');

watch(
  () => props.refusal,
  (error) => {
    if (error) {
      refuse(error);
    } else {
      clear();
    }
  },
  { immediate: true },
);

// An edit answers the refusal, so its lines go.
watch([draft, thresholds], clear, { deep: true });

// The bot names a knob in PascalCase; its JSON and the copy keys are camelCase.
const camel = (key: string): string => key.charAt(0).toLowerCase() + key.slice(1);

function numbersOf(source: Record<string, number | string | boolean | undefined>): Draft {
  return Object.fromEntries(
    Object.entries(source).map(
      ([key, value]) =>
        [key, typeof value === 'boolean'
          ? value
          : Number(value)] as const satisfies readonly [
            string,
            Value,
          ],
    ),
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

watch(() => props.policy, load, { immediate: true });

// cspell:ignore catalogue (the bot's wire name for the list)
const catalog = computed<SpamSetting[]>(() => props.policy.catalogue ?? []);

const groups = computed<{ group: string; settings: SpamSetting[] }[]>(() => {
  const order: string[] = [];

  for (const setting of catalog.value) {
    if (!order.includes(setting.group)) {
      order.push(setting.group);
    }
  }

  return order.map((group) => ({
    group,
    settings: catalog.value.filter((setting) => setting.group === group),
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

// A cap below the value it caps would silently win, so the bot refuses it; the form names it before Save.
const CAPS = {
  MaxWindowSeconds: { floor: 'windowSeconds', copyKey: 'spam.policy.error.maxWindowBelowWindow' },
  LockdownMaxMinutes: {
    floor: 'lockdownMinutes',
    copyKey: 'spam.policy.error.lockdownMaxBelowLockdown',
  },
} as const satisfies Record<string, { floor: string; copyKey: string }>;

function capBelowFloor(setting: SpamSetting): boolean {
  const cap = lookup(CAPS, setting.key);

  if (!cap)
    return false;

  const value = draft.value[camel(setting.key)];
  const floor = draft.value[cap.floor];

  return typeof value === 'number' && typeof floor === 'number' && value < floor;
}

const thresholdBlank = computed<boolean>(() =>
  Object.values(thresholds.value).some((value) => value === '' || Number(value) < 0),
);

const invalid = computed<boolean>(
  () =>
    catalog.value.some((setting) => outOfRange(setting) || capBelowFloor(setting)) ||
    exonerationAboveCampaign.value ||
    thresholdBlank.value,
);

const copy = (key: string): string => t(`dashboard.admin.spamDefaults.${key}`);
const hasCopy = (key: string): boolean => te(`dashboard.admin.spamDefaults.${key}`);
const labelOf = (setting: SpamSetting): string => copy(`settings.${camel(setting.key)}.label`);

const invariants = computed<string[]>(() =>
  (props.policy.invariants ?? []).map((invariant): string =>
    hasCopy(`invariants.${invariant.decision.toLowerCase()}`)
      ? copy(`invariants.${invariant.decision.toLowerCase()}`)
      : invariant.guaranteeKey,
  ),
);

function localInvalidTextOf(setting: SpamSetting): string[] {
  if (setting.key === 'DequalifyNoStandingShare' && exonerationAboveCampaign.value)
    return [copy('spam.policy.error.dequalifyNotBelowQualify')];

  const cap = lookup(CAPS, setting.key);

  if (cap && capBelowFloor(setting))
    return [copy(cap.copyKey)];

  if (!outOfRange(setting))
    return [];

  return [
    t('dashboard.admin.spamDefaults.spam.policy.error.outOfRange', {
      min: number(setting, 'minimum'),
      max: number(setting, 'maximum'),
    }),
  ];
}

// What the form sees and what the bot refused say the same thing in the same words, so a line shows once.
function joinedLines(lines: readonly string[]): string | undefined {
  return lines.length > 0
    ? Array.from(new Set(lines)).join(' ')
    : undefined;
}

function invalidTextOf(setting: SpamSetting): string | undefined {
  const local = localInvalidTextOf(setting);
  const refused = linesFor(camel(setting.key));

  return joinedLines(merge(local, refused));
}

function inactiveReasonOf(setting: SpamSetting): string | undefined {
  if (!setting.pendingSlice)
    return undefined;

  return t('dashboard.admin.spamDefaults.inactiveArrives', { slice: setting.pendingSlice });
}

function body(): SpamSettings {
  const settings = {
    ...draft.value,
    [THRESHOLDS_KEY]: { ...thresholds.value },
  } as const satisfies Record<string, Value | Draft>;

  return settings as SpamSettings;
}

watch(
  [draft, thresholds, saved],
  () => emit('change', {
    body: body(),
    dirty: dirty.value,
    invalid: invalid.value,
  }),
  { deep: true, immediate: true },
);
</script>

<template>
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
            :action="action"
            :platform="platform"
            :model-value="draft[camel(setting.key)] === true"
            :aria-label="labelOf(setting)"
            @update:model-value="(on: boolean) => (draft[camel(setting.key)] = on)"
          />
          <Input
            v-else
            :model-value="shown(draft[camel(setting.key)])"
            @update:model-value="(text: string | number) => set(setting, text)"
            type="number"
            :disabled="!writable"
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
            :invalid-text="joinedLines(linesFor(field))"
          >
            <Input
              :model-value="shown(thresholds[field])"
              @update:model-value="(text: string | number) => (thresholds[field] = typed(text))"
              type="number"
              min="0"
              :disabled="!writable"
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
      <li v-for="text in invariants" :key="text" class="px-4 py-3 text-sm text-muted-foreground">
        {{ text }}
      </li>
    </ul>
  </PanelCard>
</template>

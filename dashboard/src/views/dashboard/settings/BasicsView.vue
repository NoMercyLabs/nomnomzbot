<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The channel basics: command prefix, bot line marker, language, timezone and auto-join. The fields are a
// draft until Save; the bot echoes what it stored and the draft then matches it again.
import { computed, reactive, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import { ApiError } from '@/api/errors';
import { useBasics, useUpdateBasics, type ChannelBasics } from '@/api/settings';
import PanelCard from '@/components/layout/PanelCard.vue';
import type { PanelState } from '@/composables/usePanelState';
import RowGroup from '@/components/layout/RowGroup.vue';
import SettingRow from '@/components/layout/SettingRow.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { locales } from '@/i18n';
import { keysOf, merge } from '@/lib/array';

import ReadabilityRow from './components/ReadabilityRow.vue';

type Draft = {
  prefix: string;
  botLinePrefix: string;
  locale: string;
  timezone: string;
  autoJoin: boolean;
};

const prefixPattern = /^\S{1,5}$/;
const linePattern = /^.{0,4}$/u;

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const basics = useBasics(channelId);
const save = useUpdateBasics(channelId);

const draft = reactive<Draft>({
  prefix: '',
  botLinePrefix: '',
  locale: '',
  timezone: '',
  autoJoin: false,
});

function fromStored(stored: ChannelBasics): Draft {
  return {
    prefix: stored.prefix,
    botLinePrefix: stored.botLinePrefix ?? '',
    locale: stored.locale ?? '',
    timezone: stored.timezone ?? '',
    autoJoin: stored.autoJoin,
  };
}

watch(
  () => basics.data.value,
  (stored) => {
    if (stored !== undefined) {
      Object.assign(draft, fromStored(stored));
    }
  },
  { immediate: true },
);

const stored = computed(() => fromStored(basics.data.value ?? {
  prefix: '',
  autoJoin: false,
}));

const dirty = computed(() => keysOf(draft).some(
  (key) => draft[key] !== stored.value[key],
));
const prefixValid = computed(() => prefixPattern.test(draft.prefix));
const lineValid = computed(() => linePattern.test(draft.botLinePrefix));
const canSave = computed(
  () => dirty.value && prefixValid.value && lineValid.value && !save.isPending.value,
);

const state = computed<PanelState>(() => {
  if (basics.isError.value)
    return 'error';

  return basics.isPending.value
    ? 'loading'
    : 'ready';
});

// Each language is named in its own language, so a reader finds theirs without reading the current one.
function languageName(tag: string): string {
  return new Intl.DisplayNames([tag], { type: 'language' }).of(tag) ?? tag;
}

const languages = computed(() => {
  const known: readonly string[] = locales;

  return known.includes(draft.locale) || draft.locale === ''
    ? known
    : merge(known, [draft.locale]);
});

const zones = computed(() => {
  const all = Intl.supportedValuesOf('timeZone');

  return all.includes(draft.timezone) || draft.timezone === ''
    ? all
    : merge(all, [draft.timezone]);
});

function failureText(error: Error): string {
  return error instanceof ApiError
    ? t(error.messageKey)
    : t('dashboard.settings.basics.saveFailed');
}

function submit(): void {
  save.mutate(
    {
      prefix: draft.prefix,
      botLinePrefix: draft.botLinePrefix,
      locale: draft.locale === ''
        ? null
        : draft.locale,
      timezone: draft.timezone === ''
        ? null
        : draft.timezone,
      autoJoin: draft.autoJoin,
    },
    {
      onSuccess: () => toast.success(t('dashboard.settings.basics.saved')),
      onError: (error: Error) => toast.error(failureText(error)),
    },
  );
}

function discard(): void {
  Object.assign(draft, stored.value);
}
</script>

<template>
  <PanelCard
    data-view="basics"
    pad
    :title="$t('dashboard.settings.basics.title')"
    :description="$t('dashboard.settings.basics.description')"
    :state="state"
    :error-text="$t('dashboard.settings.basics.loadFailed')"
    @retry="basics.refetch()"
  >
    <RowGroup>
      <ReadabilityRow
        :title="$t('dashboard.settings.basics.prefix.title')"
        :description="$t('dashboard.settings.basics.prefix.description')"
      >
        <Input
          v-model="draft.prefix"
          class="w-24"
          maxlength="5"
          :aria-label="$t('dashboard.settings.basics.prefix.title')"
          :disabled="!can('setup:write')"
          :aria-invalid="!prefixValid"
        />
      </ReadabilityRow>
      <ReadabilityRow
        :title="$t('dashboard.settings.basics.botLinePrefix.title')"
        :description="$t('dashboard.settings.basics.botLinePrefix.description')"
      >
        <Input
          v-model="draft.botLinePrefix"
          class="w-24"
          maxlength="4"
          :aria-label="$t('dashboard.settings.basics.botLinePrefix.title')"
          :disabled="!can('setup:write')"
          :aria-invalid="!lineValid"
        />
      </ReadabilityRow>
      <ReadabilityRow
        :title="$t('dashboard.settings.basics.locale.title')"
        :description="$t('dashboard.settings.basics.locale.description')"
      >
        <Select v-model="draft.locale" :disabled="!can('setup:write')">
          <SelectTrigger
            class="w-56"
            :aria-label="$t('dashboard.settings.basics.locale.title')"
          >
            <SelectValue :placeholder="$t('dashboard.settings.basics.locale.placeholder')" />
          </SelectTrigger>
          <SelectContent position="popper" align="end">
            <SelectGroup>
              <SelectItem v-for="tag in languages" :key="tag" :value="tag" :lang="tag">{{
                languageName(tag)
              }}</SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
      </ReadabilityRow>
      <ReadabilityRow
        :title="$t('dashboard.settings.basics.timezone.title')"
        :description="$t('dashboard.settings.basics.timezone.description')"
      >
        <Select v-model="draft.timezone" :disabled="!can('setup:write')">
          <SelectTrigger
            class="w-56"
            :aria-label="$t('dashboard.settings.basics.timezone.title')"
          >
            <SelectValue :placeholder="$t('dashboard.settings.basics.timezone.placeholder')" />
          </SelectTrigger>
          <SelectContent position="popper" align="end">
            <SelectGroup>
              <SelectItem v-for="zone in zones" :key="zone" :value="zone">{{ zone }}</SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
      </ReadabilityRow>
      <SettingRow
        v-model="draft.autoJoin"
        :disabled="!can('setup:write')"
        :title="$t('dashboard.settings.basics.autoJoin.title')"
        :description="$t('dashboard.settings.basics.autoJoin.description')"
      />
    </RowGroup>
    <template v-if="can('setup:write')" #footer>
      <div class="flex w-full items-center justify-end gap-2">
        <Button variant="ghost" size="sm" :disabled="!dirty || save.isPending.value" @click="discard">
          {{ $t('dashboard.settings.basics.discard') }}
        </Button>
        <Button size="sm" :disabled="!canSave" @click="submit">
          {{ $t('dashboard.settings.basics.save') }}
        </Button>
      </div>
    </template>
  </PanelCard>
</template>

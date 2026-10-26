<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One chat filter, edited on its own page section (six fields or more are never a dialog). The filter type picks
// which field set shows. A sample message can be tried against the filter before it is saved. Save is the one
// primary action; Cancel and Try are outline.
import { computed, reactive, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import {
  CHAT_FILTER_ACTIONS,
  CHAT_FILTER_TYPES,
  useSaveChatFilter,
  useTestChatFilter,
  type ChatFilter,
  type ChatFilterTest,
} from '@/api/chatFilters';
import NumberRow from '@/components/layout/NumberRow.vue';
import OptionSelect, { type Choice } from '@/components/layout/OptionSelect.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SettingRow from '@/components/layout/SettingRow.vue';
import { Button } from '@/components/ui/button';
import { Field, FieldError, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import {
  EXEMPT_RUNGS,
  problems,
  toDraft,
  toRequest,
  type ChatFilterDraft,
} from './chatFilterDraft';

const props = defineProps<{ filter: ChatFilter | null }>();
const emit = defineEmits<{ close: [] }>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const save = useSaveChatFilter(channelId);
const tester = useTestChatFilter(channelId);

const draft = reactive<ChatFilterDraft>(toDraft(props.filter));
const tried = ref(false);
const sample = ref('');
const verdict = ref<ChatFilterTest | null>(null);

const found = computed(() => problems(draft));
const shown = computed(() =>
  tried.value
    ? found.value
    : []);

const choose = (group: string, values: readonly string[]): Choice[] =>
  values.map((value) => ({ value, label: t(`dashboard.settings.chatFilters.${group}.${value}`) }));

const typeChoices = computed<Choice[]>(() => choose('types', CHAT_FILTER_TYPES));
const actionChoices = computed<Choice[]>(() => choose('actions', CHAT_FILTER_ACTIONS));
const roleChoices = computed<Choice[]>(() => choose('roles', EXEMPT_RUNGS));

function submit(): void {
  tried.value = true;

  if (found.value.length > 0) return;

  save.mutate({ existing: props.filter, request: toRequest(draft) }, {
    onSuccess: () => {
      toast.success(t('dashboard.settings.chatFilters.saved'));
      emit('close');
    },
    onError: (error: Error) =>
      toast.error(t('dashboard.settings.chatFilters.saveError', { reason: error.message })),
  });
}

function tryIt(): void {
  const request = toRequest(draft);

  tester.mutate(
    {
      filterType: request.filterType,
      pattern: request.pattern,
      terms: request.terms,
      linkPolicy: request.linkPolicy,
      sampleMessage: sample.value,
      isCaseSensitive: request.isCaseSensitive,
    },
    {
      onSuccess: (result: ChatFilterTest) => {
        verdict.value = result;
      },
      onError: () => toast.error(t('dashboard.settings.chatFilters.testError')),
    },
  );
}

const verdictText = computed<string | null>(() => {
  if (verdict.value === null)
    return null;

  if (verdict.value.compileError)
    return t('dashboard.settings.chatFilters.test.compileError', { reason: verdict.value.compileError });

  return verdict.value.isMatch
    ? t('dashboard.settings.chatFilters.test.match')
    : t('dashboard.settings.chatFilters.test.noMatch');
});
</script>

<template>
  <div data-view="chat-filter-editor" class="grid gap-4">
    <PanelCard
      tier="lead"
      pad
      :title="
        filter
          ? $t('dashboard.settings.chatFilters.editTitle', { name: filter.name })
          : $t('dashboard.settings.chatFilters.newTitle')
      "
    >
      <div class="grid gap-4">
        <Field :data-invalid="shown.includes('name') ? true : undefined">
          <FieldLabel for="chat-filter-name">{{ $t("dashboard.settings.chatFilters.fields.name") }}</FieldLabel>
          <Input id="chat-filter-name" v-model="draft.name" data-field="name" />
          <FieldError v-if="shown.includes('name')">
            {{ $t("dashboard.settings.chatFilters.problems.name") }}
          </FieldError>
        </Field>

        <Field>
          <FieldLabel>{{ $t("dashboard.settings.chatFilters.fields.type") }}</FieldLabel>
          <OptionSelect
            v-model="draft.filterType"
            data-field="filterType"
            :options="typeChoices"
            :aria-label="$t('dashboard.settings.chatFilters.fields.type')"
          />
        </Field>

        <Field v-if="draft.filterType === 'Regex'" :data-invalid="shown.includes('pattern') ? true : undefined">
          <FieldLabel for="chat-filter-pattern">{{ $t("dashboard.settings.chatFilters.fields.pattern") }}</FieldLabel>
          <Input id="chat-filter-pattern" v-model="draft.pattern" data-field="pattern" class="font-mono" />
          <FieldError v-if="shown.includes('pattern')">
            {{ $t("dashboard.settings.chatFilters.problems.pattern") }}
          </FieldError>
        </Field>

        <Field v-if="draft.filterType === 'Blocklist'" :data-invalid="shown.includes('terms') ? true : undefined">
          <FieldLabel for="chat-filter-terms">{{ $t("dashboard.settings.chatFilters.fields.terms") }}</FieldLabel>
          <Textarea id="chat-filter-terms" v-model="draft.terms" data-field="terms" rows="4" />
          <FieldError v-if="shown.includes('terms')">
            {{ $t("dashboard.settings.chatFilters.problems.terms") }}
          </FieldError>
        </Field>

        <template v-if="draft.filterType === 'LinkPolicy'">
          <Field>
            <FieldLabel for="chat-filter-domains">{{ $t("dashboard.settings.chatFilters.fields.allowedDomains") }}</FieldLabel>
            <Textarea id="chat-filter-domains" v-model="draft.allowedDomains" data-field="allowedDomains" rows="3" />
          </Field>
          <RowGroup>
            <SettingRow
              v-model="draft.matchBareDomains"
              data-field="matchBareDomains"
              :title="$t('dashboard.settings.chatFilters.fields.bareDomains')"
              :description="$t('dashboard.settings.chatFilters.fields.bareDomainsHelp')"
            />
          </RowGroup>
        </template>

        <Field>
          <FieldLabel>{{ $t("dashboard.settings.chatFilters.fields.action") }}</FieldLabel>
          <OptionSelect
            v-model="draft.action"
            data-field="action"
            :options="actionChoices"
            :aria-label="$t('dashboard.settings.chatFilters.fields.action')"
          />
        </Field>

        <RowGroup v-if="draft.action === 'Timeout'">
          <NumberRow
            v-model="draft.timeoutSeconds"
            data-field="timeoutSeconds"
            :title="$t('dashboard.settings.chatFilters.fields.timeout')"
            :description="$t('dashboard.settings.chatFilters.fields.timeoutHelp')"
            :min="1"
            :error="shown.includes('timeout') ? $t('dashboard.settings.chatFilters.problems.timeout') : undefined"
          />
        </RowGroup>

        <Field>
          <FieldLabel>{{ $t("dashboard.settings.chatFilters.fields.exempt") }}</FieldLabel>
          <OptionSelect
            v-model="draft.exemptRung"
            data-field="exemptRung"
            :options="roleChoices"
            :aria-label="$t('dashboard.settings.chatFilters.fields.exempt')"
          />
        </Field>

        <RowGroup>
          <SettingRow
            v-model="draft.isCaseSensitive"
            data-field="isCaseSensitive"
            :title="$t('dashboard.settings.chatFilters.fields.caseSensitive')"
          />
          <SettingRow
            v-model="draft.isEnabled"
            data-field="isEnabled"
            :title="$t('dashboard.settings.chatFilters.fields.enabled')"
          />
        </RowGroup>
      </div>
    </PanelCard>

    <PanelCard
      tier="quiet"
      pad
      :title="$t('dashboard.settings.chatFilters.test.title')"
      :description="$t('dashboard.settings.chatFilters.test.description')"
    >
      <div class="grid gap-3">
        <Input
          v-model="sample"
          data-field="sample"
          :aria-label="$t('dashboard.settings.chatFilters.test.sample')"
          :placeholder="$t('dashboard.settings.chatFilters.test.sample')"
        />
        <div class="flex flex-wrap items-center gap-3">
          <Button
            variant="outline"
            size="sm"
            data-action="try"
            :disabled="sample.trim() === '' || tester.isPending.value"
            @click="tryIt"
          >
            {{ $t("dashboard.settings.chatFilters.test.run") }}
          </Button>
          <span v-if="verdictText" data-verdict class="text-sm">{{ verdictText }}</span>
        </div>
      </div>
    </PanelCard>

    <div class="flex flex-wrap gap-2">
      <Button size="sm" data-action="save" :disabled="save.isPending.value" @click="submit">
        {{ $t("dashboard.settings.chatFilters.save") }}
      </Button>
      <Button variant="outline" size="sm" data-action="cancel" @click="emit('close')">
        {{ $t("dashboard.settings.chatFilters.cancel") }}
      </Button>
    </div>
  </div>
</template>

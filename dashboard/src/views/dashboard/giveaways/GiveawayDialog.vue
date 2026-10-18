<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->

<script setup lang="ts">
// A new giveaway, or an edit of a draft or closed one: how viewers enter, who may enter, how many win
// and what they get. Every setting the API takes has a control here. The form holds text and switches;
// giveawayForm.ts turns them into the draft that is saved.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { useCodePools, useSaveGiveaway, type Giveaway } from '@/api/giveaways';
import { usePipelines } from '@/api/pipelines';
import FormDialog from '@/components/layout/FormDialog.vue';
import LabeledSwitch from '@/components/layout/LabeledSwitch.vue';
import { Field, FieldInfo, FieldLabel, FieldLegend, FieldSet } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText, fieldErrorLines } from '@/lib/errors';

import {
  draftFromForm,
  entryModes,
  formFromGiveaway,
  formProblem,
  prizeModes,
  type GiveawayForm,
} from './giveawayForm';

const open = defineModel<boolean>('open', { required: true });
const props = defineProps<{ giveaway: Giveaway | null }>();

type NumberKey = {
  [Key in keyof GiveawayForm]: GiveawayForm[Key] extends string ? Key : never;
}[keyof GiveawayForm];

const eligibilityFields = [
  'minStandingLevel',
  'minWatchMinutes',
  'minAccountAgeDays',
] as const satisfies readonly NumberKey[];

const ticketFields = ['subT1', 'subT2', 'subT3', 'vip'] as const satisfies readonly NumberKey[];

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const save = useSaveGiveaway(channelId);
const pipelines = usePipelines(channelId);
const pools = useCodePools(channelId);

const form = ref<GiveawayForm>(formFromGiveaway(props.giveaway));
const error = ref<string | undefined>(undefined);

const isKeyword = computed<boolean>(() => form.value.entryMode === 'keyword');

watch(open, (isOpen: boolean): void => {
  if (!isOpen) return;

  form.value = formFromGiveaway(props.giveaway);
  error.value = undefined;
});

async function submit(): Promise<void> {
  const problem = formProblem(form.value);

  error.value =
    problem === undefined
      ? undefined
      : t(`dashboard.giveaways.dialog.problem.${problem}`);

  if (error.value !== undefined) return;

  try {
    await save.mutateAsync({ existing: props.giveaway, draft: draftFromForm(form.value) });
  } catch (failure) {
    error.value = [errorText(failure, t), ...fieldErrorLines(failure)].join(' ');

    return;
  }

  open.value = false;
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    :title="
      giveaway
        ? $t('dashboard.giveaways.dialog.editTitle')
        : $t('dashboard.giveaways.dialog.newTitle')
    "
    :description="$t('dashboard.giveaways.dialog.description')"
    :action="
      giveaway ? $t('dashboard.giveaways.dialog.save') : $t('dashboard.giveaways.dialog.create')
    "
    :pending="save.isPending.value"
    :error="error"
    @submit="submit"
  >
    <Field>
      <FieldLabel for="giveaway-title">{{ $t("dashboard.giveaways.dialog.title") }}</FieldLabel>
      <Input id="giveaway-title" v-model="form.title" data-giveaway-title autocomplete="off" />
    </Field>

    <FieldSet class="gap-3">
      <FieldLegend variant="label">{{ $t("dashboard.giveaways.dialog.entry.legend") }}</FieldLegend>
      <Field>
        <FieldLabel for="giveaway-entry-mode">{{
          $t("dashboard.giveaways.dialog.entry.mode")
        }}</FieldLabel>
        <Select v-model="form.entryMode">
          <SelectTrigger id="giveaway-entry-mode" data-giveaway-entry-mode class="w-full">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem v-for="mode in entryModes" :key="mode" :value="mode">
              {{ $t(`dashboard.giveaways.dialog.entry.modes.${mode}`) }}
            </SelectItem>
          </SelectContent>
        </Select>
      </Field>
      <Field v-if="isKeyword">
        <FieldLabel for="giveaway-keyword">{{
          $t("dashboard.giveaways.dialog.keyword")
        }}</FieldLabel>
        <Input
          id="giveaway-keyword"
          v-model="form.keyword"
          data-giveaway-keyword
          autocomplete="off"
        />
      </Field>
      <div class="grid grid-cols-2 gap-3">
        <Field>
          <div class="flex items-center gap-1.5">
            <FieldLabel for="giveaway-entry-cost">{{
              $t("dashboard.giveaways.dialog.entry.cost")
            }}</FieldLabel>
            <FieldInfo>{{ $t("dashboard.giveaways.dialog.entry.costHelp") }}</FieldInfo>
          </div>
          <Input
            id="giveaway-entry-cost"
            v-model="form.entryCost"
            data-giveaway-entry-cost
            type="number"
            min="0"
            inputmode="numeric"
          />
        </Field>
        <Field>
          <div class="flex items-center gap-1.5">
            <FieldLabel for="giveaway-max-entries">{{
              $t("dashboard.giveaways.dialog.entry.maxPerUser")
            }}</FieldLabel>
            <FieldInfo>{{ $t("dashboard.giveaways.dialog.entry.maxPerUserHelp") }}</FieldInfo>
          </div>
          <Input
            id="giveaway-max-entries"
            v-model="form.maxEntriesPerUser"
            data-giveaway-max-entries
            type="number"
            min="1"
            inputmode="numeric"
          />
        </Field>
      </div>
    </FieldSet>

    <FieldSet class="gap-3">
      <FieldLegend variant="label">{{ $t("dashboard.giveaways.dialog.who.legend") }}</FieldLegend>
      <LabeledSwitch
        v-model="form.requireSub"
        data-giveaway-require-sub
        :label="$t('dashboard.giveaways.dialog.who.requireSub')"
      />
      <LabeledSwitch
        v-model="form.excludeModerators"
        data-giveaway-exclude-moderators
        :label="$t('dashboard.giveaways.dialog.who.excludeModerators')"
      />
      <LabeledSwitch
        v-model="form.requires18Plus"
        data-giveaway-requires-18-plus
        :label="$t('dashboard.giveaways.dialog.who.requires18Plus')"
        :help="$t('dashboard.giveaways.dialog.who.requires18PlusHelp')"
      />
      <div class="grid grid-cols-3 gap-3">
        <Field v-for="key in eligibilityFields" :key="key">
          <FieldLabel :for="`giveaway-${key}`">{{
            $t(`dashboard.giveaways.dialog.who.${key}`)
          }}</FieldLabel>
          <Input
            :id="`giveaway-${key}`"
            v-model="form[key]"
            :data-giveaway-field="key"
            type="number"
            min="0"
            inputmode="numeric"
          />
        </Field>
      </div>
    </FieldSet>

    <FieldSet class="gap-3">
      <div class="flex items-center gap-1.5">
        <FieldLegend variant="label" class="mb-0">{{
          $t("dashboard.giveaways.dialog.tickets.legend")
        }}</FieldLegend>
        <FieldInfo>{{ $t("dashboard.giveaways.dialog.tickets.help") }}</FieldInfo>
      </div>
      <div class="grid grid-cols-4 gap-3">
        <Field v-for="key in ticketFields" :key="key">
          <FieldLabel :for="`giveaway-${key}`">{{
            $t(`dashboard.giveaways.dialog.tickets.${key}`)
          }}</FieldLabel>
          <Input
            :id="`giveaway-${key}`"
            v-model="form[key]"
            :data-giveaway-field="key"
            type="number"
            min="1"
            inputmode="numeric"
          />
        </Field>
      </div>
    </FieldSet>

    <FieldSet class="gap-3">
      <FieldLegend variant="label">{{ $t("dashboard.giveaways.dialog.draw.legend") }}</FieldLegend>
      <div class="grid grid-cols-2 gap-3">
        <Field>
          <FieldLabel for="giveaway-winners">{{
            $t("dashboard.giveaways.dialog.winners")
          }}</FieldLabel>
          <Input
            id="giveaway-winners"
            v-model="form.winnerCount"
            data-giveaway-winners
            type="number"
            min="1"
            inputmode="numeric"
          />
        </Field>
        <Field>
          <div class="flex items-center gap-1.5">
            <FieldLabel for="giveaway-claim-window">{{
              $t("dashboard.giveaways.dialog.draw.claimWindow")
            }}</FieldLabel>
            <FieldInfo>{{ $t("dashboard.giveaways.dialog.draw.claimWindowHelp") }}</FieldInfo>
          </div>
          <Input
            id="giveaway-claim-window"
            v-model="form.claimWindowMinutes"
            data-giveaway-claim-window
            type="number"
            min="1"
            inputmode="numeric"
          />
        </Field>
      </div>
      <Field>
        <div class="flex items-center gap-1.5">
          <FieldLabel for="giveaway-close-at">{{
            $t("dashboard.giveaways.dialog.draw.closeAt")
          }}</FieldLabel>
          <FieldInfo>{{ $t("dashboard.giveaways.dialog.draw.closeAtHelp") }}</FieldInfo>
        </div>
        <Input
          id="giveaway-close-at"
          v-model="form.scheduledCloseAt"
          data-giveaway-close-at
          type="datetime-local"
        />
      </Field>
    </FieldSet>

    <FieldSet class="gap-3">
      <FieldLegend variant="label">{{ $t("dashboard.giveaways.dialog.prize.legend") }}</FieldLegend>
      <Field>
        <FieldLabel for="giveaway-prize-mode">{{
          $t("dashboard.giveaways.dialog.prize.mode")
        }}</FieldLabel>
        <Select v-model="form.prizeMode">
          <SelectTrigger id="giveaway-prize-mode" data-giveaway-prize-mode class="w-full">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem v-for="mode in prizeModes" :key="mode" :value="mode">
              {{ $t(`dashboard.giveaways.dialog.prize.modes.${mode}`) }}
            </SelectItem>
          </SelectContent>
        </Select>
      </Field>
      <template v-if="form.prizeMode === 'currency'">
        <Field>
          <FieldLabel for="giveaway-prize-amount">{{
            $t("dashboard.giveaways.dialog.prize.amount")
          }}</FieldLabel>
          <Input
            id="giveaway-prize-amount"
            v-model="form.prizeCurrencyAmount"
            data-giveaway-prize-amount
            type="number"
            min="1"
            inputmode="numeric"
          />
        </Field>
        <LabeledSwitch
          v-model="form.prizeFromPot"
          data-giveaway-prize-from-pot
          :label="$t('dashboard.giveaways.dialog.prize.fromPot')"
          :help="$t('dashboard.giveaways.dialog.prize.fromPotHelp')"
        />
      </template>
      <Field v-else-if="form.prizeMode === 'pipeline'">
        <FieldLabel for="giveaway-prize-pipeline">{{
          $t("dashboard.giveaways.dialog.prize.pipeline")
        }}</FieldLabel>
        <Select v-model="form.prizePipelineId">
          <SelectTrigger id="giveaway-prize-pipeline" data-giveaway-prize-pipeline class="w-full">
            <SelectValue
              :placeholder="$t('dashboard.giveaways.dialog.prize.pipelinePlaceholder')"
            />
          </SelectTrigger>
          <SelectContent>
            <SelectItem
              v-for="pipeline in pipelines.data.value ?? []"
              :key="pipeline.id"
              :value="pipeline.id"
            >
              {{ pipeline.name }}
            </SelectItem>
          </SelectContent>
        </Select>
      </Field>
      <Field v-else-if="form.prizeMode === 'code_pool'">
        <FieldLabel for="giveaway-prize-pool">{{
          $t("dashboard.giveaways.dialog.prize.pool")
        }}</FieldLabel>
        <Select v-model="form.prizeCodePoolId">
          <SelectTrigger id="giveaway-prize-pool" data-giveaway-prize-pool class="w-full">
            <SelectValue :placeholder="$t('dashboard.giveaways.dialog.prize.poolPlaceholder')" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem v-for="pool in pools.data.value ?? []" :key="pool.id" :value="pool.id">
              {{ pool.name }}
            </SelectItem>
          </SelectContent>
        </Select>
      </Field>
    </FieldSet>
  </FormDialog>
</template>

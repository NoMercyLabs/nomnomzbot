<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Add or change a store item: what it is called, what it costs, what it runs, and the limits on buying
// it. The effect kind is chosen when the item is made; the bot does not let it change afterwards.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useCreateCatalogItem,
  useUpdateCatalogItem,
  type CatalogItem,
} from '@/api/economy';
import { usePipelines } from '@/api/pipelines';
import FormDialog from '@/components/layout/FormDialog.vue';
import SettingRow from '@/components/layout/SettingRow.vue';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';
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
import { permissionLevels } from '@/views/dashboard/commands/components/permissionLevels';

const noPipeline = 'none';

const props = defineProps<{ item: CatalogItem | null }>();
const open = defineModel<boolean>('open', { required: true });

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const create = useCreateCatalogItem(channelId);
const update = useUpdateCatalogItem(channelId);
const pipelines = usePipelines(channelId);

const name = ref('');
const description = ref('');
const sinkType = ref('');
const cost = ref(0);
const iconUrl = ref('');
const permission = ref('Everyone');
const pipelineId = ref(noPipeline);
const cooldown = ref(0);
const cooldownPerUser = ref(false);
const stockLimit = ref<number | ''>('');
const perStream = ref<number | ''>('');
const sortOrder = ref(0);
const error = ref<string | undefined>(undefined);

const pending = computed<boolean>(() => create.isPending.value || update.isPending.value);

const blankToNull = (text: string): string | null =>
  text.trim() === ''
    ? null
    : text.trim();
const numberOrNull = (value: number | ''): number | null =>
  value === ''
    ? null
    : value;
const optionalNumber = (value: number | string | null | undefined): number | '' =>
  value === null || value === undefined
    ? ''
    : Number(value);

watch(
  open,
  (isOpen: boolean): void => {
    if (!isOpen) return;

    const row = props.item;

    name.value = row?.name ?? '';
    description.value = row?.description ?? '';
    sinkType.value = row?.sinkType ?? '';
    cost.value = Number(row?.cost ?? 0);
    iconUrl.value = row?.iconUrl ?? '';
    permission.value = row?.permission ?? 'Everyone';
    pipelineId.value = row?.pipelineId ?? noPipeline;
    cooldown.value = Number(row?.cooldownSeconds ?? 0);
    cooldownPerUser.value = row?.cooldownPerUser ?? false;
    stockLimit.value = optionalNumber(row?.stockLimit);
    perStream.value = optionalNumber(row?.maxPerViewerPerStream);
    sortOrder.value = Number(row?.sortOrder ?? 0);
    error.value = undefined;
  },
  { immediate: true },
);

function problem(): string | undefined {
  if (name.value.trim() === '')
    return t('dashboard.economy.catalog.dialog.nameRequired');

  return props.item === null && sinkType.value.trim() === ''
    ? t('dashboard.economy.catalog.dialog.sinkRequired')
    : undefined;
}

async function send(): Promise<void> {
  const shared = {
    name: name.value.trim(),
    description: blankToNull(description.value),
    cost: cost.value,
    iconUrl: blankToNull(iconUrl.value),
    permission: permission.value,
    pipelineId: pipelineId.value === noPipeline
      ? null
      : pipelineId.value,
    cooldownSeconds: cooldown.value,
    cooldownPerUser: cooldownPerUser.value,
    stockLimit: numberOrNull(stockLimit.value),
    maxPerViewerPerStream: numberOrNull(perStream.value),
    sortOrder: sortOrder.value,
  };

  if (props.item !== null) {
    await update.mutateAsync({ itemId: props.item.id, change: shared });

    return;
  }

  await create.mutateAsync({
    ...shared,
    sinkType: sinkType.value.trim(),
    isEnabled: true,
  });
}

async function submit(): Promise<void> {
  error.value = problem();

  if (error.value !== undefined) return;

  try {
    await send();
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
      item
        ? $t('dashboard.economy.catalog.dialog.editTitle')
        : $t('dashboard.economy.catalog.dialog.newTitle')
    "
    :description="$t('dashboard.economy.catalog.dialog.description')"
    :action="$t('dashboard.economy.catalog.dialog.save')"
    :pending="pending"
    :error="error"
    @submit="submit"
  >
    <Field>
      <FieldLabel for="catalog-name">{{ $t('dashboard.economy.catalog.dialog.name') }}</FieldLabel>
      <Input
        id="catalog-name"
        v-model="name"
        autocomplete="off"
        :aria-label="$t('dashboard.economy.catalog.dialog.name')"
      />
    </Field>
    <Field>
      <FieldLabel for="catalog-about">{{
        $t('dashboard.economy.catalog.dialog.about')
      }}</FieldLabel>
      <Input
        id="catalog-about"
        v-model="description"
        autocomplete="off"
        :aria-label="$t('dashboard.economy.catalog.dialog.about')"
      />
    </Field>
    <div class="grid gap-4 sm:grid-cols-2">
      <Field>
        <div class="flex items-center gap-1.5">
          <FieldLabel for="catalog-sink">{{
            $t('dashboard.economy.catalog.dialog.sink')
          }}</FieldLabel>
          <FieldInfo>{{ $t('dashboard.economy.catalog.dialog.sinkHelp') }}</FieldInfo>
        </div>
        <Input
          id="catalog-sink"
          v-model="sinkType"
          autocomplete="off"
          :disabled="item !== null"
          :aria-label="$t('dashboard.economy.catalog.dialog.sink')"
        />
      </Field>
      <Field>
        <FieldLabel for="catalog-cost">{{ $t('dashboard.economy.catalog.dialog.cost') }}</FieldLabel>
        <Input
          id="catalog-cost"
          v-model.number="cost"
          type="number"
          min="0"
          step="1"
          :aria-label="$t('dashboard.economy.catalog.dialog.cost')"
        />
      </Field>
      <Field>
        <FieldLabel>{{ $t('dashboard.economy.catalog.dialog.permission') }}</FieldLabel>
        <Select v-model="permission">
          <SelectTrigger
            :aria-label="$t('dashboard.economy.catalog.dialog.permission')"
            class="w-full"
          >
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem v-for="level in permissionLevels" :key="level" :value="level">
              {{ level }}
            </SelectItem>
          </SelectContent>
        </Select>
      </Field>
      <Field>
        <FieldLabel>{{ $t('dashboard.economy.catalog.dialog.pipeline') }}</FieldLabel>
        <Select v-model="pipelineId">
          <SelectTrigger
            :aria-label="$t('dashboard.economy.catalog.dialog.pipeline')"
            class="w-full"
          >
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem :value="noPipeline">
              {{ $t('dashboard.economy.catalog.dialog.pipelineNone') }}
            </SelectItem>
            <SelectItem v-for="flow in pipelines.data.value ?? []" :key="flow.id" :value="flow.id">
              {{ flow.name }}
            </SelectItem>
          </SelectContent>
        </Select>
      </Field>
      <Field>
        <FieldLabel for="catalog-cooldown">{{
          $t('dashboard.economy.catalog.dialog.cooldown')
        }}</FieldLabel>
        <Input
          id="catalog-cooldown"
          v-model.number="cooldown"
          type="number"
          min="0"
          step="1"
          :aria-label="$t('dashboard.economy.catalog.dialog.cooldown')"
        />
      </Field>
      <Field>
        <div class="flex items-center gap-1.5">
          <FieldLabel for="catalog-stock">{{
            $t('dashboard.economy.catalog.dialog.stock')
          }}</FieldLabel>
          <FieldInfo>{{ $t('dashboard.economy.catalog.dialog.stockHelp') }}</FieldInfo>
        </div>
        <Input
          id="catalog-stock"
          v-model.number="stockLimit"
          type="number"
          min="0"
          step="1"
          :aria-label="$t('dashboard.economy.catalog.dialog.stock')"
        />
      </Field>
      <Field>
        <div class="flex items-center gap-1.5">
          <FieldLabel for="catalog-per-stream">{{
            $t('dashboard.economy.catalog.dialog.perStream')
          }}</FieldLabel>
          <FieldInfo>{{ $t('dashboard.economy.catalog.dialog.perStreamHelp') }}</FieldInfo>
        </div>
        <Input
          id="catalog-per-stream"
          v-model.number="perStream"
          type="number"
          min="0"
          step="1"
          :aria-label="$t('dashboard.economy.catalog.dialog.perStream')"
        />
      </Field>
      <Field>
        <FieldLabel for="catalog-order">{{
          $t('dashboard.economy.catalog.dialog.order')
        }}</FieldLabel>
        <Input
          id="catalog-order"
          v-model.number="sortOrder"
          type="number"
          step="1"
          :aria-label="$t('dashboard.economy.catalog.dialog.order')"
        />
      </Field>
      <Field>
        <FieldLabel for="catalog-icon">{{ $t('dashboard.economy.catalog.dialog.icon') }}</FieldLabel>
        <Input
          id="catalog-icon"
          v-model="iconUrl"
          type="url"
          autocomplete="off"
          :aria-label="$t('dashboard.economy.catalog.dialog.icon')"
        />
      </Field>
    </div>
    <SettingRow
      v-model="cooldownPerUser"
      :title="$t('dashboard.economy.catalog.dialog.perUser')"
      :description="$t('dashboard.economy.catalog.dialog.perUserHelp')"
    />
  </FormDialog>
</template>

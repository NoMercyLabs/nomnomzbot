<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The audit search filters: permission, outcome, who, which tenant and a day range. All optional.
import type { AcceptableValue } from 'reka-ui';
import { computed, ref } from 'vue';

import { useAdminTenants } from '@/api/admin';
import { useAdminUsers } from '@/api/adminUsers';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible';
import { Field, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import ChevronDown from '~icons/nnz/chevron-down';

import AuditFilterPicker, { type AuditPickerOption } from './AuditFilterPicker.vue';

export type AuditFilterValues = {
  permission: string;
  outcome: string;
  principalId: string;
  targetBroadcasterId: string;
  from: string;
  to: string;
};

// The select cannot hold an empty value, so "any outcome" travels as this word and is stored as empty.
const ANY: string = 'any';

const filters = defineModel<AuditFilterValues>({ required: true });

// Below md the six fields fold behind one toggle; the badge says how many are set.
const setCount = computed<number>(
  () => Object.values(filters.value).filter((value) => value !== '').length,
);

const PICK_SIZE: number = 8;

// The operator and the channel are found by name; the filter keeps the id of the pick, the field shows the name.
const operatorSearch = ref('');
const channelSearch = ref('');
const operatorName = ref('');
const channelName = ref('');

const operatorFound = useAdminUsers(() => ({
  search: operatorSearch.value.trim(),
  page: 1,
  take: PICK_SIZE,
}));
const channelFound = useAdminTenants(() => ({
  search: channelSearch.value.trim(),
  page: 1,
  take: PICK_SIZE,
}));

const operatorOptions = computed<AuditPickerOption[]>(() =>
  (operatorFound.data.value?.data ?? []).map((user) => ({
    id: user.id,
    name: user.displayName,
    hint: user.login,
  }) as const satisfies AuditPickerOption),
);
const channelOptions = computed<AuditPickerOption[]>(() =>
  (channelFound.data.value?.data ?? []).map((tenant) => ({
    id: tenant.id,
    name: tenant.name,
    hint: tenant.status,
  }) as const satisfies AuditPickerOption),
);

function pickOperator(option: AuditPickerOption | null): void {
  operatorName.value = option?.name ?? '';
  change('principalId', option?.id ?? '');
}

function pickChannel(option: AuditPickerOption | null): void {
  channelName.value = option?.name ?? '';
  change('targetBroadcasterId', option?.id ?? '');
}

function change(key: keyof AuditFilterValues, value: string | number): void {
  filters.value = { ...filters.value, [key]: String(value) };
}

function changeOutcome(value: AcceptableValue): void {
  change('outcome', value === ANY
    ? ''
    : String(value));
}
</script>

<template>
  <Collapsible class="flex flex-col gap-3">
    <CollapsibleTrigger as-child>
      <Button variant="outline" data-filters-toggle class="w-full justify-between md:hidden">
        <span class="flex items-center gap-2">
          {{ $t("dashboard.admin.audit.filters.toggle") }}
          <Badge v-if="setCount > 0" variant="secondary" data-filters-count>{{ setCount }}</Badge>
        </span>
        <ChevronDown class="size-5 transition-transform in-data-[state=open]:rotate-180" />
      </Button>
    </CollapsibleTrigger>
    <CollapsibleContent
      force-mount
      class="grid grid-cols-2 gap-3 max-md:data-[state=closed]:hidden xl:grid-cols-6"
      data-audit-filters
    >
      <Field>
        <FieldLabel>{{ $t("dashboard.admin.audit.filters.permission") }}</FieldLabel>
        <Input
          :model-value="filters.permission"
          :placeholder="$t('dashboard.admin.audit.filters.permissionHint')"
          @update:model-value="change('permission', $event)"
        />
      </Field>
      <Field>
        <FieldLabel>{{ $t("dashboard.admin.audit.filters.outcome") }}</FieldLabel>
        <Select :model-value="filters.outcome === '' ? ANY : filters.outcome" @update:model-value="changeOutcome">
          <SelectTrigger :aria-label="$t('dashboard.admin.audit.filters.outcome')" class="w-full">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem :value="ANY">{{ $t("dashboard.admin.audit.outcome.any") }}</SelectItem>
            <SelectItem value="Allowed">{{ $t("dashboard.admin.audit.outcome.Allowed") }}</SelectItem>
            <SelectItem value="Denied">{{ $t("dashboard.admin.audit.outcome.Denied") }}</SelectItem>
          </SelectContent>
        </Select>
      </Field>
      <Field>
        <FieldLabel>{{ $t("dashboard.admin.audit.filters.principal") }}</FieldLabel>
        <AuditFilterPicker
          v-model:search="operatorSearch"
          :picked="filters.principalId === '' ? '' : operatorName"
          :options="operatorOptions"
          :settled="operatorFound.isSuccess.value"
          :placeholder="$t('dashboard.admin.audit.filters.principalHint')"
          :clear-label="$t('dashboard.admin.person.clear')"
          :none-text="$t('dashboard.admin.person.none')"
          @pick="pickOperator"
        />
      </Field>
      <Field>
        <FieldLabel>{{ $t("dashboard.admin.audit.filters.tenant") }}</FieldLabel>
        <AuditFilterPicker
          v-model:search="channelSearch"
          :picked="filters.targetBroadcasterId === '' ? '' : channelName"
          :options="channelOptions"
          :settled="channelFound.isSuccess.value"
          :placeholder="$t('dashboard.admin.audit.filters.tenantHint')"
          :clear-label="$t('dashboard.admin.audit.filters.tenantClear')"
          :none-text="$t('dashboard.admin.audit.filters.tenantNone')"
          @pick="pickChannel"
        />
      </Field>
      <Field>
        <FieldLabel>{{ $t("dashboard.admin.audit.filters.from") }}</FieldLabel>
        <Input type="date" :model-value="filters.from" @update:model-value="change('from', $event)" />
      </Field>
      <Field>
        <FieldLabel>{{ $t("dashboard.admin.audit.filters.to") }}</FieldLabel>
        <Input type="date" :model-value="filters.to" @update:model-value="change('to', $event)" />
      </Field>
    </CollapsibleContent>
  </Collapsible>
</template>

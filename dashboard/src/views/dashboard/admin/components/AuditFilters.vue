<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The audit search filters: permission, outcome, who, which tenant and a day range. All optional.
import type { AcceptableValue } from 'reka-ui';
import { computed } from 'vue';

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
        <Input
          :model-value="filters.principalId"
          @update:model-value="change('principalId', $event)"
        />
      </Field>
      <Field>
        <FieldLabel>{{ $t("dashboard.admin.audit.filters.tenant") }}</FieldLabel>
        <Input
          :model-value="filters.targetBroadcasterId"
          @update:model-value="change('targetBroadcasterId', $event)"
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

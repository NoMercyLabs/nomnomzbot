<!--
  -----------------------------------------------------------------------------
   Copyright (c) NoMercy Labs.

   This file is part of NomNomzBot. You may use, change and share it for free,
   for any noncommercial purpose, under the PolyForm Noncommercial License
   1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
   See LICENSE for details, and TRADEMARKS.md for the name and logo.

   SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
  -----------------------------------------------------------------------------
-->
<script setup lang="ts">
// Platform access: who holds which operator role, the role catalog, and the way in to act as someone.
// An operator assigns or revokes a role, deactivates or reactivates a principal, or acts as a person of a
// channel through the one Act as dialog. Other operators' changes arrive over the admin hub.
import { useLocalStorage } from '@vueuse/core';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import ChevronDown from '~icons/nnz/chevron-down';
import Cross from '~icons/nnz/cross';

import {
  useAssignIamRole,
  useDeactivateIamPrincipal,
  useIamPrincipals,
  useIamRoles,
  useReactivateIamPrincipal,
  useRevokeIamAssignment,
  type IamAssignment,
  type IamPrincipal,
} from '@/api/adminAccess';
import { useCurrentUser } from '@/api/auth';
import DateTime from '@/components/layout/DateTime.vue';
import FormDialog from '@/components/layout/FormDialog.vue';
import LabeledSwitch from '@/components/layout/LabeledSwitch.vue';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import UserName from '@/components/layout/UserName.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { usePanelState } from '@/composables/usePanelState';
import { usePlatformCan } from '@/composables/usePlatformCan';

import AccessScope from './components/AccessScope.vue';
import ActAsDialog from './components/ActAsDialog.vue';

type Step = 'assign' | 'revoke' | 'deactivate';

const { t } = useI18n();
const { can } = usePlatformCan();
const me = useCurrentUser();

// The same role held twice with the same scope and end date reads as one chip with a count.
type HeldGroup = { held: IamAssignment; count: number };

function grouped(held: IamAssignment[]): HeldGroup[] {
  const groups = new Map<string, HeldGroup>();

  for (const item of held) {
    const key = `${item.roleId}|${item.scopeChannelId ?? ''}|${item.expiresAt ?? ''}`;
    const seen = groups.get(key);

    if (seen) {
      seen.count += 1;
    } else {
      groups.set(key, { held: item, count: 1 });
    }
  }

  return [...groups.values()];
}

const hideInactive = useLocalStorage<boolean>('nnz.adminAccess.hideInactive', false);
const step = ref<Step | null>(null);
const target = ref<IamPrincipal | null>(null);
const assignment = ref<IamAssignment | null>(null);
const roleId = ref('');
const expires = ref('');
const reason = ref('');
const failure = ref<string | null>(null);
const choosingActAs = ref(false);

const principals = useIamPrincipals();
const roles = useIamRoles();
const assign = useAssignIamRole();
const revoke = useRevokeIamAssignment();
const deactivate = useDeactivateIamPrincipal();
const reactivate = useReactivateIamPrincipal();

// Principals that share a name get their place among them, so the rows can be told apart.
const ordinals = computed<Map<string, { position: number; total: number }>>(() => {
  const totals = new Map<string, number>();
  const places = new Map<string, { position: number; total: number }>();

  for (const row of visible.value) {
    totals.set(row.name, (totals.get(row.name) ?? 0) + 1);
  }

  const seen = new Map<string, number>();

  for (const row of visible.value) {
    const total = totals.get(row.name) ?? 1;
    const position = (seen.get(row.name) ?? 0) + 1;

    seen.set(row.name, position);

    if (total > 1) {
      places.set(row.id, { position, total });
    }
  }

  return places;
});

const principalState = usePanelState(principals, (rows) =>
  rows.length === 0);
const roleState = usePanelState(roles, (rows) =>
  rows.length === 0);

const visible = computed<IamPrincipal[]>(() => {
  const rows = principals.data.value ?? [];

  return hideInactive.value
    ? rows.filter((row) => row.isActive)
    : rows;
});

const pending = computed(
  () => assign.isPending.value || revoke.isPending.value || deactivate.isPending.value,
);

// Acting as someone opens a support session first, so it takes both keys.
const canActAs = computed<boolean>(() => can('tenant:access') && can('user:impersonate'));

const name = computed<string>(() => target.value?.name ?? '');
const roleName = computed<string>(() => assignment.value?.roleName ?? '');

const dialogTitle = computed<string>(() => {
  if (step.value === null)
    return '';

  return t(`dashboard.admin.access.${step.value}.title`, {
    name: name.value,
    role: roleName.value,
  });
});

const dialogAction = computed<string>(() =>
  step.value
    ? t(`dashboard.admin.access.${step.value}.action`)
    : '',
);

// The consequence is shown before the operator confirms, counted from the real active roles.
const consequence = computed<string | undefined>(() => {
  if (step.value === 'revoke')
    return t('dashboard.admin.access.revoke.consequence', {
      name: name.value,
      role: roleName.value,
    });

  if (step.value !== 'deactivate')
    return undefined;

  const count = target.value?.activeAssignments.length ?? 0;

  return t('dashboard.admin.access.deactivate.consequence', { name: name.value, count }, count);
});

const ownAccount = computed<boolean>(
  () =>
    (step.value === 'revoke' || step.value === 'deactivate') &&
    !!target.value?.userId &&
    target.value.userId === me.data.value?.id,
);

const error = computed<string | undefined>(() => failure.value ?? undefined);

function open(next: Step, principal: IamPrincipal, held: IamAssignment | null = null): void {
  target.value = principal;
  assignment.value = held;
  roleId.value = '';
  expires.value = '';
  reason.value = '';
  failure.value = null;
  step.value = next;
}

function close(): void {
  step.value = null;
}

function reasonOrNull(): string | null {
  const trimmed = reason.value.trim();

  return trimmed === ''
    ? null
    : trimmed;
}

// The datetime-local field holds the operator's local time; the API takes an instant.
function expiresOrNull(): string | null {
  return expires.value === ''
    ? null
    : new Date(expires.value).toISOString();
}

function run(current: Step): Promise<void> {
  const note = reasonOrNull();

  if (current === 'assign')
    return assign.mutateAsync({
      principalId: target.value!.id,
      roleId: roleId.value,
      expiresAt: expiresOrNull(),
      reason: note,
    });

  if (current === 'revoke')
    return revoke.mutateAsync({ id: assignment.value!.id, reason: note });

  return deactivate.mutateAsync({ id: target.value!.id, reason: note });
}

async function submit(): Promise<void> {
  const current = step.value;

  if (current === null) return;

  if (current === 'assign' && roleId.value === '') return;

  try {
    await run(current);
    step.value = null;
  } catch {
    failure.value = t('dashboard.admin.access.failed');
  }
}

</script>

<template>
  <PageHeader :title="$t('dashboard.admin.access.title')" :crumb="$t('dashboard.admin.crumb')" />
  <PageBody data-page-body>
    <LabeledSwitch
      v-model="hideInactive"
      data-hide-inactive
      data-page-inset
      :label="$t('dashboard.admin.access.hideInactive')"
      :help="$t('dashboard.admin.access.hideInactiveHelp')"
    />

    <PanelCard
      class="shrink-0"
      pad
      tier="lead"
      :title="$t('dashboard.admin.access.principals.title')"
      :description="$t('dashboard.admin.access.principals.description')"
      :state="principalState"
      :empty-text="$t('dashboard.admin.access.principals.empty')"
      :error-text="$t('dashboard.admin.access.principals.error')"
      @retry="principals.refetch()"
    >
      <ul class="divide-y divide-border">
        <li
          v-for="row in visible"
          :key="row.id"
          data-principal-row
          class="flex min-h-16 flex-wrap items-center gap-3 py-3"
        >
          <div class="min-w-40 flex-1 basis-48 max-md:basis-full">
            <UserName
              :user-id="row.userId"
              :name="row.name"
              class="block truncate text-sm font-medium"
            />
            <span
              v-if="ordinals.get(row.id)"
              data-principal-ordinal
              class="block text-xs text-muted-foreground"
            >{{ $t("dashboard.admin.access.principals.ordinal", ordinals.get(row.id)!) }}</span>
            <span class="block text-xs text-muted-foreground">
              {{
                row.principalType === 0
                  ? $t("dashboard.admin.access.principals.employee")
                  : $t("dashboard.admin.access.principals.serviceAccount")
              }}
            </span>
            <span
              v-if="row.expiresAt"
              data-principal-expires
              class="block text-xs text-muted-foreground"
            >
              {{ $t("dashboard.admin.access.principals.expires") }}
              <DateTime :value="row.expiresAt" />
            </span>
          </div>
          <div
            class="flex min-w-0 flex-[3] basis-96 flex-wrap items-center gap-2 max-md:basis-full"
          >
            <Badge v-if="!row.isActive" variant="outline">{{
              $t("dashboard.admin.access.principals.inactive")
            }}</Badge>
            <span
              v-else-if="row.activeAssignments.length === 0"
              class="text-xs text-muted-foreground"
            >{{ $t("dashboard.admin.access.principals.noRoles") }}</span>
            <span
              v-for="{ held, count } in grouped(row.activeAssignments)"
              :key="held.id"
              data-assignment
              class="inline-flex min-h-9 flex-wrap items-center gap-x-1 rounded-md border border-border pr-0.5 pl-2 text-xs"
            >
              <span class="whitespace-nowrap">{{ held.roleName }}</span>
              <span v-if="count > 1" data-assignment-count class="text-muted-foreground">{{
                $t("dashboard.admin.access.principals.times", { count })
              }}</span>
              <span v-if="held.scopeChannelId" data-assignment-scope class="text-muted-foreground">
                <AccessScope :channel-id="held.scopeChannelId" />
              </span>
              <span v-if="held.expiresAt" data-assignment-expires class="text-muted-foreground">
                {{ $t("dashboard.admin.access.principals.expires") }}
                <DateTime :value="held.expiresAt" />
              </span>
              <Button
                v-if="can('iam:manage')"
                variant="ghost"
                size="icon"
                data-revoke
                :aria-label="$t('dashboard.admin.access.actions.revoke', { role: held.roleName })"
                @click="open('revoke', row, held)"
              >
                <Cross class="size-5" aria-hidden="true" />
              </Button>
            </span>
          </div>
          <div v-if="can('iam:manage')" class="flex shrink-0 items-center gap-2 max-md:basis-full">
            <template v-if="row.isActive">
              <Button variant="outline" data-assign @click="open('assign', row)">{{
                $t("dashboard.admin.access.actions.assign")
              }}</Button>
              <Button
                variant="outline"
                class="text-(--kit-danger)"
                data-deactivate
                @click="open('deactivate', row)"
              >{{ $t("dashboard.admin.access.actions.deactivate") }}</Button>
            </template>
            <Button
              v-else
              variant="outline"
              data-reactivate
              :disabled="reactivate.isPending.value"
              @click="reactivate.mutate(row.id)"
            >{{ $t("dashboard.admin.access.actions.reactivate") }}</Button>
          </div>
        </li>
      </ul>
      <p
        v-if="reactivate.isError.value"
        data-reactivate-failure
        class="mt-3 text-sm text-destructive"
        role="alert"
      >
        {{ $t("dashboard.admin.access.failed") }}
      </p>
    </PanelCard>

    <PanelCard
      class="shrink-0"
      pad
      :title="$t('dashboard.admin.access.roles.title')"
      :description="$t('dashboard.admin.access.roles.description')"
      :state="roleState"
      :empty-text="$t('dashboard.admin.access.roles.empty')"
      :error-text="$t('dashboard.admin.access.roles.error')"
      @retry="roles.refetch()"
    >
      <ul class="divide-y divide-border">
        <Collapsible
          v-for="role in roles.data.value ?? []"
          :key="role.id"
          as="li"
          data-role-row
          class="py-2"
        >
          <div class="flex items-center gap-3">
            <CollapsibleTrigger
              data-role-toggle
              class="group touch-hit flex min-w-0 flex-1 items-center gap-2 rounded-md text-left outline-none focus-visible:ring-3 focus-visible:ring-ring/50"
            >
              <ChevronDown
                class="size-4 shrink-0 text-muted-foreground transition-transform group-data-[state=open]:rotate-180 motion-reduce:transition-none"
                aria-hidden="true"
              />
              <span class="min-w-0 flex-1">
                <span class="block truncate text-sm font-medium">{{ role.name }}</span>
                <span class="block truncate text-xs text-muted-foreground">{{
                  role.description
                }}</span>
              </span>
            </CollapsibleTrigger>
            <span data-role-badge class="flex w-24 shrink-0 justify-end">
              <Badge v-if="role.isSystem" variant="outline">{{
                $t("dashboard.admin.access.roles.system")
              }}</Badge>
            </span>
            <span data-role-count class="w-28 shrink-0 text-right text-xs text-muted-foreground">
              {{
                $t(
                  "dashboard.admin.access.roles.permissions",
                  { count: role.permissionKeys.length },
                  role.permissionKeys.length,
                )
              }}
            </span>
          </div>
          <CollapsibleContent>
            <ul class="flex flex-wrap gap-1.5 pt-2 pl-6">
              <li
                v-for="key in role.permissionKeys"
                :key="key"
                class="rounded-md border border-border px-2 py-0.5 font-mono text-xs"
              >
                {{ key }}
              </li>
            </ul>
          </CollapsibleContent>
        </Collapsible>
      </ul>
    </PanelCard>

    <PanelCard
      v-if="canActAs"
      class="shrink-0"
      pad
      tier="quiet"
      :title="$t('dashboard.admin.actAs.panelTitle')"
      :description="$t('dashboard.admin.actAs.panelDescription')"
    >
      <Button v-if="canActAs" variant="outline" data-act-as @click="choosingActAs = true">
        {{ $t("dashboard.admin.actAs.open") }}
      </Button>
    </PanelCard>
    <ActAsDialog v-if="canActAs && choosingActAs" :channel="null" @close="choosingActAs = false" />

    <FormDialog
      :open="step !== null"
      :title="dialogTitle"
      :description="consequence"
      :action="dialogAction"
      :destructive="step === 'deactivate' || step === 'revoke'"
      :pending="pending"
      :error="error"
      @update:open="close"
      @submit="submit"
    >
      <div class="grid gap-3">
        <p
          v-if="ownAccount"
          data-self-warning
          class="text-sm font-medium text-(--kit-danger)"
          role="alert"
        >
          {{ $t("dashboard.admin.access.selfWarning") }}
        </p>
        <div v-if="step === 'assign'" class="grid gap-1.5">
          <Label for="access-role">{{ $t("dashboard.admin.access.assign.role") }}</Label>
          <Select v-model="roleId">
            <SelectTrigger id="access-role" data-field-role class="w-full">
              <SelectValue :placeholder="$t('dashboard.admin.access.assign.pickRole')" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem v-for="role in roles.data.value ?? []" :key="role.id" :value="role.id">
                {{ role.name }}
              </SelectItem>
            </SelectContent>
          </Select>
        </div>
        <div v-if="step === 'assign'" class="grid gap-1.5">
          <Label for="access-expires">{{ $t("dashboard.admin.access.assign.expires") }}</Label>
          <Input id="access-expires" v-model="expires" type="datetime-local" data-field-expires />
          <p class="text-xs text-muted-foreground">
            {{ $t("dashboard.admin.access.assign.expiresHelp") }}
          </p>
        </div>
        <div class="grid gap-1.5">
          <Label for="access-reason">{{ $t("dashboard.admin.access.reason") }}</Label>
          <Input id="access-reason" v-model="reason" data-field-reason />
        </div>
      </div>
    </FormDialog>
  </PageBody>
</template>

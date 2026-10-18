<!--
  -----------------------------------------------------------------------------
   Copyright (c) NoMercy Labs.

   This file is part of NomNomzBot, free software licensed under the GNU Affero
   General Public License v3.0 or later. You may redistribute and/or modify it
   under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

   SPDX-License-Identifier: AGPL-3.0-or-later
  -----------------------------------------------------------------------------
-->
<script setup lang="ts">
// Platform access: who holds which operator role, the role catalog, and the support session tools.
// An operator assigns or revokes a role, deactivates or reactivates a principal, ends a tenant access
// grant, or acts as a user through an open grant. Other operators' changes arrive over the admin hub.
import { useQueryClient } from '@tanstack/vue-query';
import { useLocalStorage } from '@vueuse/core';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRouter } from 'vue-router';

import Cross from '~icons/nnz/cross';

import { useAdminImpersonate } from '@/api/admin';
import {
  useAssignIamRole,
  useDeactivateIamPrincipal,
  useEndTenantAccess,
  useIamPrincipals,
  useIamRoles,
  useReactivateIamPrincipal,
  useRevokeIamAssignment,
  type IamAssignment,
  type IamPrincipal,
} from '@/api/adminAccess';
import { acceptAccessToken } from '@/api/auth';
import FormDialog from '@/components/layout/FormDialog.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { usePanelState } from '@/composables/usePanelState';

type Step = 'assign' | 'revoke' | 'deactivate';

const { t } = useI18n();
const router = useRouter();
const queries = useQueryClient();

const hideInactive = useLocalStorage<boolean>('nnz.adminAccess.hideInactive', false);
const step = ref<Step | null>(null);
const target = ref<IamPrincipal | null>(null);
const assignment = ref<IamAssignment | null>(null);
const roleId = ref('');
const reason = ref('');
const failure = ref<string | null>(null);
const grantId = ref('');
const userId = ref('');
const justification = ref('');
const sessionFailure = ref<string | null>(null);

const principals = useIamPrincipals();
const roles = useIamRoles();
const assign = useAssignIamRole();
const revoke = useRevokeIamAssignment();
const deactivate = useDeactivateIamPrincipal();
const reactivate = useReactivateIamPrincipal();
const endAccess = useEndTenantAccess();
const impersonate = useAdminImpersonate();

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

const sessionPending = computed(
  () => endAccess.isPending.value || impersonate.isPending.value,
);

const name = computed<string>(() => target.value?.name ?? '');
const roleName = computed<string>(() => assignment.value?.roleName ?? '');

const dialogTitle = computed<string>(() => {
  if (step.value === null)
    return '';

  return t(`dashboard.admin.access.${step.value}.title`, { name: name.value, role: roleName.value });
});

const dialogAction = computed<string>(() =>
  step.value
    ? t(`dashboard.admin.access.${step.value}.action`)
    : '',
);

// The consequence is shown before the operator confirms, counted from the real active roles.
const consequence = computed<string | undefined>(() => {
  if (step.value === 'revoke')
    return t('dashboard.admin.access.revoke.consequence', { name: name.value, role: roleName.value });

  if (step.value !== 'deactivate')
    return undefined;

  const count = target.value?.activeAssignments.length ?? 0;

  return t('dashboard.admin.access.deactivate.consequence', { name: name.value, count }, count);
});

const error = computed<string | undefined>(() => failure.value ?? undefined);

function open(next: Step, principal: IamPrincipal, held: IamAssignment | null = null): void {
  target.value = principal;
  assignment.value = held;
  roleId.value = '';
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

function run(current: Step): Promise<void> {
  const note = reasonOrNull();

  if (current === 'assign')
    return assign.mutateAsync({
      principalId: target.value!.id,
      roleId: roleId.value,
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

async function finishEnd(): Promise<void> {
  sessionFailure.value = null;

  try {
    await endAccess.mutateAsync(grantId.value.trim());
    grantId.value = '';
  } catch {
    sessionFailure.value = t('dashboard.admin.access.failed');
  }
}

async function startImpersonation(): Promise<void> {
  sessionFailure.value = null;

  try {
    const minted = await impersonate.mutateAsync({
      userId: userId.value.trim(),
      accessGrantId: grantId.value.trim(),
      justification: justification.value.trim(),
    });

    acceptAccessToken(minted.accessToken);
    queries.clear();
    await router.push({ name: 'dashboard-home' });
  } catch {
    sessionFailure.value = t('dashboard.admin.access.failed');
  }
}
</script>

<template>
  <PageHeader :title="$t('dashboard.admin.access.title')" :crumb="$t('dashboard.admin.crumb')" />
  <div data-page-body class="flex min-h-0 min-w-0 flex-1 flex-col gap-3 p-6 max-md:p-4">
    <label class="flex items-center gap-2 text-sm">
      <input v-model="hideInactive" type="checkbox" data-hide-inactive class="size-4" />
      {{ $t("dashboard.admin.access.hideInactive") }}
    </label>

    <PanelCard
      tier="lead"
      :title="$t('dashboard.admin.access.principals.title')"
      :description="$t('dashboard.admin.access.principals.description')"
      :state="principalState"
      :empty-text="$t('dashboard.admin.access.principals.empty')"
      :error-text="$t('dashboard.admin.access.principals.error')"
      @retry="principals.refetch()"
    >
      <ul class="divide-y divide-border">
        <li v-for="row in visible" :key="row.id" data-principal-row class="flex flex-wrap items-center gap-3 py-3">
          <div class="min-w-0 flex-1">
            <span class="block truncate text-sm font-medium">{{ row.name }}</span>
            <span class="block text-xs text-muted-foreground">
              {{
                row.principalType === 0
                  ? $t("dashboard.admin.access.principals.employee")
                  : $t("dashboard.admin.access.principals.serviceAccount")
              }}
            </span>
          </div>
          <div class="flex min-w-0 flex-wrap items-center gap-2">
            <Badge v-if="!row.isActive" variant="outline">{{
              $t("dashboard.admin.access.principals.inactive")
            }}</Badge>
            <span
              v-else-if="row.activeAssignments.length === 0"
              class="text-xs text-muted-foreground"
            >{{ $t("dashboard.admin.access.principals.noRoles") }}</span>
            <span
              v-for="held in row.activeAssignments"
              :key="held.id"
              data-assignment
              class="inline-flex items-center gap-1 rounded-md border border-border py-0.5 pr-0.5 pl-2 text-xs"
            >
              {{ held.roleName }}
              <Button
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
          <div class="flex items-center gap-2">
            <template v-if="row.isActive">
              <Button variant="outline" data-assign @click="open('assign', row)">{{
                $t("dashboard.admin.access.actions.assign")
              }}</Button>
              <Button variant="outline" data-deactivate @click="open('deactivate', row)">{{
                $t("dashboard.admin.access.actions.deactivate")
              }}</Button>
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
    </PanelCard>

    <PanelCard
      :title="$t('dashboard.admin.access.roles.title')"
      :description="$t('dashboard.admin.access.roles.description')"
      :state="roleState"
      :empty-text="$t('dashboard.admin.access.roles.empty')"
      :error-text="$t('dashboard.admin.access.roles.error')"
      @retry="roles.refetch()"
    >
      <ul class="divide-y divide-border">
        <li
          v-for="role in roles.data.value ?? []"
          :key="role.id"
          data-role-row
          class="flex items-center gap-3 py-2"
        >
          <div class="min-w-0 flex-1">
            <span class="block truncate text-sm font-medium">{{ role.name }}</span>
            <span class="block truncate text-xs text-muted-foreground">{{ role.description }}</span>
          </div>
          <Badge v-if="role.isSystem" variant="outline">{{
            $t("dashboard.admin.access.roles.system")
          }}</Badge>
          <span class="text-xs text-muted-foreground">
            {{ $t("dashboard.admin.access.roles.permissions", { count: role.permissionKeys.length }, role.permissionKeys.length) }}
          </span>
        </li>
      </ul>
    </PanelCard>

    <PanelCard
      tier="quiet"
      :title="$t('dashboard.admin.access.session.title')"
      :description="$t('dashboard.admin.access.session.description')"
    >
      <div class="grid gap-3 sm:grid-cols-3">
        <div class="grid gap-1.5">
          <Label for="access-grant">{{ $t("dashboard.admin.access.session.grant") }}</Label>
          <Input id="access-grant" v-model="grantId" data-field-grant />
        </div>
        <div class="grid gap-1.5">
          <Label for="access-user">{{ $t("dashboard.admin.access.session.user") }}</Label>
          <Input id="access-user" v-model="userId" data-field-user />
        </div>
        <div class="grid gap-1.5">
          <Label for="access-justification">{{
            $t("dashboard.admin.access.session.justification")
          }}</Label>
          <Input id="access-justification" v-model="justification" data-field-justification />
        </div>
      </div>
      <p v-if="sessionFailure" class="mt-3 text-sm text-destructive" role="alert">
        {{ sessionFailure }}
      </p>
      <template #footer>
        <div class="flex w-full justify-end gap-2">
          <Button
            variant="outline"
            :disabled="sessionPending || grantId.trim() === ''"
            @click="finishEnd"
          >{{ $t("dashboard.admin.access.session.end") }}</Button>
          <Button
            variant="outline"
            :disabled="sessionPending || grantId.trim() === '' || userId.trim() === ''"
            @click="startImpersonation"
          >{{ $t("dashboard.admin.access.session.impersonate") }}</Button>
        </div>
      </template>
    </PanelCard>

    <FormDialog
      :open="step !== null"
      :title="dialogTitle"
      :description="consequence"
      :action="dialogAction"
      :pending="pending"
      :error="error"
      @update:open="close"
      @submit="submit"
    >
      <div class="grid gap-3">
        <div v-if="step === 'assign'" class="grid gap-1.5">
          <Label for="access-role">{{ $t("dashboard.admin.access.assign.role") }}</Label>
          <select
            id="access-role"
            v-model="roleId"
            data-field-role
            class="h-9 rounded-md border border-input bg-transparent px-3 text-sm"
          >
            <option value="">{{ $t("dashboard.admin.access.assign.pickRole") }}</option>
            <option v-for="role in roles.data.value ?? []" :key="role.id" :value="role.id">
              {{ role.name }}
            </option>
          </select>
        </div>
        <div class="grid gap-1.5">
          <Label for="access-reason">{{ $t("dashboard.admin.access.reason") }}</Label>
          <Input id="access-reason" v-model="reason" data-field-reason />
        </div>
      </div>
    </FormDialog>
  </div>
</template>

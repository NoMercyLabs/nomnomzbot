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

import ChevronDown from '~icons/nnz/chevron-down';
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
import type { SupportPerson } from '@/api/adminSupport';
import { acceptAccessToken, useCurrentUser } from '@/api/auth';
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
import SupportPersonPicker from './components/SupportPersonPicker.vue';

type Step = 'assign' | 'revoke' | 'deactivate';

const { t } = useI18n();
const { can } = usePlatformCan();
const router = useRouter();
const queries = useQueryClient();
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
const grantId = ref('');
const person = ref<SupportPerson | null>(null);
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

const sessionPending = computed(() => endAccess.isPending.value || impersonate.isPending.value);

const canEnd = computed<boolean>(() => can('tenant:access'));
const canImpersonate = computed<boolean>(() => can('user:impersonate'));
const showSession = computed<boolean>(() => canEnd.value || canImpersonate.value);

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
  if (justification.value.trim() === '' || person.value === null) return;

  sessionFailure.value = null;

  try {
    const minted = await impersonate.mutateAsync({
      userId: person.value.userId,
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
              class="group flex min-w-0 flex-1 items-center gap-2 rounded-md text-left outline-none focus-visible:ring-3 focus-visible:ring-ring/50"
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
      v-if="showSession"
      class="shrink-0"
      tier="quiet"
      :title="$t('dashboard.admin.access.session.title')"
      :description="$t('dashboard.admin.access.session.description')"
    >
      <div class="grid gap-3 sm:grid-cols-3">
        <div class="grid gap-1.5">
          <Label for="access-grant">{{ $t("dashboard.admin.access.session.grant") }}</Label>
          <Input id="access-grant" v-model="grantId" data-field-grant />
        </div>
        <template v-if="canImpersonate">
          <div class="grid gap-1.5">
            <Label for="access-justification">{{
              $t("dashboard.admin.access.session.justification")
            }}</Label>
            <Input
              id="access-justification"
              v-model="justification"
              data-field-justification
              required
            />
          </div>
          <div class="grid gap-1.5">
            <Label for="access-user">{{ $t("dashboard.admin.access.session.user") }}</Label>
            <SupportPersonPicker
              data-field-user
              input-id="access-user"
              :person="person"
              :reason="justification"
              :wait-hint="$t('dashboard.admin.access.session.userWait')"
              @pick="person = $event"
            />
          </div>
        </template>
      </div>
      <p v-if="sessionFailure" class="mt-3 text-sm text-destructive" role="alert">
        {{ sessionFailure }}
      </p>
      <template #footer>
        <div class="flex w-full justify-end gap-2">
          <Button
            v-if="canEnd"
            variant="outline"
            :disabled="sessionPending || grantId.trim() === ''"
            @click="finishEnd"
          >{{ $t("dashboard.admin.access.session.end") }}</Button>
          <Button
            v-if="canImpersonate"
            variant="outline"
            :disabled="
              sessionPending ||
                grantId.trim() === '' ||
                person === null ||
                justification.trim() === ''
            "
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

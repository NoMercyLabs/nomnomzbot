// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

import { computed, type ComputedRef, type WritableComputedRef } from 'vue';

import type { PageTab } from '@/components/layout/pageTab';
import { useCan } from '@/composables/useCan';
import { usePageView } from '@/composables/usePageView';

// The primary action of one tab (its New button): shown only to a person who holds the write key.
export type TabActionSpec = {
  label: string;
  write: string;
  run: () => void;
};

// One tab of a permission-filtered page. A tab with no `read` key is open to everyone.
export type TabSpec<View extends string> = {
  value: View;
  label: string;
  read?: string;
  count?: number;
  // A locked tab: it shows a lock and cannot open, and this is its tooltip.
  disabledReason?: string;
  action?: TabActionSpec;
};

export type TabAction = Pick<TabActionSpec, 'label' | 'run'>;

export type TabbedPage<View extends string> = {
  tabs: ComputedRef<PageTab<View>[]>;
  view: WritableComputedRef<View>;
  // The open tab's spec, so a page can still draw a disabled button for a tab whose action is hidden.
  current: ComputedRef<TabSpec<View> | undefined>;
  action: ComputedRef<TabAction | null>;
  // The person may run the open tab's action; pages gate their New button on it.
  canAct: ComputedRef<boolean>;
};

function toTab<View extends string>(spec: TabSpec<View>) {
  return {
    value: spec.value,
    label: spec.label,
    count: spec.count,
    disabled: spec.disabledReason !== undefined,
    reason: spec.disabledReason,
  } as const satisfies PageTab<View>;
}

// The tabs a person may open, the open one (kept in ?view= by usePageView, so a link opens a tab and a missing or
// filtered-out one falls back to the first allowed tab) and the open tab's primary action.
export function useTabbedPage<const View extends string>(
  views: readonly [View, ...View[]],
  specs: () => readonly TabSpec<View>[],
) {
  const { can } = useCan();

  const allowed = computed<TabSpec<View>[]>(() =>
    specs().filter((spec) => spec.read === undefined || can(spec.read)));
  const tabs = computed<PageTab<View>[]>(() => allowed.value.map(toTab));
  const shown = computed<View[]>(() => allowed.value.map((spec) => spec.value));
  const view = usePageView(views, 'view', shown);
  const current = computed<TabSpec<View> | undefined>(() =>
    allowed.value.find((spec) => spec.value === view.value));

  const action = computed<TabAction | null>(() => {
    const spec = current.value?.action;

    if (spec === undefined || !can(spec.write))
      return null;

    return {
      label: spec.label,
      run: spec.run,
    };
  });

  const canAct = computed<boolean>(() => action.value !== null);

  return {
    tabs,
    view,
    current,
    action,
    canAct,
  } as const satisfies TabbedPage<View>;
}

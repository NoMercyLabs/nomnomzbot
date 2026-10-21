<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { cn } from '@/lib/utils'
import { useTableStacked } from './stacked'

// In a stacked table below md a cell is a "label: value" line, the card title, or the row's button group.
const props = withDefaults(defineProps<{
  class?: HTMLAttributes['class']
  label?: string
  stack?: 'field' | 'title' | 'actions'
}>(), {
  label: undefined,
  stack: 'field',
})

const stacked = useTableStacked()

const stackClasses = {
  field: 'max-md:flex max-md:items-center max-md:justify-between max-md:gap-3 max-md:p-0 max-md:text-right max-md:whitespace-normal',
  title: 'max-md:block max-md:p-0 max-md:whitespace-normal max-md:text-left',
  actions: 'max-md:flex max-md:justify-end max-md:border-t max-md:p-0 max-md:pt-2 max-md:empty:hidden',
} as const satisfies Record<typeof props.stack, string>

const classes = computed(() => cn(
  'p-2 align-middle whitespace-nowrap [&:has([role=checkbox])]:pr-0',
  stacked.value && stackClasses[props.stack],
  props.class,
))
</script>

<template>
  <td
    data-slot="table-cell"
    :data-stack="stacked ? stack : undefined"
    :class="classes"
  >
    <span v-if="stacked && label" data-cell-label class="shrink-0 text-left text-muted-foreground md:hidden">{{ label }}</span>
    <slot />
  </td>
</template>

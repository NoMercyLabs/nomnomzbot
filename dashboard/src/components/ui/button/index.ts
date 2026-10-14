import type { VariantProps } from 'class-variance-authority'
import { cva } from 'class-variance-authority'

export { default as Button } from './Button.vue'

export const buttonVariants = cva(
  'focus-visible:border-ring focus-visible:ring-ring/50 aria-invalid:ring-destructive/20 dark:aria-invalid:ring-destructive/40 aria-invalid:border-destructive dark:aria-invalid:border-destructive/50 rounded-md border border-transparent bg-clip-padding font-(family-name:--kit-font-button) text-sm font-medium focus-visible:ring-3 aria-invalid:ring-3 active:not-aria-[haspopup]:translate-y-px [&_svg:not([class*=size-])]:size-4 group/button inline-flex shrink-0 items-center justify-center whitespace-nowrap transition-all outline-none select-none disabled:pointer-events-none disabled:opacity-50 [&_svg]:pointer-events-none [&_svg]:shrink-0',
  {
    variants: {
      variant: {
        default: 'rounded-full border-black/20 bg-linear-to-b from-(--kit-pill-from) to-(--kit-pill-to) font-extrabold text-(--kit-ink) hover:opacity-90 active:from-(--kit-pill-pressed) active:to-(--kit-pill-from) focus-visible:border-(--kit-focus) focus-visible:ring-2 focus-visible:ring-(--kit-focus)',
        outline: 'rounded-[12px] bg-(--kit-surface) font-semibold text-(--kit-action-text) hover:bg-(--kit-surface-hover) hover:text-white active:bg-(--kit-surface-pressed) aria-expanded:bg-(--kit-surface-hover) aria-expanded:text-white focus-visible:border-(--kit-focus-soft) focus-visible:ring-2 focus-visible:ring-(--kit-focus-soft)',
        secondary: 'rounded-full border-black/20 bg-(--kit-surface) font-semibold text-(--kit-lilac) hover:bg-(--kit-surface-hover) active:border-(--kit-surface) active:bg-(--kit-surface-pressed) aria-expanded:bg-(--kit-surface-hover) focus-visible:border-(--kit-focus-soft) focus-visible:ring-2 focus-visible:ring-(--kit-focus-soft)',
        ghost: 'hover:bg-muted hover:text-foreground dark:hover:bg-muted/50 aria-expanded:bg-muted aria-expanded:text-foreground',
        destructive: 'rounded-full border-black/20 bg-(--kit-danger) font-extrabold text-(--kit-lilac) shadow-[inset_0_0_0_999px_rgb(0_0_0/0.16)] hover:shadow-none active:shadow-[inset_0_0_0_999px_rgb(0_0_0/0.32)] focus-visible:border-(--kit-lilac) focus-visible:ring-2 focus-visible:ring-(--kit-lilac)',
        link: 'text-primary underline-offset-4 hover:underline',
      },
      size: {
        'default': 'h-9 gap-1.5 px-2.5 has-data-[icon=inline-end]:pr-2 has-data-[icon=inline-start]:pl-2',
        'xs': 'h-6 gap-1 px-2 text-xs has-data-[icon=inline-end]:pr-1.5 has-data-[icon=inline-start]:pl-1.5 [&_svg:not([class*=size-])]:size-3',
        'sm': 'h-8 gap-1 px-2.5 has-data-[icon=inline-end]:pr-1.5 has-data-[icon=inline-start]:pl-1.5',
        'lg': 'h-10 gap-1.5 px-2.5 has-data-[icon=inline-end]:pr-2 has-data-[icon=inline-start]:pl-2',
        'icon': 'size-9',
        'icon-xs': 'size-6 [&_svg:not([class*=size-])]:size-3',
        'icon-sm': 'size-8',
        'icon-lg': 'size-10',
      },
    },
    defaultVariants: {
      variant: 'default',
      size: 'default',
    },
  },
)
export type ButtonVariants = VariantProps<typeof buttonVariants>

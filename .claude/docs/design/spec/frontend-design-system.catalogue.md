# Design-system component catalogue (authoritative manifest)

The **closed, authoritative list** of shadcn components ported to Compose, referenced by
`frontend-design-system.md` §4. This file is the manifest: a component may exist in
`core/designsystem/component/` only if it has a complete row here, and every row must be fully filled
(no blank variant/size/state/base cell) before the component is built (a review rule). The §4 table in the design-
system spec is a readable summary; **this file is the source of truth**.

**Conventions (per `frontend-design-system.md`):** one component = one file, named exactly as shadcn;
variants/sizes are shadcn's exact published set (new-york, version-pinned); the **base** is the
most-correct primitive (DS7); every component is a pure, token-driven, stateless composable whose style
comes from a `resolve(variant, size, state, tokens)` lookup. `InteractionState` is the closed flag set
in §4.2. Foundation = `androidx.compose.foundation`; M3 = a themed `androidx.compose.material3` wrapper;
`Popup`/`Dialog` = Compose's overlay primitives.

---

## First batch (as-needed: covers setup · dashboard · commands · pipeline · community · moderation · rewards · timers · widgets · integrations · settings)

| Component | Base | Variants | Sizes | States | Notes |
|---|---|---|---|---|---|
| `Button` | Foundation | default · destructive · destructiveSecondary · outline · secondary · ghost · destructiveGhost · link | sm · default · lg · icon | default · hovered · focused (focus-visible) · pressed · disabled · loading | `loading` shows a spinner + disables; `icon` is square; `DestructiveSecondary` is the quiet destructive pill, `DestructiveGhost` the transparent destructive icon action |
| `Input` | Foundation (`BasicTextField`) | (single) | sm · default · lg | default · focused · disabled · invalid | `invalid` driven by the field, not internal validation |
| `Textarea` | Foundation (`BasicTextField`, multiline) | (single) | (single) | default · focused · disabled · invalid | min/max rows are params |
| `Label` | Foundation | (single) | (single) | default · disabled | pairs with a field id for a11y |
| `Checkbox` | M3-wrapped | (single) | (single) | unchecked · checked · focused · disabled | two-state (`checked: Boolean`); lives in `SelectionControls.kt` beside `RadioButton` |
| `Switch` | Foundation | (single) | (single) | off · on · focused · disabled | pill track + sliding circular thumb via `toggleable`/`Role.Switch` — Foundation so it reads as shadcn, not Material |
| `RadioGroup` / `RadioItem` | M3-wrapped | (single) | (single) | unselected · selected · focused · disabled | group owns selection |
| `Select` | M3-wrapped (menu semantics) | (single) | (single) | closed · open · focused · disabled | single-select; trigger + content |
| `DropdownMenu` | M3-wrapped (menu semantics) | (single) | (single) | closed · open; item: default · hovered · focused · disabled | supports separators, checkable items |
| `Combobox` | Foundation (`Popover` + `Command`) | (single) | (single) | closed · open · focused | composite — no 1:1 shadcn primitive |
| `Dialog` | Foundation (`Dialog`) | (single) | (single) | open · closed | parts: Header/Title/Description/Content/Footer |
| `Sheet` | Foundation (`Popup`/`Dialog`) | side: top · right · bottom · left | (single) | open · closed | slide-in panel |
| `Popover` | Foundation (`Popup`) | (single) | (single) | open · closed | anchored overlay |
| `Tooltip` | Foundation (`Popup`) | (single) | (single) | shown · hidden | hover/focus triggered; delay param |
| `Card` | Foundation | (single) | (single) | default | parts: Header/Title/Description/Content/Footer |
| `Badge` | Foundation | default · secondary · destructive · outline | (single) | default · selected (selectable) | selectable state (`selected` + `onClick`) covers single-select chip rows in place of a Toggle |
| `Alert` | Foundation | default · destructive | (single) | default | parts: Icon/Title/Description |
| `Separator` | Foundation | orientation: horizontal · vertical | (single) | default | decorative by default |
| `Skeleton` | Foundation | (single) | (single) | default (animated shimmer) | placeholder while loading |
| `Avatar` | Foundation | (single) | sm · default · lg | image · fallback | fallback = initials when no image |
| `Progress` | Foundation | (single) | (single) | determinate | value 0–100; for indeterminate/loading use `Spinner` |
| `Spinner` | M3-wrapped | (single) | sm · default · lg | indeterminate | circular loading indicator (shadcn Spinner); replaces `CircularProgressIndicator` |
| `Tabs` | Foundation | (single) | (single) | tab: selected · unselected · focused · disabled | parts: List/Trigger/Content |
| `Table` | Foundation | (single) | (single) | row: default · hovered · selected | parts: Header/Body/Row/Head/Cell/Caption |
| `ScrollArea` | Foundation | orientation: vertical · horizontal · both | (single) | default | styled scrollbar |
| `Slider` | M3-wrapped | (single) | (single) | default · focused · disabled · dragging | single + range |
| `Toast` | Foundation (`Popup`) | default · destructive | (single) | enter · visible · exit | modeled on shadcn's Sonner; queue + auto-dismiss |
| `Stepper` | Foundation | orientation: horizontal · vertical | (single) | step: completed · current · upcoming | numbered/labeled steps + connector line; drives multi-step flows (e.g. setup wizard) |

**Build status:** all catalogue rows built in `core/designsystem/component/` (`Checkbox` in `SelectionControls.kt`, `RadioGroup`/`RadioItem` in `RadioGroup.kt`). The catalogue is closed.

---

## Patterns (app composites — second tier)

Composites built **from** the components above for recurring dashboard needs. Same folder, same token/variant/
state rules, no raw colour/`dp`; each is one file. They are not shadcn primitives and never substitute for a
catalogued component that exists. The 23 shipped:

| Pattern | Built from | Purpose |
|---|---|---|
| `AppSelectField` | `DropdownMenu` + field chrome | labelled single-select field for free-form menu content |
| `AppTextField` | Foundation `BasicTextField` + field chrome | labelled text field, single/multiline, with optional icon and inset action |
| `ColorField` | `AppTextField` + swatch | hex colour input with live swatch |
| `ConfirmDialog` | `Dialog` + `Button` | destructive-action confirmation (`message`, confirm/cancel) |
| `CopyButton` | `Button`(icon) + `Tooltip` | copy-to-clipboard with confirmation |
| `CopyLinkButton` | `CopyButton` | copy a URL (tokens/overlay links) |
| `EntityPickerField` | `AppTextField` + `DropdownMenu` | pick a server entity (pipeline, reward, …) by search |
| `FieldPair` | Foundation `Row`/`Column` | two related fields side by side, stacked on Compact, with an optional trailing action |
| `FileTree` | Foundation lazy list | multi-file project tree for the code/widget editor |
| `GlyphButton` | `Button`(icon) | icon-only button over `IconKey` (`imageVector`) |
| `InlineError` | `Text` + `destructive` token | persistent section-scoped load/preview failure, no auto-hide |
| `LimitedCreateAction` | `Button` + `ResourceUsage` | the one definition of "approaching a limit" (`LimitBanding`) for every limited-resource create action |
| `LinkedText` | Foundation text | inline links/mentions in prose |
| `ManageGate` | wrapper | disables (never hides) children below the manage floor with a reason tooltip |
| `MetricChart` | Foundation canvas | time-series chart for analytics tiles |
| `PageHeader` | text + `Button` slot | page title/description + primary action |
| `PipelineBindPicker` | `EntityPickerField` + `AppTextField` | bind one pipeline to a trigger, with inline create-and-bind |
| `ResizableSplit` | Foundation | draggable two-pane split (editor + preview) |
| `ResourcePickerField` | `AppTextField` + option list | backend-sourced rich picker (label, secondary text, image) for pipeline step resource fields |
| `RevealableSecretField` | `AppTextField` + `GlyphButton` | masked secret with reveal toggle |
| `SearchPickerField` | `AppTextField` + list | typeahead picker over a remote search |
| `ShieldModeToggle` | `Button` + `ManageGate` | the one Shield Mode control every moderation and chat surface renders |
| `TemplateHelpersDialog` | `Dialog` + `AppTextField` | "All helpers" link + dialog that inserts a template placeholder from the backend helper registry |

---

## Adding a component (the closed-growth rule)

A new component is added when a screen needs it (Rule of Three over speculative ports), by appending a
**fully-filled row** here and creating `core/designsystem/component/<Name>.kt` with its variant/size
enums + `resolve()`. Row parity between this file and the folder is a review rule; no tool checks it.

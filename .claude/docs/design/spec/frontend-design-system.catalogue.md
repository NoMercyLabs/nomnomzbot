# Design-system component catalogue (authoritative manifest)

The **closed, authoritative list** of shadcn components ported to Compose, referenced by
`frontend-design-system.md` §4. This file is the **linter's input**: a component may exist in
`core/designsystem/component/` only if it has a complete row here, and every row must be fully filled
(no blank variant/size/state/base cell) before the component is built. The §4 table in the design-
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
| `Button` | Foundation | default · destructive · outline · secondary · ghost · link | sm · default · lg · icon | default · hovered · focused (focus-visible) · pressed · disabled · loading | `loading` shows a spinner + disables; `icon` is square |
| `Input` | Foundation (`BasicTextField`) | (single) | sm · default · lg | default · focused · disabled · invalid | `invalid` driven by the field, not internal validation |
| `Textarea` | Foundation (`BasicTextField`, multiline) | (single) | (single) | default · focused · disabled · invalid | min/max rows are params |
| `Label` | Foundation | (single) | (single) | default · disabled | pairs with a field id for a11y |
| `Checkbox` | M3-wrapped | (single) | (single) | unchecked · checked · indeterminate · focused · disabled | tristate supported |
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

**Build status (as-built, `core/designsystem/component/`):** built — `Button`, `Textarea`, `Switch`, `DropdownMenu`,
`Dialog`, `Sheet`, `Tooltip`, `Card`, `Badge`, `Separator`, `Progress`, `Spinner`, `Tabs`, `Slider`, `Stepper`,
`Alert`, `Label`, `Skeleton` (S051a); `Input`, `RadioGroup` (+ `RadioItem`), `Toast` (S051b); `Popover`,
`Select`, `Combobox` (S051c); `ScrollArea`, `Table` (S051d). `Checkbox` was already built (`SelectionControls.kt`, alongside `RadioButton`)
prior to the S051a manifest catching up — it satisfies the row above; a follow-up may split it into its own
`Checkbox.kt` for one-file-per-component, but there is no functional gap to close. `RadioGroup`/`RadioItem`
(`RadioGroup.kt`) is a group wrapper over the existing `RadioButton` indicator, not a duplicate. `Toast`
(`Toast.kt`) is the primitive now rendered by the app-shell's `FeedbackHost` (`core/feedback/`), which
previously inlined the same banner — `FeedbackHost` was refactored onto the catalogued primitive rather than
kept as a second, undeclared implementation. `Select` (`Select.kt`) is the closed-catalogue primitive;
`AppSelectField` (patterns tier, below) remains a separate app composite for free-form menu content rather
than a flat option list, so it is not superseded. `Combobox` (`Combobox.kt`) is built from `Input` + `Popover`
per its catalogue row (`Foundation (Popover + Command)`); `EntityPickerField`/`SearchPickerField` (patterns
tier) remain the id-keyed entity-search composites built on their own chrome, not on this primitive.
`Avatar` (`Avatar.kt`) was also found already built — like `Checkbox` before it, the manifest had not caught
up; no new file was needed for it, only this correction. `ScrollArea` (`ScrollArea.kt`) and `Table`
(`Table.kt`, S051d) close the series — no pre-existing scroll wrapper or data-grid primitive was found
under another name (only app-composite `DataTable`/`FileTree`-style screens built on raw `Column`/`Row`,
none of which duplicate this closed-catalogue primitive), so both are new files. This closes the S051
catalogue-gap series: every row in §4's table now has a built file.
**To build** — none; the catalogue is closed (all rows built).

---

## Patterns (app composites — second tier)

Composites built **from** the components above for recurring dashboard needs. Same folder, same token/variant/
state rules, no raw colour/`dp`; each is one file. They are not shadcn primitives and never substitute for a
catalogued component that exists. The 17 shipped:

| Pattern | Built from | Purpose |
|---|---|---|
| `ActionErrorBanner` | `Alert`-style surface + `Button` | inline failure banner for a page action with retry |
| `AppSelectField` | `DropdownMenu` + field chrome | labelled single-select field (stands in for `Select` until it is built) |
| `AppTextField` | M3 `OutlinedTextField` + `appFieldColors()` | labelled text field, single/multiline (stands in for `Input`) |
| `ColorField` | `AppTextField` + swatch | hex colour input with live swatch |
| `ConfirmDialog` | `Dialog` + `Button` | destructive-action confirmation (`message`, confirm/cancel) |
| `CopyButton` | `Button`(icon) + `Tooltip` | copy-to-clipboard with confirmation |
| `CopyLinkButton` | `CopyButton` | copy a URL (tokens/overlay links) |
| `EntityPickerField` | `AppTextField` + `DropdownMenu` | pick a server entity (pipeline, reward, …) by search |
| `FileTree` | Foundation lazy list | multi-file project tree for the code/widget editor |
| `GlyphButton` | `Button`(icon) | icon-only button over `IconKey` (`imageVector`) |
| `LinkedText` | Foundation text | inline links/mentions in prose |
| `ManageGate` | wrapper | disables (never hides) children below the manage floor with a reason tooltip |
| `MetricChart` | Foundation canvas | time-series chart for analytics tiles |
| `PageHeader` | text + `Button` slot | page title/description + primary action |
| `ResizableSplit` | Foundation | draggable two-pane split (editor + preview) |
| `RevealableSecretField` | `AppTextField` + `GlyphButton` | masked secret with reveal toggle |
| `SearchPickerField` | `AppTextField` + list | typeahead picker over a remote search |

---

## Adding a component (the closed-growth rule)

A new component is added when a screen needs it (Rule of Three over speculative ports), by appending a
**fully-filled row** here and creating `core/designsystem/component/<Name>.kt` with its variant/size
enums + `resolve()`. The `nomnomz-component` scaffold (`frontend-structure.md` §6) drops both, with the
row pre-stubbed; the linter fails the build on a row with any empty cell or a component file with no
matching row.

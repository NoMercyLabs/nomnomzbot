# Design system and layout consolidation TODO

Source-only audit of the Vue dashboard. This document covers the registered screens, nested tabs, drawers,
dialogs, editors and public/viewer flows. It intentionally does not prescribe a visual redesign; the goal is to
make existing layouts, placement rules and interaction patterns reusable and predictable.

## Audit summary

The project already has a strong low-level design system. The main gap is the composition layer above those
primitives, plus incomplete adoption across older or more complex screens.

The development build contains approximately:

- 30 core dashboard routes
- 28 settings destinations
- 22 administration screens
- 7 public screens
- 5 viewer screens
- The chat popout and many tab, drawer, dialog and editor sub-screens

Existing primitives are already used heavily:

- `PanelCard`: 193 view files
- `PageHeader`: 78 view files
- `RowGroup`: 71 view files
- `PageBody`: 47 view files
- `PageTabs`: 14 view files
- `ListDetail`: 11 view files

The duplication checker reports 2.26% duplicated lines, below the configured limit. The issue is therefore not
primarily copied code. It is parallel page composition: similar screens independently assemble the same pieces
with slightly different spacing, placement, state handling and responsive behavior.

## Preserve these foundations

Do not replace these with a universal page component. They are sound foundations:

- `dashboard/src/components/layout/PageHeader.vue`
- `dashboard/src/components/layout/PageBody.vue`
- `dashboard/src/components/layout/PanelCard.vue`
- `dashboard/src/components/layout/PageTabs.vue`
- `dashboard/src/components/layout/ListDetail.vue`
- `dashboard/src/components/layout/ListPanel.vue`
- `dashboard/src/components/layout/BoardGrid.vue`
- `dashboard/src/components/layout/PanelBeside.vue`
- `dashboard/src/components/layout/SidePanel.vue`
- `dashboard/src/components/layout/PageActionFooter.vue`
- `dashboard/src/components/layout/PageCreateButton.vue`
- `dashboard/src/components/layout/EditorFooter.vue`
- `dashboard/src/components/layout/RowEditDelete.vue`
- `dashboard/src/components/layout/RowGroup.vue`
- `dashboard/src/components/layout/SummaryRow.vue`
- `dashboard/src/components/layout/SettingRow.vue`
- `dashboard/src/components/layout/FormDialog.vue`
- `dashboard/src/components/layout/ConfirmDialog.vue`
- `dashboard/src/composables/usePanelState.ts`
- The tokens, density and touch-target system in `dashboard/src/styles/tokens.css`
- The responsive frame rules and structure tests
- The development component catalog at `/app/catalog`

Keep Chat, OBS, Music and Pipeline Workbench specialized. They should follow shared placement, row, state and
token rules, but should not be forced into a generic CRUD page.

## Placement contract

Treat the following as the default contract for new and migrated screens:

| Element | Stable placement |
|---|---|
| Page title, status and primary action | `PageHeader` |
| Tabs and compact filters | `PageHeader` `bar` slot |
| Search | `ListPanel` header |
| Create action | `PageCreateButton` |
| Responsive list/detail layout | `ListDetail` |
| Editor Save, Discard and Delete | `EditorFooter` |
| Row Edit and Delete | `RowEditDelete` |
| Mobile sticky actions | `PageActionFooter` |
| Panel loading, empty and error state | `PanelCard.state` |
| Settings and summary rows | `RowGroup` with `SettingRow` or `SummaryRow` |

The repository already describes this list contract in
`dashboard/src/__tests__/structure.spec.ts` around the Phase 1/Phase 2 list migration.

## P0 — finish the existing Phase 2 list migration

This is the highest-value first step. Phase 1 already moved Pipelines, Overlays, Commands, Timers/chat
triggers, Scripts and Event Responses to the intended list-page system.

### Search placement

- [ ] Move hand-built panel-header searches to `ListPanel` where appropriate:
  - `dashboard/src/views/dashboard/admin/AdminSupportView.vue`
  - `dashboard/src/views/dashboard/admin/AdminTenantsView.vue`
  - `dashboard/src/views/dashboard/admin/components/TenantMembersPanel.vue`
  - `dashboard/src/views/dashboard/people/PeopleView.vue`
  - `dashboard/src/views/dashboard/settings/permissions/components/ActionAreaList.vue`
- [ ] Replace raw list-page search inputs:
  - `dashboard/src/views/dashboard/picklists/PickListsView.vue`
  - `dashboard/src/views/dashboard/quotes/QuotesView.vue`
- [ ] Keep search inputs inside genuine pickers/popovers local; they are not list-page searches.

### Edit and delete placement

- [ ] Migrate entity row actions to `RowEditDelete` and editor actions to `EditorFooter` where applicable:
  - Economy Catalog
  - Economy Earning Rules
  - Economy Leaderboards
  - Economy Savings Jars
  - Giveaways
  - Pick Lists
  - Quotes
  - API device tokens
  - Custom event feeds
  - Escalation ladder
  - Permission grants
  - Inbound and outbound webhooks
  - Sounds
  - TTS Lexicon
  - Voice Triggers
- [ ] Review the other delete-icon allowlist entries individually. Do not migrate actions that are not entity
      deletion, such as a chat-line action, pipeline step removal or editor-internal string removal.
- [ ] Remove each file from the Phase 2 allowlist as it is migrated.
- [ ] Keep the existing “no clean file on the allowlist” tests passing.

### Acceptance criteria

- Search appears in the same place on every comparable list screen.
- Create is always the page-level primary action.
- Edit precedes Delete in row actions.
- Save, Discard and Delete have one editor-footer order.
- Phone actions use the shared sticky footer behavior.
- No migrated screen remains on a structure-test allowlist.

## P1 — approved page-body recipes

Many pages rebuild their scrolling body manually. At least eleven admin screens repeat a variant of:

```html
<div data-page-body class="flex min-h-0 min-w-0 flex-1 flex-col gap-3 p-6 max-md:p-4">
```

Examples include Audit, Content, Data Requests, EventSub Health, Event Replay, Scheduled Jobs, Tenant Usage,
Error Budget, Platform Bot and Webhook Deliveries.

- [ ] Define a small approved set of page-body compositions:
  - Standard scrolling `PageBody`
  - Dense data/table body
  - `ListDetail`
  - `BoardGrid`
  - Full-bleed workspace
  - `PanelBeside` containing one approved body
- [ ] Add narrowly scoped `PageBody` variants such as `dense`, `compact` or `flush` only where real layouts
      require them.
- [ ] Migrate repeated admin `data-page-body` containers to the approved recipe.
- [ ] Review raw bodies in Economy, Pipelines and Stream sub-tabs.
- [ ] Leave Chat and true board/workbench layouts specialized.
- [ ] Add or extend a structure test preventing new ad hoc page-body implementations.

### Acceptance criteria

- Every routed page has one clear scroll owner.
- Desktop scrollbar gutter, mobile edge-to-edge panels and child shrink behavior come from shared layout code.
- Screen files do not repeat the core page-body class string.

## P1 — reusable admin collection page

Audit Log, EventSub Health, Webhook Deliveries, Scheduled Jobs, Tenant Usage and Error Budget share nearly the
same composition:

1. Page header with result count
2. Optional filters
3. Lead panel with loading/empty/error state
4. Table
5. Desktop pager in the panel footer
6. Mobile pager in `PageActionFooter`

- [ ] Create a narrowly scoped `AdminCollectionPage` or `PaginatedCollectionPage` composition.
- [ ] Keep data fetching, parameters, filters and table components inside each domain screen.
- [ ] Provide slots for filters, notices, table content and optional secondary content.
- [ ] Standardize result count in `PageHeader#status`.
- [ ] Standardize pager placement across desktop and phone.
- [ ] Migrate the six closest admin pages first, then evaluate Tenants and Data Requests separately.

## P1 — actionable empty states and inline notices

`PanelCard` has good loading/empty/error behavior, but its empty state only accepts text. Screens therefore
recreate actionable empty blocks and page-level mutation messages.

- [ ] Add an `EmptyState` component or an `empty` slot to `PanelCard` supporting:
  - Optional icon
  - Title
  - Description
  - Optional primary action
- [ ] Explicitly support these meanings:
  - No entities exist yet
  - Search has no matches
  - Current filter has no items
  - Feature is unavailable due to permission or plan
- [ ] Add `InlineNotice`/`PageNotice` tones:
  - Information
  - Warning
  - Error
  - Success
  - Plan/read-only gating
- [ ] Replace repeated `text-sm text-destructive` mutation paragraphs.
- [ ] Replace matching bordered-muted notices in Scripts, Channel Points and similar pages.
- [ ] Keep whole-panel failures in `PanelCard.state`; use notices only for page or mutation feedback.

## P1 — consolidate list/detail editor composition

Commands, Timers, Event Responses, Voice Triggers, People, Overlays, Scripts, Admin Support and Permissions
repeat variants of the same detail column:

- Conditional selected editor
- Mobile Back action
- Idle/select-something panel
- Not-found state
- Optional aside/drawer
- Editor sizing and scroll behavior

- [ ] Add a small `DetailEditorPane` composition or extend the existing `DetailPane`.
- [ ] Let it own the mobile Back action, idle/not-found presentation and detail-column sizing.
- [ ] Keep the domain editor itself local.
- [ ] Migrate Commands, Timers and Event Responses first because their wrappers are closest.
- [ ] Evaluate People, Voice Triggers, Overlays and Scripts after the base behavior is proven.

### Filtered resource lists

Scripts and Overlays are structural twins: filter tabs, tab counts, search, permission/state handling,
query-backed selection, list/detail and “no matches vs empty tab vs no entities” states.

- [ ] Extract a typed `useFilteredResourceList` composable.
- [ ] Consider a small filtered-list shell only after the composable removes the behavioral duplication.
- [ ] Do not put domain-specific item rendering or editors into the shared abstraction.

## P2 — typed tabbed-page descriptor

Economy, Analytics, OBS, TTS, Stream, VTS, Commands, Timers, Scripts, People, Overlays, Permissions and Event
Journal manually repeat combinations of:

- A `VIEWS` tuple
- Tab label mappings
- Permission filtering
- `usePageView`
- Tab counts
- Tab-specific primary actions
- Long conditional content chains

- [ ] Design a typed tab descriptor carrying the fields a page actually needs:
  - Value
  - Label
  - Read permission
  - Optional count
  - Optional disabled reason
  - Optional primary action descriptor
- [ ] Keep tab contents as slots or local components.
- [ ] Start with the permission-filtered Economy, TTS, OBS and Timers pages.
- [ ] Preserve URL-backed tab selection and the existing responsive `PageTabs` implementation.
- [ ] Avoid a mega-component that owns domain state.

## P2 — complete the row vocabulary

Many screens still hand-code variants of:

```text
flex min-h-11 items-center gap-3 rounded-[8px] px-3 py-2
```

This occurs across Integrations, Federation, Webhooks, Channel Management, Event Journal, Giveaways, Games and
admin detail panels.

- [ ] Migrate simple title/detail/meta/leading/trailing rows to `SummaryRow`.
- [ ] Use `SettingRow` for boolean settings.
- [ ] Use `RowGroup` instead of raw `divide-y` lists where the semantics match.
- [ ] Add one `ActionRow`/`StatusActionRow` only for explanatory copy plus a trailing administrative or
      destructive action that `SummaryRow` cannot express cleanly.
- [ ] Preserve domain components when they own behavior, not merely layout.
- [ ] Normalize status display through `StatusBadge` rather than local badge-variant decisions.

## P2 — adopt the Field system consistently

Many forms still use raw `Label` plus `grid gap-1.5` wrappers. The existing Field primitives already cover the
required semantics.

- [ ] Standardize forms on:
  - `Field`
  - `FieldLabel`
  - `FieldDescription`
  - `FieldError`
  - `FieldGroup`
- [ ] Use `ChatFilterEditor.vue` and `AuditFilters.vue` as reference implementations.
- [ ] Prioritize forms with validation errors and helper descriptions.
- [ ] Add a structure test only after enough legacy forms have migrated to avoid an oversized permanent
      allowlist.

## P2 — unify connection and credential presentation

Admin Providers, Integrations, White-label Bot, Spotify credentials, Viewer linked accounts and API devices all
express versions of the same state machine:

- Provider identity/logo
- Connected, disconnected or needs-attention state
- Account name
- Scope/capability details
- Connect, Reconnect and Disconnect actions
- Credential present/missing status
- Destructive disconnect confirmation

- [ ] Define a reusable `ConnectionRow` presentation contract.
- [ ] Support optional detail and credential slots.
- [ ] Reuse `StoredCredentialRow` where it already fits.
- [ ] Keep authentication flows and provider-specific mutations outside the presentation component.
- [ ] Standardize disconnected, needs-attention and connected status wording and tones.

## P2 — normalize settings page ownership

Settings currently uses three models:

- Four content-only screens rendered inside `SettingsView.vue`
- Most settings screens own their own `PageHeader` and `PageBody`
- Supporter Sources reuses a page from another area

- [ ] Choose and document one authoring contract.
- [ ] Preferred direction: create a `SettingsPageFrame` providing title, crumb and an approved body.
- [ ] Allow named slots/configuration for header actions, tabs, list/detail and side panels.
- [ ] Move title/crumb metadata closer to the route/navigation definition where practical.
- [ ] Do not prevent complex settings screens from selecting a different approved body recipe.
- [ ] Review the settings information architecture separately; 28 destinations is workable only while grouping
      and naming remain strong.
- [ ] Give the access/safety destinations more distinct navigation icons; several currently share the same
      shield-alert symbol.

## P2 — viewer page shell

My Channels, Me and Account manually repeat title, horizontal padding, action/status placement and local
loading/error handling inside the viewer frame.

- [ ] Add a lightweight `ViewerPageHeader` and `ViewerPageBody`, or a focused `ViewerPage` composition.
- [ ] Respect the `max-w-4xl` responsibility already owned by `ViewerLayout`.
- [ ] Support title, status, action and consistent loading/error/empty presentation.
- [ ] Do not import management-shell assumptions such as channel switching or live-management navigation.

## P2 — public task shell and public-flow consistency

Login, Invite and 404 repeat a centered viewport frame with language picker, brand, compact card and optional
below-card content. Setup is a wider wizard variant.

- [ ] Add a focused `PublicTaskShell`/`SiteTaskFrame` with:
  - `compact` variant for Login, Invite and 404
  - `wizard` variant for Setup
  - Language-picker position
  - Brand/home-link position
  - Semantic page title
  - Content width
  - Optional below-card content
- [ ] Keep the marketing home and legal document pages specialized.
- [ ] Correct heading hierarchy: the task title should be the page `h1`; the product name should not displace it.
- [ ] Standardize public async/loading/error/empty presentation using `PanelCard.state` or a small shared
      `AsyncState`/`InlineFeedback` primitive.
- [ ] Add `TaskActions` for compact full-width action stacks.
- [ ] Add `WizardActions` for Back-left/Next-right flows.
- [ ] Make brand marks in compact task flows link home unless there is a product reason not to.
- [ ] Replace Setup's binary step renderer with a typed step renderer registry and an explicit unknown-step
      fallback.

### Public-flow cleanup

- [ ] Reuse the global copy-feedback behavior instead of maintaining a separate `CopyAddressButton` timer and
      clipboard implementation.
- [ ] Add shared `LegalLinks`/`ConsentNotice` and make Login's Terms and Privacy references actual links.
- [ ] Decide whether Setup's stepper and card should share a width/alignment grid.
- [ ] Add a reusable marketing section heading/tag vocabulary without abstracting entire marketing sections.

## P3 — quick consistency fixes

- [ ] Fix the marketing Features link: `HomeHeader.vue` points to `#outcomes`, while `FeatureGroups.vue` uses
      `id="features"`.
- [ ] Consolidate public one-off radii into semantic component variants where the same surface is intended.
- [ ] Use the existing spinner/skeleton vocabulary for long-running public actions.
- [ ] Standardize advanced filters as a documented third query-control recipe:
  1. Compact filters in `PageHeader#bar`
  2. Search in the list header
  3. Collapsible advanced filters
- [ ] Document `PanelCard` tiers:
  - `lead`: the page's main purpose
  - default: supporting workflow
  - `quiet`: secondary status or danger content

## Catalog and governance

The dev-only catalog at `/app/catalog` should be the canonical designer/developer reference rather than adding
a second component showcase immediately.

- [ ] Add every accepted page recipe and state component to the catalog.
- [ ] Show desktop, narrow and phone behavior for the composition primitives.
- [ ] Include loading, empty, no-match, error, read-only, gated and destructive states.
- [ ] Document stable action placement with examples.
- [ ] Add structure tests once a migration lane is established.
- [ ] Use temporary allowlists that shrink as files migrate; fail when a clean file remains allowlisted.

## Recommended delivery order

1. Finish the existing Phase 2 list migration.
2. Add actionable `EmptyState` and `InlineNotice`.
3. Enforce approved page-body recipes.
4. Extract the paginated admin collection composition.
5. Consolidate the list/detail editor wrapper.
6. Extract filtered-resource behavior from Scripts and Overlays.
7. Introduce the typed tab descriptor.
8. Migrate raw rows and forms.
9. Normalize settings and viewer page ownership.
10. Consolidate connection presentation.
11. Add the public task shell and public-flow cleanup.
12. Keep the catalog and structure tests current throughout the work.

## Final architectural constraint

Add a small composition layer, not one universal mega-component. Shared components should own visual layout,
placement, responsive behavior and state presentation. Domain screens should continue to own queries,
permissions, mutations and feature-specific interactions.

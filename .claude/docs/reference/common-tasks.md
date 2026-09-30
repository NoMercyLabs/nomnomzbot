# Common Tasks

### Adding a New API Endpoint

1. Define the service interface in `NomNomzBot.Application/<Module>/Services/`
2. Implement it in `NomNomzBot.Infrastructure/<Module>/`
3. Register it in `NomNomzBot.Infrastructure/DependencyInjection.cs`
4. Create controller in `NomNomzBot.Api/Controllers/V1/` with `[ApiVersion("1.0")]` and `[Route("api/v{version:apiVersion}/...")]`
5. Return `StatusResponseDto<T>` or `PaginatedResponse<T>`

### Adding a New Dashboard Page

1. **Load the `sleak` skill first** — before a line of Compose is written, not as a review pass
   afterwards. Hierarchy decisions (which action is primary, what the eye lands on) are made while
   the screen is being laid out; retrofitting them means rewriting it.
2. Screen + state under `app/composeApp/src/commonMain/.../feature/<domain>/`; navigation and page
   placement per `frontend-ia.md` (the definitive page inventory).
3. Fetch/mutate exclusively through the **typed shared KMP client** (`core/network`, REST + SignalR) —
   never call the API ad hoc. New DTOs register in `ApiContractTest`; refresh `server/openapi/v1.json`
   on any contract change.
4. Design-system components only (`frontend-design-system.md` + catalogue) — no raw hex/`dp`.
5. i18n keys for both `en` and `nl`; never hardcode user-facing strings.
6. Role-gate per `frontend-ia.md` §7 — hide pages below the read floor; **disable** (don't hide)
   actions below the manage floor, with a reason tooltip.
7. Re-run the Sleak checklist on the **rendered** screen before calling it done — one primary action
   per group, concentric radius, accent spent once. Source alone does not show hierarchy.

### Adding a New Twitch EventSub Subscription

Per the `twitch-eventsub.md` spec: drop a translator class (auto-discovered via
`AddImplementationsOf<IEventSubEventTranslator>`) beside the existing ones in
`NomNomzBot.Infrastructure/Platform/Eventing/Translators/` — one translator class per topic in the
Translators folder — and add the topic's condition/version/token-owner to `EventSubConditionBuilder`.
`TwitchEventSubHostedService` re-registers the full set on every (re)connect, and the translator
turns the wire payload into a domain event on the bus.

### Adding a New Integration (OAuth pattern)

1. Add the provider constant (`AuthEnums.IntegrationProvider`) and an `OAuthProviderRegistry` entry
2. Add the `{Provider}:ClientId/ClientSecret` config keys to `appsettings.json`, `.env.example` and `docker-compose.yml`
3. Add the Integrations screen row (`feature/integrations`); gate the feature in the frontend on the integration's connection state (placement per `frontend-ia.md`).

Connect, disconnect and callback are served by the generic `IntegrationOAuthController` — no per-provider controller.

### Adding a New Pipeline Action

1. Create the action implementing `ICommandAction` in `NomNomzBot.Infrastructure/Platform/Pipeline/CoreActions/` (core) or `NomNomzBot.Infrastructure/<Module>/PipelineActions/` (side-effecting)
2. Set the `ActionType` property (not `Type`) to a unique snake_case string — registration is automatic via the `ICommandAction` assembly scan (`AddImplementationsOf<ICommandAction>`); no DI edit
3. Add the contract/DTO to `NomNomzBot.Application/Abstractions/Pipeline/`
4. Surface the action in the dashboard's pipeline builder block palette (`feature/pipelines`).

using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using NomNomzBot.Domain.Chat.ValueObjects;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:hstore", ",,");

            migrationBuilder.CreateTable(
                name: "ActionDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Plane = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DefaultLevel = table.Column<int>(type: "integer", nullable: false),
                    FloorLevel = table.Column<int>(type: "integer", nullable: false),
                    FloorTier = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsGrantableViaPermit = table.Column<bool>(type: "boolean", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BotAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BotUserId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    BotUsername = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BotAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CatalogItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NameNormalized = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    SinkType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Cost = table.Column<long>(type: "bigint", nullable: false),
                    IconUrl = table.Column<string>(type: "text", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Permission = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PipelineId = table.Column<Guid>(type: "uuid", nullable: true),
                    CooldownSeconds = table.Column<int>(type: "integer", nullable: false),
                    CooldownPerUser = table.Column<bool>(type: "boolean", nullable: false),
                    StockLimit = table.Column<int>(type: "integer", nullable: true),
                    StockRemaining = table.Column<int>(type: "integer", nullable: true),
                    MaxPerViewerPerStream = table.Column<int>(type: "integer", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CatalogPurchases",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    CatalogItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    BuyerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    BuyerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CostPaid = table.Column<long>(type: "bigint", nullable: false),
                    ItemNameSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LedgerEntryId = table.Column<long>(type: "bigint", nullable: true),
                    InputArgs = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogPurchases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChannelActionOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OverrideLevel = table.Column<int>(type: "integer", nullable: false),
                    SetByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelActionOverrides", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChannelCommunityStandings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Standing = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LevelValue = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SubTier = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelCommunityStandings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChannelMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ManagementRole = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LevelValue = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GrantedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelMemberships", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CurrencyAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    ViewerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ViewerTwitchUserId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Balance = table.Column<long>(type: "bigint", nullable: false),
                    LifetimeEarned = table.Column<long>(type: "bigint", nullable: false),
                    LifetimeSpent = table.Column<long>(type: "bigint", nullable: false),
                    IsFrozen = table.Column<bool>(type: "boolean", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurrencyAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CurrencyConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrencyName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CurrencyNamePlural = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IconUrl = table.Column<string>(type: "text", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    StartingBalance = table.Column<long>(type: "bigint", nullable: false),
                    MaxBalance = table.Column<long>(type: "bigint", nullable: true),
                    DecimalPlaces = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurrencyConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CurrencyLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantPosition = table.Column<long>(type: "bigint", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ViewerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ViewerTwitchUserId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    BalanceAfter = table.Column<long>(type: "bigint", nullable: false),
                    EntryType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SourceType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    RelatedEntryId = table.Column<long>(type: "bigint", nullable: true),
                    EventId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurrencyLedgerEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeletionAuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SubjectIdHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TablesAffected = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    RowsDeleted = table.Column<int>(type: "integer", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeletionAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EarningRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Rate = table.Column<long>(type: "bigint", nullable: false),
                    UnitWindowSeconds = table.Column<int>(type: "integer", nullable: true),
                    PerWindowCap = table.Column<long>(type: "bigint", nullable: true),
                    PerStreamCap = table.Column<long>(type: "bigint", nullable: true),
                    MinRoleLevel = table.Column<int>(type: "integer", nullable: true),
                    ConfigSchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    BonusConfigJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EarningRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EventJournals",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: true),
                    StreamPosition = table.Column<long>(type: "bigint", nullable: false),
                    EventType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    EventVersion = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    PayloadIsEncrypted = table.Column<bool>(type: "boolean", nullable: false),
                    SubjectKeyId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CausationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorTwitchUserId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Metadata = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventJournals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EventSubConduits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ConduitId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ShardCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LastReconciledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventSubConduits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    GameType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Requires18Plus = table.Column<bool>(type: "boolean", nullable: false),
                    MinBet = table.Column<long>(type: "bigint", nullable: true),
                    MaxBet = table.Column<long>(type: "bigint", nullable: true),
                    HouseEdgePercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    WinChancePercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    PayoutMultiplier = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    CooldownSeconds = table.Column<int>(type: "integer", nullable: false),
                    MaxPlaysPerStream = table.Column<int>(type: "integer", nullable: true),
                    ConfigJson = table.Column<string>(type: "text", nullable: true),
                    Permission = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GamePlays",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    GameConfigId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BetAmount = table.Column<long>(type: "bigint", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PayoutAmount = table.Column<long>(type: "bigint", nullable: false),
                    NetResult = table.Column<long>(type: "bigint", nullable: false),
                    ResultJson = table.Column<string>(type: "text", nullable: true),
                    BetLedgerEntryId = table.Column<long>(type: "bigint", nullable: true),
                    PayoutLedgerEntryId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GamePlays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IamAuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PrincipalId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrincipalType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Permission = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    TargetBroadcasterId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetResource = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Justification = table.Column<string>(type: "text", nullable: true),
                    BreakGlass = table.Column<bool>(type: "boolean", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SourceIpCipher = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IamAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IamPermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsSensitive = table.Column<bool>(type: "boolean", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IamPermissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IamPrincipals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrincipalType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EmailCipher = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SubjectKeyId = table.Column<Guid>(type: "uuid", nullable: true),
                    ServiceAccountKeyHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IamPrincipals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IamRoleAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrincipalId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedByPrincipalId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IamRoleAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IamRolePermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IamRolePermissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IamRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IamRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyKeys",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Scope = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResultHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PermitGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    GrantedRole = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ActionDefinitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    GrantedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermitGrants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectionCheckpoints",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectionName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastPosition = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    LastProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectionCheckpoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Pronouns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Subject = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Object = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Singular = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pronouns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantSequences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    SequenceName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NextValue = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSequences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TtsCacheEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AudioData = table.Column<byte[]>(type: "bytea", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    VoiceId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TtsCacheEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TtsUsageRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CharacterCount = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    VoiceId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TtsUsageRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TtsVoices",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Gender = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TtsVoices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserTtsVoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    VoiceId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTtsVoices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ViewerAgeConsents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    ViewerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ViewerTwitchUserId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ConsentRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Granted = table.Column<bool>(type: "boolean", nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConfirmationMethod = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ViewerAgeConsents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EventSubConduitShards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConduitId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShardId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Transport = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CallbackUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    SessionId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventSubConduitShards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventSubConduitShards_EventSubConduits_ConduitId",
                        column: x => x.ConduitId,
                        principalTable: "EventSubConduits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TwitchUserId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Username = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    UsernameNormalized = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    NickName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    EmailCipher = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SubjectKeyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Timezone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ProfileImageUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    OfflineImageUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    BroadcasterType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: ""),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    IsPlatformPrincipal = table.Column<bool>(type: "boolean", nullable: false),
                    IsBot = table.Column<bool>(type: "boolean", nullable: false),
                    IsAnonymized = table.Column<bool>(type: "boolean", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PronounId = table.Column<int>(type: "integer", nullable: true),
                    PronounManualOverride = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Pronouns_PronounId",
                        column: x => x.PronounId,
                        principalTable: "Pronouns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Channels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TwitchChannelId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false),
                    NameNormalized = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SuspendedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SuspendedReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DeploymentMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BillingTierKey = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    ShoutoutTemplate = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    LastShoutout = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ShoutoutInterval = table.Column<int>(type: "integer", nullable: false, defaultValue: 10),
                    UsernamePronunciation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsOnboarded = table.Column<bool>(type: "boolean", nullable: false),
                    BotJoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OverlayToken = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    IsLive = table.Column<bool>(type: "boolean", nullable: false),
                    Language = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    GameId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    GameName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    StreamDelay = table.Column<int>(type: "integer", nullable: false),
                    Tags = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    ContentLabels = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    IsBrandedContent = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Channels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Channels_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IpcDevModeKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IpcDevModeKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IpcDevModeKeys_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AuthSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClientType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IpAddressCipher = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthSessions_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AuthSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChannelBotAuthorizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    BotAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorizedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AuthorizedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    BotJoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelBotAuthorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelBotAuthorizations_BotAccounts_BotAccountId",
                        column: x => x.BotAccountId,
                        principalTable: "BotAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChannelBotAuthorizations_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChannelEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", maxLength: 50, nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", maxLength: 50, nullable: true),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Data = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelEvents_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ChannelEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ChannelFeatures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeatureKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    EnabledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RequiredScopes = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelFeatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelFeatures_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChannelModerators",
                columns: table => new
                {
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "moderator"),
                    GrantedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GrantedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelModerators", x => new { x.ChannelId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ChannelModerators_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChannelModerators_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChannelSubscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tier = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "free"),
                    StripeCustomerId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    StripeSubscriptionId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CurrentPeriodEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "active"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelSubscriptions_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Commands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Permission = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "everyone"),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "text"),
                    Response = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Responses = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    PipelineJson = table.Column<string>(type: "jsonb", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CooldownSeconds = table.Column<int>(type: "integer", nullable: false),
                    CooldownPerUser = table.Column<bool>(type: "boolean", nullable: false),
                    Aliases = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    IsPlatform = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Commands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Commands_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Configurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: true),
                    Key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true),
                    SecureValue = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Configurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Configurations_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DiscordServerAuthorizations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    GuildId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    GuildName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "pending"),
                    ApprovedBy = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscordServerAuthorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscordServerAuthorizations_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventResponses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ResponseType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PipelineJson = table.Column<string>(type: "text", nullable: true),
                    Metadata = table.Column<Dictionary<string, string>>(type: "hstore", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventResponses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventResponses_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventSubSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Condition = table.Column<string>(type: "text", nullable: false),
                    Transport = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TwitchSubscriptionId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SessionId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ConduitId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ShardId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Cost = table.Column<int>(type: "integer", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventSubSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventSubSubscriptions_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: true),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProviderAccountId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ProviderAccountName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Scopes = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    ClientId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IsByok = table.Column<bool>(type: "boolean", nullable: false),
                    Settings = table.Column<string>(type: "text", nullable: true),
                    ConnectedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConnectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastRefreshedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastErrorAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsecutiveFailureCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntegrationConnections_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Permissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    SubjectId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ResourceId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    PermissionValue = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Permissions_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Pipelines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    GraphJson = table.Column<string>(type: "text", nullable: false),
                    TriggerCount = table.Column<int>(type: "integer", nullable: false),
                    LastTriggeredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pipelines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pipelines_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Records",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Data = table.Column<string>(type: "jsonb", nullable: false),
                    UserId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Records", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Records_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Rewards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Response = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Permission = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "everyone"),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PipelineJson = table.Column<string>(type: "jsonb", nullable: true),
                    IsPlatform = table.Column<bool>(type: "boolean", nullable: false),
                    TwitchRewardId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Cost = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rewards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Rewards_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Services",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClientId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ClientSecret = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    UserName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    UserId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Scopes = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    AccessToken = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    RefreshToken = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    TokenExpiry = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Services", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Services_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Storages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: true),
                    Key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true),
                    SecureValue = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Storages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Storages_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Streams",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", maxLength: 50, nullable: false),
                    Language = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    GameId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    GameName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Delay = table.Column<int>(type: "integer", nullable: false),
                    Tags = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    ContentLabels = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    IsBrandedContent = table.Column<bool>(type: "boolean", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Streams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Streams_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Timers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Messages = table.Column<List<string>>(type: "text[]", nullable: false),
                    IntervalMinutes = table.Column<int>(type: "integer", nullable: false),
                    MinChatActivity = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LastFiredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextMessageIndex = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Timers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Timers_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WatchStreaks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    UserDisplayName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CurrentStreak = table.Column<int>(type: "integer", nullable: false),
                    MaxStreak = table.Column<int>(type: "integer", nullable: false),
                    LastSeenDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchStreaks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WatchStreaks_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Widgets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "1.0.0"),
                    Framework = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "vanilla"),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    TemplateId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EventSubscriptions = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    Settings = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    CustomCode = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Widgets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Widgets_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PreviousTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedReason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_AuthSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "AuthSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: true),
                    TokenType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CipherText = table.Column<string>(type: "text", nullable: false),
                    Nonce = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EncryptionKeyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RotatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntegrationTokens_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_IntegrationTokens_IntegrationConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "IntegrationConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Username = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    UserType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ColorHex = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    Message = table.Column<string>(type: "text", nullable: false),
                    Fragments = table.Column<List<ChatMessageFragment>>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    Badges = table.Column<List<ChatBadge>>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    MessageType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "text"),
                    IsCommand = table.Column<bool>(type: "boolean", nullable: false),
                    IsCheer = table.Column<bool>(type: "boolean", nullable: false),
                    BitsAmount = table.Column<int>(type: "integer", nullable: true),
                    IsHighlighted = table.Column<bool>(type: "boolean", nullable: false),
                    ReplyToMessageId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    StreamId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatMessages_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatMessages_Streams_StreamId",
                        column: x => x.StreamId,
                        principalTable: "Streams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActionDefinitions_ActionKey",
                table: "ActionDefinitions",
                column: "ActionKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthSessions_BroadcasterId",
                table: "AuthSessions",
                column: "BroadcasterId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthSessions_UserId_RevokedAt",
                table: "AuthSessions",
                columns: new[] { "UserId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BotAccounts_BotUserId",
                table: "BotAccounts",
                column: "BotUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BotAccounts_Platform_IdentityType",
                table: "BotAccounts",
                columns: new[] { "Platform", "IdentityType" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogItems_BroadcasterId_NameNormalized",
                table: "CatalogItems",
                columns: new[] { "BroadcasterId", "NameNormalized" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogPurchases_BroadcasterId_BuyerUserId",
                table: "CatalogPurchases",
                columns: new[] { "BroadcasterId", "BuyerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogPurchases_BroadcasterId_CatalogItemId",
                table: "CatalogPurchases",
                columns: new[] { "BroadcasterId", "CatalogItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelActionOverrides_BroadcasterId_ActionDefinitionId",
                table: "ChannelActionOverrides",
                columns: new[] { "BroadcasterId", "ActionDefinitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelBotAuthorization_Broadcaster_BotAccount",
                table: "ChannelBotAuthorizations",
                columns: new[] { "BroadcasterId", "BotAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelBotAuthorizations_BotAccountId",
                table: "ChannelBotAuthorizations",
                column: "BotAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelCommunityStandings_BroadcasterId_UserId",
                table: "ChannelCommunityStandings",
                columns: new[] { "BroadcasterId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelEvent_ChannelId_CreatedAt",
                table: "ChannelEvents",
                columns: new[] { "ChannelId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelEvent_ChannelId_Type",
                table: "ChannelEvents",
                columns: new[] { "ChannelId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelEvents_UserId",
                table: "ChannelEvents",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelFeatures_BroadcasterId_FeatureKey",
                table: "ChannelFeatures",
                columns: new[] { "BroadcasterId", "FeatureKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelMemberships_BroadcasterId_UserId",
                table: "ChannelMemberships",
                columns: new[] { "BroadcasterId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelModerators_UserId",
                table: "ChannelModerators",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Channel_NameNormalized",
                table: "Channels",
                column: "NameNormalized");

            migrationBuilder.CreateIndex(
                name: "IX_Channel_OverlayToken",
                table: "Channels",
                column: "OverlayToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Channel_OwnerUserId",
                table: "Channels",
                column: "OwnerUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Channel_TwitchChannelId",
                table: "Channels",
                column: "TwitchChannelId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelSubscription_BroadcasterId",
                table: "ChannelSubscriptions",
                column: "BroadcasterId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessage_BroadcasterId_CreatedAt",
                table: "ChatMessages",
                columns: new[] { "BroadcasterId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessage_UserId",
                table: "ChatMessages",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_StreamId",
                table: "ChatMessages",
                column: "StreamId");

            migrationBuilder.CreateIndex(
                name: "IX_Command_BroadcasterId_IsEnabled",
                table: "Commands",
                columns: new[] { "BroadcasterId", "IsEnabled" });

            migrationBuilder.CreateIndex(
                name: "IX_Command_Name_BroadcasterId",
                table: "Commands",
                columns: new[] { "Name", "BroadcasterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Configurations_BroadcasterId",
                table: "Configurations",
                column: "BroadcasterId");

            migrationBuilder.CreateIndex(
                name: "IX_CurrencyAccounts_BroadcasterId_ViewerUserId",
                table: "CurrencyAccounts",
                columns: new[] { "BroadcasterId", "ViewerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_CurrencyConfigs_BroadcasterId",
                table: "CurrencyConfigs",
                column: "BroadcasterId");

            migrationBuilder.CreateIndex(
                name: "IX_CurrencyLedgerEntries_BroadcasterId_AccountId_Id",
                table: "CurrencyLedgerEntries",
                columns: new[] { "BroadcasterId", "AccountId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CurrencyLedgerEntries_BroadcasterId_TenantPosition",
                table: "CurrencyLedgerEntries",
                columns: new[] { "BroadcasterId", "TenantPosition" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiscordServerAuthorizations_BroadcasterId",
                table: "DiscordServerAuthorizations",
                column: "BroadcasterId");

            migrationBuilder.CreateIndex(
                name: "IX_EarningRules_BroadcasterId_Source",
                table: "EarningRules",
                columns: new[] { "BroadcasterId", "Source" });

            migrationBuilder.CreateIndex(
                name: "IX_EventJournal_ActorUserId",
                table: "EventJournals",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EventJournal_BroadcasterId",
                table: "EventJournals",
                column: "BroadcasterId");

            migrationBuilder.CreateIndex(
                name: "IX_EventJournal_CorrelationId",
                table: "EventJournals",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_EventJournal_EventId",
                table: "EventJournals",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventJournal_EventType",
                table: "EventJournals",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_EventJournal_OccurredAt",
                table: "EventJournals",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_EventJournal_SubjectKeyId",
                table: "EventJournals",
                column: "SubjectKeyId");

            migrationBuilder.CreateIndex(
                name: "UX_EventJournal_BroadcasterId_StreamPosition",
                table: "EventJournals",
                columns: new[] { "BroadcasterId", "StreamPosition" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventResponses_BroadcasterId",
                table: "EventResponses",
                column: "BroadcasterId");

            migrationBuilder.CreateIndex(
                name: "UX_EventSubConduit_ConduitId",
                table: "EventSubConduits",
                column: "ConduitId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_EventSubConduitShard_ConduitId_ShardId",
                table: "EventSubConduitShards",
                columns: new[] { "ConduitId", "ShardId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventSubSubscription_TwitchSubscriptionId",
                table: "EventSubSubscriptions",
                column: "TwitchSubscriptionId");

            migrationBuilder.CreateIndex(
                name: "UX_EventSubSubscription_Broadcaster_Provider_Type_Version",
                table: "EventSubSubscriptions",
                columns: new[] { "BroadcasterId", "Provider", "EventType", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameConfigs_BroadcasterId_GameType",
                table: "GameConfigs",
                columns: new[] { "BroadcasterId", "GameType" });

            migrationBuilder.CreateIndex(
                name: "IX_GamePlays_BroadcasterId_GameConfigId",
                table: "GamePlays",
                columns: new[] { "BroadcasterId", "GameConfigId" });

            migrationBuilder.CreateIndex(
                name: "IX_GamePlays_BroadcasterId_PlayerUserId",
                table: "GamePlays",
                columns: new[] { "BroadcasterId", "PlayerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_IamAuditLogs_OccurredAt",
                table: "IamAuditLogs",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_IamAuditLogs_PrincipalId",
                table: "IamAuditLogs",
                column: "PrincipalId");

            migrationBuilder.CreateIndex(
                name: "IX_IamPermissions_Key",
                table: "IamPermissions",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IamPrincipals_UserId",
                table: "IamPrincipals",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_IamRoleAssignments_PrincipalId_RoleId",
                table: "IamRoleAssignments",
                columns: new[] { "PrincipalId", "RoleId" });

            migrationBuilder.CreateIndex(
                name: "IX_IamRolePermissions_RoleId_PermissionId",
                table: "IamRolePermissions",
                columns: new[] { "RoleId", "PermissionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IamRoles_Name",
                table: "IamRoles",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyKey_ExpiresAt",
                table: "IdempotencyKeys",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "UX_IdempotencyKey_Scope_Key_Broadcaster",
                table: "IdempotencyKeys",
                columns: new[] { "Scope", "Key", "BroadcasterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationConnection_Broadcaster_Provider_Account",
                table: "IntegrationConnections",
                columns: new[] { "BroadcasterId", "Provider", "ProviderAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationToken_Connection_TokenType",
                table: "IntegrationTokens",
                columns: new[] { "ConnectionId", "TokenType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationTokens_BroadcasterId",
                table: "IntegrationTokens",
                column: "BroadcasterId");

            migrationBuilder.CreateIndex(
                name: "IX_IpcDevModeKeys_CreatedByUserId",
                table: "IpcDevModeKeys",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_IpcDevModeKeys_KeyHash",
                table: "IpcDevModeKeys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Permission_BroadcasterId_Subject_ResourceType",
                table: "Permissions",
                columns: new[] { "BroadcasterId", "SubjectType", "SubjectId", "ResourceType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PermitGrants_BroadcasterId_UserId",
                table: "PermitGrants",
                columns: new[] { "BroadcasterId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Pipelines_BroadcasterId",
                table: "Pipelines",
                column: "BroadcasterId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectionCheckpoint_BroadcasterId",
                table: "ProjectionCheckpoints",
                column: "BroadcasterId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectionCheckpoint_ProjectionName",
                table: "ProjectionCheckpoints",
                column: "ProjectionName");

            migrationBuilder.CreateIndex(
                name: "UX_ProjectionCheckpoint_ProjectionName_BroadcasterId",
                table: "ProjectionCheckpoints",
                columns: new[] { "ProjectionName", "BroadcasterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Record_BroadcasterId_RecordType",
                table: "Records",
                columns: new[] { "BroadcasterId", "RecordType" });

            migrationBuilder.CreateIndex(
                name: "IX_Record_UserId",
                table: "Records",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_SessionId",
                table: "RefreshTokens",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId_RevokedAt",
                table: "RefreshTokens",
                columns: new[] { "UserId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Rewards_BroadcasterId_TwitchRewardId",
                table: "Rewards",
                columns: new[] { "BroadcasterId", "TwitchRewardId" },
                unique: true,
                filter: "\"TwitchRewardId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Service_Name_BroadcasterId",
                table: "Services",
                columns: new[] { "Name", "BroadcasterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Services_BroadcasterId",
                table: "Services",
                column: "BroadcasterId");

            migrationBuilder.CreateIndex(
                name: "IX_Storage_Key_BroadcasterId",
                table: "Storages",
                columns: new[] { "Key", "BroadcasterId" },
                unique: true,
                filter: "\"BroadcasterId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Storages_BroadcasterId",
                table: "Storages",
                column: "BroadcasterId");

            migrationBuilder.CreateIndex(
                name: "IX_Streams_ChannelId",
                table: "Streams",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSequence_BroadcasterId",
                table: "TenantSequences",
                column: "BroadcasterId");

            migrationBuilder.CreateIndex(
                name: "UX_TenantSequence_BroadcasterId_SequenceName",
                table: "TenantSequences",
                columns: new[] { "BroadcasterId", "SequenceName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Timers_BroadcasterId",
                table: "Timers",
                column: "BroadcasterId");

            migrationBuilder.CreateIndex(
                name: "IX_TtsCacheEntry_ContentHash",
                table: "TtsCacheEntries",
                column: "ContentHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_User_TwitchUserId",
                table: "Users",
                column: "TwitchUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_User_UsernameNormalized",
                table: "Users",
                column: "UsernameNormalized");

            migrationBuilder.CreateIndex(
                name: "IX_Users_PronounId",
                table: "Users",
                column: "PronounId");

            migrationBuilder.CreateIndex(
                name: "IX_UserTtsVoice_BroadcasterId_UserId",
                table: "UserTtsVoices",
                columns: new[] { "BroadcasterId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ViewerAgeConsents_BroadcasterId_ViewerUserId",
                table: "ViewerAgeConsents",
                columns: new[] { "BroadcasterId", "ViewerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_WatchStreaks_BroadcasterId",
                table: "WatchStreaks",
                column: "BroadcasterId");

            migrationBuilder.CreateIndex(
                name: "IX_Widgets_BroadcasterId",
                table: "Widgets",
                column: "BroadcasterId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActionDefinitions");

            migrationBuilder.DropTable(
                name: "CatalogItems");

            migrationBuilder.DropTable(
                name: "CatalogPurchases");

            migrationBuilder.DropTable(
                name: "ChannelActionOverrides");

            migrationBuilder.DropTable(
                name: "ChannelBotAuthorizations");

            migrationBuilder.DropTable(
                name: "ChannelCommunityStandings");

            migrationBuilder.DropTable(
                name: "ChannelEvents");

            migrationBuilder.DropTable(
                name: "ChannelFeatures");

            migrationBuilder.DropTable(
                name: "ChannelMemberships");

            migrationBuilder.DropTable(
                name: "ChannelModerators");

            migrationBuilder.DropTable(
                name: "ChannelSubscriptions");

            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "Commands");

            migrationBuilder.DropTable(
                name: "Configurations");

            migrationBuilder.DropTable(
                name: "CurrencyAccounts");

            migrationBuilder.DropTable(
                name: "CurrencyConfigs");

            migrationBuilder.DropTable(
                name: "CurrencyLedgerEntries");

            migrationBuilder.DropTable(
                name: "DeletionAuditLogs");

            migrationBuilder.DropTable(
                name: "DiscordServerAuthorizations");

            migrationBuilder.DropTable(
                name: "EarningRules");

            migrationBuilder.DropTable(
                name: "EventJournals");

            migrationBuilder.DropTable(
                name: "EventResponses");

            migrationBuilder.DropTable(
                name: "EventSubConduitShards");

            migrationBuilder.DropTable(
                name: "EventSubSubscriptions");

            migrationBuilder.DropTable(
                name: "GameConfigs");

            migrationBuilder.DropTable(
                name: "GamePlays");

            migrationBuilder.DropTable(
                name: "IamAuditLogs");

            migrationBuilder.DropTable(
                name: "IamPermissions");

            migrationBuilder.DropTable(
                name: "IamPrincipals");

            migrationBuilder.DropTable(
                name: "IamRoleAssignments");

            migrationBuilder.DropTable(
                name: "IamRolePermissions");

            migrationBuilder.DropTable(
                name: "IamRoles");

            migrationBuilder.DropTable(
                name: "IdempotencyKeys");

            migrationBuilder.DropTable(
                name: "IntegrationTokens");

            migrationBuilder.DropTable(
                name: "IpcDevModeKeys");

            migrationBuilder.DropTable(
                name: "Permissions");

            migrationBuilder.DropTable(
                name: "PermitGrants");

            migrationBuilder.DropTable(
                name: "Pipelines");

            migrationBuilder.DropTable(
                name: "ProjectionCheckpoints");

            migrationBuilder.DropTable(
                name: "Records");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "Rewards");

            migrationBuilder.DropTable(
                name: "Services");

            migrationBuilder.DropTable(
                name: "Storages");

            migrationBuilder.DropTable(
                name: "TenantSequences");

            migrationBuilder.DropTable(
                name: "Timers");

            migrationBuilder.DropTable(
                name: "TtsCacheEntries");

            migrationBuilder.DropTable(
                name: "TtsUsageRecords");

            migrationBuilder.DropTable(
                name: "TtsVoices");

            migrationBuilder.DropTable(
                name: "UserTtsVoices");

            migrationBuilder.DropTable(
                name: "ViewerAgeConsents");

            migrationBuilder.DropTable(
                name: "WatchStreaks");

            migrationBuilder.DropTable(
                name: "Widgets");

            migrationBuilder.DropTable(
                name: "BotAccounts");

            migrationBuilder.DropTable(
                name: "Streams");

            migrationBuilder.DropTable(
                name: "EventSubConduits");

            migrationBuilder.DropTable(
                name: "IntegrationConnections");

            migrationBuilder.DropTable(
                name: "AuthSessions");

            migrationBuilder.DropTable(
                name: "Channels");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Pronouns");
        }
    }
}

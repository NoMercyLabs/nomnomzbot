using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddGuidNocaseCollationSqlite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "PrimaryBroadcasterId",
                table: "YouTubeLiveChatBans",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "YouTubeLiveChatBans",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "YouTubeLiveChatBans",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "YouTubeLiveChatBans",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "WidgetId",
                table: "WidgetVersions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "WidgetVersions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "WidgetVersions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PlatformSourceDefinitionId",
                table: "Widgets",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "GalleryItemId",
                table: "Widgets",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "Widgets",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "Widgets",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ActiveVersionId",
                table: "Widgets",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Widgets",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "GalleryItemId",
                table: "WidgetGallerySubmissionEvents",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ChangedByUserId",
                table: "WidgetGallerySubmissionEvents",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "WidgetGallerySubmissionEvents",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubmitterUserId",
                table: "WidgetGalleryItems",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ReviewedByUserId",
                table: "WidgetGalleryItems",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "WidgetGalleryItems",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "WidgetGalleryItems",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "WatchStreaks",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "WatchStreaks",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerUserId",
                table: "WatchSessions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerProfileId",
                table: "WatchSessions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "WatchSessions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "VtsConnections",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "VtsConnections",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "VtsConnections",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ResolvedByUserId",
                table: "ViewerReports",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ReporterUserId",
                table: "ViewerReports",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ReportedUserId",
                table: "ViewerReports",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ViewerReports",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ViewerReports",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ViewerReports",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerUserId",
                table: "ViewerProfiles",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ViewerProfiles",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ViewerProfiles",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ViewerProfiles",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerUserId",
                table: "ViewerEngagementStates",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ViewerEngagementStates",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ViewerEngagementStates",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ViewerEngagementStates",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerUserId",
                table: "ViewerEngagementDailies",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerProfileId",
                table: "ViewerEngagementDailies",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ViewerEngagementDailies",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerUserId",
                table: "ViewerData",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ViewerData",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ViewerData",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ViewerData",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerUserId",
                table: "ViewerAgeConsents",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ViewerAgeConsents",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ConsentRecordId",
                table: "ViewerAgeConsents",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ViewerAgeConsents",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ViewerAgeConsents",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "UserTtsVoices",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubjectUserId",
                table: "UserTrustScores",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "UserTrustScores",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubjectKeyId",
                table: "Users",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Users",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubjectUserId",
                table: "UserModerationHistories",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "UserModerationHistories",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "UserIdentities",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "UserIdentities",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ConnectionId",
                table: "UserIdentities",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "UserIdentities",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "UsageRecords",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "StreamId",
                table: "TtsUsageRecords",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "TtsUsageRecords",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "TtsLexiconEntries",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "TtsLexiconEntries",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "TtsLexiconEntries",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubjectKeyId",
                table: "TtsConfigs",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "TtsConfigs",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "TtsConfigs",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "TtsConfigs",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "StreamId",
                table: "TtsApprovalQueueEntries",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ReviewedByUserId",
                table: "TtsApprovalQueueEntries",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "RequestedByUserId",
                table: "TtsApprovalQueueEntries",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "TtsApprovalQueueEntries",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "TtsApprovalQueueEntries",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "TtsApprovalQueueEntries",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "TrustPolicies",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "TrustPolicies",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "TrustPolicies",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PipelineId",
                table: "Timers",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "Timers",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "Timers",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Timers",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "TierId",
                table: "TierLimits",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "TierLimits",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "TierLimits",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "TenantSequences",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "TenantSequences",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "GrantedByPrincipalId",
                table: "TenantLimitOverrides",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "TenantLimitOverrides",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "TenantLimitOverrides",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "TenantLimitOverrides",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SupporterUserId",
                table: "SupporterEvents",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "SupporterEvents",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "SupporterEvents",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SupporterEvents",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "IntegrationConnectionId",
                table: "SupporterConnections",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "InboundWebhookEndpointId",
                table: "SupporterConnections",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "SupporterConnections",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "SupporterConnections",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SupporterConnections",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "TierId",
                table: "Subscriptions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubjectKeyId",
                table: "Subscriptions",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "Subscriptions",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "Subscriptions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Subscriptions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ChannelId",
                table: "Streams",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldMaxLength: 50
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "Storages",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "SpamSignatures",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SpamSignatures",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "SpamDetections",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "SpamDetections",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SpamDetections",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "SpamDefensePolicies",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "SpamDefensePolicies",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SpamDefensePolicies",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "SpamCampaigns",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "SpamCampaigns",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SpamCampaigns",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "SoundClips",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByUserId",
                table: "SoundClips",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "SoundClips",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SoundClips",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "RequesterUserId",
                table: "SongRequestQueueItems",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SongRequestQueueItems",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ShoutoutOverrides",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ShoutoutOverrides",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ShoutoutOverrides",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "TrustedChannelId",
                table: "SharedBanTrustedChannels",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "SharedBanTrustedChannels",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "AddedByUserId",
                table: "SharedBanTrustedChannels",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SharedBanTrustedChannels",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "SharedBanSettings",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SharedBanSettings",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "Services",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "TargetUserId",
                table: "SecurityNotices",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "SecurityNotices",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "SecurityNotices",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ActorPrincipalId",
                table: "SecurityNotices",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "AcknowledgedByUserId",
                table: "SecurityNotices",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "AccessGrantId",
                table: "SecurityNotices",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SecurityNotices",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PipelineId",
                table: "ScheduledPipelineTasks",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ScheduledPipelineTasks",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ScheduledPipelineTasks",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "OwnerBroadcasterId",
                table: "SavingsJars",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "SavingsJars",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SavingsJars",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "MemberBroadcasterId",
                table: "SavingsJarMemberships",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "JarId",
                table: "SavingsJarMemberships",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "InvitedByBroadcasterId",
                table: "SavingsJarMemberships",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "SavingsJarMemberships",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SavingsJarMemberships",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PipelineId",
                table: "Rewards",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "Rewards",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "Rewards",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Rewards",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "RenderedAlertCaptures",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "RenderedAlertCaptures",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "RefreshTokens",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SessionId",
                table: "RefreshTokens",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "RefreshTokens",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "RedemptionTimers",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "RedemptionTimers",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "Redemptions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "Records",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "Records",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "Quotes",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByUserId",
                table: "Quotes",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "Quotes",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Quotes",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ProjectionCheckpoints",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PublishedByPrincipalId",
                table: "PlatformContentVersions",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DraftedByPrincipalId",
                table: "PlatformContentVersions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DefinitionId",
                table: "PlatformContentVersions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "PlatformContentVersions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "RequestedByPrincipalId",
                table: "PlatformContentPublishJobs",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DefinitionId",
                table: "PlatformContentPublishJobs",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "PlatformContentPublishJobs",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "LatestDraftVersionId",
                table: "PlatformContentDefinitions",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CurrentVersionId",
                table: "PlatformContentDefinitions",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByPrincipalId",
                table: "PlatformContentDefinitions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "PlatformContentDefinitions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "PlatformConnections",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ChannelId",
                table: "PlatformConnections",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "PlatformConnections",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PipelineId",
                table: "PipelineTriggers",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "PipelineTriggers",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "PipelineTriggers",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PipelineId",
                table: "PipelineSteps",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ParentStepId",
                table: "PipelineSteps",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CodeScriptId",
                table: "PipelineSteps",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "PipelineSteps",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "PipelineSteps",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PipelineStepId",
                table: "PipelineStepConditions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ParentConditionId",
                table: "PipelineStepConditions",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "PipelineStepConditions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "PipelineStepConditions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PlatformSourceDefinitionId",
                table: "Pipelines",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "Pipelines",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "Pipelines",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Pipelines",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "TriggeredByUserId",
                table: "PipelineRunStates",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SuspendedAtStepId",
                table: "PipelineRunStates",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PipelineId",
                table: "PipelineRunStates",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "PipelineRunStates",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "PipelineRunStates",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "TriggeredByUserId",
                table: "PipelineExecutions",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PipelineId",
                table: "PipelineExecutions",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "PipelineExecutions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "PickLists",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "PickLists",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "PickLists",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "PermitGrants",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "GrantedByUserId",
                table: "PermitGrants",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "PermitGrants",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "PermitGrants",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ActionDefinitionId",
                table: "PermitGrants",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "PermitGrants",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "Permissions",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "Permissions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "HttpEgressAllowlistId",
                table: "OutboundWebhookEndpoints",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "EncryptionKeyId",
                table: "OutboundWebhookEndpoints",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "OutboundWebhookEndpoints",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "OutboundWebhookEndpoints",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "OutboundWebhookEndpoints",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "WebhookMessageId",
                table: "OutboundWebhookDeliveries",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "JournalEventId",
                table: "OutboundWebhookDeliveries",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "EndpointId",
                table: "OutboundWebhookDeliveries",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "OutboundWebhookDeliveries",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ObsConnections",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ObsConnections",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ObsConnections",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "TargetUserId",
                table: "NetworkNukeBatches",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "RevertedByUserId",
                table: "NetworkNukeBatches",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "OriginBroadcasterId",
                table: "NetworkNukeBatches",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "InitiatedByUserId",
                table: "NetworkNukeBatches",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "NetworkNukeBatches",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "NetworkNukeBatches",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "NamedCounters",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "NamedCounters",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "NamedCounters",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "TargetUserId",
                table: "ModerationQueueItems",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ResolvedByUserId",
                table: "ModerationQueueItems",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ModerationQueueItems",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ModerationQueueItems",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ModerationQueueItems",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubjectUserId",
                table: "ModerationEscalationStates",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ModerationEscalationStates",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ModerationEscalationStates",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ModerationEscalationPolicies",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ModerationEscalationPolicies",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ModerationEscalationPolicies",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerUserId",
                table: "MessageActivityDailies",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerProfileId",
                table: "MessageActivityDailies",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "MessageActivityDailies",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "RequesterUserId",
                table: "MediaShareRequests",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "MediaShareRequests",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DecidedByUserId",
                table: "MediaShareRequests",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "MediaShareRequests",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "MediaShareRequests",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "MediaShareConfigs",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "MediaShareConfigs",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "MediaShareConfigs",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubjectUserId",
                table: "LeaderboardSnapshots",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubjectAccountId",
                table: "LeaderboardSnapshots",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "LeaderboardConfigId",
                table: "LeaderboardSnapshots",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "LeaderboardSnapshots",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerUserId",
                table: "LeaderboardOptOuts",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "LeaderboardOptOuts",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "LeaderboardOptOuts",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "LeaderboardOptOuts",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "JarId",
                table: "LeaderboardConfigs",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "LeaderboardConfigs",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "LeaderboardConfigs",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "LeaderboardConfigs",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CryptoKeyId",
                table: "KeyUsageBindings",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "KeyUsageBindings",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SourceBroadcasterId",
                table: "JarContributions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "JarId",
                table: "JarContributions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ContributorUserId",
                table: "JarContributions",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ContributorAccountId",
                table: "JarContributions",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ActorUserId",
                table: "JarContributions",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "IpcDevModeKeys",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByUserId",
                table: "IpcDevModeKeys",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "IpcDevModeKeys",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubscriptionId",
                table: "Invoices",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "Invoices",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "Invoices",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Invoices",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "GrantsTierId",
                table: "InviteCodes",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "InviteCodes",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "InviteCodes",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "EncryptionKeyId",
                table: "IntegrationTokens",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "IntegrationTokens",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ConnectionId",
                table: "IntegrationTokens",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "IntegrationTokens",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "IntegrationTokens",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "IntegrationConnections",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ConnectedByUserId",
                table: "IntegrationConnections",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "IntegrationConnections",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "IntegrationConnections",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "InstalledByUserId",
                table: "InstalledBundles",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "InstalledBundles",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "InstalledBundles",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "InstalledBundles",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "TargetPipelineId",
                table: "InboundWebhookEndpoints",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "EncryptionKeyId",
                table: "InboundWebhookEndpoints",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "InboundWebhookEndpoints",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "InboundWebhookEndpoints",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "InboundWebhookEndpoints",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "IdempotencyKeys",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "IamRoles",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "IamRoles",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "RoleId",
                table: "IamRolePermissions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PermissionId",
                table: "IamRolePermissions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "IamRolePermissions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ScopeChannelId",
                table: "IamRoleAssignments",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "RoleId",
                table: "IamRoleAssignments",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PrincipalId",
                table: "IamRoleAssignments",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "AssignedByPrincipalId",
                table: "IamRoleAssignments",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "IamRoleAssignments",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "IamPrincipals",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubjectKeyId",
                table: "IamPrincipals",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "IamPrincipals",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "IamPrincipals",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "IamPermissions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "TargetPrincipalId",
                table: "IamAuditLogs",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "TargetBroadcasterId",
                table: "IamAuditLogs",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "RoleId",
                table: "IamAuditLogs",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PublishJobId",
                table: "IamAuditLogs",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PrincipalId",
                table: "IamAuditLogs",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "HttpEgressAllowlists",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "HttpEgressAllowlists",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ApprovedByUserId",
                table: "HttpEgressAllowlists",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "HttpEgressAllowlists",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerUserId",
                table: "GiveawayWinners",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "GiveawayId",
                table: "GiveawayWinners",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "GiveawayWinners",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "AssignedCodeId",
                table: "GiveawayWinners",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "GiveawayWinners",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PrizePipelineId",
                table: "Giveaways",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PrizeCodePoolId",
                table: "Giveaways",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "Giveaways",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "Giveaways",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Giveaways",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerUserId",
                table: "GiveawayEntries",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "GiveawayId",
                table: "GiveawayEntries",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "GiveawayEntries",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "GiveawayEntries",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "GiveawayEntries",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "GiveawayCodes",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CodePoolId",
                table: "GiveawayCodes",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "GiveawayCodes",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "AssignedWinnerId",
                table: "GiveawayCodes",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "GiveawayCodes",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "GiveawayCodePools",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "GiveawayCodePools",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "GiveawayCodePools",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "StartedByUserId",
                table: "GameSessions",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "GameConfigId",
                table: "GameSessions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "GameSessions",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "GameSessions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "GameSessions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PlayerUserId",
                table: "GamePlays",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PlayerAccountId",
                table: "GamePlays",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "GameSessionId",
                table: "GamePlays",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "GameConfigId",
                table: "GamePlays",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "GamePlays",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "GameConfigs",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "GameConfigs",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "GameConfigs",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "FoundersBadges",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "FoundersBadges",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "FollowBotBlocks",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "FollowBotBlocks",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BatchId",
                table: "FollowBotBlocks",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "FollowBotBlocks",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "FederationPeers",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "FederationPeers",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PeerId",
                table: "FederationPeerKeys",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "FederationPeerKeys",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "MinTierId",
                table: "FeatureFlags",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "FeatureFlags",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "FeatureFlagId",
                table: "FeatureFlagOverrides",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "FeatureFlagOverrides",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "FeatureFlagOverrides",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "EventSubSubscriptions",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "EventSubSubscriptions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "EventSubSubscriptions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubjectKeyId",
                table: "EventSubjectKeys",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "EventId",
                table: "EventSubjectKeys",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "EventSubjectKeys",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "EventSubjectKeys",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ConduitId",
                table: "EventSubConduitShards",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "EventSubConduitShards",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "EventSubConduits",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PipelineId",
                table: "EventResponses",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "EventResponses",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "EventResponses",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "EventResponses",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubjectKeyId",
                table: "EventJournals",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "OnBehalfOfUserId",
                table: "EventJournals",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ImpersonationSessionId",
                table: "EventJournals",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "EventId",
                table: "EventJournals",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CorrelationId",
                table: "EventJournals",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CausationId",
                table: "EventJournals",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "EventJournals",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ActorUserId",
                table: "EventJournals",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubjectUserId",
                table: "ErasureRequests",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubjectKeyId",
                table: "ErasureRequests",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ErasureRequests",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ErasureRequests",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "IssuedByAdminId",
                table: "EntitlementGrants",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "GrantedTierId",
                table: "EntitlementGrants",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "EntitlementGrants",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "EntitlementGrants",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "EntitlementGrants",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "EngagementConfigs",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "EngagementConfigs",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "EngagementConfigs",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "EarningRules",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "EarningRules",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "EarningRules",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "GuildConnectionId",
                table: "DiscordNotificationRoles",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "DiscordNotificationRoles",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "DiscordNotificationRoles",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "DiscordNotificationRoles",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "StreamId",
                table: "DiscordNotificationDispatches",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "NotificationConfigId",
                table: "DiscordNotificationDispatches",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "DiscordNotificationDispatches",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "DiscordNotificationDispatches",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PingRoleId",
                table: "DiscordNotificationConfigs",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "GuildConnectionId",
                table: "DiscordNotificationConfigs",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "DiscordNotificationConfigs",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "DiscordNotificationConfigs",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "DiscordNotificationConfigs",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "NotificationRoleId",
                table: "DiscordMemberOptIns",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "DiscordMemberOptIns",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "DiscordMemberOptIns",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "DiscordMemberOptIns",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "GuildConnectionId",
                table: "DiscordLiveRoleConfigs",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "DiscordLiveRoleConfigs",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "DiscordLiveRoleConfigs",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "DiscordLiveRoleConfigs",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "DiscordGuildConnections",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "DiscordGuildConnections",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "DiscordGuildConnections",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "InstanceId",
                table: "DeploymentProfiles",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "DeploymentProfiles",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "InboundWebhookEndpointId",
                table: "CustomDataSources",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "CustomDataSources",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByUserId",
                table: "CustomDataSources",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "CustomDataSources",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "CustomDataSources",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerUserId",
                table: "CurrencyLedgerEntries",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SourceId",
                table: "CurrencyLedgerEntries",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "EventId",
                table: "CurrencyLedgerEntries",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "CurrencyLedgerEntries",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ActorUserId",
                table: "CurrencyLedgerEntries",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "AccountId",
                table: "CurrencyLedgerEntries",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "CurrencyConfigs",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "CurrencyConfigs",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "CurrencyConfigs",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerUserId",
                table: "CurrencyAccounts",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "CurrencyAccounts",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "CurrencyAccounts",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "CurrencyAccounts",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "RotatedFromKeyId",
                table: "CryptoKeys",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ErasureRequestId",
                table: "CryptoKeys",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "CryptoKeys",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "CryptoKeys",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubjectUserId",
                table: "ConsentRecords",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubjectKeyId",
                table: "ConsentRecords",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ConsentRecords",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ConsentRecords",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ConsentRecords",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "Configurations",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ErasureRequestId",
                table: "ComplianceAuditLogs",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ComplianceAuditLogs",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerUserId",
                table: "CommandUsages",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerProfileId",
                table: "CommandUsages",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CommandId",
                table: "CommandUsages",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "CommandUsages",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PipelineId",
                table: "Commands",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "Commands",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "Commands",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Commands",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "CommandCooldownStates",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CommandId",
                table: "CommandCooldownStates",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "CommandCooldownStates",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "CodeScriptVersions",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CodeScriptId",
                table: "CodeScriptVersions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "CodeScriptVersions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "AuthorUserId",
                table: "CodeScriptVersions",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "CodeScriptVersions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "CodeScripts",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CurrentVersionId",
                table: "CodeScripts",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "CodeScripts",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "AuthorUserId",
                table: "CodeScripts",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "CodeScripts",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PipelineId",
                table: "ChatTriggers",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ChatTriggers",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChatTriggers",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ChatTriggers",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PollId",
                table: "ChatPollVotes",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChatPollVotes",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ChatPollVotes",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChatPolls",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ChatPolls",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ChatMessages",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChatMessages",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ChatFilters",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChatFilters",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ChatFilters",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChannelSubscriptions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "OwnerUserId",
                table: "Channels",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "Channels",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Channels",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "GrantedByUserId",
                table: "ChannelModerators",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ChannelModerators",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "ChannelModerators",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ChannelId",
                table: "ChannelModerators",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByUserId",
                table: "ChannelModerationStandings",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChannelModerationStandings",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ChannelModerationStandings",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChannelMissingScopes",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ChannelMissingScopes",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "ChannelMemberships",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "GrantedByUserId",
                table: "ChannelMemberships",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ChannelMemberships",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChannelMemberships",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ChannelMemberships",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PeerId",
                table: "ChannelFederationOptIns",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "EnabledByUserId",
                table: "ChannelFederationOptIns",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ChannelFederationOptIns",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChannelFederationOptIns",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ChannelFederationOptIns",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChannelFeatures",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "ChannelEvents",
                type: "TEXT",
                maxLength: 50,
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldMaxLength: 50,
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ChannelId",
                table: "ChannelEvents",
                type: "TEXT",
                maxLength: 50,
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldMaxLength: 50,
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "ChannelCommunityStandings",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChannelCommunityStandings",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ChannelCommunityStandings",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChannelChatterDays",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PlatformSourceDefinitionId",
                table: "ChannelBuiltinCommands",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ChannelBuiltinCommands",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChannelBuiltinCommands",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ChannelBuiltinCommands",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ChannelBotAuthorizations",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChannelBotAuthorizations",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BotAccountId",
                table: "ChannelBotAuthorizations",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "AuthorizedByUserId",
                table: "ChannelBotAuthorizations",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ChannelBotAuthorizations",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ChannelAssets",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByUserId",
                table: "ChannelAssets",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChannelAssets",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ChannelAssets",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChannelAnalyticsDailies",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SetByUserId",
                table: "ChannelActionOverrides",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ChannelActionOverrides",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChannelActionOverrides",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ActionDefinitionId",
                table: "ChannelActionOverrides",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ChannelActionOverrides",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CatalogItemId",
                table: "CatalogPurchases",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BuyerUserId",
                table: "CatalogPurchases",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BuyerAccountId",
                table: "CatalogPurchases",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "CatalogPurchases",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PipelineId",
                table: "CatalogItems",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "CatalogItems",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "CatalogItems",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "CatalogItems",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "BotAccounts",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ConnectionId",
                table: "BotAccounts",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "BotAccounts",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "BlockedTracks",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "BlockedTracks",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "BlockedTracks",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "BillingTiers",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "BillingTiers",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "AutomationApiTokens",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByUserId",
                table: "AutomationApiTokens",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "AutomationApiTokens",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "AutomationApiTokens",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "AuthSessions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "AuthSessions",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "AuthSessions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DismissedByUserId",
                table: "ActionRequiredDismissals",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ActionRequiredDismissals",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ChannelId",
                table: "ActionRequiredDismissals",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ActionRequiredDismissals",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ActionDefinitions",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "PrimaryBroadcasterId",
                table: "YouTubeLiveChatBans",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "YouTubeLiveChatBans",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "YouTubeLiveChatBans",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "YouTubeLiveChatBans",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "WidgetId",
                table: "WidgetVersions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "WidgetVersions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "WidgetVersions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PlatformSourceDefinitionId",
                table: "Widgets",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "GalleryItemId",
                table: "Widgets",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "Widgets",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "Widgets",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ActiveVersionId",
                table: "Widgets",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Widgets",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "GalleryItemId",
                table: "WidgetGallerySubmissionEvents",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ChangedByUserId",
                table: "WidgetGallerySubmissionEvents",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "WidgetGallerySubmissionEvents",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubmitterUserId",
                table: "WidgetGalleryItems",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ReviewedByUserId",
                table: "WidgetGalleryItems",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "WidgetGalleryItems",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "WidgetGalleryItems",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "WatchStreaks",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "WatchStreaks",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerUserId",
                table: "WatchSessions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerProfileId",
                table: "WatchSessions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "WatchSessions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "VtsConnections",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "VtsConnections",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "VtsConnections",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ResolvedByUserId",
                table: "ViewerReports",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ReporterUserId",
                table: "ViewerReports",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ReportedUserId",
                table: "ViewerReports",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ViewerReports",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ViewerReports",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ViewerReports",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerUserId",
                table: "ViewerProfiles",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ViewerProfiles",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ViewerProfiles",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ViewerProfiles",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerUserId",
                table: "ViewerEngagementStates",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ViewerEngagementStates",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ViewerEngagementStates",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ViewerEngagementStates",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerUserId",
                table: "ViewerEngagementDailies",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerProfileId",
                table: "ViewerEngagementDailies",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ViewerEngagementDailies",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerUserId",
                table: "ViewerData",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ViewerData",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ViewerData",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ViewerData",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerUserId",
                table: "ViewerAgeConsents",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ViewerAgeConsents",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ConsentRecordId",
                table: "ViewerAgeConsents",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ViewerAgeConsents",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ViewerAgeConsents",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "UserTtsVoices",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubjectUserId",
                table: "UserTrustScores",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "UserTrustScores",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubjectKeyId",
                table: "Users",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Users",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubjectUserId",
                table: "UserModerationHistories",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "UserModerationHistories",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "UserIdentities",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "UserIdentities",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ConnectionId",
                table: "UserIdentities",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "UserIdentities",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "UsageRecords",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "StreamId",
                table: "TtsUsageRecords",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "TtsUsageRecords",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "TtsLexiconEntries",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "TtsLexiconEntries",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "TtsLexiconEntries",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubjectKeyId",
                table: "TtsConfigs",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "TtsConfigs",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "TtsConfigs",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "TtsConfigs",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "StreamId",
                table: "TtsApprovalQueueEntries",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ReviewedByUserId",
                table: "TtsApprovalQueueEntries",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "RequestedByUserId",
                table: "TtsApprovalQueueEntries",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "TtsApprovalQueueEntries",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "TtsApprovalQueueEntries",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "TtsApprovalQueueEntries",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "TrustPolicies",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "TrustPolicies",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "TrustPolicies",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PipelineId",
                table: "Timers",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "Timers",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "Timers",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Timers",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "TierId",
                table: "TierLimits",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "TierLimits",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "TierLimits",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "TenantSequences",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "TenantSequences",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "GrantedByPrincipalId",
                table: "TenantLimitOverrides",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "TenantLimitOverrides",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "TenantLimitOverrides",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "TenantLimitOverrides",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SupporterUserId",
                table: "SupporterEvents",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "SupporterEvents",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "SupporterEvents",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SupporterEvents",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "IntegrationConnectionId",
                table: "SupporterConnections",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "InboundWebhookEndpointId",
                table: "SupporterConnections",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "SupporterConnections",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "SupporterConnections",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SupporterConnections",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "TierId",
                table: "Subscriptions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubjectKeyId",
                table: "Subscriptions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "Subscriptions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "Subscriptions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Subscriptions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ChannelId",
                table: "Streams",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldMaxLength: 50,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "Storages",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "SpamSignatures",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SpamSignatures",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "SpamDetections",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "SpamDetections",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SpamDetections",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "SpamDefensePolicies",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "SpamDefensePolicies",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SpamDefensePolicies",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "SpamCampaigns",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "SpamCampaigns",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SpamCampaigns",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "SoundClips",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByUserId",
                table: "SoundClips",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "SoundClips",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SoundClips",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "RequesterUserId",
                table: "SongRequestQueueItems",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SongRequestQueueItems",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ShoutoutOverrides",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ShoutoutOverrides",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ShoutoutOverrides",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "TrustedChannelId",
                table: "SharedBanTrustedChannels",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "SharedBanTrustedChannels",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "AddedByUserId",
                table: "SharedBanTrustedChannels",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SharedBanTrustedChannels",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "SharedBanSettings",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SharedBanSettings",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "Services",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "TargetUserId",
                table: "SecurityNotices",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "SecurityNotices",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "SecurityNotices",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ActorPrincipalId",
                table: "SecurityNotices",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "AcknowledgedByUserId",
                table: "SecurityNotices",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "AccessGrantId",
                table: "SecurityNotices",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SecurityNotices",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PipelineId",
                table: "ScheduledPipelineTasks",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ScheduledPipelineTasks",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ScheduledPipelineTasks",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "OwnerBroadcasterId",
                table: "SavingsJars",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "SavingsJars",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SavingsJars",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "MemberBroadcasterId",
                table: "SavingsJarMemberships",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "JarId",
                table: "SavingsJarMemberships",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "InvitedByBroadcasterId",
                table: "SavingsJarMemberships",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "SavingsJarMemberships",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SavingsJarMemberships",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PipelineId",
                table: "Rewards",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "Rewards",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "Rewards",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Rewards",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "RenderedAlertCaptures",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "RenderedAlertCaptures",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "RefreshTokens",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SessionId",
                table: "RefreshTokens",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "RefreshTokens",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "RedemptionTimers",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "RedemptionTimers",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "Redemptions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "Records",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "Records",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "Quotes",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByUserId",
                table: "Quotes",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "Quotes",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Quotes",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ProjectionCheckpoints",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PublishedByPrincipalId",
                table: "PlatformContentVersions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DraftedByPrincipalId",
                table: "PlatformContentVersions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DefinitionId",
                table: "PlatformContentVersions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "PlatformContentVersions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "RequestedByPrincipalId",
                table: "PlatformContentPublishJobs",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DefinitionId",
                table: "PlatformContentPublishJobs",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "PlatformContentPublishJobs",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "LatestDraftVersionId",
                table: "PlatformContentDefinitions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CurrentVersionId",
                table: "PlatformContentDefinitions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByPrincipalId",
                table: "PlatformContentDefinitions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "PlatformContentDefinitions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "PlatformConnections",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ChannelId",
                table: "PlatformConnections",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "PlatformConnections",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PipelineId",
                table: "PipelineTriggers",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "PipelineTriggers",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "PipelineTriggers",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PipelineId",
                table: "PipelineSteps",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ParentStepId",
                table: "PipelineSteps",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CodeScriptId",
                table: "PipelineSteps",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "PipelineSteps",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "PipelineSteps",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PipelineStepId",
                table: "PipelineStepConditions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ParentConditionId",
                table: "PipelineStepConditions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "PipelineStepConditions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "PipelineStepConditions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PlatformSourceDefinitionId",
                table: "Pipelines",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "Pipelines",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "Pipelines",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Pipelines",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "TriggeredByUserId",
                table: "PipelineRunStates",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SuspendedAtStepId",
                table: "PipelineRunStates",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PipelineId",
                table: "PipelineRunStates",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "PipelineRunStates",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "PipelineRunStates",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "TriggeredByUserId",
                table: "PipelineExecutions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PipelineId",
                table: "PipelineExecutions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "PipelineExecutions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "PickLists",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "PickLists",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "PickLists",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "PermitGrants",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "GrantedByUserId",
                table: "PermitGrants",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "PermitGrants",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "PermitGrants",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ActionDefinitionId",
                table: "PermitGrants",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "PermitGrants",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "Permissions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "Permissions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "HttpEgressAllowlistId",
                table: "OutboundWebhookEndpoints",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "EncryptionKeyId",
                table: "OutboundWebhookEndpoints",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "OutboundWebhookEndpoints",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "OutboundWebhookEndpoints",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "OutboundWebhookEndpoints",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "WebhookMessageId",
                table: "OutboundWebhookDeliveries",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "JournalEventId",
                table: "OutboundWebhookDeliveries",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "EndpointId",
                table: "OutboundWebhookDeliveries",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "OutboundWebhookDeliveries",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ObsConnections",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ObsConnections",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ObsConnections",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "TargetUserId",
                table: "NetworkNukeBatches",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "RevertedByUserId",
                table: "NetworkNukeBatches",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "OriginBroadcasterId",
                table: "NetworkNukeBatches",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "InitiatedByUserId",
                table: "NetworkNukeBatches",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "NetworkNukeBatches",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "NetworkNukeBatches",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "NamedCounters",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "NamedCounters",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "NamedCounters",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "TargetUserId",
                table: "ModerationQueueItems",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ResolvedByUserId",
                table: "ModerationQueueItems",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ModerationQueueItems",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ModerationQueueItems",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ModerationQueueItems",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubjectUserId",
                table: "ModerationEscalationStates",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ModerationEscalationStates",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ModerationEscalationStates",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ModerationEscalationPolicies",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ModerationEscalationPolicies",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ModerationEscalationPolicies",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerUserId",
                table: "MessageActivityDailies",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerProfileId",
                table: "MessageActivityDailies",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "MessageActivityDailies",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "RequesterUserId",
                table: "MediaShareRequests",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "MediaShareRequests",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DecidedByUserId",
                table: "MediaShareRequests",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "MediaShareRequests",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "MediaShareRequests",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "MediaShareConfigs",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "MediaShareConfigs",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "MediaShareConfigs",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubjectUserId",
                table: "LeaderboardSnapshots",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubjectAccountId",
                table: "LeaderboardSnapshots",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "LeaderboardConfigId",
                table: "LeaderboardSnapshots",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "LeaderboardSnapshots",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerUserId",
                table: "LeaderboardOptOuts",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "LeaderboardOptOuts",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "LeaderboardOptOuts",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "LeaderboardOptOuts",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "JarId",
                table: "LeaderboardConfigs",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "LeaderboardConfigs",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "LeaderboardConfigs",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "LeaderboardConfigs",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CryptoKeyId",
                table: "KeyUsageBindings",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "KeyUsageBindings",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SourceBroadcasterId",
                table: "JarContributions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "JarId",
                table: "JarContributions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ContributorUserId",
                table: "JarContributions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ContributorAccountId",
                table: "JarContributions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ActorUserId",
                table: "JarContributions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "IpcDevModeKeys",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByUserId",
                table: "IpcDevModeKeys",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "IpcDevModeKeys",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubscriptionId",
                table: "Invoices",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "Invoices",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "Invoices",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Invoices",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "GrantsTierId",
                table: "InviteCodes",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "InviteCodes",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "InviteCodes",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "EncryptionKeyId",
                table: "IntegrationTokens",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "IntegrationTokens",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ConnectionId",
                table: "IntegrationTokens",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "IntegrationTokens",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "IntegrationTokens",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "IntegrationConnections",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ConnectedByUserId",
                table: "IntegrationConnections",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "IntegrationConnections",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "IntegrationConnections",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "InstalledByUserId",
                table: "InstalledBundles",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "InstalledBundles",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "InstalledBundles",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "InstalledBundles",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "TargetPipelineId",
                table: "InboundWebhookEndpoints",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "EncryptionKeyId",
                table: "InboundWebhookEndpoints",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "InboundWebhookEndpoints",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "InboundWebhookEndpoints",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "InboundWebhookEndpoints",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "IdempotencyKeys",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "IamRoles",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "IamRoles",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "RoleId",
                table: "IamRolePermissions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PermissionId",
                table: "IamRolePermissions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "IamRolePermissions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ScopeChannelId",
                table: "IamRoleAssignments",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "RoleId",
                table: "IamRoleAssignments",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PrincipalId",
                table: "IamRoleAssignments",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "AssignedByPrincipalId",
                table: "IamRoleAssignments",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "IamRoleAssignments",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "IamPrincipals",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubjectKeyId",
                table: "IamPrincipals",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "IamPrincipals",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "IamPrincipals",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "IamPermissions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "TargetPrincipalId",
                table: "IamAuditLogs",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "TargetBroadcasterId",
                table: "IamAuditLogs",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "RoleId",
                table: "IamAuditLogs",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PublishJobId",
                table: "IamAuditLogs",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PrincipalId",
                table: "IamAuditLogs",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "HttpEgressAllowlists",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "HttpEgressAllowlists",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ApprovedByUserId",
                table: "HttpEgressAllowlists",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "HttpEgressAllowlists",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerUserId",
                table: "GiveawayWinners",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "GiveawayId",
                table: "GiveawayWinners",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "GiveawayWinners",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "AssignedCodeId",
                table: "GiveawayWinners",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "GiveawayWinners",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PrizePipelineId",
                table: "Giveaways",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PrizeCodePoolId",
                table: "Giveaways",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "Giveaways",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "Giveaways",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Giveaways",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerUserId",
                table: "GiveawayEntries",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "GiveawayId",
                table: "GiveawayEntries",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "GiveawayEntries",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "GiveawayEntries",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "GiveawayEntries",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "GiveawayCodes",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CodePoolId",
                table: "GiveawayCodes",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "GiveawayCodes",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "AssignedWinnerId",
                table: "GiveawayCodes",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "GiveawayCodes",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "GiveawayCodePools",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "GiveawayCodePools",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "GiveawayCodePools",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "StartedByUserId",
                table: "GameSessions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "GameConfigId",
                table: "GameSessions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "GameSessions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "GameSessions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "GameSessions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PlayerUserId",
                table: "GamePlays",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PlayerAccountId",
                table: "GamePlays",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "GameSessionId",
                table: "GamePlays",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "GameConfigId",
                table: "GamePlays",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "GamePlays",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "GameConfigs",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "GameConfigs",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "GameConfigs",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "FoundersBadges",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "FoundersBadges",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "FollowBotBlocks",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "FollowBotBlocks",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BatchId",
                table: "FollowBotBlocks",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "FollowBotBlocks",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "FederationPeers",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "FederationPeers",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PeerId",
                table: "FederationPeerKeys",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "FederationPeerKeys",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "MinTierId",
                table: "FeatureFlags",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "FeatureFlags",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "FeatureFlagId",
                table: "FeatureFlagOverrides",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "FeatureFlagOverrides",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "FeatureFlagOverrides",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "EventSubSubscriptions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "EventSubSubscriptions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "EventSubSubscriptions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubjectKeyId",
                table: "EventSubjectKeys",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "EventId",
                table: "EventSubjectKeys",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "EventSubjectKeys",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "EventSubjectKeys",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ConduitId",
                table: "EventSubConduitShards",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "EventSubConduitShards",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "EventSubConduits",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PipelineId",
                table: "EventResponses",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "EventResponses",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "EventResponses",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "EventResponses",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubjectKeyId",
                table: "EventJournals",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "OnBehalfOfUserId",
                table: "EventJournals",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ImpersonationSessionId",
                table: "EventJournals",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "EventId",
                table: "EventJournals",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CorrelationId",
                table: "EventJournals",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CausationId",
                table: "EventJournals",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "EventJournals",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ActorUserId",
                table: "EventJournals",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubjectUserId",
                table: "ErasureRequests",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubjectKeyId",
                table: "ErasureRequests",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ErasureRequests",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ErasureRequests",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "IssuedByAdminId",
                table: "EntitlementGrants",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "GrantedTierId",
                table: "EntitlementGrants",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "EntitlementGrants",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "EntitlementGrants",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "EntitlementGrants",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "EngagementConfigs",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "EngagementConfigs",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "EngagementConfigs",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "EarningRules",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "EarningRules",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "EarningRules",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "GuildConnectionId",
                table: "DiscordNotificationRoles",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "DiscordNotificationRoles",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "DiscordNotificationRoles",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "DiscordNotificationRoles",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "StreamId",
                table: "DiscordNotificationDispatches",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "NotificationConfigId",
                table: "DiscordNotificationDispatches",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "DiscordNotificationDispatches",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "DiscordNotificationDispatches",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PingRoleId",
                table: "DiscordNotificationConfigs",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "GuildConnectionId",
                table: "DiscordNotificationConfigs",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "DiscordNotificationConfigs",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "DiscordNotificationConfigs",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "DiscordNotificationConfigs",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "NotificationRoleId",
                table: "DiscordMemberOptIns",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "DiscordMemberOptIns",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "DiscordMemberOptIns",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "DiscordMemberOptIns",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "GuildConnectionId",
                table: "DiscordLiveRoleConfigs",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "DiscordLiveRoleConfigs",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "DiscordLiveRoleConfigs",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "DiscordLiveRoleConfigs",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "DiscordGuildConnections",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "DiscordGuildConnections",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "DiscordGuildConnections",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "InstanceId",
                table: "DeploymentProfiles",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "DeploymentProfiles",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "InboundWebhookEndpointId",
                table: "CustomDataSources",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "CustomDataSources",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByUserId",
                table: "CustomDataSources",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "CustomDataSources",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "CustomDataSources",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerUserId",
                table: "CurrencyLedgerEntries",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SourceId",
                table: "CurrencyLedgerEntries",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "EventId",
                table: "CurrencyLedgerEntries",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "CurrencyLedgerEntries",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ActorUserId",
                table: "CurrencyLedgerEntries",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "AccountId",
                table: "CurrencyLedgerEntries",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "CurrencyConfigs",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "CurrencyConfigs",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "CurrencyConfigs",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerUserId",
                table: "CurrencyAccounts",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "CurrencyAccounts",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "CurrencyAccounts",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "CurrencyAccounts",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "RotatedFromKeyId",
                table: "CryptoKeys",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ErasureRequestId",
                table: "CryptoKeys",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "CryptoKeys",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "CryptoKeys",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubjectUserId",
                table: "ConsentRecords",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SubjectKeyId",
                table: "ConsentRecords",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ConsentRecords",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ConsentRecords",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ConsentRecords",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "Configurations",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ErasureRequestId",
                table: "ComplianceAuditLogs",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ComplianceAuditLogs",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerUserId",
                table: "CommandUsages",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewerProfileId",
                table: "CommandUsages",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CommandId",
                table: "CommandUsages",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "CommandUsages",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PipelineId",
                table: "Commands",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "Commands",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "Commands",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Commands",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "CommandCooldownStates",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CommandId",
                table: "CommandCooldownStates",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "CommandCooldownStates",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "CodeScriptVersions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CodeScriptId",
                table: "CodeScriptVersions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "CodeScriptVersions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "AuthorUserId",
                table: "CodeScriptVersions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "CodeScriptVersions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "CodeScripts",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CurrentVersionId",
                table: "CodeScripts",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "CodeScripts",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "AuthorUserId",
                table: "CodeScripts",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "CodeScripts",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PipelineId",
                table: "ChatTriggers",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ChatTriggers",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChatTriggers",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ChatTriggers",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PollId",
                table: "ChatPollVotes",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChatPollVotes",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ChatPollVotes",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChatPolls",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ChatPolls",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ChatMessages",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChatMessages",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ChatFilters",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChatFilters",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ChatFilters",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChannelSubscriptions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "OwnerUserId",
                table: "Channels",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "Channels",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Channels",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "GrantedByUserId",
                table: "ChannelModerators",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ChannelModerators",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "ChannelModerators",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ChannelId",
                table: "ChannelModerators",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByUserId",
                table: "ChannelModerationStandings",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChannelModerationStandings",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ChannelModerationStandings",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChannelMissingScopes",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ChannelMissingScopes",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "ChannelMemberships",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "GrantedByUserId",
                table: "ChannelMemberships",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ChannelMemberships",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChannelMemberships",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ChannelMemberships",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PeerId",
                table: "ChannelFederationOptIns",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "EnabledByUserId",
                table: "ChannelFederationOptIns",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ChannelFederationOptIns",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChannelFederationOptIns",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ChannelFederationOptIns",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChannelFeatures",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "ChannelEvents",
                type: "TEXT",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldMaxLength: 50,
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ChannelId",
                table: "ChannelEvents",
                type: "TEXT",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldMaxLength: 50,
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "ChannelCommunityStandings",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChannelCommunityStandings",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ChannelCommunityStandings",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChannelChatterDays",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PlatformSourceDefinitionId",
                table: "ChannelBuiltinCommands",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ChannelBuiltinCommands",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChannelBuiltinCommands",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ChannelBuiltinCommands",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ChannelBotAuthorizations",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChannelBotAuthorizations",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BotAccountId",
                table: "ChannelBotAuthorizations",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "AuthorizedByUserId",
                table: "ChannelBotAuthorizations",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ChannelBotAuthorizations",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ChannelAssets",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByUserId",
                table: "ChannelAssets",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChannelAssets",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ChannelAssets",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChannelAnalyticsDailies",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SetByUserId",
                table: "ChannelActionOverrides",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ChannelActionOverrides",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "ChannelActionOverrides",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ActionDefinitionId",
                table: "ChannelActionOverrides",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ChannelActionOverrides",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CatalogItemId",
                table: "CatalogPurchases",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BuyerUserId",
                table: "CatalogPurchases",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BuyerAccountId",
                table: "CatalogPurchases",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "CatalogPurchases",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PipelineId",
                table: "CatalogItems",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "CatalogItems",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "CatalogItems",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "CatalogItems",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "BotAccounts",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ConnectionId",
                table: "BotAccounts",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "BotAccounts",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "BlockedTracks",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "BlockedTracks",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "BlockedTracks",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "BillingTiers",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "BillingTiers",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "AutomationApiTokens",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByUserId",
                table: "AutomationApiTokens",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "AutomationApiTokens",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "AutomationApiTokens",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "AuthSessions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "BroadcasterId",
                table: "AuthSessions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "AuthSessions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DismissedByUserId",
                table: "ActionRequiredDismissals",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DeletedBy",
                table: "ActionRequiredDismissals",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true,
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ChannelId",
                table: "ActionRequiredDismissals",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ActionRequiredDismissals",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ActionDefinitions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldCollation: "NOCASE"
            );
        }
    }
}

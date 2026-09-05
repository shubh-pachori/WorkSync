-- =============================================================================
-- AI Timesheet Generator — reference schema
--
-- READ THIS BEFORE RUNNING ANYTHING.
--
-- This file is DOCUMENTATION, not a setup script. The services own their schemas
-- through EF Core migrations and apply them automatically on boot
-- (Database:AutoMigrate). Running this file against either database first will
-- leave you with tables EF cannot migrate over.
--
-- The previous version of this file also could not have worked as advertised:
--   * it used snake_case column names, while the migrations generate PascalCase
--     (only TABLE names are mapped in OnModelCreating, not columns);
--   * it declared TIMESTAMP columns where the model uses TIMESTAMPTZ;
--   * it defaulted ids with uuid_generate_v4() while the CREATE EXTENSION line
--     was commented out, and the entities generate their own GUIDs anyway;
--   * it declared foreign keys from activities/timesheets/approvals to users,
--     which is impossible: users live in a SEPARATE DATABASE. The two services
--     are joined over HTTP (IdentityServiceClient), not by referential integrity.
--
-- What follows mirrors what the migrations actually produce, so it can be used to
-- reason about the schema or to hand-provision a database that EF will then find
-- already up to date.
--
-- To generate a guaranteed-accurate script instead, run:
--   dotnet ef migrations script --project backend/AITimesheet.IdentityService
--   dotnet ef migrations script --project backend/AITimesheet.TimesheetService
-- =============================================================================


-- =============================================================================
-- DATABASE 1 of 2: ai_timesheet_identity_db  (AITimesheet.IdentityService)
-- =============================================================================

CREATE TABLE users (
    "Id"              uuid PRIMARY KEY,
    "FullName"        text NOT NULL,
    "Email"           text NOT NULL,
    -- PBKDF2-HMAC-SHA256, encoded as: v1.{iterations}.{base64 salt}.{base64 key}
    "PasswordHash"    text NOT NULL DEFAULT '',
    "AzureAdObjectId" text NULL,
    "Role"            text NOT NULL,          -- Employee | Manager | Admin
    -- No FK: self-reference is fine here, but it is left off to match the model.
    "ManagerId"       uuid NULL,
    "CreatedAt"       timestamp with time zone NOT NULL,
    "IsActive"        boolean NOT NULL
);

CREATE UNIQUE INDEX "IX_users_Email" ON users ("Email");

CREATE TABLE audit_logs (
    "Id"        uuid PRIMARY KEY,
    "UserId"    uuid NOT NULL,
    "Action"    text NOT NULL,
    "Details"   text NULL,
    "Timestamp" timestamp with time zone NOT NULL
);

CREATE INDEX "IX_audit_logs_UserId_Timestamp" ON audit_logs ("UserId", "Timestamp");

-- Seeded demo users (see IdentityDbContext.SeedDemoUsers). Both use "Demo@123".
-- Priya reports to Sarah, so the approval flow works on a fresh database.
INSERT INTO users ("Id", "FullName", "Email", "PasswordHash", "AzureAdObjectId", "Role", "ManagerId", "CreatedAt", "IsActive")
VALUES
    ('2c6b9a04-57e3-4f81-b3d7-0a94e2f16c58', 'Sarah Jenkins', 'sarah@company.com',
     'v1.210000.OtcOUci5Tyag0T58W4gkbg==.gG5/8s0AXIhS3m+PHHR0o1v0gQwuIaNxnsGH4Io4Ggs=',
     NULL, 'Manager', NULL, TIMESTAMPTZ '2026-01-01 00:00:00+00', TRUE),
    ('8f7d3c1e-1b64-4a2f-9d05-6c1a7e93b420', 'Priya Sharma', 'priya@company.com',
     'v1.210000.nyxBq31eCMMWSpsC3ncxXw==.aWncJ51yUS7BXGfjejJdJguvbAIfuKPhJTH6zBC0GYw=',
     NULL, 'Employee', '2c6b9a04-57e3-4f81-b3d7-0a94e2f16c58', TIMESTAMPTZ '2026-01-01 00:00:00+00', TRUE);


-- =============================================================================
-- DATABASE 2 of 2: ai_timesheet_timesheet_db  (AITimesheet.TimesheetService)
--
-- "UserId" columns here refer to users in the OTHER database. There is no foreign
-- key and there cannot be one.
-- =============================================================================

CREATE TABLE connections (
    "Id"                uuid PRIMARY KEY,
    "UserId"            uuid NOT NULL,
    "Provider"          text NOT NULL,  -- GitHub | AzureDevOps | Jira | OutlookCalendar | TeamsCalendar
    -- Encrypted at rest via ASP.NET Data Protection; never a raw provider token.
    "AccessToken"       text NOT NULL,
    "RefreshToken"      text NULL,
    "ExternalAccountId" text NULL,
    "ConnectedAt"       timestamp with time zone NOT NULL,
    "IsActive"          boolean NOT NULL,
    -- Why the last sync failed, surfaced on the Connect Accounts screen.
    "LastError"         text NULL,
    "LastSyncedAt"      timestamp with time zone NULL
);

-- One row per user per provider; the connect endpoint upserts on this pair.
CREATE UNIQUE INDEX "IX_connections_UserId_Provider" ON connections ("UserId", "Provider");

CREATE TABLE activities (
    "Id"                uuid PRIMARY KEY,
    "UserId"            uuid NOT NULL,
    "Source"            text NOT NULL,  -- GitCommit | PullRequest | JiraTicket | Meeting | CodeReview | WorkItem
    "Title"             text NOT NULL,
    "Description"       text NULL,
    "ExternalReference" text NULL,
    "Status"            text NULL,
    "ActivityDate"      timestamp with time zone NOT NULL,
    "EstimatedHours"    double precision NULL,
    "CreatedAt"         timestamp with time zone NOT NULL
);

CREATE INDEX "IX_activities_UserId_ActivityDate" ON activities ("UserId", "ActivityDate");

CREATE TABLE timesheets (
    "Id"                 uuid PRIMARY KEY,
    "UserId"             uuid NOT NULL,
    "WeekStartDate"      date NOT NULL,   -- always a Monday; the server snaps to it
    "WeekEndDate"        date NOT NULL,
    "Status"             text NOT NULL,   -- Draft | Generated | Submitted | Approved | Rejected
    "AiWeeklySummary"    text NULL,
    -- JSON array of nudges for days with little detected activity.
    "MissingHourPrompts" text NOT NULL DEFAULT '[]',
    "GeneratedAt"        timestamp with time zone NOT NULL,
    "SubmittedAt"        timestamp with time zone NULL
);

-- The invariant behind the duplicate-week bug: one timesheet per user per week.
CREATE UNIQUE INDEX "IX_timesheets_UserId_WeekStartDate" ON timesheets ("UserId", "WeekStartDate");
CREATE INDEX "IX_timesheets_Status" ON timesheets ("Status");

CREATE TABLE timesheet_entries (
    "Id"                  uuid PRIMARY KEY,
    "TimesheetId"         uuid NOT NULL REFERENCES timesheets ("Id") ON DELETE CASCADE,
    "EntryDate"           date NOT NULL,
    "ActivityDescription" text NOT NULL,
    "Hours"               double precision NOT NULL,
    "DevelopmentHours"    double precision NOT NULL,
    "MeetingHours"        double precision NOT NULL,
    "ReviewHours"         double precision NOT NULL,
    "IsEdited"            boolean NOT NULL
);

CREATE INDEX "IX_timesheet_entries_TimesheetId" ON timesheet_entries ("TimesheetId");

CREATE TABLE approvals (
    "Id"          uuid PRIMARY KEY,
    "TimesheetId" uuid NOT NULL REFERENCES timesheets ("Id") ON DELETE CASCADE,
    -- The deciding manager, recorded when the decision is made. Lives in the other DB.
    "ManagerId"   uuid NULL,
    "Status"      text NOT NULL,  -- Pending | Approved | Rejected
    "Comments"    text NULL,
    "DecidedAt"   timestamp with time zone NULL
);

CREATE UNIQUE INDEX "IX_approvals_TimesheetId" ON approvals ("TimesheetId");

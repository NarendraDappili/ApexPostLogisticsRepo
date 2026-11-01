IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE TABLE [AuditEntries] (
        [AuditEntryId] bigint NOT NULL IDENTITY,
        [OccurredAtUtc] datetime2(3) NOT NULL,
        [StaffCode] varchar(48) NULL,
        [Role] varchar(30) NULL,
        [BranchCode] varchar(48) NULL,
        [Controller] varchar(100) NOT NULL,
        [Action] varchar(100) NOT NULL,
        [RouteValuesJson] nvarchar(1000) NOT NULL,
        [StatusCode] int NOT NULL,
        [DurationMilliseconds] bigint NOT NULL,
        [CorrelationId] varchar(64) NOT NULL,
        [Outcome] varchar(10) NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(48) NOT NULL DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        CONSTRAINT [PK_AuditEntries] PRIMARY KEY ([AuditEntryId]),
        CONSTRAINT [CK_AuditEntries_DurationMilliseconds] CHECK ([DurationMilliseconds] >= 0),
        CONSTRAINT [CK_AuditEntries_Outcome] CHECK ([Outcome] IN ('Succeeded', 'Failed')),
        CONSTRAINT [CK_AuditEntries_RouteValuesJson] CHECK (ISJSON([RouteValuesJson]) = 1),
        CONSTRAINT [CK_AuditEntries_StatusCode] CHECK ([StatusCode] BETWEEN 100 AND 599)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE TABLE [Branches] (
        [BranchId] int NOT NULL IDENTITY,
        [BranchCode] varchar(48) NOT NULL,
        [Name] nvarchar(160) NOT NULL,
        [Region] nvarchar(80) NOT NULL,
        [OpeningHours] nvarchar(120) NOT NULL,
        [IsOpen] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedAtUtc] datetime2(3) NOT NULL DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(48) NOT NULL DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        CONSTRAINT [PK_Branches] PRIMARY KEY ([BranchId]),
        CONSTRAINT [AK_Branches_BranchCode] UNIQUE ([BranchCode])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE TABLE [ProductCategories] (
        [ProductCategoryId] int NOT NULL IDENTITY,
        [CategoryCode] varchar(20) NOT NULL,
        [Name] nvarchar(80) NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(48) NOT NULL DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        CONSTRAINT [PK_ProductCategories] PRIMARY KEY ([ProductCategoryId]),
        CONSTRAINT [AK_ProductCategories_CategoryCode] UNIQUE ([CategoryCode])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE TABLE [Roles] (
        [RoleId] tinyint NOT NULL,
        [Name] varchar(30) NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(48) NOT NULL DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        CONSTRAINT [PK_Roles] PRIMARY KEY ([RoleId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE TABLE [ShippingZones] (
        [ShippingZoneId] int NOT NULL IDENTITY,
        [ZoneCode] varchar(48) NOT NULL,
        [Name] nvarchar(80) NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(48) NOT NULL DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        CONSTRAINT [PK_ShippingZones] PRIMARY KEY ([ShippingZoneId]),
        CONSTRAINT [AK_ShippingZones_ZoneCode] UNIQUE ([ZoneCode])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE TABLE [Counters] (
        [CounterId] int NOT NULL IDENTITY,
        [CounterCode] varchar(48) NOT NULL,
        [BranchCode] varchar(48) NOT NULL,
        [IsOpen] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedAtUtc] datetime2(3) NOT NULL DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(48) NOT NULL DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        CONSTRAINT [PK_Counters] PRIMARY KEY ([CounterId]),
        CONSTRAINT [AK_Counters_BranchCode_CounterCode] UNIQUE ([BranchCode], [CounterCode]),
        CONSTRAINT [FK_Counters_Branches_BranchCode] FOREIGN KEY ([BranchCode]) REFERENCES [Branches] ([BranchCode]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE TABLE [MoneyOrders] (
        [MoneyOrderId] int NOT NULL IDENTITY,
        [MoneyOrderCode] varchar(48) NOT NULL,
        [BranchCode] varchar(48) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Fee] decimal(18,2) NOT NULL,
        [Sender] nvarchar(160) NOT NULL,
        [Recipient] nvarchar(160) NOT NULL,
        [TransactionContextCode] varchar(48) NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(48) NOT NULL DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        CONSTRAINT [PK_MoneyOrders] PRIMARY KEY ([MoneyOrderId]),
        CONSTRAINT [AK_MoneyOrders_MoneyOrderCode] UNIQUE ([MoneyOrderCode]),
        CONSTRAINT [CK_MoneyOrders_Amount] CHECK ([Amount] > 0),
        CONSTRAINT [CK_MoneyOrders_Fee] CHECK ([Fee] >= 0),
        CONSTRAINT [FK_MoneyOrders_Branches_BranchCode] FOREIGN KEY ([BranchCode]) REFERENCES [Branches] ([BranchCode]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE TABLE [Products] (
        [ProductId] int NOT NULL IDENTITY,
        [ProductCode] varchar(48) NOT NULL,
        [Sku] varchar(32) NOT NULL,
        [Name] nvarchar(160) NOT NULL,
        [CategoryCode] varchar(20) NOT NULL,
        [UnitPrice] decimal(18,2) NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(48) NOT NULL DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        CONSTRAINT [PK_Products] PRIMARY KEY ([ProductId]),
        CONSTRAINT [AK_Products_ProductCode] UNIQUE ([ProductCode]),
        CONSTRAINT [CK_Products_UnitPrice] CHECK ([UnitPrice] >= 0),
        CONSTRAINT [FK_Products_ProductCategories_CategoryCode] FOREIGN KEY ([CategoryCode]) REFERENCES [ProductCategories] ([CategoryCode]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE TABLE [Staff] (
        [StaffMemberId] int NOT NULL IDENTITY,
        [StaffCode] varchar(48) NOT NULL,
        [Name] nvarchar(160) NOT NULL,
        [Email] varchar(254) NULL,
        [RoleId] tinyint NOT NULL,
        [BranchCode] varchar(48) NOT NULL,
        [PasswordHash] varchar(200) NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedAtUtc] datetime2(3) NOT NULL DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(48) NOT NULL DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        CONSTRAINT [PK_Staff] PRIMARY KEY ([StaffMemberId]),
        CONSTRAINT [AK_Staff_StaffCode] UNIQUE ([StaffCode]),
        CONSTRAINT [FK_Staff_Branches_BranchCode] FOREIGN KEY ([BranchCode]) REFERENCES [Branches] ([BranchCode]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Staff_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([RoleId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE TABLE [Shipments] (
        [ShipmentId] bigint NOT NULL IDENTITY,
        [ShipmentCode] varchar(48) NOT NULL,
        [OriginBranchCode] varchar(48) NOT NULL,
        [ZoneCode] varchar(48) NOT NULL,
        [WeightKg] decimal(9,3) NOT NULL,
        [LengthCm] decimal(9,2) NOT NULL,
        [WidthCm] decimal(9,2) NOT NULL,
        [HeightCm] decimal(9,2) NOT NULL,
        [ShipmentType] varchar(10) NOT NULL,
        [ServiceLevel] varchar(12) NOT NULL,
        [TransactionCode] varchar(48) NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(48) NOT NULL DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        CONSTRAINT [PK_Shipments] PRIMARY KEY ([ShipmentId]),
        CONSTRAINT [AK_Shipments_ShipmentCode] UNIQUE ([ShipmentCode]),
        CONSTRAINT [CK_Shipments_Dimensions] CHECK ([LengthCm] > 0 AND [WidthCm] > 0 AND [HeightCm] > 0),
        CONSTRAINT [CK_Shipments_ServiceLevel] CHECK ([ServiceLevel] IN ('Standard', 'Express', 'Registered')),
        CONSTRAINT [CK_Shipments_ShipmentType] CHECK ([ShipmentType] IN ('Letter', 'Parcel')),
        CONSTRAINT [CK_Shipments_WeightKg] CHECK ([WeightKg] > 0 AND [WeightKg] <= 30),
        CONSTRAINT [FK_Shipments_Branches_OriginBranchCode] FOREIGN KEY ([OriginBranchCode]) REFERENCES [Branches] ([BranchCode]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Shipments_ShippingZones_ZoneCode] FOREIGN KEY ([ZoneCode]) REFERENCES [ShippingZones] ([ZoneCode]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE TABLE [ShippingRates] (
        [ShippingRateId] int NOT NULL IDENTITY,
        [ShipmentType] varchar(10) NOT NULL,
        [ServiceLevel] varchar(12) NOT NULL,
        [ZoneCode] varchar(48) NOT NULL,
        [Charge] decimal(18,2) NOT NULL,
        [MaximumWeightKg] decimal(9,3) NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(48) NOT NULL DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        CONSTRAINT [PK_ShippingRates] PRIMARY KEY ([ShippingRateId]),
        CONSTRAINT [CK_ShippingRates_Charge] CHECK ([Charge] > 0),
        CONSTRAINT [CK_ShippingRates_MaximumWeightKg] CHECK ([MaximumWeightKg] > 0 AND [MaximumWeightKg] <= 30),
        CONSTRAINT [CK_ShippingRates_ServiceLevel] CHECK ([ServiceLevel] IN ('Standard', 'Express', 'Registered')),
        CONSTRAINT [CK_ShippingRates_ShipmentType] CHECK ([ShipmentType] IN ('Letter', 'Parcel')),
        CONSTRAINT [FK_ShippingRates_ShippingZones_ZoneCode] FOREIGN KEY ([ZoneCode]) REFERENCES [ShippingZones] ([ZoneCode]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE TABLE [IdentityVerifications] (
        [IdentityVerificationId] int NOT NULL IDENTITY,
        [MoneyOrderId] int NOT NULL,
        [DocumentType] varchar(80) NOT NULL,
        [MaskedDocumentReference] varchar(8) NOT NULL,
        [Outcome] varchar(10) NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(48) NOT NULL DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        CONSTRAINT [PK_IdentityVerifications] PRIMARY KEY ([IdentityVerificationId]),
        CONSTRAINT [CK_IdentityVerifications_Masked] CHECK ([MaskedDocumentReference] LIKE '****%'),
        CONSTRAINT [CK_IdentityVerifications_Outcome] CHECK ([Outcome] IN ('Verified', 'Failed')),
        CONSTRAINT [FK_IdentityVerifications_MoneyOrders_MoneyOrderId] FOREIGN KEY ([MoneyOrderId]) REFERENCES [MoneyOrders] ([MoneyOrderId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE TABLE [BranchStock] (
        [BranchStockId] int NOT NULL IDENTITY,
        [BranchCode] varchar(48) NOT NULL,
        [ProductCode] varchar(48) NOT NULL,
        [Quantity] int NOT NULL DEFAULT 0,
        [CreatedAtUtc] datetime2(3) NOT NULL DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(48) NOT NULL DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_BranchStock] PRIMARY KEY ([BranchStockId]),
        CONSTRAINT [AK_BranchStock_BranchCode_ProductCode] UNIQUE ([BranchCode], [ProductCode]),
        CONSTRAINT [CK_BranchStock_Quantity] CHECK ([Quantity] >= 0),
        CONSTRAINT [FK_BranchStock_Branches_BranchCode] FOREIGN KEY ([BranchCode]) REFERENCES [Branches] ([BranchCode]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BranchStock_Products_ProductCode] FOREIGN KEY ([ProductCode]) REFERENCES [Products] ([ProductCode]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE TABLE [CounterTransactions] (
        [CounterTransactionId] bigint NOT NULL IDENTITY,
        [TransactionCode] varchar(48) NOT NULL,
        [BranchCode] varchar(48) NOT NULL,
        [CounterCode] varchar(48) NOT NULL,
        [ClerkStaffCode] varchar(48) NOT NULL,
        [Subtotal] decimal(18,2) NOT NULL,
        [Tax] decimal(18,2) NOT NULL,
        [OccurredAtUtc] datetime2(3) NOT NULL,
        [Status] varchar(10) NOT NULL,
        [StatusReason] nvarchar(500) NULL,
        [AuthorizedByStaffCode] varchar(48) NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(48) NOT NULL DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_CounterTransactions] PRIMARY KEY ([CounterTransactionId]),
        CONSTRAINT [AK_CounterTransactions_TransactionCode] UNIQUE ([TransactionCode]),
        CONSTRAINT [CK_CounterTransactions_Amounts] CHECK ([Subtotal] >= 0 AND [Tax] >= 0),
        CONSTRAINT [CK_CounterTransactions_Reversal] CHECK (([Status] IN ('Open', 'Paid') AND [AuthorizedByStaffCode] IS NULL AND [StatusReason] IS NULL) OR ([Status] IN ('Voided', 'Refunded') AND [AuthorizedByStaffCode] IS NOT NULL AND [StatusReason] IS NOT NULL)),
        CONSTRAINT [CK_CounterTransactions_Status] CHECK ([Status] IN ('Open', 'Paid', 'Voided', 'Refunded')),
        CONSTRAINT [FK_CounterTransactions_Counters_BranchCode_CounterCode] FOREIGN KEY ([BranchCode], [CounterCode]) REFERENCES [Counters] ([BranchCode], [CounterCode]) ON DELETE NO ACTION,
        CONSTRAINT [FK_CounterTransactions_Staff_AuthorizedByStaffCode] FOREIGN KEY ([AuthorizedByStaffCode]) REFERENCES [Staff] ([StaffCode]) ON DELETE NO ACTION,
        CONSTRAINT [FK_CounterTransactions_Staff_ClerkStaffCode] FOREIGN KEY ([ClerkStaffCode]) REFERENCES [Staff] ([StaffCode]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE TABLE [StockAdjustments] (
        [StockAdjustmentId] bigint NOT NULL IDENTITY,
        [BranchCode] varchar(48) NOT NULL,
        [ProductCode] varchar(48) NOT NULL,
        [Delta] int NOT NULL,
        [Reason] nvarchar(200) NOT NULL,
        [ChangedByStaffCode] varchar(48) NOT NULL,
        [OccurredAtUtc] datetime2(3) NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(48) NOT NULL DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        CONSTRAINT [PK_StockAdjustments] PRIMARY KEY ([StockAdjustmentId]),
        CONSTRAINT [CK_StockAdjustments_Delta] CHECK ([Delta] <> 0),
        CONSTRAINT [FK_StockAdjustments_BranchStock_BranchCode_ProductCode] FOREIGN KEY ([BranchCode], [ProductCode]) REFERENCES [BranchStock] ([BranchCode], [ProductCode]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StockAdjustments_Staff_ChangedByStaffCode] FOREIGN KEY ([ChangedByStaffCode]) REFERENCES [Staff] ([StaffCode]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE TABLE [Payments] (
        [CounterTransactionId] bigint NOT NULL,
        [PaymentMethod] varchar(16) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(48) NOT NULL DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        CONSTRAINT [PK_Payments] PRIMARY KEY ([CounterTransactionId]),
        CONSTRAINT [CK_Payments_Amount] CHECK ([Amount] >= 0),
        CONSTRAINT [CK_Payments_PaymentMethod] CHECK ([PaymentMethod] IN ('Cash', 'Card', 'DigitalWallet')),
        CONSTRAINT [FK_Payments_CounterTransactions_CounterTransactionId] FOREIGN KEY ([CounterTransactionId]) REFERENCES [CounterTransactions] ([CounterTransactionId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE TABLE [TransactionLines] (
        [TransactionLineId] bigint NOT NULL IDENTITY,
        [ProductCode] varchar(48) NULL,
        [Description] nvarchar(250) NOT NULL,
        [Quantity] int NOT NULL,
        [UnitPrice] decimal(18,2) NOT NULL,
        [CounterTransactionId] bigint NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(48) NOT NULL DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        CONSTRAINT [PK_TransactionLines] PRIMARY KEY ([TransactionLineId]),
        CONSTRAINT [CK_TransactionLines_Quantity] CHECK ([Quantity] > 0),
        CONSTRAINT [CK_TransactionLines_UnitPrice] CHECK ([UnitPrice] >= 0),
        CONSTRAINT [FK_TransactionLines_CounterTransactions_CounterTransactionId] FOREIGN KEY ([CounterTransactionId]) REFERENCES [CounterTransactions] ([CounterTransactionId]) ON DELETE CASCADE,
        CONSTRAINT [FK_TransactionLines_Products_ProductCode] FOREIGN KEY ([ProductCode]) REFERENCES [Products] ([ProductCode]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'ProductCategoryId', N'CategoryCode', N'CreatedAtUtc', N'CreatedBy', N'ModifiedAtUtc', N'Name') AND [object_id] = OBJECT_ID(N'[ProductCategories]'))
        SET IDENTITY_INSERT [ProductCategories] ON;
    EXEC(N'INSERT INTO [ProductCategories] ([ProductCategoryId], [CategoryCode], [CreatedAtUtc], [CreatedBy], [ModifiedAtUtc], [Name])
    VALUES (1, ''GENERAL'', ''2026-01-01T00:00:00.000Z'', ''migration'', NULL, N''General'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'ProductCategoryId', N'CategoryCode', N'CreatedAtUtc', N'CreatedBy', N'ModifiedAtUtc', N'Name') AND [object_id] = OBJECT_ID(N'[ProductCategories]'))
        SET IDENTITY_INSERT [ProductCategories] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'CreatedAtUtc', N'CreatedBy', N'ModifiedAtUtc', N'Name') AND [object_id] = OBJECT_ID(N'[Roles]'))
        SET IDENTITY_INSERT [Roles] ON;
    EXEC(N'INSERT INTO [Roles] ([RoleId], [CreatedAtUtc], [CreatedBy], [ModifiedAtUtc], [Name])
    VALUES (CAST(0 AS tinyint), ''2026-01-01T00:00:00.000Z'', ''migration'', NULL, ''CounterClerk''),
    (CAST(1 AS tinyint), ''2026-01-01T00:00:00.000Z'', ''migration'', NULL, ''BranchManager''),
    (CAST(2 AS tinyint), ''2026-01-01T00:00:00.000Z'', ''migration'', NULL, ''Auditor''),
    (CAST(3 AS tinyint), ''2026-01-01T00:00:00.000Z'', ''migration'', NULL, ''Administrator'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'CreatedAtUtc', N'CreatedBy', N'ModifiedAtUtc', N'Name') AND [object_id] = OBJECT_ID(N'[Roles]'))
        SET IDENTITY_INSERT [Roles] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_BranchStock_ProductCode] ON [BranchStock] ([ProductCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CounterTransactions_AuthorizedByStaffCode] ON [CounterTransactions] ([AuthorizedByStaffCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CounterTransactions_BranchCode_CounterCode] ON [CounterTransactions] ([BranchCode], [CounterCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CounterTransactions_ClerkStaffCode] ON [CounterTransactions] ([ClerkStaffCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_IdentityVerifications_MoneyOrderId] ON [IdentityVerifications] ([MoneyOrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_MoneyOrders_BranchCode] ON [MoneyOrders] ([BranchCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Products_CategoryCode] ON [Products] ([CategoryCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Products_Sku] ON [Products] ([Sku]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Roles_Name] ON [Roles] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Shipments_OriginBranchCode] ON [Shipments] ([OriginBranchCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Shipments_ZoneCode] ON [Shipments] ([ZoneCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_ShippingRates_Band] ON [ShippingRates] ([ZoneCode], [ShipmentType], [ServiceLevel], [MaximumWeightKg]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Staff_BranchCode] ON [Staff] ([BranchCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Staff_RoleId] ON [Staff] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Staff_Email] ON [Staff] ([Email]) WHERE [Email] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_StockAdjustments_BranchCode_ProductCode] ON [StockAdjustments] ([BranchCode], [ProductCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_StockAdjustments_ChangedByStaffCode] ON [StockAdjustments] ([ChangedByStaffCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TransactionLines_CounterTransactionId] ON [TransactionLines] ([CounterTransactionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TransactionLines_ProductCode] ON [TransactionLines] ([ProductCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030172857_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261030172857_InitialCreate', N'10.0.13');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031195516_AddStoredProcedures'
)
BEGIN
    EXEC(N'-- Paid sales of one branch on one UTC day, split into retail (catalogue products) and services
    -- (shipments and money orders). Refunded and voided sales are excluded.
    CREATE OR ALTER PROCEDURE dbo.usp_DailyBranchSales
        @BranchCode varchar(48),
        @Date date
    AS
    BEGIN
        SET NOCOUNT ON;

        IF @BranchCode IS NULL OR LTRIM(RTRIM(@BranchCode)) = ''''
            THROW 50001, ''usp_DailyBranchSales: @BranchCode is required.'', 1;
        IF @Date IS NULL
            THROW 50002, ''usp_DailyBranchSales: @Date is required.'', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.Branches WHERE BranchCode = @BranchCode)
            THROW 50003, ''usp_DailyBranchSales: the branch does not exist.'', 1;

        -- A half-open range keeps the predicate on OccurredAtUtc SARGable (no function applied to the column).
        -- The bounds are written as expressions of @Date rather than local variables: the optimizer cannot see a local
        -- variable''s value, so it guessed thousands of sales for a day that has about a hundred and scanned every line
        -- (PERFORMANCE.md, "Estimated vs actual rows").
        WITH PaidSales AS
        (
            SELECT t.CounterTransactionId, t.Tax
            FROM dbo.CounterTransactions AS t
            WHERE t.BranchCode = @BranchCode
              AND t.Status = ''Paid''
              AND t.OccurredAtUtc >= CAST(@Date AS datetime2(3))
              AND t.OccurredAtUtc < DATEADD(DAY, 1, CAST(@Date AS datetime2(3)))
        ),
        LineTotals AS
        (
            SELECT
                SUM(CASE WHEN l.ProductCode IS NOT NULL THEN l.UnitPrice * l.Quantity ELSE 0 END) AS RetailSales,
                SUM(CASE WHEN l.ProductCode IS NULL THEN l.UnitPrice * l.Quantity ELSE 0 END) AS ServiceSales
            FROM dbo.TransactionLines AS l
            INNER JOIN PaidSales AS p ON p.CounterTransactionId = l.CounterTransactionId
        )
        SELECT
            @BranchCode AS BranchCode,
            @Date AS SalesDate,
            CAST(ISNULL(lt.RetailSales, 0) AS decimal(18, 2)) AS RetailSales,
            CAST(ISNULL(lt.ServiceSales, 0) AS decimal(18, 2)) AS ServiceSales,
            CAST(ISNULL((SELECT SUM(p.Tax) FROM PaidSales AS p), 0) AS decimal(18, 2)) AS Tax,
            (SELECT COUNT(*) FROM PaidSales) AS PaidTransactionCount
        FROM LineTotals AS lt;
    END
    ');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031195516_AddStoredProcedures'
)
BEGIN
    EXEC(N'-- Products at or below @Threshold units. With @BranchCode the report covers one branch; with NULL it covers
    -- every branch (head-office view).
    CREATE OR ALTER PROCEDURE dbo.usp_LowStockReport
        @BranchCode varchar(48) = NULL,
        @Threshold int
    AS
    BEGIN
        SET NOCOUNT ON;

        IF @Threshold IS NULL OR @Threshold < 0
            THROW 50011, ''usp_LowStockReport: @Threshold must be zero or more.'', 1;
        IF @BranchCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Branches WHERE BranchCode = @BranchCode)
            THROW 50012, ''usp_LowStockReport: the branch does not exist.'', 1;

        SELECT s.BranchCode, s.ProductCode, p.Sku, p.Name, s.Quantity
        FROM dbo.BranchStock AS s
        INNER JOIN dbo.Products AS p ON p.ProductCode = s.ProductCode
        WHERE (@BranchCode IS NULL OR s.BranchCode = @BranchCode)
          AND s.Quantity <= @Threshold
        ORDER BY s.BranchCode, s.ProductCode
        -- The optional filter makes one cached plan wrong for half the calls (a seek for one branch, a scan for all).
        -- RECOMPILE builds the plan for the actual values each time; a report runs rarely, so the compile cost is small.
        OPTION (RECOMPILE);
    END
    ');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031195516_AddStoredProcedures'
)
BEGIN
    EXEC(N'-- Manual stock correction: changes one branch stock row by @Delta and records who did it and why in
    -- StockAdjustments, both or neither. Returns the new quantity. Stock can never go below zero.
    -- Works on its own or inside a caller''s transaction: it only starts and commits a transaction when none is open.
    CREATE OR ALTER PROCEDURE dbo.usp_AdjustStock
        @BranchCode varchar(48),
        @Sku varchar(32),
        @Delta int,
        @Reason nvarchar(200),
        @ChangedBy varchar(48)
    AS
    BEGIN
        SET NOCOUNT ON;
        -- Any run-time error dooms the transaction, so no partial adjustment can be committed.
        SET XACT_ABORT ON;

        IF @BranchCode IS NULL OR LTRIM(RTRIM(@BranchCode)) = ''''
            THROW 50021, ''usp_AdjustStock: @BranchCode is required.'', 1;
        IF @Sku IS NULL OR LTRIM(RTRIM(@Sku)) = ''''
            THROW 50022, ''usp_AdjustStock: @Sku is required.'', 1;
        IF @Delta IS NULL OR @Delta = 0
            THROW 50023, ''A stock adjustment cannot be zero.'', 1;
        IF @Reason IS NULL OR LTRIM(RTRIM(@Reason)) = ''''
            THROW 50024, ''An adjustment reason is required.'', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.Staff WHERE StaffCode = @ChangedBy AND IsActive = 1)
            THROW 50025, ''The adjustment must be made by an active staff member.'', 1;

        DECLARE @StartedTransaction bit = 0;
        BEGIN TRY
            IF @@TRANCOUNT = 0
            BEGIN
                BEGIN TRANSACTION;
                SET @StartedTransaction = 1;
            END;

            DECLARE @ProductCode varchar(48) = (SELECT ProductCode FROM dbo.Products WHERE Sku = @Sku);
            IF @ProductCode IS NULL
                THROW 50026, ''No product has this SKU.'', 1;

            -- The quantity check is part of the UPDATE, so a concurrent change cannot slip between check and write.
            UPDATE dbo.BranchStock
            SET Quantity = Quantity + @Delta,
                ModifiedAtUtc = SYSUTCDATETIME()
            WHERE BranchCode = @BranchCode
              AND ProductCode = @ProductCode
              AND Quantity + @Delta >= 0;

            IF @@ROWCOUNT = 0
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM dbo.BranchStock WHERE BranchCode = @BranchCode AND ProductCode = @ProductCode)
                    THROW 50027, ''The branch does not stock this product.'', 1;
                THROW 50028, ''Stock cannot be reduced below zero.'', 1;
            END;

            INSERT INTO dbo.StockAdjustments (BranchCode, ProductCode, Delta, Reason, ChangedByStaffCode, OccurredAtUtc, CreatedBy)
            VALUES (@BranchCode, @ProductCode, @Delta, LTRIM(RTRIM(@Reason)), @ChangedBy, SYSUTCDATETIME(), @ChangedBy);

            SELECT Quantity AS NewQuantity
            FROM dbo.BranchStock
            WHERE BranchCode = @BranchCode AND ProductCode = @ProductCode;

            IF @StartedTransaction = 1 COMMIT TRANSACTION;
        END TRY
        BEGIN CATCH
            IF @StartedTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
            THROW;
        END CATCH;
    END
    ');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031195516_AddStoredProcedures'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261031195516_AddStoredProcedures', N'10.0.13');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101123330_AddWorkloadIndexes'
)
BEGIN
    DROP INDEX [IX_TransactionLines_CounterTransactionId] ON [TransactionLines];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101123330_AddWorkloadIndexes'
)
BEGIN
    DROP INDEX [IX_Shipments_OriginBranchCode] ON [Shipments];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101123330_AddWorkloadIndexes'
)
BEGIN
    DROP INDEX [IX_IdentityVerifications_MoneyOrderId] ON [IdentityVerifications];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101123330_AddWorkloadIndexes'
)
BEGIN
    CREATE INDEX [IX_TransactionLines_CounterTransactionId] ON [TransactionLines] ([CounterTransactionId]) INCLUDE ([ProductCode], [Quantity], [UnitPrice]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101123330_AddWorkloadIndexes'
)
BEGIN
    CREATE INDEX [IX_Shipments_OriginBranchCode_ZoneCode_ServiceLevel] ON [Shipments] ([OriginBranchCode], [ZoneCode], [ServiceLevel]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101123330_AddWorkloadIndexes'
)
BEGIN
    CREATE UNIQUE INDEX [IX_IdentityVerifications_MoneyOrderId] ON [IdentityVerifications] ([MoneyOrderId]) INCLUDE ([Outcome]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101123330_AddWorkloadIndexes'
)
BEGIN
    CREATE INDEX [IX_CounterTransactions_BranchCode_OccurredAtUtc] ON [CounterTransactions] ([BranchCode], [OccurredAtUtc]) INCLUDE ([Status], [Subtotal], [Tax]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101123330_AddWorkloadIndexes'
)
BEGIN
    CREATE INDEX [IX_BranchStock_BranchCode_Quantity] ON [BranchStock] ([BranchCode], [Quantity]) INCLUDE ([ProductCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101123330_AddWorkloadIndexes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261101123330_AddWorkloadIndexes', N'10.0.13');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101123522_RedeployDailyBranchSales'
)
BEGIN
    EXEC(N'-- Paid sales of one branch on one UTC day, split into retail (catalogue products) and services
    -- (shipments and money orders). Refunded and voided sales are excluded.
    CREATE OR ALTER PROCEDURE dbo.usp_DailyBranchSales
        @BranchCode varchar(48),
        @Date date
    AS
    BEGIN
        SET NOCOUNT ON;

        IF @BranchCode IS NULL OR LTRIM(RTRIM(@BranchCode)) = ''''
            THROW 50001, ''usp_DailyBranchSales: @BranchCode is required.'', 1;
        IF @Date IS NULL
            THROW 50002, ''usp_DailyBranchSales: @Date is required.'', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.Branches WHERE BranchCode = @BranchCode)
            THROW 50003, ''usp_DailyBranchSales: the branch does not exist.'', 1;

        -- A half-open range keeps the predicate on OccurredAtUtc SARGable (no function applied to the column).
        -- The bounds are written as expressions of @Date rather than local variables: the optimizer cannot see a local
        -- variable''s value, so it guessed thousands of sales for a day that has about a hundred and scanned every line
        -- (PERFORMANCE.md, "Estimated vs actual rows").
        WITH PaidSales AS
        (
            SELECT t.CounterTransactionId, t.Tax
            FROM dbo.CounterTransactions AS t
            WHERE t.BranchCode = @BranchCode
              AND t.Status = ''Paid''
              AND t.OccurredAtUtc >= CAST(@Date AS datetime2(3))
              AND t.OccurredAtUtc < DATEADD(DAY, 1, CAST(@Date AS datetime2(3)))
        ),
        LineTotals AS
        (
            SELECT
                SUM(CASE WHEN l.ProductCode IS NOT NULL THEN l.UnitPrice * l.Quantity ELSE 0 END) AS RetailSales,
                SUM(CASE WHEN l.ProductCode IS NULL THEN l.UnitPrice * l.Quantity ELSE 0 END) AS ServiceSales
            FROM dbo.TransactionLines AS l
            INNER JOIN PaidSales AS p ON p.CounterTransactionId = l.CounterTransactionId
        )
        SELECT
            @BranchCode AS BranchCode,
            @Date AS SalesDate,
            CAST(ISNULL(lt.RetailSales, 0) AS decimal(18, 2)) AS RetailSales,
            CAST(ISNULL(lt.ServiceSales, 0) AS decimal(18, 2)) AS ServiceSales,
            CAST(ISNULL((SELECT SUM(p.Tax) FROM PaidSales AS p), 0) AS decimal(18, 2)) AS Tax,
            (SELECT COUNT(*) FROM PaidSales) AS PaidTransactionCount
        FROM LineTotals AS lt;
    END
    ');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101123522_RedeployDailyBranchSales'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261101123522_RedeployDailyBranchSales', N'10.0.13');
END;

COMMIT;
GO


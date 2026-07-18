using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KestrelBooks.Api.Migrations
{
    /// <inheritdoc />
    public partial class Phase4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Journals_BusinessId_Number",
                table: "Journals");

            migrationBuilder.AddColumn<bool>(
                name: "IsOpeningBalance",
                table: "SalesInvoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsOpeningBalance",
                table: "PurchaseInvoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseCreditNoteId",
                table: "MoneyTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesCreditNoteId",
                table: "MoneyTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FlatRatePercent",
                table: "Businesses",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "LockedThrough",
                table: "Businesses",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VatScheme",
                table: "Businesses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Attachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityKind = table.Column<int>(type: "integer", nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    StoredName = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attachments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseCreditNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reference = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    NetTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    VatTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    GrossTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AmountPaid = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IsOpeningBalance = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseCreditNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseCreditNotes_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecurringInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    NumberPrefix = table.Column<string>(type: "text", nullable: false),
                    NextNumber = table.Column<int>(type: "integer", nullable: false),
                    Frequency = table.Column<int>(type: "integer", nullable: false),
                    PaymentTermsDays = table.Column<int>(type: "integer", nullable: false),
                    NextRunDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    AutoPost = table.Column<bool>(type: "boolean", nullable: false),
                    Paused = table.Column<bool>(type: "boolean", nullable: false),
                    LastGeneratedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    GeneratedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringInvoices_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesCreditNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reference = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    NetTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    VatTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    GrossTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AmountPaid = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IsOpeningBalance = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesCreditNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesCreditNotes_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseCreditNoteLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseCreditNoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    VatRate = table.Column<int>(type: "integer", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseCreditNoteLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseCreditNoteLine_PurchaseCreditNotes_PurchaseCreditNo~",
                        column: x => x.PurchaseCreditNoteId,
                        principalTable: "PurchaseCreditNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecurringInvoiceLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecurringInvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    VatRate = table.Column<int>(type: "integer", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringInvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringInvoiceLines_RecurringInvoices_RecurringInvoiceId",
                        column: x => x.RecurringInvoiceId,
                        principalTable: "RecurringInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesCreditNoteLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesCreditNoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    VatRate = table.Column<int>(type: "integer", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesCreditNoteLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesCreditNoteLine_SalesCreditNotes_SalesCreditNoteId",
                        column: x => x.SalesCreditNoteId,
                        principalTable: "SalesCreditNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MoneyTransactions_PurchaseCreditNoteId",
                table: "MoneyTransactions",
                column: "PurchaseCreditNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_MoneyTransactions_PurchaseInvoiceId",
                table: "MoneyTransactions",
                column: "PurchaseInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_MoneyTransactions_SalesCreditNoteId",
                table: "MoneyTransactions",
                column: "SalesCreditNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_MoneyTransactions_SalesInvoiceId",
                table: "MoneyTransactions",
                column: "SalesInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Journals_BusinessId_Number",
                table: "Journals",
                columns: new[] { "BusinessId", "Number" },
                unique: true,
                filter: "\"Number\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_Journals_BusinessId_Source_SourceId",
                table: "Journals",
                columns: new[] { "BusinessId", "Source", "SourceId" },
                unique: true,
                filter: "\"SourceId\" IS NOT NULL AND \"Source\" IN (1, 2, 3, 4, 9, 10)");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_BusinessId_EntityKind_EntityId",
                table: "Attachments",
                columns: new[] { "BusinessId", "EntityKind", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseCreditNoteLine_PurchaseCreditNoteId",
                table: "PurchaseCreditNoteLine",
                column: "PurchaseCreditNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseCreditNotes_VendorId",
                table: "PurchaseCreditNotes",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringInvoiceLines_RecurringInvoiceId",
                table: "RecurringInvoiceLines",
                column: "RecurringInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringInvoices_BusinessId_NextRunDate",
                table: "RecurringInvoices",
                columns: new[] { "BusinessId", "NextRunDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringInvoices_CustomerId",
                table: "RecurringInvoices",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesCreditNoteLine_SalesCreditNoteId",
                table: "SalesCreditNoteLine",
                column: "SalesCreditNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesCreditNotes_CustomerId",
                table: "SalesCreditNotes",
                column: "CustomerId");

            migrationBuilder.AddForeignKey(
                name: "FK_MoneyTransactions_PurchaseCreditNotes_PurchaseCreditNoteId",
                table: "MoneyTransactions",
                column: "PurchaseCreditNoteId",
                principalTable: "PurchaseCreditNotes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MoneyTransactions_PurchaseInvoices_PurchaseInvoiceId",
                table: "MoneyTransactions",
                column: "PurchaseInvoiceId",
                principalTable: "PurchaseInvoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MoneyTransactions_SalesCreditNotes_SalesCreditNoteId",
                table: "MoneyTransactions",
                column: "SalesCreditNoteId",
                principalTable: "SalesCreditNotes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MoneyTransactions_SalesInvoices_SalesInvoiceId",
                table: "MoneyTransactions",
                column: "SalesInvoiceId",
                principalTable: "SalesInvoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MoneyTransactions_PurchaseCreditNotes_PurchaseCreditNoteId",
                table: "MoneyTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_MoneyTransactions_PurchaseInvoices_PurchaseInvoiceId",
                table: "MoneyTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_MoneyTransactions_SalesCreditNotes_SalesCreditNoteId",
                table: "MoneyTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_MoneyTransactions_SalesInvoices_SalesInvoiceId",
                table: "MoneyTransactions");

            migrationBuilder.DropTable(
                name: "Attachments");

            migrationBuilder.DropTable(
                name: "PurchaseCreditNoteLine");

            migrationBuilder.DropTable(
                name: "RecurringInvoiceLines");

            migrationBuilder.DropTable(
                name: "SalesCreditNoteLine");

            migrationBuilder.DropTable(
                name: "PurchaseCreditNotes");

            migrationBuilder.DropTable(
                name: "RecurringInvoices");

            migrationBuilder.DropTable(
                name: "SalesCreditNotes");

            migrationBuilder.DropIndex(
                name: "IX_MoneyTransactions_PurchaseCreditNoteId",
                table: "MoneyTransactions");

            migrationBuilder.DropIndex(
                name: "IX_MoneyTransactions_PurchaseInvoiceId",
                table: "MoneyTransactions");

            migrationBuilder.DropIndex(
                name: "IX_MoneyTransactions_SalesCreditNoteId",
                table: "MoneyTransactions");

            migrationBuilder.DropIndex(
                name: "IX_MoneyTransactions_SalesInvoiceId",
                table: "MoneyTransactions");

            migrationBuilder.DropIndex(
                name: "IX_Journals_BusinessId_Number",
                table: "Journals");

            migrationBuilder.DropIndex(
                name: "IX_Journals_BusinessId_Source_SourceId",
                table: "Journals");

            migrationBuilder.DropColumn(
                name: "IsOpeningBalance",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "IsOpeningBalance",
                table: "PurchaseInvoices");

            migrationBuilder.DropColumn(
                name: "PurchaseCreditNoteId",
                table: "MoneyTransactions");

            migrationBuilder.DropColumn(
                name: "SalesCreditNoteId",
                table: "MoneyTransactions");

            migrationBuilder.DropColumn(
                name: "FlatRatePercent",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "LockedThrough",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "VatScheme",
                table: "Businesses");

            migrationBuilder.CreateIndex(
                name: "IX_Journals_BusinessId_Number",
                table: "Journals",
                columns: new[] { "BusinessId", "Number" });
        }
    }
}

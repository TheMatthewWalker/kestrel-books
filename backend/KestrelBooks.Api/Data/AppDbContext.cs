using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Data;

/// <summary>
/// Multi-tenant DbContext. Every business-scoped entity carries a global query
/// filter bound to the current TenantProvider (set per request by
/// TenantMiddleware after membership verification). A query that "forgets" to
/// filter now fails closed — it returns nothing rather than another tenant's
/// data. Filters read TenantId at query time (not construction time), because
/// the middleware sets the tenant after this scoped context is created.
/// </summary>
public class AppDbContext : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>
{
    private readonly TenantProvider _tenant;
    private Guid? TenantId => _tenant.BusinessId;

    public AppDbContext(DbContextOptions<AppDbContext> options, TenantProvider tenant)
        : base(options) => _tenant = tenant;

    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<UserBusinessAccess> UserBusinessAccess => Set<UserBusinessAccess>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<JournalEntry> Journals => Set<JournalEntry>();
    public DbSet<JournalLine> JournalLines => Set<JournalLine>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<SalesInvoice> SalesInvoices => Set<SalesInvoice>();
    public DbSet<PurchaseInvoice> PurchaseInvoices => Set<PurchaseInvoice>();
    public DbSet<SalesCreditNote> SalesCreditNotes => Set<SalesCreditNote>();
    public DbSet<PurchaseCreditNote> PurchaseCreditNotes => Set<PurchaseCreditNote>();
    public DbSet<MoneyTransaction> MoneyTransactions => Set<MoneyTransaction>();
    public DbSet<FixedAsset> FixedAssets => Set<FixedAsset>();
    public DbSet<BankStatementImport> BankStatementImports => Set<BankStatementImport>();
    public DbSet<BankStatementLine> BankStatementLines => Set<BankStatementLine>();
    public DbSet<ReceiptScan> ReceiptScans => Set<ReceiptScan>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<BillOfMaterial> BillOfMaterials => Set<BillOfMaterial>();
    public DbSet<ProductionOrder> ProductionOrders => Set<ProductionOrder>();
    public DbSet<HmrcConnection> HmrcConnections => Set<HmrcConnection>();
    public DbSet<VatSubmission> VatSubmissions => Set<VatSubmission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<OneTimeCode> OneTimeCodes => Set<OneTimeCode>();
    public DbSet<AuthEvent> AuthEvents => Set<AuthEvent>();
    public DbSet<Attachment> Attachments => Set<Attachment>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<UserBusinessAccess>().HasKey(x => new { x.UserId, x.BusinessId });
        b.Entity<Business>().Property(x => x.FlatRatePercent).HasPrecision(5, 2);

        b.Entity<Account>().HasIndex(x => new { x.BusinessId, x.Code }).IsUnique();
        b.Entity<Account>().Property(x => x.Code).HasMaxLength(10);

        // Posted journal numbers are unique per business (drafts sit at Number = 0).
        // The filter excludes drafts; PostingService retries on collision.
        b.Entity<JournalEntry>().HasIndex(x => new { x.BusinessId, x.Number })
            .IsUnique().HasFilter("\"Number\" > 0");
        // One journal per source document (sales/purchase invoice, receipt, payment):
        // concurrent double-posts violate this index and surface as HTTP 409.
        b.Entity<JournalEntry>().HasIndex(x => new { x.BusinessId, x.Source, x.SourceId })
            .IsUnique().HasFilter("\"SourceId\" IS NOT NULL AND \"Source\" IN (1, 2, 3, 4, 9, 10)");
        b.Entity<JournalEntry>()
            .HasMany(x => x.Lines).WithOne(x => x.JournalEntry)
            .HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<JournalLine>().Property(x => x.Debit).HasPrecision(18, 2);
        b.Entity<JournalLine>().Property(x => x.Credit).HasPrecision(18, 2);
        b.Entity<JournalLine>()
            .HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        foreach (var t in new[] { typeof(SalesInvoice), typeof(PurchaseInvoice), typeof(SalesCreditNote), typeof(PurchaseCreditNote) })
        {
            var e = b.Entity(t);
            e.Property("NetTotal").HasPrecision(18, 2);
            e.Property("VatTotal").HasPrecision(18, 2);
            e.Property("GrossTotal").HasPrecision(18, 2);
            e.Property("AmountPaid").HasPrecision(18, 2);
        }
        b.Entity<SalesInvoice>().HasMany(x => x.Lines).WithOne()
            .HasForeignKey(x => x.SalesInvoiceId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<PurchaseInvoice>().HasMany(x => x.Lines).WithOne()
            .HasForeignKey(x => x.PurchaseInvoiceId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SalesCreditNote>().HasMany(x => x.Lines).WithOne()
            .HasForeignKey(x => x.SalesCreditNoteId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<PurchaseCreditNote>().HasMany(x => x.Lines).WithOne()
            .HasForeignKey(x => x.PurchaseCreditNoteId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<MoneyTransaction>().Property(x => x.Amount).HasPrecision(18, 2);
        b.Entity<MoneyTransaction>().HasOne(x => x.SalesInvoice).WithMany()
            .HasForeignKey(x => x.SalesInvoiceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MoneyTransaction>().HasOne(x => x.PurchaseInvoice).WithMany()
            .HasForeignKey(x => x.PurchaseInvoiceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MoneyTransaction>().HasOne(x => x.SalesCreditNote).WithMany()
            .HasForeignKey(x => x.SalesCreditNoteId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MoneyTransaction>().HasOne(x => x.PurchaseCreditNote).WithMany()
            .HasForeignKey(x => x.PurchaseCreditNoteId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<FixedAsset>().Property(x => x.Cost).HasPrecision(18, 2);
        b.Entity<FixedAsset>().Property(x => x.ResidualValue).HasPrecision(18, 2);
        b.Entity<FixedAsset>().Property(x => x.AccumulatedDepreciation).HasPrecision(18, 2);
        b.Entity<FixedAsset>().Property(x => x.AnnualRatePercent).HasPrecision(9, 4);

        b.Entity<BankStatementImport>()
            .HasMany(x => x.Lines).WithOne(x => x.Import)
            .HasForeignKey(x => x.ImportId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<BankStatementLine>().Property(x => x.Amount).HasPrecision(18, 2);
        b.Entity<BankStatementLine>().HasIndex(x => new { x.BusinessId, x.BankAccountId, x.ExternalRef });

        b.Entity<ReceiptScan>().Property(x => x.NetAmount).HasPrecision(18, 2);
        b.Entity<ReceiptScan>().Property(x => x.VatAmount).HasPrecision(18, 2);
        b.Entity<ReceiptScan>().Property(x => x.GrossAmount).HasPrecision(18, 2);

        b.Entity<Item>().Property(x => x.QuantityOnHand).HasPrecision(18, 3);
        b.Entity<Item>().Property(x => x.AvgUnitCost).HasPrecision(18, 4);
        b.Entity<StockMovement>().Property(x => x.Quantity).HasPrecision(18, 3);
        b.Entity<StockMovement>().Property(x => x.UnitCost).HasPrecision(18, 4);
        b.Entity<StockMovement>().Property(x => x.Value).HasPrecision(18, 2);
        b.Entity<StockMovement>().Property(x => x.QuantityAfter).HasPrecision(18, 3);
        b.Entity<StockMovement>().HasIndex(x => new { x.BusinessId, x.ItemId, x.Date });
        b.Entity<StockMovement>()
            .HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<BillOfMaterial>().HasIndex(x => new { x.BusinessId, x.ParentItemId }).IsUnique();
        b.Entity<BillOfMaterial>().Property(x => x.LabourCostPerUnit).HasPrecision(18, 4);
        b.Entity<BillOfMaterial>().Property(x => x.OverheadCostPerUnit).HasPrecision(18, 4);
        b.Entity<BillOfMaterial>()
            .HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.BillOfMaterialId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<BomLine>().Property(x => x.QuantityPer).HasPrecision(18, 4);
        b.Entity<BomLine>()
            .HasOne(x => x.ComponentItem).WithMany().HasForeignKey(x => x.ComponentItemId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<ProductionOrder>().Property(x => x.QuantityPlanned).HasPrecision(18, 3);
        b.Entity<ProductionOrder>().Property(x => x.QuantityCompleted).HasPrecision(18, 3);
        b.Entity<ProductionOrder>().Property(x => x.MaterialCost).HasPrecision(18, 2);
        b.Entity<ProductionOrder>().Property(x => x.LabourCost).HasPrecision(18, 2);
        b.Entity<ProductionOrder>().Property(x => x.OverheadCost).HasPrecision(18, 2);
        b.Entity<ProductionOrder>()
            .HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<HmrcConnection>().HasIndex(x => x.BusinessId).IsUnique();
        b.Entity<RefreshToken>().HasIndex(x => x.TokenHash).IsUnique();
        b.Entity<RefreshToken>().HasIndex(x => x.UserId);
        b.Entity<OneTimeCode>().HasIndex(x => new { x.UserId, x.Purpose });
        b.Entity<AuthEvent>().HasIndex(x => x.AtUtc);
        b.Entity<Attachment>().HasIndex(x => new { x.BusinessId, x.EntityKind, x.EntityId });

        // ---- Tenant isolation: global query filters ----
        b.Entity<Account>().HasQueryFilter(e => TenantId == null || e.BusinessId == TenantId);
        b.Entity<JournalEntry>().HasQueryFilter(e => TenantId == null || e.BusinessId == TenantId);
        b.Entity<JournalLine>().HasQueryFilter(e => TenantId == null || e.JournalEntry.BusinessId == TenantId);
        b.Entity<Customer>().HasQueryFilter(e => TenantId == null || e.BusinessId == TenantId);
        b.Entity<Vendor>().HasQueryFilter(e => TenantId == null || e.BusinessId == TenantId);
        b.Entity<Item>().HasQueryFilter(e => TenantId == null || e.BusinessId == TenantId);
        b.Entity<SalesInvoice>().HasQueryFilter(e => TenantId == null || e.BusinessId == TenantId);
        b.Entity<PurchaseInvoice>().HasQueryFilter(e => TenantId == null || e.BusinessId == TenantId);
        b.Entity<SalesCreditNote>().HasQueryFilter(e => TenantId == null || e.BusinessId == TenantId);
        b.Entity<PurchaseCreditNote>().HasQueryFilter(e => TenantId == null || e.BusinessId == TenantId);
        b.Entity<MoneyTransaction>().HasQueryFilter(e => TenantId == null || e.BusinessId == TenantId);
        b.Entity<FixedAsset>().HasQueryFilter(e => TenantId == null || e.BusinessId == TenantId);
        b.Entity<BankStatementImport>().HasQueryFilter(e => TenantId == null || e.BusinessId == TenantId);
        b.Entity<BankStatementLine>().HasQueryFilter(e => TenantId == null || e.BusinessId == TenantId);
        b.Entity<ReceiptScan>().HasQueryFilter(e => TenantId == null || e.BusinessId == TenantId);
        b.Entity<StockMovement>().HasQueryFilter(e => TenantId == null || e.BusinessId == TenantId);
        b.Entity<BillOfMaterial>().HasQueryFilter(e => TenantId == null || e.BusinessId == TenantId);
        b.Entity<BomLine>().HasQueryFilter(e => TenantId == null || e.ComponentItem.BusinessId == TenantId);
        b.Entity<ProductionOrder>().HasQueryFilter(e => TenantId == null || e.BusinessId == TenantId);
        b.Entity<HmrcConnection>().HasQueryFilter(e => TenantId == null || e.BusinessId == TenantId);
        b.Entity<VatSubmission>().HasQueryFilter(e => TenantId == null || e.BusinessId == TenantId);
        b.Entity<Attachment>().HasQueryFilter(e => TenantId == null || e.BusinessId == TenantId);
    }
}

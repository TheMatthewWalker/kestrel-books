using KestrelBooks.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Data;

public class AppDbContext : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

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
    public DbSet<MoneyTransaction> MoneyTransactions => Set<MoneyTransaction>();
    public DbSet<FixedAsset> FixedAssets => Set<FixedAsset>();
    public DbSet<BankStatementImport> BankStatementImports => Set<BankStatementImport>();
    public DbSet<BankStatementLine> BankStatementLines => Set<BankStatementLine>();
    public DbSet<ReceiptScan> ReceiptScans => Set<ReceiptScan>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<UserBusinessAccess>().HasKey(x => new { x.UserId, x.BusinessId });

        b.Entity<Account>().HasIndex(x => new { x.BusinessId, x.Code }).IsUnique();
        b.Entity<Account>().Property(x => x.Code).HasMaxLength(10);

        b.Entity<JournalEntry>().HasIndex(x => new { x.BusinessId, x.Number });
        b.Entity<JournalEntry>()
            .HasMany(x => x.Lines).WithOne(x => x.JournalEntry)
            .HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<JournalLine>().Property(x => x.Debit).HasPrecision(18, 2);
        b.Entity<JournalLine>().Property(x => x.Credit).HasPrecision(18, 2);
        b.Entity<JournalLine>()
            .HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        foreach (var t in new[] { typeof(SalesInvoice), typeof(PurchaseInvoice) })
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

        b.Entity<MoneyTransaction>().Property(x => x.Amount).HasPrecision(18, 2);
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
    }
}

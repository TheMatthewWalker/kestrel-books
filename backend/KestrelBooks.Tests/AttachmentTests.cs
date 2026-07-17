using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KestrelBooks.Tests;

/// <summary>Attachment round-trip on a temp-dir disk store, plus the guards and tenant isolation.</summary>
public class AttachmentTests : IDisposable
{
    private sealed class TempEnv : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } =
            Directory.CreateTempSubdirectory("kestrel-attach-").FullName;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private readonly TestDb _db = new();
    private readonly TempEnv _env = new();
    private readonly Guid _businessId;
    private readonly Guid _user = Guid.NewGuid();
    private Guid _customerId;

    public AttachmentTests()
    {
        using var ctx = _db.Create();
        var (b, _, _, _, _) = TestDb.SeedBusiness(ctx, "Attach Ltd");
        _businessId = b.Id;
        var customer = new Customer { Id = Guid.NewGuid(), BusinessId = b.Id, Name = "C" };
        ctx.Customers.Add(customer);
        ctx.SaveChanges();
        _customerId = customer.Id;
        _db.Tenant.Set(_businessId, BusinessRole.Owner);
    }

    private AttachmentService Service(AppDbContext ctx) => new(ctx, new DiskReceiptStorage(_env));

    [Fact]
    public async Task Upload_List_Download_Delete_RoundTrip()
    {
        using var ctx = _db.Create();
        var svc = Service(ctx);
        var bytes = System.Text.Encoding.UTF8.GetBytes("%PDF-1.4 pretend contract");

        var saved = await svc.SaveAsync(_businessId, AttachedTo.Customer, _customerId,
            "contract.pdf", "application/pdf", bytes, _user);

        var list = await svc.ListAsync(_businessId, AttachedTo.Customer, _customerId);
        Assert.Single(list);
        Assert.Equal("contract.pdf", list[0].FileName);

        var fetched = await svc.GetAsync(_businessId, saved.Id);
        Assert.NotNull(fetched);
        Assert.Equal(bytes, fetched!.Value.data);

        Assert.True(await svc.DeleteAsync(_businessId, saved.Id));
        Assert.Empty(await svc.ListAsync(_businessId, AttachedTo.Customer, _customerId));
        Assert.Null(await svc.GetAsync(_businessId, saved.Id));
    }

    [Fact]
    public async Task Guards_RejectBadTypes_Oversize_AndMissingTargets()
    {
        using var ctx = _db.Create();
        var svc = Service(ctx);
        var pdf = new byte[] { 1, 2, 3 };

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SaveAsync(
            _businessId, AttachedTo.Customer, _customerId, "virus.exe", "application/x-msdownload", pdf, _user));
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SaveAsync(
            _businessId, AttachedTo.Customer, _customerId, "big.pdf", "application/pdf",
            new byte[11 * 1024 * 1024], _user));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.SaveAsync(
            _businessId, AttachedTo.SalesInvoice, Guid.NewGuid(), "orphan.pdf", "application/pdf", pdf, _user));
    }

    [Fact]
    public async Task Attachments_AreTenantIsolated()
    {
        Guid attachmentId;
        using (var ctx = _db.Create())
        {
            attachmentId = (await Service(ctx).SaveAsync(_businessId, AttachedTo.Customer, _customerId,
                "secret.pdf", "application/pdf", new byte[] { 9 }, _user)).Id;
        }

        // Seed a second business (inserts ignore filters), switch tenant to it:
        // the first tenant's attachment must be invisible even by direct id.
        Guid otherBusinessId;
        using (var ctx = _db.Create())
            otherBusinessId = TestDb.SeedBusiness(ctx, "Other Ltd").business.Id;
        _db.Tenant.Set(otherBusinessId, BusinessRole.Owner);

        using var ctx2 = _db.Create();
        Assert.Null(await Service(ctx2).GetAsync(_businessId, attachmentId));
        Assert.Empty(await ctx2.Attachments.ToListAsync());
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_env.ContentRootPath, recursive: true); } catch { }
    }
}

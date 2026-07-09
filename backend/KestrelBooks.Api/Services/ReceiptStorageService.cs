namespace KestrelBooks.Api.Services;

/// <summary>Stores receipt images on the local disk under Storage/receipts/{businessId}/.</summary>
public class ReceiptStorageService
{
    private readonly string _root;
    public ReceiptStorageService(IHostEnvironment env) =>
        _root = Path.Combine(env.ContentRootPath, "Storage", "receipts");

    public async Task<string> SaveAsync(Guid businessId, string extension, byte[] data)
    {
        var dir = Path.Combine(_root, businessId.ToString());
        Directory.CreateDirectory(dir);
        var name = $"{Guid.NewGuid():N}{extension}";
        await File.WriteAllBytesAsync(Path.Combine(dir, name), data);
        return name;
    }

    public async Task<byte[]?> LoadAsync(Guid businessId, string storedName)
    {
        var path = Path.Combine(_root, businessId.ToString(), Path.GetFileName(storedName));
        return File.Exists(path) ? await File.ReadAllBytesAsync(path) : null;
    }
}

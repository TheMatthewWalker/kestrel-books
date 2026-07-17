using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace KestrelBooks.Api.Services;

/// <summary>
/// Receipt image storage. Disk works for a single server with a mounted
/// volume; S3-compatible object storage (AWS S3, Cloudflare R2, Backblaze B2,
/// MinIO) survives container replacement and scales past one machine —
/// configure the S3 section to switch, no code change.
/// </summary>
public interface IReceiptStorage
{
    Task<string> SaveAsync(Guid businessId, string extension, byte[] data);
    Task<byte[]?> LoadAsync(Guid businessId, string storedName);
    Task DeleteAsync(Guid businessId, string storedName);
}

/// <summary>Local disk under Storage/receipts/{businessId}/ — mount a volume in Docker.</summary>
public class DiskReceiptStorage : IReceiptStorage
{
    private readonly string _root;
    public DiskReceiptStorage(IHostEnvironment env) =>
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

    public Task DeleteAsync(Guid businessId, string storedName)
    {
        var path = Path.Combine(_root, businessId.ToString(), Path.GetFileName(storedName));
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }
}

/// <summary>S3-compatible object storage. Keys are {businessId}/{name}.</summary>
public class S3ReceiptStorage : IReceiptStorage
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;

    public S3ReceiptStorage(IConfiguration config)
    {
        _bucket = config["S3:Bucket"]!;
        _s3 = new AmazonS3Client(
            new BasicAWSCredentials(config["S3:AccessKey"], config["S3:SecretKey"]),
            new AmazonS3Config
            {
                ServiceURL = config["S3:Endpoint"],
                ForcePathStyle = true, // required by R2/B2/MinIO
            });
    }

    public async Task<string> SaveAsync(Guid businessId, string extension, byte[] data)
    {
        var name = $"{Guid.NewGuid():N}{extension}";
        using var ms = new MemoryStream(data);
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = $"{businessId}/{name}",
            InputStream = ms,
        });
        return name;
    }

    public async Task<byte[]?> LoadAsync(Guid businessId, string storedName)
    {
        try
        {
            using var res = await _s3.GetObjectAsync(_bucket, $"{businessId}/{Path.GetFileName(storedName)}");
            using var ms = new MemoryStream();
            await res.ResponseStream.CopyToAsync(ms);
            return ms.ToArray();
        }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task DeleteAsync(Guid businessId, string storedName) =>
        _s3.DeleteObjectAsync(_bucket, $"{businessId}/{Path.GetFileName(storedName)}");
}

using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Extensions.Options;
using Odm.Modules.Software.Application;

namespace Odm.Modules.Software.Infrastructure;

/// <summary>Package storage on S3 (AWS or compatible, e.g. MinIO).</summary>
public sealed class S3PackageStorage : IPackageStorage, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly string _bucket;
    private readonly SemaphoreSlim _bucketLock = new(1, 1);
    private bool _bucketReady;

    public S3PackageStorage(IOptions<S3Options> options)
    {
        var o = options.Value;
        _bucket = o.Bucket;

        var config = new AmazonS3Config
        {
            ForcePathStyle = o.ForcePathStyle,
            // Compatible with S3 implementations that do not know the new default checksums.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
        };

        if (!string.IsNullOrWhiteSpace(o.ServiceUrl))
        {
            config.ServiceURL = o.ServiceUrl;
            config.AuthenticationRegion = o.Region;
        }
        else
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(o.Region);
        }

        _client = new AmazonS3Client(new BasicAWSCredentials(o.AccessKey, o.SecretKey), config);
    }

    public async Task EnsureBucketAsync(CancellationToken ct)
    {
        if (_bucketReady)
        {
            return;
        }

        await _bucketLock.WaitAsync(ct);
        try
        {
            if (!_bucketReady)
            {
                if (!await AmazonS3Util.DoesS3BucketExistV2Async(_client, _bucket))
                {
                    await _client.PutBucketAsync(new PutBucketRequest { BucketName = _bucket }, ct);
                }

                _bucketReady = true;
            }
        }
        finally
        {
            _bucketLock.Release();
        }
    }

    public async Task PutFileAsync(string key, string filePath, CancellationToken ct)
    {
        await EnsureBucketAsync(ct);
        await _client.PutObjectAsync(
            new PutObjectRequest { BucketName = _bucket, Key = key, FilePath = filePath, ContentType = "application/zip" },
            ct);
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct)
    {
        var response = await _client.GetObjectAsync(_bucket, key, ct);
        return response.ResponseStream;
    }

    public async Task DeleteAsync(string key, CancellationToken ct) =>
        await _client.DeleteObjectAsync(_bucket, key, ct);

    public void Dispose()
    {
        _client.Dispose();
        _bucketLock.Dispose();
    }
}

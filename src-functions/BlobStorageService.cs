using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace DrPodcast.Functions;

public class BlobStorageService : IStorageService
{
    private readonly string _connectionString;
    private readonly string _containerName;

    public BlobStorageService()
    {
        _connectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING")
            ?? throw new InvalidOperationException("AZURE_STORAGE_CONNECTION_STRING environment variable is not set");
        _containerName = Environment.GetEnvironmentVariable("STORAGE_CONTAINER_NAME") ?? "$web";
    }

    public async Task UploadDirectoryAsync(string localDirectory, string containerPath = "")
    {
        if (!Directory.Exists(localDirectory))
        {
            throw new DirectoryNotFoundException($"Local directory not found: {localDirectory}");
        }

        var blobServiceClient = new BlobServiceClient(_connectionString);
        var containerClient = blobServiceClient.GetBlobContainerClient(_containerName);

        // Ensure container exists
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);

        var files = Directory.GetFiles(localDirectory, "*.*", SearchOption.AllDirectories);
        Console.WriteLine($"Uploading {files.Length} files to Azure Blob Storage...");

        var uploadTasks = files.Select(async filePath =>
        {
            var relativePath = Path.GetRelativePath(localDirectory, filePath);
            var blobName = string.IsNullOrEmpty(containerPath)
                ? relativePath.Replace("\\", "/")
                : $"{containerPath}/{relativePath}".Replace("\\", "/");

            var blobClient = containerClient.GetBlobClient(blobName);

            // Determine content type based on file extension
            var contentType = GetContentType(filePath);

            var blobHttpHeaders = new BlobHttpHeaders
            {
                ContentType = contentType
            };

            await using var fileStream = File.OpenRead(filePath);
            await blobClient.UploadAsync(fileStream, new BlobUploadOptions
            {
                HttpHeaders = blobHttpHeaders
            });

            Console.WriteLine($"✓ Uploaded: {blobName}");
        });

        await Task.WhenAll(uploadTasks);
        Console.WriteLine($"Upload complete! {files.Length} files uploaded.");
    }

    private static string GetContentType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".html" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };
    }
}

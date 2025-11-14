namespace DrPodcast.Functions;

public interface IStorageService
{
    Task UploadDirectoryAsync(string localDirectory, string containerPath = "");
}

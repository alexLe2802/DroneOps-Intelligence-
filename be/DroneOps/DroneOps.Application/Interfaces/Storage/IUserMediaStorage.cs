using Microsoft.AspNetCore.Http;

namespace DroneOps.Application.Interfaces.Storage;

public interface IUserMediaStorage
{
    Task ValidateAsync(
        IFormFile file,
        bool avatar,
        CancellationToken cancellationToken = default);

    Task UploadAsync(
        string path,
        IFormFile file,
        CancellationToken cancellationToken = default);

    Task<byte[]> ReadValidatedUavImageAsync(
        Stream stream,
        string fileName,
        string contentType,
        long fileSize,
        CancellationToken cancellationToken = default);

    Task UploadAsync(
        string path,
        Stream stream,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<string> SignAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        IEnumerable<string> paths,
        CancellationToken cancellationToken = default);
}

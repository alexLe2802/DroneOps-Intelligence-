namespace DroneOps.Application.DTOs.Response.Common;

public sealed class PagedResponse<T>
{
    public int PageNumber { get; set; }

    public int PageSize { get; set; }

    public int TotalItems { get; set; }

    public int TotalPages { get; set; }
    public IReadOnlyCollection<T> Items { get; set; } = [];
}
namespace DroneOps.Application.DTOs.Request;

public sealed record RegistryFile(string Name, string Base64)
{
    public DroneOps.Domain.Entities.RegistryDocument ToDocument(Guid actor)
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 180 || Path.GetFileName(Name) != Name ||
            Name.Any(char.IsControl) || !Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(Base64) || Base64.Length > 2796204)
            throw new ArgumentException("Choose a PDF document up to 2 MB.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(Base64); }
        catch (FormatException) { throw new ArgumentException("Invalid document encoding."); }
        if (bytes.Length > 2097152 || !bytes.AsSpan().StartsWith("%PDF-"u8))
            throw new ArgumentException("Choose a valid PDF document up to 2 MB.");
        return new() { Name = Name.Trim(), UploadedBy = actor, Content = bytes };
    }
}

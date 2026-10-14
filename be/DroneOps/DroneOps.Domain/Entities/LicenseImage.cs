using System.ComponentModel.DataAnnotations.Schema;

[Table("LicenseImage")]
public sealed class LicenseImage
{
    public Guid Id { get; set; }

    public Guid PilotRegistrationId { get; set; }

    public string ImageUrl { get; set; } = string.Empty;

    public string? ImageType { get; set; }

    public DateTime CreatedAt { get; set; }

    public PilotRegistration PilotRegistration { get; set; } = null!;
}
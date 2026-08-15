using System.ComponentModel.DataAnnotations;

namespace DocIntelApi.Models.Requests;

public class UploadDocumentRequest
{
    [Required]
    public IFormFile File { get; set; } = null!;

    // Optional description the user can add
    [MaxLength(500)]
    public string? Description { get; set; }
}
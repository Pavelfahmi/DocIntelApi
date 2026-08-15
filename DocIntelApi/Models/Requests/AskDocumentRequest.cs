using System.ComponentModel.DataAnnotations;

namespace DocIntelApi.Models.Requests;

public class AskDocumentRequest
{
    [Required]
    [MinLength(3)]
    [MaxLength(2000)]
    public string Question { get; set; } = string.Empty;

    /// <summary>How many chunks to retrieve from the vector store (1–10).</summary>
    [Range(1, 10)]
    public int TopK { get; set; } = 5;
}

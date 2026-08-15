using System.ComponentModel.DataAnnotations;

namespace DocIntelApi.Models.Requests;

public class RegisterRequest
{
    [Required]
    [EmailAddress]           // validates format — must contain @ and domain
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]           // enforce minimum password length
    [MaxLength(100)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string FullName { get; set; } = string.Empty;
}
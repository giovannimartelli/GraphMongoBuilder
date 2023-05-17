using System.ComponentModel.DataAnnotations;

namespace HotChocolateBuilder.Authentication.Models;

public class AuthenticateModel
{
    [Required] public string? Username { get; set; }

    [Required] public string? Password { get; set; }
}
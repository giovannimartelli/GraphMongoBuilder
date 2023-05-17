using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HotChocolateBuilder.Authentication.Entities;
using HotChocolateBuilder.Authentication.Helpers;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;

namespace HotChocolateBuilder.Authentication.Services;

public interface IUserService
{
    User? Authenticate(string? username, string? password);
}

public class UserService : IUserService
{
    private readonly AppSettings _appSettings;

    private readonly List<User> _users;

    public UserService(IOptions<AppSettings> appSettings, IMongoCollection<UserDto> collection)
    {
        _appSettings = appSettings.Value;
        _users = collection.FindSync(x => true).ToList().Select(e => new User
        {
            Username = e.Username,
            Password = e.Password,
            Role = e.Role
        }).ToList();
    }

    public User? Authenticate(string? username, string? password)
    {
        var user = _users.SingleOrDefault(x =>
            x.Username == username && BCrypt.Net.BCrypt.Verify(password, x.Password));

        // return null if user not found
        if (user == null)
            return null;

        // authentication successful so generate jwt token
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(_appSettings.Secret ?? string.Empty);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, user.Username.ToString()),
                new Claim(ClaimTypes.Role, user.Role??throw new InvalidOperationException())
            }),
            Expires = DateTime.UtcNow.AddDays(1),
            SigningCredentials =
                new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        user.Token = tokenHandler.WriteToken(token);

        return user.WithoutPassword();
    }
}
using HotChocolateBuilder.Authentication.Entities;
using MongoDB.Bson;

namespace HotChocolateBuilder.Authentication;

// ReSharper disable once ClassNeverInstantiated.Global
public class UserDto : User
{
    public ObjectId Id { get; init; }
}
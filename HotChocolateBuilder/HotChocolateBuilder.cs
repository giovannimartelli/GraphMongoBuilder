using System.Diagnostics.CodeAnalysis;
using System.Text;
using HotChocolate.Execution.Configuration;
using HotChocolateBuilder.Authentication;
using HotChocolateBuilder.Authentication.Helpers;
using HotChocolateBuilder.Authentication.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MongoDB.Bson;
using MongoDB.Driver;
using NLog;
using NLog.Extensions.Logging;
using NLog.Web;

namespace HotChocolateBuilder;

[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
public class HotChocolateBuilder
{
    private readonly string _projectName;
    
    private readonly WebApplicationBuilder _webApplicationBuilder = WebApplication.CreateBuilder();

    private IConfiguration? _configuration;

    private IConfigurationBuilder? _configurationBuilder;
    private IMongoDatabase? _dataBase;

    private IRequestExecutorBuilder? _graphQlBuilder;

    private string? _mongoDbConnectionString;

    private WebApplication? _webApplication;

    public HotChocolateBuilder(string projectName)
    {
        _projectName = projectName;
    }


    public HotChocolateBuilder LoadAppSettings()
    {
        _configurationBuilder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", false, true);

        return this;
    }

    public HotChocolateBuilder WithAdditionalAppsettings(string path)
    {
        (_configurationBuilder ?? throw new InvalidOperationException()).AddJsonFile(path, false, true);
        return this;
    }

    public HotChocolateBuilder BuildConfig()
    {
        _configuration = _configurationBuilder?.Build();
        return this;
    }

    public HotChocolateBuilder WithEnvVariable(string varName, string varValue)
    {
        Environment.SetEnvironmentVariable(varName, varValue);
        return this;
    }

    [Refactor]
    public HotChocolateBuilder WithElkNLogTargetFromEnvVars()
    {
        if (_configuration != null)
        {
            _configuration["NLog:targets:ELK:uri"] = Environment.GetEnvironmentVariable("ELK_URI") ??
                                                     throw new Exception("Missing ELK_URI env var");
            _configuration["NLog:targets:ELK:username"] = Environment.GetEnvironmentVariable("ELK_USERNAME") ??
                                                          throw new Exception("Missing ELK_USERNAME env var");
            _configuration["NLog:targets:ELK:password"] = Environment.GetEnvironmentVariable("ELK_PASSWORD") ??
                                                          throw new Exception("Missing ELK_PASSWORD env var");
            _configuration["NLog:targets:ELK:index"] = Environment.GetEnvironmentVariable("ELK_INDEX") ??
                                                       throw new Exception("Missing ELK_INDEX env var");
        }

        return this;
    }

    [Refactor]
    public HotChocolateBuilder WithElkNLogTarget(string elkUri, string elkUsername, string elkPassword, string elkIndex)
    {
        if (_configuration != null)
        {
            _configuration["NLog:targets:ELK:uri"] = elkUri;
            _configuration["NLog:targets:ELK:username"] = elkUsername;
            _configuration["NLog:targets:ELK:password"] = elkPassword;
            _configuration["NLog:targets:ELK:index"] = elkIndex;
        }

        return this;
    }

    public HotChocolateBuilder WithNLog()
    {
        LogManager.Configuration = new NLogLoggingConfiguration(_configuration!.GetSection("NLog"));
        _webApplicationBuilder.Host.ConfigureLogging(e => e.ClearProviders()).UseNLog();
        return this;
    }

    public HotChocolateBuilder WithMongoDbConnectionParamsFromEnv(string dbName)
    {
        Environment.SetEnvironmentVariable("DATABASE_NAME", dbName);
        return WithMongoDbConnectionParamsFromEnv();
    }

    public HotChocolateBuilder WithMongoDbConnectionParamsFromEnv()
    {
        var host = Environment.GetEnvironmentVariable("MONGO_HOST") ??
                   throw new Exception("Missing MONGO_HOST env var");
        var port = Environment.GetEnvironmentVariable("MONGO_PORT") ??
                   throw new Exception("Missing MONGO_PORT env var");
        var username = Environment.GetEnvironmentVariable("MONGO_USER") ??
                       throw new Exception("Missing MONGO_USER env var");
        var password = Environment.GetEnvironmentVariable("MONGO_PASSWORD") ??
                       throw new Exception("Missing MONGO_PASSWORD env var");
        _mongoDbConnectionString = $"mongodb://{username}:{password}@{host}:{port}";
        var mongoConnectionUrl = new MongoUrl(_mongoDbConnectionString);
        var mongoClientSettings = MongoClientSettings.FromUrl(mongoConnectionUrl);
        var client = new MongoClient(mongoClientSettings);
        _dataBase = client.GetDatabase(Environment.GetEnvironmentVariable("DATABASE_NAME"))
                    ?? throw new Exception("Missing DATABASE_NAME env var");
        _webApplicationBuilder.Services.AddSingleton(_dataBase);
        return this;
    }

    public HotChocolateBuilder WithMongoCollection<TDto>(string collectionName)
    {
        _webApplicationBuilder.Services
            .AddSingleton(_ => _dataBase?.GetCollection<TDto>(collectionName) ??
                               throw new Exception($"Missing collection {collectionName} in database"));
        return this;
    }

    public HotChocolateBuilder WithMongoUserCollection(string name)
    {
        _webApplicationBuilder.Services.AddSingleton(_ =>
            _dataBase?.GetCollection<UserDto>(name) ?? throw new Exception($"Missing collection {name} in database"));
        return this;
    }

    public HotChocolateBuilder AddAspNetAuthorization()
    {
        _webApplicationBuilder.Services.AddAuthorization();
        var appSettingsSection = _configuration!.GetSection("jwt") ??
                                 throw new Exception("Missing jwt section in appsettings.json");
        _webApplicationBuilder.Services.Configure<AppSettings>(appSettingsSection);

        // configure jwt authentication
        var appSettings = appSettingsSection.Get<AppSettings>();
        var key = Encoding.ASCII.GetBytes(appSettings?.Secret ??
                                          throw new Exception("Missing jwt:secret in appsettings.json"));
        _webApplicationBuilder.Services.AddAuthentication(x =>
            {
                x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(x =>
            {
                x.RequireHttpsMetadata = false;
                x.SaveToken = true;
                x.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = false,
                    ValidateAudience = false
                };
            });
        _webApplicationBuilder.Services.AddScoped<IUserService, UserService>();
        _webApplicationBuilder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = $"{_projectName} API", Version = "v1" });
        });
        return this;
    }

    public HotChocolateBuilder AddGraphQl()
    {
        _graphQlBuilder = _webApplicationBuilder.Services.AddGraphQLServer();
        _graphQlBuilder.ModifyRequestOptions(opt => opt.IncludeExceptionDetails = true);  
        return this;
    }

    public HotChocolateBuilder AddGraphQlWithAuthorization()
    {
        AddGraphQl();
        if (_graphQlBuilder == null) throw new Exception("GraphQlBuilder is null");
        _graphQlBuilder.AddGraphQLServer()
            .AddAuthorization();
        return this;
    }

    public HotChocolateBuilder AddQueryType<TQuery>() where TQuery : class
    {
        if (_graphQlBuilder == null) throw new Exception("GraphQlBuilder is null");
        _graphQlBuilder.AddQueryType<TQuery>();
        return this;
    }

    public HotChocolateBuilder SetQueryTimeout(TimeSpan timeout)
    {
        if (_graphQlBuilder == null) throw new Exception("GraphQlBuilder is null");
        _graphQlBuilder.SetRequestOptions(_ => new HotChocolate.Execution.Options.RequestExecutorOptions
            { ExecutionTimeout = timeout });
        return this;
    }

    public HotChocolateBuilder AddMutationType<TMutation>() where TMutation : class
    {
        if (_graphQlBuilder == null) throw new Exception("GraphQlBuilder is null");
        _graphQlBuilder.AddMutationType<TMutation>();
        return this;
    }

    public HotChocolateBuilder WithDefaultMongodbLayer()
    {
        if (_graphQlBuilder == null) throw new Exception("GraphQlBuilder is null");
        _graphQlBuilder
            .BindRuntimeType<ObjectId, IdType>()
            .AddTypeConverter<ObjectId, string>(o => o.ToString())
            .AddTypeConverter<string, ObjectId>(ObjectId.Parse)
            .AddMongoDbFiltering()
            .AddMongoDbSorting()
            .AddMongoDbProjections()
            .AddMongoDbPagingProviders();
        return this;
    }


    public IRequestExecutorBuilder GetGraphQlBuilder()
    {
        if (_graphQlBuilder == null) throw new Exception("GraphQlBuilder is null");
        return _graphQlBuilder;
    }

    public HotChocolateBuilder WithSingleton<T>() where T : class
    {
        _webApplicationBuilder.Services.AddSingleton<T>();
        return this;
    }

    public HotChocolateBuilder WithSingleton<T, TImpl>() where TImpl : class, T where T : class
    {
        _webApplicationBuilder.Services.AddSingleton<T, TImpl>();
        return this;
    }

    public HotChocolateBuilder Build()
    {
        _webApplicationBuilder.Services.AddControllers();
        _webApplicationBuilder.Services.AddCors();
        _webApplicationBuilder.Services.AddGraphQLServer();
        _webApplication = _webApplicationBuilder.Build();
        return this;
    }

    public HotChocolateBuilder MapAuthRoute(string route = "login",
        string pattern = "{controller=login}/{action=authenticate}/{model}")
    {
        if (_webApplication == null) throw new Exception("WebApplication is null");
        _webApplication.MapControllerRoute(
            route,
            pattern);
        _webApplication.MapSwagger();
        _webApplication.UseSwagger();
        _webApplication.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("v1/swagger.json", "My API V1");
        });
        return this;
    }

    public HotChocolateBuilder MapGraphQl()
    {
        if (_webApplication == null) throw new Exception("WebApplication is null");

        _webApplication.MapGraphQL();
        return this;
    }

    public HotChocolateBuilder AddDefaultAuth()
    {
        if (_webApplication == null) throw new Exception("WebApplication is null");
        _webApplication.UseAuthentication();
        _webApplication.UseAuthorization();
        return this;
    }

    public WebApplication Get()
    {
        if (_webApplication == null) throw new Exception("WebApplication is null");
        return _webApplication;
    }
}

public class RefactorAttribute : Attribute
{
}
using System.Text;
using System.Text.Json.Serialization;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;
using TaskTracker.Api.Auth;
using TaskTracker.Api.Swagger;
using TaskTracker.Application.Auth;
using TaskTracker.Application.Common;
using TaskTracker.Infrastructure;
using TaskTracker.Infrastructure.Auth;
using TaskTracker.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

// --- Logging (console only). Request bodies are never logged, so passwords/tokens never reach the log. ---
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console());

// --- Persistence, Identity, auth services ---
builder.Services.AddInfrastructure(cfg);

// --- JWT authentication ---
var jwt = cfg.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
          ?? throw new InvalidOperationException("Jwt configuration section is missing.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false; // keep "sub", "email", "role" as-is
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = jwt.Issuer,
            ValidateAudience = true, ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = AuthClaimTypes.Subject,
            RoleClaimType = AuthClaimTypes.Role,
        };
        o.Events = new JwtBearerEvents
        {
            // Stateless JWT + this check = logout/deactivate/role change take effect on the next request.
            // (AddSecurityStampValidator() only applies to cookie auth, so it is done here instead.)
            OnTokenValidated = async ctx =>
            {
                var sub = ctx.Principal?.FindFirst(AuthClaimTypes.Subject)?.Value;
                var stamp = ctx.Principal?.FindFirst(AuthClaimTypes.SecurityStamp)?.Value;
                var auth = ctx.HttpContext.RequestServices.GetRequiredService<IAuthService>();

                if (!Guid.TryParse(sub, out var userId) || stamp is null
                    || !await auth.IsSessionValidAsync(userId, stamp))
                    ctx.Fail("Session is no longer valid.");
            },
            OnChallenge = async ctx =>
            {
                ctx.HandleResponse();
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsJsonAsync(ApiResponse.Fail("Unauthorized"));
            },
            OnForbidden = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return ctx.Response.WriteAsJsonAsync(ApiResponse.Fail("Forbidden"));
            },
        };
    });

// --- Authorization: AdminOnly (global role) + resource-based project policies (TASKS.md §8) ---
builder.Services.AddAuthorization(Policies.Register);
builder.Services.AddSingleton<IAuthorizationHandler, ProjectRoleHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, AuthorOrProjectOwnerHandler>();

// --- MVC + validation ---
builder.Services
    .AddControllers(o => o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true)
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter())) // enums as strings
    .ConfigureApiBehaviorOptions(o =>
        o.InvalidModelStateResponseFactory = ctx => new BadRequestObjectResult(ApiResponse.Fail(
            ctx.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).Distinct())));
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();

// --- Health + Swagger ---
builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "TaskTracker API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Paste the access token (without the 'Bearer ' prefix).",
    });
    c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", doc)] = [],
    });
    OptionalSchemas.Register(c); // show PATCH fields as their plain types, not as Optional<T> objects
});

var app = builder.Build();

// Development: apply migrations + seed demo data (Section 10). Every environment: ensure roles exist.
await DbInitializer.InitializeAsync(app.Services, isDevelopment: app.Environment.IsDevelopment());

// Unhandled exceptions -> 500 in the standard envelope (no stack traces or request data in the body).
app.UseExceptionHandler(e => e.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    Log.Error(ex, "Unhandled exception on {Path}", ctx.Request.Path);
    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await ctx.Response.WriteAsJsonAsync(ApiResponse.Fail("An unexpected error occurred."));
}));

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapControllers();

app.Run();

// Exposed for WebApplicationFactory<Program> in integration tests.
public partial class Program;

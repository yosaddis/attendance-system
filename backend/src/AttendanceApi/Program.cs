using System.Text;
using AttendanceApi.Auth;
using AttendanceApi.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<AttendanceApi.Services.ITemplateCipher, AttendanceApi.Services.AesTemplateCipher>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Issuer"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
        };
    })
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, AttendanceApi.Auth.StationKeyAuthHandler>(
        AttendanceApi.Auth.StationKeySchemes.Name, _ => { });

builder.Services.AddAuthorization(options => options.AddAttendancePolicies());

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler(exceptionApp => exceptionApp.Run(async context =>
{
    var exceptionFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
    if (exceptionFeature is not null)
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(exceptionFeature.Error, "Unhandled exception processing {Method} {Path}",
            context.Request.Method, context.Request.Path);
    }

    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    context.Response.ContentType = "application/problem+json";
    await context.Response.WriteAsJsonAsync(new Microsoft.AspNetCore.Mvc.ProblemDetails
    {
        Status = StatusCodes.Status500InternalServerError,
        Title = "An unexpected error occurred.",
    });
}));

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AttendanceApi.Data.AppDbContext>();
    db.Database.Migrate();

    var operatorEmail = builder.Configuration["Operator:Email"];
    var operatorPassword = builder.Configuration["Operator:Password"];
    if (!string.IsNullOrEmpty(operatorEmail) && !string.IsNullOrEmpty(operatorPassword)
        && !db.Users.Any(u => u.Email == operatorEmail))
    {
        var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<AttendanceApi.Entities.User>();
        var user = new AttendanceApi.Entities.User
        {
            Email = operatorEmail,
            PasswordHash = "",
            Role = AttendanceApi.Entities.UserRole.Operator,
        };
        user.PasswordHash = hasher.HashPassword(user, operatorPassword);
        db.Users.Add(user);
        db.SaveChanges();
    }
}

app.Run();

public partial class Program { }

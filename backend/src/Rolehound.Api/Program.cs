using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddHttpDefaults();

var app = builder.Build();

app.UseHttpDefaults();
app.MapHealthEndpoints();

app.Run();

// Exposed so integration tests can host the API with WebApplicationFactory.
public partial class Program;

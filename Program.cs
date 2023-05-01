using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<ForwardedHeadersOptions>(
    options => options.ForwardedHeaders = ForwardedHeaders.All
);

var app = builder.Build();
app.UseForwardedHeaders();
app.UseWebSockets();

app.MapGet("/", () => "Hello World!");

app.Run();

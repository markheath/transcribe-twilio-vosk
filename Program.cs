using Microsoft.AspNetCore.HttpOverrides;
using System.Net.WebSockets;
using System.Text.Json;
using Twilio.AspNet.Core;
using Twilio.TwiML;
using Vosk;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<ForwardedHeadersOptions>(
    options => options.ForwardedHeaders = ForwardedHeaders.All
);
builder.Services.AddScoped<AudioConverter>();
builder.Services.AddScoped<VoskRecognizer>(_ =>
{
    // You can set to -1 to disable logging messages
    Vosk.Vosk.SetLogLevel(-1);
    var model = new Model("model");
    var spkModel = new SpkModel("model-spk");
    var recognizer = new VoskRecognizer(model, 16000.0f);
    recognizer.SetSpkModel(spkModel);
    recognizer.SetMaxAlternatives(0);
    recognizer.SetWords(true);
    return recognizer;
});

var app = builder.Build();
app.UseForwardedHeaders();
app.UseWebSockets();

app.MapGet("/", () => "Hello World!");

app.MapPost("/voice", (HttpRequest request) =>
{
    var response = new VoiceResponse();
    var connect = new Twilio.TwiML.Voice.Connect();
    connect.Stream(url: $"wss://{request.Host}/stream");
    response.Append(connect);
    return Results.Extensions.TwiML(response);
});

app.MapGet("/stream", async (HttpContext context, IHostApplicationLifetime appLifetime) =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        await Echo(webSocket, context.RequestServices);
    }
    else
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
    }
});

async Task Echo(
    WebSocket webSocket,
    IServiceProvider serviceProvider
)
{
    var appLifetime = serviceProvider.GetRequiredService<IHostApplicationLifetime>();
    var audioConverter = serviceProvider.GetRequiredService<AudioConverter>();
    var recognizer = serviceProvider.GetRequiredService<VoskRecognizer>();

    var buffer = new byte[1024 * 4];
    var receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

    while (!receiveResult.CloseStatus.HasValue &&
           !appLifetime.ApplicationStopping.IsCancellationRequested)
    {
        using var jsonDocument = JsonSerializer.Deserialize<JsonDocument>(buffer.AsSpan(0, receiveResult.Count));
        var eventMessage = jsonDocument.RootElement.GetProperty("event").GetString();

        switch (eventMessage)
        {
            case "connected":
                Console.WriteLine("Event: connected");
                break;
            case "start":
                Console.WriteLine("Event: start");
                var streamSid = jsonDocument.RootElement.GetProperty("streamSid").GetString();
                Console.WriteLine($"StreamId: {streamSid}");
                break;
            case "media":
                var payload = jsonDocument.RootElement.GetProperty("media").GetProperty("payload").GetString();
                byte[] data = Convert.FromBase64String(payload);
                var (converted, convertedLength) = audioConverter.ConvertBuffer(data);
                if (recognizer.AcceptWaveform(converted, convertedLength))
                {
                    var json = recognizer.Result();
                    var jsonDoc = JsonSerializer.Deserialize<JsonDocument>(json);
                    Console.WriteLine(jsonDoc.RootElement.GetProperty("text").GetString());
                }
                else
                {
                    var json = recognizer.PartialResult();
                    var jsonDoc = JsonSerializer.Deserialize<JsonDocument>(recognizer.PartialResult());
                    //Console.WriteLine(jsonDoc.RootElement.GetProperty("partial").GetString());
                }
                break;
            case "stop":
                Console.WriteLine("Event: stop");
                break;
        }

        receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
    }

    if (receiveResult.CloseStatus.HasValue)
    {
        await webSocket.CloseAsync(
            receiveResult.CloseStatus.Value,
            receiveResult.CloseStatusDescription,
            CancellationToken.None);
    }
    else if (appLifetime.ApplicationStopping.IsCancellationRequested)
    {
        await webSocket.CloseAsync(
            WebSocketCloseStatus.EndpointUnavailable,
            "Server shutting down",
            CancellationToken.None);
    }
}

app.Run();

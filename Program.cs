using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.HttpOverrides;
using Twilio.AspNet.Core;
using Twilio.TwiML;
using Vosk;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<ForwardedHeadersOptions>(
    options => options.ForwardedHeaders = ForwardedHeaders.All
);

var app = builder.Build();

app.UseForwardedHeaders();
app.UseWebSockets();

AudioConverter converter = new AudioConverter();

// You can set to -1 to disable logging messages
Vosk.Vosk.SetLogLevel(-1);
var model = new Model("model");
var spkModel = new SpkModel("model-spk");
var rec = new VoskRecognizer(model, 16000.0f);
rec.SetSpkModel(spkModel);
rec.SetMaxAlternatives(0);
rec.SetWords(true);


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
        await Echo(webSocket, appLifetime);
    }
    else
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
    }
});

async Task Echo(WebSocket webSocket, IHostApplicationLifetime appLifetime)
{
    var buffer = new byte[1024 * 4];
    var receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);


    while (!receiveResult.CloseStatus.HasValue &&
           !appLifetime.ApplicationStopping.IsCancellationRequested)
    {
        using var jsonDocument = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, receiveResult.Count));
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
                if (payload == null) throw new InvalidOperationException("Couldn't parse audio payload from media event");
                byte[] data = Convert.FromBase64String(payload);

                var (converted, convertedLength) = converter.ConvertBuffer(data);

                if (rec.AcceptWaveform(converted, convertedLength))
                {
                    var json = rec.Result();
                    var jsonDoc = JsonSerializer.Deserialize<JsonDocument>(json);
                    if (jsonDoc == null) throw new InvalidOperationException("Couldn't parse JSON result from Vosk");
                    Console.WriteLine(jsonDoc.RootElement.GetProperty("text").GetString());
                    // result includes confidence for each word and start times
                }
                else
                {
                    var json = rec.PartialResult();
                    var jsonDoc = JsonSerializer.Deserialize<JsonDocument>(rec.PartialResult());
                    //Console.WriteLine(jsonDoc.RootElement.GetProperty("partial").GetString());
                    // note: we get lots of partials very fast and they build on the previous one
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

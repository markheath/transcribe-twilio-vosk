using Microsoft.AspNetCore.HttpOverrides;
using Twilio.AspNet.Core;
using Twilio.TwiML;
using Twilio.TwiML.Voice;
using Vosk;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<ForwardedHeadersOptions>(
    options => options.ForwardedHeaders = ForwardedHeaders.All
);

var app = builder.Build();
app.UseForwardedHeaders();
app.UseWebSockets();

// You can set to -1 to disable logging messages
Vosk.Vosk.SetLogLevel(0);
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
    var connect = new Connect();
    connect.Stream(url: $"wss://{request.Host}/stream");
    response.Append(connect);
    return Results.Extensions.TwiML(response);
});

app.Run();

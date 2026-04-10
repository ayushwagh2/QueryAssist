using QueryAssist.Services;
using QueryAssist.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddProblemDetails();

builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection("Gemini"));

builder.Services.AddHttpClient<AiAgentService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});

builder.Services.AddScoped<IAiAgentService, AiAgentService>();
builder.Services.AddScoped<ISqlExecutorService, SqlExecutorService>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapControllers();

app.Run();

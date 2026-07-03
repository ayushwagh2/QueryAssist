using QueryAssist.Services;
using QueryAssist.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddProblemDetails();

builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection("Gemini"));
builder.Services.Configure<EmbeddingOptions>(builder.Configuration.GetSection("Embedding"));

builder.Services.AddHttpClient<AiAgentService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});

builder.Services.AddHttpClient<EmbeddingService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(120);
});

builder.Services.AddScoped<IAiAgentService, AiAgentService>();
builder.Services.AddScoped<ISqlExecutorService, SqlExecutorService>();
builder.Services.AddSingleton<IEmbeddingService, EmbeddingService>();
builder.Services.AddSingleton<ISchemaEmbeddingService, SchemaEmbeddingService>();

builder.Services.AddSingleton<SpEmbeddingQueue>();
builder.Services.AddHostedService<SpEmbeddingBackgroundWorker>();

builder.Services.AddSingleton<TableEmbeddingQueue>();
builder.Services.AddHostedService<TableEmbeddingBackgroundWorker>();

builder.Services.AddSingleton<FunctionEmbeddingQueue>();
builder.Services.AddHostedService<FunctionEmbeddingBackgroundWorker>();

var app = builder.Build();

// DO NOT pre-initialize schema embeddings on startup
// Let it initialize lazily on first /ask request

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapControllers();

app.Run();

using QueryAssist.Services;
using QueryAssist.Options;

var builder = WebApplication.CreateBuilder(args);
//this changes are there to test if my PIPE is working
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

// Register AI Tools
builder.Services.AddScoped<QueryAssist.Services.Tools.IAiTool, QueryAssist.Services.Tools.ExplainTableTool>();
builder.Services.AddScoped<QueryAssist.Services.Tools.IAiTool, QueryAssist.Services.Tools.ExplainSpTool>();
builder.Services.AddScoped<QueryAssist.Services.Tools.IAiTool, QueryAssist.Services.Tools.ExplainFunctionTool>();
builder.Services.AddScoped<QueryAssist.Services.Tools.IAiTool, QueryAssist.Services.Tools.FindRelationshipsTool>();
builder.Services.AddScoped<QueryAssist.Services.Tools.IAiTool, QueryAssist.Services.Tools.GenerateSqlTool>();

// Register Orchestrator
builder.Services.AddScoped<IAskOrchestratorService, AskOrchestratorService>();

builder.Services.AddSingleton<SpEmbeddingQueue>();
builder.Services.AddHostedService<SpEmbeddingBackgroundWorker>();

builder.Services.AddSingleton<TableEmbeddingQueue>();
builder.Services.AddHostedService<TableEmbeddingBackgroundWorker>();

builder.Services.AddSingleton<FunctionEmbeddingQueue>();
builder.Services.AddHostedService<FunctionEmbeddingBackgroundWorker>();

builder.Services.AddSingleton<RelationshipEmbeddingQueue>();
builder.Services.AddHostedService<RelationshipEmbeddingBackgroundWorker>();

var app = builder.Build();

// DO NOT pre-initialize schema embeddings on startup
// Let it initialize lazily on first /ask request

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapControllers();

app.Run();

using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace QueryAssist.Services.Tools;

public interface IAiTool
{
    string Name { get; }
    string Description { get; }
    JsonObject Parameters { get; }
    
    Task<string> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken);
}

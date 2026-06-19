using System.Threading.Tasks;

namespace McpBridge.Middleware;

public interface IMiddleware
{
	Task InvokeAsync( McpContext ctx, MiddlewareDelegate next );
}

public delegate Task MiddlewareDelegate();

public class McpContext
{
	public int? Id { get; set; }
	public string Method { get; set; }
	public string Body { get; set; }
	public string SessionId { get; set; }
	public string Response { get; set; }
	public bool Handled { get; set; }
	public System.Collections.Generic.Dictionary<string, object> Items { get; } = new();
}

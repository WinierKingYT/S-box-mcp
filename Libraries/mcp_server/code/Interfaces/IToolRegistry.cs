using System.Collections.Generic;
using Sandbox;

namespace McpBridge.Interfaces;

public interface IToolRegistry
{
	void EnsureInitialized();
	bool TryGet( string name, out MethodDescription method, out object instance );
	object RunSingle( string method, string paramsJson );
	List<(string name, string description, string group)> ListAll();
	Dictionary<string, MethodDescription> GetToolsDict();
}

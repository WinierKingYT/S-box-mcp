using System.Collections.Generic;

namespace McpBridge.Interfaces;

public interface IMacroStore
{
	MacroData Get( string name );
	void Save( MacroData macro );
	void Delete( string name );
	List<MacroData> List();
}

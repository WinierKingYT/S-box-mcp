using System.Collections.Generic;

namespace McpBridge.Interfaces;

public interface ISnapshotStore
{
	string Save( string name, WorldSnapshot snapshot );
	WorldSnapshot Load( string name );
	void Delete( string name );
	List<string> List();
}

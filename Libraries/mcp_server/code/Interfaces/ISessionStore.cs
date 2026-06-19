using System.Collections.Generic;

namespace McpBridge.Interfaces;

public interface ISessionStore
{
	SessionData Get( string id );
	void Save( SessionData data );
	void Remove( string id );
	List<SessionData> List();
	void Cleanup();
	void Flush();
}

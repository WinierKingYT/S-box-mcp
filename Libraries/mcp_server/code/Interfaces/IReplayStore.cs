using System.Collections.Generic;

namespace McpBridge.Interfaces;

public interface IReplayStore
{
	void Record( string method, string input, string output, long durationMs, bool success = true );
	void Clear();
	List<ReplayRecord> GetHistory( int count = 50 );
	void StartPlayback();
	ReplayRecord NextPlayback();
	void StopPlayback();
	bool ExportAsScript( string path );
	Dictionary<string, object> GetAnalytics();
}

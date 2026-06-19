using McpBridge.Interfaces;
using System.Collections.Generic;

namespace McpBridge;

public static class ServiceFactory
{
	public static ISessionStore SessionStore { get; } = new SessionStoreWrapper();
	public static IMacroStore MacroStore { get; } = new MacroStoreWrapper();
	public static ISnapshotStore SnapshotStore { get; } = new SnapshotStoreWrapper();
	public static IReplayStore ReplayStore { get; } = new ReplayStoreWrapper();
	public static IConfigStore ConfigStore { get; } = new ConfigStoreWrapper();

	private class SessionStoreWrapper : ISessionStore
	{
		public SessionData Get( string id ) => McpSession.Get( id );
		public void Save( SessionData data ) { McpSession.MarkDirty(); }
		public void Remove( string id ) => McpSession.Remove( id );
		public List<SessionData> List() => McpSession.List();
		public void Cleanup() => McpSession.Cleanup();
		public void Flush() => McpSession.Flush();
	}

	private class MacroStoreWrapper : IMacroStore
	{
		public MacroData Get( string name ) => McpMacroManager.Get( name );
		public void Save( MacroData macro ) => McpMacroManager.Save( macro );
		public void Delete( string name ) => McpMacroManager.Delete( name );
		public List<MacroData> List() => McpMacroManager.List();
	}

	private class SnapshotStoreWrapper : ISnapshotStore
	{
		public string Save( string name, WorldSnapshot snapshot ) => McpSnapshotManager.Save( name, snapshot );
		public WorldSnapshot Load( string name ) => McpSnapshotManager.Load( name );
		public void Delete( string name ) => McpSnapshotManager.Delete( name );
		public List<string> List() => McpSnapshotManager.List();
	}

	private class ReplayStoreWrapper : IReplayStore
	{
		public void Record( string m, string i, string o, long d, bool s = true ) => McpReplay.Record( m, i, o, d, s );
		public void Clear() => McpReplay.Clear();
		public List<ReplayRecord> GetHistory( int count = 50 ) => McpReplay.GetHistory( count );
		public void StartPlayback() => McpReplay.StartPlayback();
		public ReplayRecord NextPlayback() => McpReplay.NextPlayback();
		public void StopPlayback() => McpReplay.StopPlayback();
		public bool ExportAsScript( string path ) => McpReplay.ExportAsScript( path );
		public Dictionary<string, object> GetAnalytics() => McpReplay.GetAnalytics();
	}

	private class ConfigStoreWrapper : IConfigStore
	{
		private McpConfig _cached;
		private McpConfig Config => _cached ??= McpConfig.Load();
		public int Port => Config.Port;
		public int TimeoutSeconds => Config.TimeoutSeconds;
		public int CleanupDays => Config.CleanupDays;
		public int RateLimit => 30;
		public int ToolTimeoutSeconds => 30;
		public void Validate() => Config.Validate();
	}
}

using System;
using System.IO;
using System.Linq;

namespace Editor;

internal sealed class McpControlCenterPanel : Widget
{
	private static McpControlCenterPanel _instance;
	private static int _frame;

	public McpControlCenterPanel( Widget parent = null ) : base( parent )
	{
		_instance = this;
		MinimumWidth = 420;
		Layout = Layout.Column();
		Layout.Margin = 12;
		Layout.Spacing = 8;
		Refresh();
	}

	[Menu( "Editor", "MCP Server/Control Center", "smart_toy" )]
	public static void Open()
	{
		var dockManager = EditorWindow.DockManager;
		if ( dockManager == null ) return;
		var existing = dockManager.FindDockWidget( "MCP Control Center" );
		if ( existing == null )
		{
			_instance = new McpControlCenterPanel();
			dockManager.AddDock(
				"MCP Control Center",
				"smart_toy",
				_instance,
				DockArea.Right );
		}
		dockManager.SetDockState( "MCP Control Center", true );
		if ( _instance != null ) dockManager.RaiseDock( _instance );
	}

	public static void RefreshOpenPanel()
	{
		if ( _instance == null ) return;
		_frame++;
		if ( _frame % 30 == 0 ) _instance.Refresh();
	}

	private void Refresh()
	{
		Layout.Clear( true );
		var runtime = McpRuntimeState.Snapshot();
		var config = McpConfig.Current;
		var snapshot = McpControlCenterViewModel.Build(
			McpEditorServer.Approvals.PendingItems(),
			McpEditorServer.Activity.Snapshot() );
		var pending = snapshot.Pending;

		Layout.Add( new Label.Title( "MCP Control Center" ) );
		Layout.Add( new Label.Subtitle( "Connection" ) );
		Layout.Add( new Label.Body(
			$"{(McpEditorServer.IsRunning ? "Running" : "Stopped")} · localhost:{config.Port} · server {runtime.ServerVersion} · SSE sessions {McpEditorServer.ActiveSseSessions}" ) );
		Layout.Add( new Label.Body(
			$"Client: {runtime.ClientName} {runtime.ClientVersion} · Protocol: {runtime.ProtocolVersion} · {runtime.Compatibility}" ) );

		var controls = Layout.AddRow();
		var startStop = controls.Add( new Button(
			McpEditorServer.IsRunning ? "Stop" : "Start",
			McpEditorServer.IsRunning ? "stop" : "play_arrow" ) );
		startStop.Clicked += () =>
		{
			if ( McpEditorServer.IsRunning ) McpEditorServer.Stop();
			else McpEditorServer.Start();
			Refresh();
		};
		var reload = controls.Add( new Button( "Reload Config", "refresh" ) );
		reload.Clicked += () =>
		{
			McpConfig.Reload();
			Refresh();
		};
		var clear = controls.Add( new Button( "Clear History", "delete_sweep" ) );
		clear.Clicked += () =>
		{
			McpEditorServer.Activity.Clear();
			Refresh();
		};

		Layout.Add( new Label.Subtitle( $"Pending approvals ({pending.Count})" ) );
		if ( pending.Count == 0 )
		{
			Layout.Add( new Label.Body( "No changes are waiting for approval." ) );
		}
		foreach ( var request in pending )
		{
			Layout.Add( new Label.Body(
				$"{request.Summary}\n{string.Join( "\n", request.Preview )}\nExpires {request.ExpiresAt.LocalDateTime:T}" ) );
			var row = Layout.AddRow();
			var approve = row.Add( new Button.Primary( "Approve", "check" ) );
			approve.Clicked += () =>
			{
				McpEditorServer.Approvals.Approve( request.Id );
				Refresh();
			};
			var reject = row.Add( new Button.Danger( "Reject", "close" ) );
			reject.Clicked += () =>
			{
				McpEditorServer.Approvals.Reject( request.Id );
				Refresh();
			};
		}

		Layout.Add( new Label.Subtitle( "Request history" ) );
		foreach ( var entry in snapshot.History )
		{
			Layout.Add( new Label.Body(
				$"{entry.Timestamp.LocalDateTime:T} · {entry.Tool} · {entry.Status} · {entry.DurationMs} ms\n{entry.Summary}" ) );
		}

		Layout.Add( new Label.Subtitle( "Errors" ) );
		var errors = snapshot.Errors.Take( 20 ).ToArray();
		if ( errors.Length == 0 )
			Layout.Add( new Label.Body( "No recorded errors." ) );
		foreach ( var entry in errors )
			Layout.Add( new Label.Body(
				$"{entry.Timestamp.LocalDateTime:T} · {entry.Tool}: {entry.Error}" ) );

		var migrations = McpConfigMigration.Snapshot();
		if ( migrations.Count > 0 )
		{
			Layout.Add( new Label.Subtitle( "Configuration migration" ) );
			foreach ( var migration in migrations.TakeLast( 5 ) )
				Layout.Add( new Label.Body(
					$"v{migration.FromVersion} → v{migration.ToVersion}: {migration.Message}" ) );
			var openMigrationFolder = Layout.Add( new Button(
				"Open Migration Folder",
				"folder_open" ) );
			openMigrationFolder.Clicked += () =>
			{
				var directory = Path.GetDirectoryName( migrations[^1].Path );
				if ( !string.IsNullOrWhiteSpace( directory ) )
					EditorUtility.OpenFolder( directory );
			};
		}
		Layout.AddStretchCell();
	}
}

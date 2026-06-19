using Editor;
using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Editor;

[Dock( "Editor", "MCP Server Dashboard", "api" )]
public class McpDashboardWidget : Widget
{
	// ── Tab Containers ────────────────────────────────────────────────────
	private Widget _statusContainer;
	private Widget _toolsContainer;
	private Widget _logsContainer;
	private Widget _sceneContainer;
	private Widget _trafficContainer;
	private Widget _resourceContainer;

	// ── Status Page ───────────────────────────────────────────────────────
	private Label _statusLabel;
	private Label _sessionCountLabel;
	private Label _sessionsListLabel;
	private LineEdit _portEdit;
	private LineEdit _apiKeyEdit;
	private Button _startBtn;
	private Button _stopBtn;
	private Button _saveBtn;

	// ── Playground Page ───────────────────────────────────────────────────
	private LineEdit _toolSearchEdit;
	private Label _selectedToolLabel;
	private LineEdit _toolArgsEdit;
	private Button _runToolBtn;
	private Label _toolResultLabel;
	private Widget _matchingToolsWidget;
	private Button[] _matchButtons = new Button[5];
	private string[] _matchToolNames = new string[5];
	private Dictionary<string, (string description, string group, string schema)> _allTools = new();
	private string _selectedToolName = "";

	// ── Logs Page ─────────────────────────────────────────────────────────
	private Label _logsLabel;
	private string _logFilter = "ALL";

	// ── Scene Explorer Page ───────────────────────────────────────────────
	private LineEdit _sceneSearchEdit;
	private Label _selectedObjLabel;
	private Widget _matchingObjectsWidget;
	private Button[] _objMatchButtons = new Button[5];
	private string[] _objMatchGuids = new string[5];

	// ── Traffic & Replay Page ─────────────────────────────────────────────
	private Label _trafficStatsLabel;
	private Widget _trafficListWidget;
	private Button[] _trafficButtons = new Button[15];
	private int[] _trafficIndices = new int[15];
	private Label _trafficDetailLabel;
	private List<McpBridge.ReplayRecord> _lastTrafficSnapshot = new();

	// ── Resource Explorer Page ────────────────────────────────────────────
	private Widget _resourceListWidget;
	private Button[] _resourceButtons;
	private Label _resourceDescLabel;
	private LineEdit _resourceArgEdit;
	private Label _resourceContentLabel;
	private List<(string uri, string name, string mimeType, string description, bool isTemplate)> _resourceDefs = new();

	// ─────────────────────────────────────────────────────────────────────
	public McpDashboardWidget( Widget parent ) : base( parent )
	{
		Layout = Editor.Layout.Column();
		Layout.Margin = 10;
		Layout.Spacing = 8;

		// ── Tab Navigation Strip ─────────────────────────────────────────
		var tabStrip = Editor.Layout.Row();
		tabStrip.Spacing = 4;

		void AddTab( string label, int idx )
		{
			var btn = new Button( label, this );
			btn.Clicked += () => SetTab( idx );
			tabStrip.Add( btn );
		}

		AddTab( "Status",    0 );
		AddTab( "Playground",1 );
		AddTab( "Scene",     2 );
		AddTab( "Logs",      3 );
		AddTab( "Traffic",   4 );
		AddTab( "Resources", 5 );
		Layout.Add( tabStrip );

		// ── Create Containers ────────────────────────────────────────────
		Widget MakeContainer()
		{
			var w = new Widget( this );
			w.Layout = Editor.Layout.Column();
			w.Layout.Spacing = 6;
			Layout.Add( w );
			return w;
		}

		_statusContainer   = MakeContainer();
		_toolsContainer    = MakeContainer();
		_sceneContainer    = MakeContainer();
		_logsContainer     = MakeContainer();
		_trafficContainer  = MakeContainer();
		_resourceContainer = MakeContainer();

		// ════════════════════════════════════════════════════════════════
		// SEKME 0 — Status & Config
		// ════════════════════════════════════════════════════════════════
		_statusLabel       = new Label( "Status: Checking...", _statusContainer );
		_sessionCountLabel = new Label( "Active Connections: 0", _statusContainer );
		_statusContainer.Layout.Add( _statusLabel );
		_statusContainer.Layout.Add( _sessionCountLabel );

		var controlRow = Editor.Layout.Row();
		controlRow.Spacing = 5;
		_startBtn = new Button( "Start Server", _statusContainer );
		_startBtn.Clicked += () => OnStartClicked();
		controlRow.Add( _startBtn );
		_stopBtn = new Button( "Stop Server", _statusContainer );
		_stopBtn.Clicked += () => OnStopClicked();
		controlRow.Add( _stopBtn );
		_statusContainer.Layout.Add( controlRow );

		_statusContainer.Layout.Add( new Label( "Configuration Settings", _statusContainer ) );

		var portRow = Editor.Layout.Row();
		portRow.Add( new Label( "Port: ", _statusContainer ) );
		_portEdit = new LineEdit( _statusContainer );
		portRow.Add( _portEdit );
		_statusContainer.Layout.Add( portRow );

		var keyRow = Editor.Layout.Row();
		keyRow.Add( new Label( "API Key: ", _statusContainer ) );
		_apiKeyEdit = new LineEdit( _statusContainer );
		keyRow.Add( _apiKeyEdit );
		_statusContainer.Layout.Add( keyRow );

		_saveBtn = new Button( "Save & Apply Settings", _statusContainer );
		_saveBtn.Clicked += () => OnSaveClicked();
		_statusContainer.Layout.Add( _saveBtn );

		_statusContainer.Layout.Add( new Label( "Active Sessions:", _statusContainer ) );
		_sessionsListLabel = new Label( "(No active connections)", _statusContainer );
		_statusContainer.Layout.Add( _sessionsListLabel );

		// ════════════════════════════════════════════════════════════════
		// SEKME 1 — Playground
		// ════════════════════════════════════════════════════════════════
		var searchRow = Editor.Layout.Row();
		searchRow.Add( new Label( "Search: ", _toolsContainer ) );
		_toolSearchEdit = new LineEdit( _toolsContainer );
		_toolSearchEdit.PlaceholderText = "Type tool name...";
		_toolSearchEdit.TextChanged += OnSearchTextChanged;
		searchRow.Add( _toolSearchEdit );
		_toolsContainer.Layout.Add( searchRow );

		_matchingToolsWidget = new Widget( _toolsContainer );
		var matchLayout = Editor.Layout.Column();
		matchLayout.Spacing = 2;
		_matchingToolsWidget.Layout = matchLayout;
		for ( int i = 0; i < 5; i++ )
		{
			int idx = i;
			_matchButtons[i] = new Button( "", _matchingToolsWidget );
			_matchButtons[i].Clicked += () => OnMatchButtonClicked( idx );
			_matchButtons[i].Hide();
			matchLayout.Add( _matchButtons[i] );
		}
		_toolsContainer.Layout.Add( _matchingToolsWidget );

		_selectedToolLabel = new Label( "No tool selected. Search and select above.", _toolsContainer );
		_toolsContainer.Layout.Add( _selectedToolLabel );

		var argsRow = Editor.Layout.Row();
		argsRow.Add( new Label( "Arguments (JSON): ", _toolsContainer ) );
		_toolArgsEdit = new LineEdit( _toolsContainer );
		argsRow.Add( _toolArgsEdit );
		_toolsContainer.Layout.Add( argsRow );

		_runToolBtn = new Button( "Execute Tool", _toolsContainer );
		_runToolBtn.Clicked += () => OnExecuteToolClicked();
		_toolsContainer.Layout.Add( _runToolBtn );

		_toolResultLabel = new Label( "", _toolsContainer );
		_toolsContainer.Layout.Add( _toolResultLabel );

		// ════════════════════════════════════════════════════════════════
		// SEKME 2 — Interactive Scene Explorer
		// ════════════════════════════════════════════════════════════════
		var sceneSearchRow = Editor.Layout.Row();
		sceneSearchRow.Add( new Label( "Search Object: ", _sceneContainer ) );
		_sceneSearchEdit = new LineEdit( _sceneContainer );
		_sceneSearchEdit.PlaceholderText = "Type GameObject name...";
		_sceneSearchEdit.TextChanged += OnSceneSearchTextChanged;
		sceneSearchRow.Add( _sceneSearchEdit );
		_sceneContainer.Layout.Add( sceneSearchRow );

		_matchingObjectsWidget = new Widget( _sceneContainer );
		var matchObjLayout = Editor.Layout.Column();
		matchObjLayout.Spacing = 2;
		_matchingObjectsWidget.Layout = matchObjLayout;
		for ( int i = 0; i < 5; i++ )
		{
			int idx = i;
			_objMatchButtons[i] = new Button( "", _matchingObjectsWidget );
			_objMatchButtons[i].Clicked += () => OnObjectMatchButtonClicked( idx );
			_objMatchButtons[i].Hide();
			matchObjLayout.Add( _objMatchButtons[i] );
		}
		_sceneContainer.Layout.Add( _matchingObjectsWidget );

		_selectedObjLabel = new Label( "No GameObject selected. Search and select above.", _sceneContainer );
		_sceneContainer.Layout.Add( _selectedObjLabel );

		// ════════════════════════════════════════════════════════════════
		// SEKME 3 — Logs with Filter Buttons
		// ════════════════════════════════════════════════════════════════
		var logFilterRow = Editor.Layout.Row();
		logFilterRow.Spacing = 4;

		void AddLogFilter( string label, string filter )
		{
			var btn = new Button( label, _logsContainer );
			btn.Clicked += () => { _logFilter = filter; UpdateUI(); };
			logFilterRow.Add( btn );
		}

		AddLogFilter( "All Logs", "ALL" );
		AddLogFilter( "Info",     "INFO" );
		AddLogFilter( "Warning",  "WARNING" );
		AddLogFilter( "Error",    "ERROR" );

		_logsContainer.Layout.Add( logFilterRow );
		_logsLabel = new Label( "No logs captured yet.", _logsContainer );
		_logsContainer.Layout.Add( _logsLabel );

		// ════════════════════════════════════════════════════════════════
		// SEKME 4 — Traffic & Replay Inspector
		// ════════════════════════════════════════════════════════════════
		_trafficStatsLabel = new Label( "Total: 0  |  Avg: 0ms  |  Errors: 0", _trafficContainer );
		_trafficContainer.Layout.Add( _trafficStatsLabel );

		var trafficCtrlRow = Editor.Layout.Row();
		trafficCtrlRow.Spacing = 4;
		var trafficRefreshBtn = new Button( "Refresh", _trafficContainer );
		trafficRefreshBtn.Clicked += () => RefreshTrafficList();
		trafficCtrlRow.Add( trafficRefreshBtn );
		var trafficClearBtn = new Button( "Clear History", _trafficContainer );
		trafficClearBtn.Clicked += () => { McpEditorServer.ClearReplayHistory(); RefreshTrafficList(); };
		trafficCtrlRow.Add( trafficClearBtn );
		var trafficExportBtn = new Button( "Export Script", _trafficContainer );
		trafficExportBtn.Clicked += () => McpEditorServer.ExportReplayScript( "mcp_replay_export.txt" );
		trafficCtrlRow.Add( trafficExportBtn );
		_trafficContainer.Layout.Add( trafficCtrlRow );

		_trafficContainer.Layout.Add( new Label( "Recent Calls (click to inspect):", _trafficContainer ) );

		_trafficListWidget = new Widget( _trafficContainer );
		var trafficListLayout = Editor.Layout.Column();
		trafficListLayout.Spacing = 2;
		_trafficListWidget.Layout = trafficListLayout;
		for ( int i = 0; i < 15; i++ )
		{
			int idx = i;
			_trafficButtons[i] = new Button( "", _trafficListWidget );
			_trafficButtons[i].Clicked += () => OnTrafficButtonClicked( idx );
			_trafficButtons[i].Hide();
			trafficListLayout.Add( _trafficButtons[i] );
		}
		_trafficContainer.Layout.Add( _trafficListWidget );

		_trafficContainer.Layout.Add( new Label( "Selected Call Detail:", _trafficContainer ) );
		_trafficDetailLabel = new Label( "Select a call above to inspect.", _trafficContainer );
		_trafficContainer.Layout.Add( _trafficDetailLabel );

		// ════════════════════════════════════════════════════════════════
		// SEKME 5 — Resource Explorer
		// ════════════════════════════════════════════════════════════════
		_resourceDefs    = McpEditorServer.GetResourceDefinitions();
		_resourceButtons = new Button[_resourceDefs.Count];

		_resourceListWidget = new Widget( _resourceContainer );
		var resListLayout = Editor.Layout.Column();
		resListLayout.Spacing = 2;
		_resourceListWidget.Layout = resListLayout;

		for ( int i = 0; i < _resourceDefs.Count; i++ )
		{
			int idx = i;
			var def = _resourceDefs[i];
			_resourceButtons[i] = new Button( def.isTemplate ? $"[Template] {def.name}" : def.name, _resourceListWidget );
			_resourceButtons[i].Clicked += () => OnResourceButtonClicked( idx );
			resListLayout.Add( _resourceButtons[i] );
		}
		_resourceContainer.Layout.Add( _resourceListWidget );

		_resourceDescLabel = new Label( "Select a resource from the list above.", _resourceContainer );
		_resourceContainer.Layout.Add( _resourceDescLabel );

		var resArgRow = Editor.Layout.Row();
		resArgRow.Add( new Label( "Path / Arg: ", _resourceContainer ) );
		_resourceArgEdit = new LineEdit( _resourceContainer );
		_resourceArgEdit.PlaceholderText = "e.g.  Editor/McpDashboardWidget.cs";
		resArgRow.Add( _resourceArgEdit );
		_resourceContainer.Layout.Add( resArgRow );

		var resReadBtn = new Button( "Read / Refresh", _resourceContainer );
		resReadBtn.Clicked += () => OnResourceReadClicked();
		_resourceContainer.Layout.Add( resReadBtn );

		_resourceContentLabel = new Label( "", _resourceContainer );
		_resourceContainer.Layout.Add( _resourceContentLabel );

		// ── Init ─────────────────────────────────────────────────────────
		var config = McpBridge.McpConfig.Load();
		_portEdit.Text   = config.Port.ToString();
		_apiKeyEdit.Text = config.ApiKey;

		_ = LoadAllTools();
		SetTab( 0 );
		_ = UpdateLoop();
	}

	// ── Tab Switching ─────────────────────────────────────────────────────
	private void SetTab( int index )
	{
		_statusContainer.Hide();
		_toolsContainer.Hide();
		_sceneContainer.Hide();
		_logsContainer.Hide();
		_trafficContainer.Hide();
		_resourceContainer.Hide();

		switch ( index )
		{
			case 0: _statusContainer.Show();                              break;
			case 1: _toolsContainer.Show();                               break;
			case 2: _sceneContainer.Show();                               break;
			case 3: _logsContainer.Show();                                break;
			case 4: _trafficContainer.Show(); RefreshTrafficList();       break;
			case 5: _resourceContainer.Show();                            break;
		}
	}

	// ── Tool Loading ──────────────────────────────────────────────────────
	private async Task LoadAllTools()
	{
		try { _allTools = await McpEditorServer.GetToolDescriptionsAsync(); }
		catch { }
	}

	// ── Playground Handlers ───────────────────────────────────────────────
	private void OnSearchTextChanged( string text )
	{
		for ( int i = 0; i < 5; i++ ) { _matchToolNames[i] = ""; _matchButtons[i].Hide(); }
		if ( string.IsNullOrEmpty( text ) ) return;

		var matches = _allTools.Keys
			.Where( k => k.Contains( text, StringComparison.OrdinalIgnoreCase ) )
			.Take( 5 ).ToList();

		for ( int i = 0; i < matches.Count; i++ )
		{
			_matchToolNames[i] = matches[i];
			_matchButtons[i].Text = $"{matches[i]}  ({_allTools[matches[i]].group})";
			_matchButtons[i].Show();
		}
	}

	private void OnMatchButtonClicked( int index )
	{
		var name = _matchToolNames[index];
		if ( string.IsNullOrEmpty( name ) || !_allTools.TryGetValue( name, out var info ) ) return;

		_selectedToolName = name;
		_selectedToolLabel.Text = $"Tool: {name}\nGroup: {info.group}\nDesc: {info.description}";

		var template = "{}";
		if ( !string.IsNullOrEmpty( info.schema ) && info.schema != "{}" )
		{
			try
			{
				using var doc = JsonDocument.Parse( info.schema );
				if ( doc.RootElement.TryGetProperty( "properties", out var props ) && props.ValueKind == JsonValueKind.Object )
				{
					var dict = new Dictionary<string, string>();
					foreach ( var p in props.EnumerateObject() ) dict[p.Name] = "";
					template = JsonSerializer.Serialize( dict );
				}
			}
			catch { }
		}
		_toolArgsEdit.Text    = template;
		_toolResultLabel.Text = "";
	}

	private async void OnExecuteToolClicked()
	{
		if ( string.IsNullOrEmpty( _selectedToolName ) ) return;
		_toolResultLabel.Text = "Executing...";
		try
		{
			var res = await McpEditorServer.ExecuteRegisteredTool( _selectedToolName, _toolArgsEdit.Text );
			_toolResultLabel.Text = "Result:\n" + JsonSerializer.Serialize( res, new JsonSerializerOptions { WriteIndented = true } );
		}
		catch ( Exception ex ) { _toolResultLabel.Text = $"Error: {ex.Message}"; }
	}

	// ── Scene Explorer Handlers ───────────────────────────────────────────
	private void OnSceneSearchTextChanged( string text )
	{
		for ( int i = 0; i < 5; i++ ) { _objMatchGuids[i] = ""; _objMatchButtons[i].Hide(); }

		var scene = Game.ActiveScene;
		if ( scene == null || string.IsNullOrEmpty( text ) ) return;

		var matches = scene.GetAllObjects( true )
			.Where( go => go.IsValid() && (go.Name ?? "").Contains( text, StringComparison.OrdinalIgnoreCase ) )
			.Take( 5 ).ToList();

		for ( int i = 0; i < matches.Count; i++ )
		{
			_objMatchGuids[i] = matches[i].Id.ToString();
			_objMatchButtons[i].Text = matches[i].Name ?? "GameObject";
			_objMatchButtons[i].Show();
		}
	}

	private void OnObjectMatchButtonClicked( int index )
	{
		var guidStr = _objMatchGuids[index];
		if ( string.IsNullOrEmpty( guidStr ) ) return;

		var scene = Game.ActiveScene;
		if ( scene == null ) return;
		if ( !Guid.TryParse( guidStr, out var guid ) ) return;
		var go = scene.Directory.FindByGuid( guid );
		if ( !go.IsValid() ) return;

		_ = McpEditorServer.ExecuteRegisteredTool( "sbox_focus_camera", $"{{\"id\":\"{guidStr}\"}}" );

		var components = go.Components.GetAll<Component>()
			.Where( c => c.IsValid() )
			.Select( c => c.GetType().Name )
			.ToList();

		_selectedObjLabel.Text =
			$"GameObject : {go.Name}\n" +
			$"GUID       : {guidStr}\n" +
			$"Position   : {go.WorldPosition}\n" +
			$"Components :\n  - {string.Join( "\n  - ", components )}";
	}

	// ── Traffic & Replay Handlers ─────────────────────────────────────────
	private void RefreshTrafficList()
	{
		_lastTrafficSnapshot = McpEditorServer.GetReplayHistory( 50 );

		var analytics = McpEditorServer.GetReplayAnalytics();
		var total  = analytics.TryGetValue( "totalCalls",    out var tc ) ? tc : 0;
		var avg    = analytics.TryGetValue( "avgDurationMs", out var av ) ? av : 0;
		var errors = analytics.TryGetValue( "errorCount",    out var ec ) ? ec : 0;
		_trafficStatsLabel.Text = $"Total: {total}  |  Avg: {avg}ms  |  Errors: {errors}";

		var records = _lastTrafficSnapshot.AsEnumerable().Reverse().Take( 15 ).ToList();
		for ( int i = 0; i < 15; i++ )
		{
			if ( i < records.Count )
			{
				var r    = records[i];
				_trafficIndices[i] = _lastTrafficSnapshot.Count - 1 - i;
				var icon = r.Success ? "OK" : "ERR";
				var time = r.Timestamp.ToLocalTime().ToString( "HH:mm:ss" );
				_trafficButtons[i].Text = $"[{icon}]  {time}  {r.Method}  ({r.DurationMs}ms)";
				_trafficButtons[i].Show();
			}
			else
			{
				_trafficButtons[i].Hide();
			}
		}

		_trafficDetailLabel.Text = "Select a call above to inspect.";
	}

	private void OnTrafficButtonClicked( int buttonIndex )
	{
		var recordIndex = _trafficIndices[buttonIndex];
		if ( recordIndex < 0 || recordIndex >= _lastTrafficSnapshot.Count ) return;

		var r = _lastTrafficSnapshot[recordIndex];
		_trafficDetailLabel.Text =
			$"Method   : {r.Method}\n" +
			$"Time     : {r.Timestamp.ToLocalTime():HH:mm:ss.fff}\n" +
			$"Duration : {r.DurationMs}ms\n" +
			$"Status   : {(r.Success ? "Success" : "Failed")}\n\n" +
			$"-- Input --\n{FormatJson( r.Input )}\n\n" +
			$"-- Output --\n{FormatJson( r.Output )}";
	}

	private static string FormatJson( string raw )
	{
		if ( string.IsNullOrEmpty( raw ) ) return "(empty)";
		try
		{
			using var doc = JsonDocument.Parse( raw );
			return JsonSerializer.Serialize( doc.RootElement, new JsonSerializerOptions { WriteIndented = true } );
		}
		catch { return raw; }
	}

	// ── Resource Explorer Handlers ────────────────────────────────────────
	private string _selectedResourceUri = "";

	private void OnResourceButtonClicked( int index )
	{
		if ( index < 0 || index >= _resourceDefs.Count ) return;
		var def = _resourceDefs[index];
		_selectedResourceUri      = def.uri;
		_resourceDescLabel.Text   = $"URI  : {def.uri}\nType : {def.mimeType}\nDesc : {def.description}";
		_resourceContentLabel.Text = "";

		if ( !def.isTemplate )
		{
			_resourceContentLabel.Text = "Loading...";
			try
			{
				var content = McpEditorServer.ReadResourceContent( def.uri );
				_resourceContentLabel.Text = content.Length > 2000 ? content.Substring( 0, 2000 ) + "\n...(truncated)" : content;
			}
			catch ( Exception ex ) { _resourceContentLabel.Text = $"Error: {ex.Message}"; }
		}
		else
		{
			_resourceContentLabel.Text = "Template resource — enter the path in 'Path / Arg' and click Read.";
		}
	}

	private void OnResourceReadClicked()
	{
		if ( string.IsNullOrEmpty( _selectedResourceUri ) ) return;

		var uri = _selectedResourceUri;
		if ( uri.Contains( "{path}" ) )
		{
			var arg = _resourceArgEdit.Text.Trim().Replace( "\\", "/" );
			if ( string.IsNullOrEmpty( arg ) ) { _resourceContentLabel.Text = "Please enter a path first."; return; }
			uri = uri.Replace( "{path}", arg );
		}

		_resourceContentLabel.Text = "Loading...";
		try
		{
			var content = McpEditorServer.ReadResourceContent( uri );
			_resourceContentLabel.Text = content.Length > 2000 ? content.Substring( 0, 2000 ) + "\n...(truncated)" : content;
		}
		catch ( Exception ex ) { _resourceContentLabel.Text = $"Error: {ex.Message}"; }
	}

	// ── Status Handlers ───────────────────────────────────────────────────
	private void OnStartClicked()
	{
		if ( int.TryParse( _portEdit.Text, out var port ) )
			McpEditorServer.Start( port, _apiKeyEdit.Text );
		else
			McpEditorServer.Start( null, _apiKeyEdit.Text );
		UpdateUI();
	}

	private void OnStopClicked()
	{
		McpEditorServer.Stop();
		UpdateUI();
	}

	private void OnSaveClicked()
	{
		var config = McpBridge.McpConfig.Load();
		if ( int.TryParse( _portEdit.Text, out var port ) ) config.Port = port;
		config.ApiKey = _apiKeyEdit.Text;
		McpBridge.McpConfig.Save( config );

		if ( McpEditorServer.IsRunning )
		{
			McpEditorServer.Stop();
			McpEditorServer.Start( config.Port, config.ApiKey );
		}
		UpdateUI();
	}

	// ── UpdateUI ──────────────────────────────────────────────────────────
	private void UpdateUI()
	{
		if ( !this.IsValid() ) return;

		if ( _statusContainer.Visible )
		{
			var running = McpEditorServer.IsRunning;
			_statusLabel.Text       = running ? $"Status: RUNNING (Port {McpEditorServer.Port})" : "Status: STOPPED";
			_startBtn.Enabled       = !running;
			_stopBtn.Enabled        = running;
			_sessionCountLabel.Text = $"Active Connections: {McpEditorServer.SessionCount}";
			var sessions = McpEditorServer.ActiveSessions.ToList();
			_sessionsListLabel.Text = sessions.Count > 0 ? string.Join( "\n", sessions ) : "(No active connections)";
		}

		if ( _logsContainer.Visible )
		{
			var logs = McpEditorServer.GetServerLogs().ToList();
			if ( _logFilter != "ALL" )
				logs = logs.Where( l => l.Contains( $"[{_logFilter}]", StringComparison.OrdinalIgnoreCase ) ).ToList();

			_logsLabel.Text = logs.Count > 0
				? string.Join( "\n", logs.TakeLast( 30 ) )
				: $"No logs matching filter '{_logFilter}'.";
		}
	}

	// ── Background Update Loop ────────────────────────────────────────────
	private async Task UpdateLoop()
	{
		while ( this.IsValid() )
		{
			try { UpdateUI(); }
			catch { }
			await Task.Delay( 1000 );
		}
	}
}

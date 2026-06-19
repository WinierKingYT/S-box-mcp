using McpBridge;
using Sandbox;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Editor;

internal static class McpCodeTools
{
	internal static void Register()
	{
		McpEditorServer.RegisterTool( "sbox_create_component_class", "Create a new C# Component script template in the project's Code folder.", args =>
		{
			var name = args.GetProperty( "name" ).GetString().Trim();
			var subFolder = args.TryGetProperty( "subFolder", out var sf ) ? sf.GetString() ?? "" : "";

			var className = new string( name.Where( char.IsLetterOrDigit ).ToArray() );
			if ( string.IsNullOrEmpty( className ) || !char.IsLetter( className[0] ) )
				return new { error = "Invalid component name. Must start with a letter and contain only alphanumeric characters." };

			var folderPath = string.IsNullOrEmpty( subFolder ) 
				? System.IO.Path.Combine( Environment.CurrentDirectory, "Code" ) 
				: System.IO.Path.Combine( Environment.CurrentDirectory, "Code", subFolder );

			var filePath = System.IO.Path.Combine( folderPath, $"{className}.cs" );

			try
			{
				if ( !System.IO.Directory.Exists( folderPath ) )
				{
					System.IO.Directory.CreateDirectory( folderPath );
				}

				if ( System.IO.File.Exists( filePath ) )
					return new { error = $"File already exists at: {filePath.Replace( '\\', '/' )}" };

				var template = $@"using Sandbox;
using System;

{( string.IsNullOrEmpty( subFolder ) ? "namespace Sandbox;" : $"namespace Sandbox.{subFolder.Replace( '/', '.' ).Replace( '\\', '.' )};" )}

public sealed class {className} : Component
{{
	[Property] public float Speed {{ get; set; }} = 100f;

	protected override void OnStart()
	{{
		Log.Info( ""{className} started!"" );
	}}

	protected override void OnUpdate()
	{{
	}}

	protected override void OnFixedUpdate()
	{{
	}}
}}
";

				System.IO.File.WriteAllText( filePath, template );
				return new { success = true, filePath = filePath.Replace( '\\', '/' ), className };
			}
			catch ( Exception e )
			{
				return new { error = $"Failed to create component class: {e.Message}" };
			}
		}, new { type = "object", properties = new { name = new { type = "string", description = "Name of the class (e.g. MyTriggerListener)" }, subFolder = new { type = "string", description = "Optional subfolder inside Code/ (e.g. Player, AI, UI)" } }, required = new[] { "name" } }, annotations: new { destructiveHint = true }, runOnMainThread: false );

		McpEditorServer.RegisterToolAsync( "sbox_compile_project", "Compile the C# project and return structured build errors/warnings.", async _ =>
		{
			try
			{
				var pInfo = new ProcessStartInfo
				{
					FileName = "dotnet",
					Arguments = "build \"Code/blackfriday2.csproj\" --nologo",
					WorkingDirectory = Environment.CurrentDirectory,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					UseShellExecute = false,
					CreateNoWindow = true
				};

				using var proc = Process.Start( pInfo );
				if ( proc == null ) return new { success = false, error = "Failed to start dotnet build process" };

				var stdoutTask = proc.StandardOutput.ReadToEndAsync();
				var stderrTask = proc.StandardError.ReadToEndAsync();

				await Task.WhenAll( stdoutTask, stderrTask );
				proc.WaitForExit();

				var stdout = stdoutTask.Result;
				var errors = new List<object>();
				var warnings = new List<object>();

				var lines = stdout.Split( new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries );
				foreach ( var line in lines )
				{
					if ( line.Contains( "): error " ) )
					{
						var parsed = McpEditorServer.ParseBuildMessage( line, "error" );
						if ( parsed != null ) errors.Add( parsed );
					}
					else if ( line.Contains( "): warning " ) )
					{
						var parsed = McpEditorServer.ParseBuildMessage( line, "warning" );
						if ( parsed != null ) warnings.Add( parsed );
					}
				}

				return new
				{
					success = proc.ExitCode == 0,
					exitCode = proc.ExitCode,
					errorCount = errors.Count,
					warningCount = warnings.Count,
					errors,
					warnings,
					rawOutput = stdout.Length > 5000 ? stdout[..5000] + "..." : stdout
				};
			}
			catch ( Exception e )
			{
				return new { success = false, error = e.Message };
			}
		}, new { type = "object", properties = new { }, required = Array.Empty<string>() }, annotations: new { readOnlyHint = true }, runOnMainThread: false );

		McpEditorServer.RegisterTool( "sbox_read_file", "Read any file from the project directory. Returns text content.", args =>
		{
			var relPath = args.GetProperty( "path" ).GetString()?.Replace( "\\", "/" ) ?? "";
			try
			{
				string text = null;
				if ( FileSystem.Mounted.FileExists( relPath ) )
				{
					text = FileSystem.Mounted.ReadAllText( relPath );
				}
				else
				{
					var absPath = System.IO.Path.Combine( FileSystem.Mounted.GetFullPath( "." ), relPath.Replace( "/", System.IO.Path.DirectorySeparatorChar.ToString() ) );
					if ( !System.IO.File.Exists( absPath ) ) return new { error = $"File not found: {relPath}" };
					text = System.IO.File.ReadAllText( absPath );
				}
				var lines = text.Split( '\n' );
				var maxLines = args.TryGetProperty( "maxLines", out var ml ) ? ml.GetInt32() : 300;
				var truncated = lines.Length > maxLines;
				var preview = string.Join( "\n", lines.Take( maxLines ) );
				return new { path = relPath, lineCount = lines.Length, sizeBytes = text.Length, truncated, content = preview };
			}
			catch ( Exception e ) { return new { error = e.Message }; }
		}, new { type = "object", properties = new { path = new { type = "string", description = "Relative file path e.g. Code/Core/MyScript.cs" }, maxLines = new { type = "integer", description = "Max lines to return (default 300)" } }, required = new[] { "path" } }, runOnMainThread: false );

		McpEditorServer.RegisterTool( "sbox_write_file", "Write or create a file in the project. Path is relative to the project root.", args =>
		{
			var relPath = args.GetProperty( "path" ).GetString()?.Replace( "/", System.IO.Path.DirectorySeparatorChar.ToString() ) ?? "";
			var content = args.GetProperty( "content" ).GetString() ?? "";
			if ( relPath.Contains( ".." ) ) return new { error = "Path traversal not allowed" };
			try
			{
				var absPath = System.IO.Path.Combine( FileSystem.Mounted.GetFullPath( "." ), relPath );
				var dirPath = System.IO.Path.GetDirectoryName( absPath );
				if ( !string.IsNullOrEmpty( dirPath ) ) System.IO.Directory.CreateDirectory( dirPath );
				System.IO.File.WriteAllText( absPath, content );
				return new { success = true, path = relPath, absPath, sizeBytes = content.Length };
			}
			catch ( Exception e ) { return new { error = e.Message }; }
		}, new { type = "object", properties = new { path = new { type = "string", description = "Relative path to write, e.g. Code/MyScript.cs" }, content = new { type = "string", description = "Full file content to write" } }, required = new[] { "path", "content" } }, annotations: new { destructiveHint = true }, runOnMainThread: false );

		McpEditorServer.RegisterTool( "sbox_read_script", "Read a C# script file with line numbers and a class/method outline.", args =>
		{
			var relPath = args.GetProperty( "path" ).GetString()?.Replace( "\\", "/" ) ?? "";
			try
			{
				string text = null;
				if ( FileSystem.Mounted.FileExists( relPath ) )
				{
					text = FileSystem.Mounted.ReadAllText( relPath );
				}
				else
				{
					var absPath = System.IO.Path.Combine( FileSystem.Mounted.GetFullPath( "." ), relPath.Replace( "/", System.IO.Path.DirectorySeparatorChar.ToString() ) );
					if ( !System.IO.File.Exists( absPath ) ) return new { error = $"File not found: {relPath}" };
					text = System.IO.File.ReadAllText( absPath );
				}
				var path = relPath;

				var lines     = text.Split( '\n' );
				var maxLines  = args.TryGetProperty( "maxLines", out var ml ) ? ml.GetInt32() : 300;
				var startLine = args.TryGetProperty( "startLine", out var sl ) ? sl.GetInt32() - 1 : 0;
				startLine = Math.Max( 0, Math.Min( startLine, lines.Length - 1 ) );
				var endLine = Math.Min( startLine + maxLines, lines.Length );

				// Build numbered snippet
				var sb = new System.Text.StringBuilder();
				for ( int i = startLine; i < endLine; i++ )
					sb.AppendLine( $"{i + 1,4}: {lines[i]}" );

				// Quick outline: find class/method/property declarations
				var outline = new List<string>();
				for ( int i = 0; i < lines.Length; i++ )
				{
					var t = lines[i].Trim();
					if ( t.StartsWith( "public " ) || t.StartsWith( "private " ) || t.StartsWith( "protected " ) || t.StartsWith( "internal " ) )
					{
						if ( t.Contains( " class " ) || t.Contains( " void " ) || t.Contains( " Task " ) || t.Contains( " async " ) || t.Contains( " bool " ) || t.Contains( " int " ) || t.Contains( " float " ) || t.Contains( " string " ) )
							outline.Add( $"L{i + 1}: {t.Substring( 0, Math.Min( t.Length, 90 ) )}" );
					}
				}

				return new
				{
					path,
					totalLines = lines.Length,
					shownRange = $"{startLine + 1}-{endLine}",
					truncated  = endLine < lines.Length,
					outline    = outline.Take( 40 ),
					content    = sb.ToString()
				};
			}
			catch ( Exception e ) { return new { error = e.Message }; }
		}, new { type = "object", properties = new { path = new { type = "string", description = "Relative .cs file path" }, startLine = new { type = "integer", description = "First line to read (1-indexed, default 1)" }, maxLines = new { type = "integer", description = "Lines to return (default 300)" } }, required = new[] { "path" } }, runOnMainThread: false );

		McpEditorServer.RegisterToolAsync( "sbox_patch_script", "Replace a text fragment in a C# script file. After patching, compiles the project and returns build result.", async args =>
		{
			var path    = args.GetProperty( "path" ).GetString()?.Replace( "\\", "/" ) ?? "";
			var oldText = args.GetProperty( "old_text" ).GetString() ?? "";
			var newText = args.GetProperty( "new_text" ).GetString() ?? "";

			if ( path.Contains( ".." ) ) return (object)new { error = "Path traversal not allowed" };

			try
			{
				string original = null;
				string absPath;
				if ( FileSystem.Mounted.FileExists( path ) )
				{
					absPath  = FileSystem.Mounted.GetFullPath( path );
					original = System.IO.File.ReadAllText( absPath );
				}
				else
				{
					absPath  = System.IO.Path.Combine( FileSystem.Mounted.GetFullPath( "." ), path.Replace( "/", System.IO.Path.DirectorySeparatorChar.ToString() ) );
					if ( !System.IO.File.Exists( absPath ) ) return new { error = $"File not found: {path}" };
					original = System.IO.File.ReadAllText( absPath );
				}

				if ( !original.Contains( oldText ) )
					return new { error = "old_text not found in file — no changes made", path };

				var patched = original.Replace( oldText, newText );
				System.IO.File.WriteAllText( absPath, patched );

				// Trigger compile
				object buildResult;
				try
				{
					var psi = new System.Diagnostics.ProcessStartInfo
					{
						FileName        = "dotnet",
						Arguments       = $"build \"{FileSystem.Mounted.GetFullPath( "." )}\" --no-restore -v quiet 2>&1",
						RedirectStandardOutput = true,
						RedirectStandardError  = true,
						UseShellExecute = false,
						CreateNoWindow  = true
					};
					using var proc = System.Diagnostics.Process.Start( psi );
					var stdout = await proc.StandardOutput.ReadToEndAsync();
					var stderr = await proc.StandardError.ReadToEndAsync();
					await proc.WaitForExitAsync();
					buildResult = new { exitCode = proc.ExitCode, output = (stdout + stderr).Trim() };
				}
				catch ( Exception be ) { buildResult = new { error = be.Message }; }

				return new { success = true, path, replacements = (original.Length - patched.Length + newText.Length - oldText.Length) != 0 ? 1 : 0, build = buildResult };
			}
			catch ( Exception e ) { return (object)new { error = e.Message }; }
		}, new { type = "object", properties = new { path = new { type = "string", description = "Relative .cs path" }, old_text = new { type = "string", description = "Exact text to replace" }, new_text = new { type = "string", description = "Replacement text" } }, required = new[] { "path", "old_text", "new_text" } }, annotations: new { destructiveHint = true }, runOnMainThread: false );

		McpEditorServer.RegisterToolAsync( "sbox_auto_fix_errors", "Compile the project and return structured error list with suggested fixes for each error.", async _ =>
		{
			try
			{
				var psi = new System.Diagnostics.ProcessStartInfo
				{
					FileName               = "dotnet",
					Arguments              = $"build \"{FileSystem.Mounted.GetFullPath( "." )}\" --no-restore -v quiet 2>&1",
					RedirectStandardOutput = true,
					RedirectStandardError  = true,
					UseShellExecute        = false,
					CreateNoWindow         = true
				};
				using var proc = System.Diagnostics.Process.Start( psi );
				var raw = await proc.StandardOutput.ReadToEndAsync() + await proc.StandardError.ReadToEndAsync();
				await proc.WaitForExitAsync();

				// Parse lines like: File.cs(10,3): error CS0246: ...
				var errorRx = new System.Text.RegularExpressions.Regex( @"([^(]+)\((\d+),(\d+)\):\s+(error|warning)\s+(\w+):\s+(.+)" );
				var errors  = new List<object>();
				foreach ( var line in raw.Split( '\n' ) )
				{
					var m = errorRx.Match( line.Trim() );
					if ( !m.Success ) continue;
					var code = m.Groups[5].Value;
					var msg  = m.Groups[6].Value.Trim();
					errors.Add( new
					{
						file     = System.IO.Path.GetFileName( m.Groups[1].Value.Trim() ),
						fullPath = m.Groups[1].Value.Trim(),
						line     = int.Parse( m.Groups[2].Value ),
						col      = int.Parse( m.Groups[3].Value ),
						severity = m.Groups[4].Value,
						code,
						message  = msg,
						hint     = code switch
						{
							"CS0618" => "Use the newer API suggested in the message (e.g. WorldPosition instead of Transform.Position)",
							"CS0169" => "Field is declared but never used — remove it or add usage",
							"CS0246" => "Type not found — check using directives or namespace",
							"CS1061" => "Method/property does not exist — check API or spelling",
							"SB1000" => "s&box whitelist violation — use TypeLibrary or GameTask instead of standard .NET API",
							_        => "Review the message and fix accordingly"
						}
					} );
				}

				var success = proc.ExitCode == 0;
				return (object)new { success, errorCount = errors.Count, errors };
			}
			catch ( Exception e ) { return (object)new { error = e.Message }; }
		}, runOnMainThread: false );
	}
}

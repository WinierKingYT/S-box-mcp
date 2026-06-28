using McpBridge;
using Sandbox;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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

				McpBridge.MemoryValidator.RecordCodeModification( path, newText, "patch_script" );

				// Trigger compile
				object buildResult;
				try
				{
					var psi = new System.Diagnostics.ProcessStartInfo
					{
						FileName        = "dotnet",
						Arguments       = $"build \"{FileSystem.Mounted.GetFullPath( "." )}\" --no-restore -v quiet",
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
					McpBridge.MemoryValidator.OnCompilationFinished( proc.ExitCode == 0, (stdout + stderr).Trim() );
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
					Arguments              = $"build \"{FileSystem.Mounted.GetFullPath( "." )}\" --no-restore -v quiet",
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
					var bracketIdx = msg.LastIndexOf( '[' );
					if ( bracketIdx != -1 )
						msg = msg.Substring( 0, bracketIdx ).Trim();

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

		McpEditorServer.RegisterToolAsync( "sbox_modify_code_block", "Surgically modify a method or property in a C# script file by locating its signature, counting braces to extract the block, and applying replacements/injections.", async args =>
		{
			var path = args.GetProperty( "path" ).GetString()?.Replace( "\\", "/" ) ?? "";
			var targetSignature = args.GetProperty( "target_signature" ).GetString() ?? "";
			var replacementCode = args.GetProperty( "replacement_code" ).GetString() ?? "";
			var mode = args.TryGetProperty( "mode", out var m ) ? m.GetString() ?? "replace_body" : "replace_body";

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

				int signatureIndex = original.IndexOf( targetSignature );
				if ( signatureIndex == -1 )
					return new { error = $"Target signature '{targetSignature}' not found in file.", path };

				bool inString = false;
				bool inChar = false;
				bool inVerbatimString = false;
				bool inLineComment = false;
				bool inBlockComment = false;
				int openBraceIndex = original.IndexOf( '{', signatureIndex );
				if ( openBraceIndex == -1 )
					return new { error = "Opening brace not found after signature", path };

				int braceCount = 1;
				int closeBraceIndex = -1;
				for ( int i = openBraceIndex + 1; i < original.Length; i++ )
				{
					char c = original[i];
					char next = i + 1 < original.Length ? original[i + 1] : '\0';

					if ( inLineComment )
					{
						if ( c == '\n' || c == '\r' ) inLineComment = false;
						continue;
					}
					if ( inBlockComment )
					{
						if ( c == '*' && next == '/' ) { inBlockComment = false; i++; }
						continue;
					}
					if ( inString )
					{
						if ( c == '\\' ) { i++; continue; }
						if ( c == '"' ) inString = false;
						continue;
					}
					if ( inVerbatimString )
					{
						if ( c == '"' && next == '"' ) { i++; continue; }
						if ( c == '"' ) inVerbatimString = false;
						continue;
					}
					if ( inChar )
					{
						if ( c == '\\' ) { i++; continue; }
						if ( c == '\'' ) inChar = false;
						continue;
					}

					if ( c == '/' && next == '/' ) { inLineComment = true; i++; continue; }
					if ( c == '/' && next == '*' ) { inBlockComment = true; i++; continue; }

					if ( c == '@' && next == '"' ) { inVerbatimString = true; i++; continue; }
					if ( c == '"' ) { inString = true; continue; }
					if ( c == '\'' ) { inChar = true; continue; }

					if ( c == '{' ) braceCount++;
					else if ( c == '}' )
					{
						braceCount--;
						if ( braceCount == 0 )
						{
							closeBraceIndex = i;
							break;
						}
					}
				}

				if ( closeBraceIndex == -1 )
					return new { error = "Matching closing brace not found", path };

				string patched;
				if ( mode == "replace_body" )
				{
					var before = original.Substring( 0, openBraceIndex + 1 );
					var after = original.Substring( closeBraceIndex );
					patched = before + "\n" + replacementCode + "\n" + after;
				}
				else if ( mode == "replace_method" )
				{
					var before = original.Substring( 0, signatureIndex );
					var after = original.Substring( closeBraceIndex + 1 );
					patched = before + replacementCode + after;
				}
				else if ( mode == "inject_before" )
				{
					var before = original.Substring( 0, openBraceIndex + 1 );
					var after = original.Substring( openBraceIndex + 1 );
					patched = before + "\n" + replacementCode + "\n" + after;
				}
				else if ( mode == "inject_after" )
				{
					var before = original.Substring( 0, closeBraceIndex );
					var after = original.Substring( closeBraceIndex );
					patched = before + "\n" + replacementCode + "\n" + after;
				}
				else
				{
					return new { error = $"Invalid mode '{mode}'. Choose 'replace_body', 'replace_method', 'inject_before', or 'inject_after'." };
				}

				System.IO.File.WriteAllText( absPath, patched );

				McpBridge.MemoryValidator.RecordCodeModification( path, replacementCode, $"modify_code_block:{mode}" );

				object buildResult;
				try
				{
					var psi = new System.Diagnostics.ProcessStartInfo
					{
						FileName        = "dotnet",
						Arguments       = $"build \"{FileSystem.Mounted.GetFullPath( "." )}\" --no-restore -v quiet",
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
					McpBridge.MemoryValidator.OnCompilationFinished( proc.ExitCode == 0, (stdout + stderr).Trim() );
				}
				catch ( Exception be ) { buildResult = new { error = be.Message }; }

				return new { success = true, path, mode, build = buildResult };
			}
			catch ( Exception e ) { return (object)new { error = e.Message }; }
		}, new { type = "object", properties = new { path = new { type = "string", description = "Relative .cs path" }, target_signature = new { type = "string", description = "Exact signature to locate" }, replacement_code = new { type = "string", description = "New code block" }, mode = new { type = "string", description = "Mod mode: replace_body, replace_method, inject_before, inject_after" } }, required = new[] { "path", "target_signature", "replacement_code" } }, annotations: new { destructiveHint = true }, runOnMainThread: false );

		McpEditorServer.RegisterTool( "sbox_dry_run_code", "Dry-run C# code by compiling it in-memory using Roslyn and performing static AST analysis to detect unsafe blocks, P/Invokes, or native pointers that could crash the engine.", args =>
		{
			var code = args.GetProperty( "code" ).GetString() ?? "";
			var filename = args.TryGetProperty( "filename", out var fn ) ? fn.GetString() ?? "DryRunTemp.cs" : "DryRunTemp.cs";

			try
			{
				var syntaxTree = CSharpSyntaxTree.ParseText( code );
				var root = syntaxTree.GetRoot();

				var violations = new List<string>();

				var hasUnsafe = root.DescendantNodes().Any( n => n.IsKind( SyntaxKind.UnsafeStatement ) ) || root.DescendantTokens().Any( t => t.IsKind( SyntaxKind.UnsafeKeyword ) );
				if ( hasUnsafe )
				{
					violations.Add( "Unsafe code blocks are not allowed in Sandbox execution." );
				}

				var hasPointer = root.DescendantNodes().Any( n => n.IsKind( SyntaxKind.PointerType ) || n.IsKind( SyntaxKind.PointerMemberAccessExpression ) );
				if ( hasPointer )
				{
					violations.Add( "Native pointer types and pointer member accesses are strictly prohibited to prevent memory corruption." );
				}

				var attributes = root.DescendantNodes().OfType<AttributeSyntax>();
				foreach ( var attr in attributes )
				{
					var name = attr.Name.ToString();
					if ( name.Contains( "DllImport" ) || name.Contains( "LibraryImport" ) || name.Contains( "MarshalAs" ) )
					{
						violations.Add( $"Native Interop attribute '{name}' is not allowed in Dry-Run sandbox." );
					}
				}

				var usings = root.DescendantNodes().OfType<UsingDirectiveSyntax>();
				foreach ( var u in usings )
				{
					var name = u.Name.ToString();
					if ( name == "System.Runtime.InteropServices" || name == "System.Reflection" )
					{
						violations.Add( $"Forbidden using directive: '{name}' violates immutable core axioms." );
					}
				}

				if ( violations.Count > 0 )
				{
					return new
					{
						success = false,
						safe = false,
						violations
					};
				}

				var assemblyName = System.IO.Path.GetRandomFileName();
				
				var references = AppDomain.CurrentDomain.GetAssemblies()
					.Where( a => !a.IsDynamic && !string.IsNullOrEmpty( a.Location ) )
					.Select( a => MetadataReference.CreateFromFile( a.Location ) )
					.Cast<MetadataReference>()
					.ToList();

				var compilation = CSharpCompilation.Create(
					assemblyName,
					new[] { syntaxTree },
					references,
					new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary ) );

				using var ms = new System.IO.MemoryStream();
				var result = compilation.Emit( ms );

				if ( !result.Success )
				{
					var errors = result.Diagnostics
						.Where( d => d.Severity == DiagnosticSeverity.Error )
						.Select( d => new
						{
							line = d.Location.GetLineSpan().StartLinePosition.Line + 1,
							col = d.Location.GetLineSpan().StartLinePosition.Character + 1,
							id = d.Id,
							message = d.GetMessage()
						} )
						.ToList();

					return new
					{
						success = false,
						safe = true,
						compiled = false,
						errorCount = errors.Count,
						errors
					};
				}

				return new
				{
					success = true,
					safe = true,
					compiled = true,
					note = "Code compiled successfully and contains no safety violations."
				};
			}
			catch ( Exception e )
			{
				return new { success = false, error = e.Message };
			}
		}, new { type = "object", properties = new { code = new { type = "string", description = "C# source code to dry-run" }, filename = new { type = "string", description = "Mock filename (default DryRunTemp.cs)" } }, required = new[] { "code" } } );

		McpEditorServer.RegisterTool( "sbox_profile_code_allocation", "Compiles and executes a C# code snippet in memory, measuring the heap memory allocated by the snippet with zero overhead using CoreCLR runtime monitoring.", args =>
		{
			var code = args.GetProperty( "code" ).GetString() ?? "";

			try
			{
				if ( !code.Contains( "class" ) )
				{
					code = @"
using System;
using System.Collections.Generic;
using Sandbox;

public static class CodeEvaluator
{
	public static void Run()
	{
		" + code + @"
	}
}";
				}

				var syntaxTree = CSharpSyntaxTree.ParseText( code );
				var assemblyName = System.IO.Path.GetRandomFileName();
				
				var references = AppDomain.CurrentDomain.GetAssemblies()
					.Where( a => !a.IsDynamic && !string.IsNullOrEmpty( a.Location ) )
					.Select( a => MetadataReference.CreateFromFile( a.Location ) )
					.Cast<MetadataReference>()
					.ToList();

				var compilation = CSharpCompilation.Create(
					assemblyName,
					new[] { syntaxTree },
					references,
					new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary ) );

				using var ms = new System.IO.MemoryStream();
				var result = compilation.Emit( ms );

				if ( !result.Success )
				{
					var errors = result.Diagnostics
						.Where( d => d.Severity == DiagnosticSeverity.Error )
						.Select( d => d.GetMessage() )
						.ToList();
					return new { success = false, error = "Compilation failed", details = errors };
				}

				ms.Seek( 0, System.IO.SeekOrigin.Begin );
				var assembly = System.Reflection.Assembly.Load( ms.ToArray() );
				var type = assembly.GetTypes().FirstOrDefault( t => t.Name.Contains( "Evaluator" ) );
				if ( type == null )
				{
					return new { success = false, error = "No evaluator class found. Ensure a class name contains 'Evaluator'." };
				}
				var method = type.GetMethod( "Run", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static );
				if ( method == null )
				{
					return new { success = false, error = "No static void Run() method found in evaluator class." };
				}

				try { AppDomain.MonitoringIsEnabled = true; } catch { }

				long startAllocated = 0;
				try { startAllocated = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize; } catch { }
				long startGc = GC.GetTotalMemory( false );

				var sw = System.Diagnostics.Stopwatch.StartNew();
				
				method.Invoke( null, null );
				
				sw.Stop();
				long endGc = GC.GetTotalMemory( false );
				long endAllocated = 0;
				try { endAllocated = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize; } catch { }

				long bytesAllocated = (endAllocated > startAllocated) ? (endAllocated - startAllocated) : (endGc - startGc);
				if ( bytesAllocated < 0 ) bytesAllocated = 0;

				return new
				{
					success = true,
					durationMs = sw.Elapsed.TotalMilliseconds,
					allocatedBytes = bytesAllocated,
					allocatedMb = Math.Round( bytesAllocated / (1024f * 1024f), 4 ),
					note = bytesAllocated > 5 * 1024 * 1024 ? "WARNING: High allocation count. Consider using object pools or caching structures to avoid GC spikes." : "Allocation footprint is within normal limits."
				};
			}
			catch ( Exception e )
			{
				return new { success = false, error = e.Message, stackTrace = e.ToString() };
			}
		}, new { type = "object", properties = new { code = new { type = "string", description = "The C# code snippet containing an Evaluator class with static void Run() or raw statements." } }, required = new[] { "code" } } );

		McpEditorServer.RegisterTool( "sbox_query_ast", "Queries C# files using Roslyn AST to retrieve only a specific class or method definition, avoiding full file dumps and saving tokens.", args =>
		{
			var path = args.GetProperty( "path" ).GetString() ?? "";
			var targetName = args.TryGetProperty( "targetName", out var tn ) ? tn.GetString() ?? "" : "";
			var type = args.TryGetProperty( "type", out var tp ) ? tp.GetString() ?? "method" : "method";

			try
			{
				string absPath;
				if ( FileSystem.Mounted.FileExists( path ) )
					absPath = FileSystem.Mounted.GetFullPath( path );
				else
					absPath = System.IO.Path.Combine( FileSystem.Mounted.GetFullPath( "." ), path.Replace( "/", System.IO.Path.DirectorySeparatorChar.ToString() ) );

				if ( !System.IO.File.Exists( absPath ) )
					return new { error = $"File not found: {path}" };

				var code = System.IO.File.ReadAllText( absPath );
				var syntaxTree = CSharpSyntaxTree.ParseText( code );
				var root = syntaxTree.GetRoot();

				if ( type.Equals( "class", StringComparison.OrdinalIgnoreCase ) )
				{
					var cls = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
						.FirstOrDefault( c => c.Identifier.Text.Equals( targetName, StringComparison.OrdinalIgnoreCase ) );
					if ( cls != null )
					{
						return new { success = true, path, targetName, type, code = cls.ToFullString() };
					}
					return new { error = $"Class '{targetName}' not found in {path}" };
				}
				else
				{
					var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
						.FirstOrDefault( m => m.Identifier.Text.Equals( targetName, StringComparison.OrdinalIgnoreCase ) );
					if ( method != null )
					{
						return new { success = true, path, targetName, type, code = method.ToFullString() };
					}
					return new { error = $"Method '{targetName}' not found in {path}" };
				}
			}
			catch ( Exception e )
			{
				return new { error = e.Message };
			}
		}, new { type = "object", properties = new { path = new { type = "string", description = "Relative .cs path" }, targetName = new { type = "string", description = "Name of class or method to extract" }, type = new { type = "string", description = "Type of node to query: class or method (default method)" } }, required = new[] { "path", "targetName" } } );
	}
}

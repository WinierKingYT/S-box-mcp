using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Editor;

public static class SchemaCompiler
{
	public static string ProjectDir { get; set; }

	public static void Compile()
	{
		try
		{
			var projectDir = Directory.GetCurrentDirectory();
			while ( !string.IsNullOrEmpty( projectDir ) && !File.Exists( Path.Combine( projectDir, "blackfriday2.slnx" ) ) )
			{
				var parent = Path.GetDirectoryName( projectDir );
				if ( parent == projectDir ) break;
				projectDir = parent;
			}
			if ( string.IsNullOrEmpty( projectDir ) || !File.Exists( Path.Combine( projectDir, "blackfriday2.slnx" ) ) )
			{
				projectDir = @"c:\Users\faruk\Documents\s&box projects\blackfriday2";
			}
			ProjectDir = projectDir;

			var searchDirs = new[]
			{
				Path.Combine(projectDir, "Libraries", "mcp_server", "code", "Tools"),
				Path.Combine(projectDir, "Editor", "Tools")
			};

			var toolsList = new List<GeneratedToolInfo>();

			foreach ( var dir in searchDirs )
			{
				if ( !Directory.Exists( dir ) ) continue;

				foreach ( var file in Directory.GetFiles( dir, "*.cs" ) )
				{
					ParseFile( file, toolsList );
				}
			}

			GenerateCode( toolsList );
		}
		catch ( Exception e )
		{
			System.Console.WriteLine( $"[MCP] Schema compiler failed: {e.Message}" );
		}
	}

	private class GeneratedToolInfo
	{
		public string GroupName { get; set; }
		public string ClassName { get; set; }
		public string FullClassName { get; set; }
		public string ToolName { get; set; }
		public string Description { get; set; }
		public string MethodName { get; set; }
		public bool IsStatic { get; set; }
		public bool ReadOnlyHint { get; set; }
		public bool DestructiveHint { get; set; }
		public List<ParamInfo> Parameters { get; set; } = new();
	}

	private class ParamInfo
	{
		public string Name { get; set; }
		public string Type { get; set; }
		public string Description { get; set; }
		public bool IsOptional { get; set; }
	}

	private static void ParseFile( string filePath, List<GeneratedToolInfo> list )
	{
		var code = File.ReadAllText( filePath );
		var tree = CSharpSyntaxTree.ParseText( code );
		var root = tree.GetRoot();

		var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
		foreach ( var cls in classes )
		{
			var groupAttr = cls.AttributeLists.SelectMany( al => al.Attributes )
				.FirstOrDefault( a => a.Name.ToString().Contains( "McpToolGroup" ) );
			
			var groupName = cls.Identifier.Text;
			if ( groupName.EndsWith( "Tools" ) ) groupName = groupName.Substring( 0, groupName.Length - 5 );

			var fullClassName = cls.Identifier.Text;
			var ns = cls.Ancestors().OfType<NamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString();
			if ( !string.IsNullOrEmpty( ns ) )
			{
				fullClassName = ns + "." + fullClassName;
			}
			else
			{
				var fileScopedNs = cls.Ancestors().OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString();
				if ( !string.IsNullOrEmpty( fileScopedNs ) )
				{
					fullClassName = fileScopedNs + "." + fullClassName;
				}
			}

			var methods = cls.DescendantNodes().OfType<MethodDeclarationSyntax>();
			foreach ( var m in methods )
			{
				var toolAttr = m.AttributeLists.SelectMany( al => al.Attributes )
					.FirstOrDefault( a => a.Name.ToString().Contains( "McpTool" ) );

				if ( toolAttr == null ) continue;

				var toolName = toolAttr.ArgumentList?.Arguments.FirstOrDefault()?.Expression.ToString().Trim( '"' ) ?? m.Identifier.Text;
				var toolDesc = toolAttr.ArgumentList?.Arguments.Skip( 1 ).FirstOrDefault()?.Expression.ToString().Trim( '"' ) ?? "";

				var isStatic = m.Modifiers.Any( mod => mod.IsKind( SyntaxKind.StaticKeyword ) );
				var readOnly = toolAttr.ArgumentList?.Arguments.Any( arg => arg.NameColon?.Name.Identifier.Text == "ReadOnlyHint" && arg.Expression.ToString() == "true" ) ?? false;
				var destructive = toolAttr.ArgumentList?.Arguments.Any( arg => arg.NameColon?.Name.Identifier.Text == "DestructiveHint" && arg.Expression.ToString() == "true" ) ?? false;

				var info = new GeneratedToolInfo
				{
					GroupName = groupName,
					ClassName = cls.Identifier.Text,
					FullClassName = fullClassName,
					ToolName = toolName,
					Description = toolDesc,
					MethodName = m.Identifier.Text,
					IsStatic = isStatic,
					ReadOnlyHint = readOnly,
					DestructiveHint = destructive
				};

				foreach ( var p in m.ParameterList.Parameters )
				{
					var pName = p.Identifier.Text;
					var pType = p.Type?.ToString() ?? "object";
					var isOpt = p.Default != null;

					info.Parameters.Add( new ParamInfo
					{
						Name = pName,
						Type = pType,
						Description = "",
						IsOptional = isOpt
					} );
				}

				list.Add( info );
			}
		}
	}

	private static void GenerateCode( List<GeneratedToolInfo> list )
	{
		var sb = new System.Text.StringBuilder();
		sb.AppendLine( "// <auto-generated />" );
		sb.AppendLine( "using System;" );
		sb.AppendLine( "using System.Collections.Generic;" );
		sb.AppendLine( "using System.Text.Json;" );
		sb.AppendLine( "" );
		sb.AppendLine( "namespace McpBridge.Execution;" );
		sb.AppendLine( "" );
		sb.AppendLine( "public static class McpGeneratedTools" );
		sb.AppendLine( "{" );
		sb.AppendLine( "	public class StaticallyBoundTool" );
		sb.AppendLine( "	{" );
		sb.AppendLine( "		public string Name { get; set; }" );
		sb.AppendLine( "		public string Description { get; set; }" );
		sb.AppendLine( "		public string Group { get; set; }" );
		sb.AppendLine( "		public bool ReadOnlyHint { get; set; }" );
		sb.AppendLine( "		public bool DestructiveHint { get; set; }" );
		sb.AppendLine( "		public Func<JsonElement, object> Invoke { get; set; }" );
		sb.AppendLine( "	}" );
		sb.AppendLine( "" );
		sb.AppendLine( "	public static readonly Dictionary<string, StaticallyBoundTool> Tools = new()" );
		sb.AppendLine( "	{" );

		foreach ( var tool in list )
		{
			sb.AppendLine( $"		[\"{tool.ToolName}\"] = new StaticallyBoundTool" );
			sb.AppendLine( "		{" );
			sb.AppendLine( $"			Name = \"{tool.ToolName}\"," );
			sb.AppendLine( $"			Description = \"{tool.Description.Replace("\"", "\\\"")}\"," );
			sb.AppendLine( $"			Group = \"{tool.GroupName}\"," );
			sb.AppendLine( $"			ReadOnlyHint = {tool.ReadOnlyHint.ToString().ToLower()}," );
			sb.AppendLine( $"			DestructiveHint = {tool.DestructiveHint.ToString().ToLower()}," );
			sb.AppendLine( "			Invoke = (el) =>" );
			sb.AppendLine( "			{" );

			if ( !tool.IsStatic )
			{
				sb.AppendLine( $"				var inst = new {tool.FullClassName}();" );
			}

			for ( int i = 0; i < tool.Parameters.Count; i++ )
			{
				var p = tool.Parameters[i];
				var deserializer = $"el.TryGetProperty(\"{p.Name}\", out var v{i}) ? JsonSerializer.Deserialize<{p.Type}>(v{i}.GetRawText()) : default({p.Type})";
				sb.AppendLine( $"				var p{i} = {deserializer};" );
			}

			var callPrefix = tool.IsStatic ? tool.FullClassName : "inst";
			var callArgs = string.Join( ", ", Enumerable.Range( 0, tool.Parameters.Count ).Select( i => $"p{i}" ) );
			sb.AppendLine( $"				return {callPrefix}.{tool.MethodName}({callArgs});" );

			sb.AppendLine( "			}" );
			sb.AppendLine( "		}," );
		}

		sb.AppendLine( "	};" );
		sb.AppendLine( "}" );

		var targetPath = Path.Combine( ProjectDir, "Libraries", "mcp_server", "code", "Execution", "McpGeneratedTools.g.cs" );
		File.WriteAllText( targetPath, sb.ToString() );
		System.Console.WriteLine( $"[MCP] Compiled {list.Count} tools statically into {targetPath}" );
	}
}

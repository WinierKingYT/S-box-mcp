using Xunit;

namespace Editor;

public class SceneAdvancedMutationToolsTests
{
	[Theory]
	[InlineData( "prefabs/props/crate.prefab" )]
	[InlineData( "prefabs/crate.PREFAB" )]
	[InlineData( "crate.prefab" )]
	public void IsSafePrefabPath_AcceptsProjectRelativePrefabAssets( string path )
	{
		Assert.True( SceneAdvancedMutationTools.IsSafePrefabPath( path ) );
	}

	[Theory]
	[InlineData( null )]
	[InlineData( "" )]
	[InlineData( "C:/project/crate.prefab" )]
	[InlineData( "../outside.prefab" )]
	[InlineData( "prefabs/../outside.prefab" )]
	[InlineData( "prefabs\\crate.prefab" )]
	[InlineData( "prefabs/crate.scene" )]
	public void IsSafePrefabPath_RejectsUnsafeOrNonPrefabPaths( string path )
	{
		Assert.False( SceneAdvancedMutationTools.IsSafePrefabPath( path ) );
	}
}

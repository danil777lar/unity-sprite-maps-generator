using System.Collections.Generic;
using UnityEditor;

namespace Larje.SpriteMapsGenerator.Editor
{
    public sealed class SpriteMapsAssetTracker : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets( string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths )
        {
            SpriteMapsPaths paths = new();
            List< string > deletedGuids = new();
            List< string > movedFrom = new();
            List< string > movedTo = new();

            for ( int i = 0; i < deletedAssets.Length; i++ )
            {
                if ( !paths.IsInOutputFolder( deletedAssets[ i ] ) )
                {
                    deletedGuids.Add( AssetDatabase.AssetPathToGUID( deletedAssets[ i ] ) );
                }
            }

            for ( int i = 0; i < movedAssets.Length; i++ )
            {
                if ( !paths.IsInOutputFolder( movedAssets[ i ] ) )
                {
                    movedFrom.Add( movedFromAssetPaths[ i ] );
                    movedTo.Add( movedAssets[ i ] );
                }
            }

            if ( deletedGuids.Count == 0 && movedTo.Count == 0 )
            {
                return;
            }

            EditorApplication.delayCall += () =>
            {
                for ( int i = 0; i < deletedGuids.Count; i++ )
                {
                    paths.DeleteMaps( deletedGuids[ i ] );
                }

                for ( int i = 0; i < movedTo.Count; i++ )
                {
                    paths.RenameMaps( movedFrom[ i ], movedTo[ i ] );
                }
            };
        }
    }
}

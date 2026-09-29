using System;
using System.IO;
using UnityEditor;

namespace Larje.SpriteMapsGenerator.Editor
{
    public sealed class SpriteMapsPaths
    {
        public bool IsInOutputFolder( string assetPath )
        {
            string folder = SpriteMapsProjectSettings.instance.OutputFolder;

            return !string.IsNullOrEmpty( folder ) && assetPath.StartsWith( folder + "/", StringComparison.Ordinal );
        }

        public string GetFolder( string sourcePath )
        {
            return GetFolderByGuid( AssetDatabase.AssetPathToGUID( sourcePath ) );
        }

        public string GetMapPath( string sourcePath, string suffix )
        {
            return GetMapPath( GetFolder( sourcePath ), Path.GetFileNameWithoutExtension( sourcePath ), suffix );
        }

        public void EnsureFolder( string sourcePath )
        {
            EnsureAssetFolder( GetFolder( sourcePath ) );
        }

        public void RenameMaps( string oldSourcePath, string newSourcePath )
        {
            string oldName = Path.GetFileNameWithoutExtension( oldSourcePath );
            string newName = Path.GetFileNameWithoutExtension( newSourcePath );
            string folder = GetFolder( newSourcePath );

            if ( oldName == newName || !AssetDatabase.IsValidFolder( folder ) )
            {
                return;
            }

            string[] suffixes = GetSuffixes();

            for ( int i = 0; i < suffixes.Length; i++ )
            {
                string oldMapPath = GetMapPath( folder, oldName, suffixes[ i ] );
                string newMapPath = GetMapPath( folder, newName, suffixes[ i ] );

                if ( File.Exists( oldMapPath ) && !File.Exists( newMapPath ) )
                {
                    AssetDatabase.MoveAsset( oldMapPath, newMapPath );
                }
            }
        }

        public void DeleteMaps( string sourceGuid )
        {
            string folder = GetFolderByGuid( sourceGuid );

            if ( !string.IsNullOrEmpty( sourceGuid ) && AssetDatabase.IsValidFolder( folder ) )
            {
                AssetDatabase.DeleteAsset( folder );
            }
        }

        private string GetFolderByGuid( string sourceGuid )
        {
            return SpriteMapsProjectSettings.instance.OutputFolder + "/" + sourceGuid;
        }

        private string GetMapPath( string folder, string sourceName, string suffix )
        {
            return folder + "/" + sourceName + suffix + ".png";
        }

        private string[] GetSuffixes()
        {
            SpriteMapsProjectSettings settings = SpriteMapsProjectSettings.instance;

            return new[] { settings.NormalSuffix, settings.ProceduralNormalSuffix, settings.AiNormalSuffix, settings.MaskSuffix };
        }

        private void EnsureAssetFolder( string folder )
        {
            if ( AssetDatabase.IsValidFolder( folder ) )
            {
                return;
            }

            if ( Directory.Exists( folder ) )
            {
                AssetDatabase.ImportAsset( folder, ImportAssetOptions.ForceSynchronousImport );
                return;
            }

            string parent = Path.GetDirectoryName( folder ).Replace( '\\', '/' );
            EnsureAssetFolder( parent );
            AssetDatabase.CreateFolder( parent, Path.GetFileName( folder ) );
        }
    }
}

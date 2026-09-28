using System;
using System.IO;

namespace Larje.SpriteMapsGenerator.Editor
{
    public sealed class SpineAtlasLocator
    {
        private static readonly string[] AtlasPatterns = { "*.atlas.txt", "*.atlas" };

        public string Find( string texturePath )
        {
            string directory = Path.GetDirectoryName( texturePath );
            string pageName = Path.GetFileName( texturePath );

            if ( string.IsNullOrEmpty( directory ) || !Directory.Exists( directory ) )
            {
                return null;
            }

            for ( int p = 0; p < AtlasPatterns.Length; p++ )
            {
                string[] atlases = Directory.GetFiles( directory, AtlasPatterns[ p ] );

                for ( int i = 0; i < atlases.Length; i++ )
                {
                    if ( ContainsPage( atlases[ i ], pageName ) )
                    {
                        return Path.GetFullPath( atlases[ i ] );
                    }
                }
            }

            return null;
        }

        private bool ContainsPage( string atlasPath, string pageName )
        {
            string[] lines = File.ReadAllLines( atlasPath );

            for ( int i = 0; i < lines.Length; i++ )
            {
                if ( string.Equals( lines[ i ].Trim(), pageName, StringComparison.Ordinal ) )
                {
                    return true;
                }
            }

            return false;
        }
    }
}

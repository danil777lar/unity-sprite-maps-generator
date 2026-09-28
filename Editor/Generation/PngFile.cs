using System.IO;
using UnityEngine;

namespace Larje.SpriteMapsGenerator.Editor
{
    internal static class PngFile
    {
        public static Color32[] Read( string path, out int width, out int height )
        {
            Texture2D texture = new( 2, 2, TextureFormat.RGBA32, false, true );
            texture.LoadImage( File.ReadAllBytes( path ) );

            width = texture.width;
            height = texture.height;

            Color32[] pixels = texture.GetPixels32();
            Object.DestroyImmediate( texture );

            return pixels;
        }

        public static void Write( string path, Color32[] pixels, int width, int height )
        {
            Texture2D texture = new( width, height, TextureFormat.RGBA32, false, true );
            texture.SetPixels32( pixels );
            texture.Apply( false );

            File.WriteAllBytes( path, texture.EncodeToPNG() );
            Object.DestroyImmediate( texture );
        }
    }
}

using UnityEditor;
using UnityEngine;

namespace Larje.SpriteMapsGenerator.Editor
{
    public sealed class SpriteMapsResult
    {
        public SpriteMapsResult( Texture2D source, TextureImporter importer, Texture2D normalMap, Texture2D maskMap )
        {
            Source = source;
            Importer = importer;
            NormalMap = normalMap;
            MaskMap = maskMap;
        }

        public Texture2D Source { get; }
        public TextureImporter Importer { get; }
        public Texture2D NormalMap { get; }
        public Texture2D MaskMap { get; }
    }
}

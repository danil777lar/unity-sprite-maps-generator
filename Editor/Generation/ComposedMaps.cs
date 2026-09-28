using UnityEngine;

namespace Larje.SpriteMapsGenerator.Editor
{
    public sealed class ComposedMaps
    {
        public ComposedMaps( int width, int height, Color32[] normals, Color32[] mask )
        {
            Width = width;
            Height = height;
            Normals = normals;
            Mask = mask;
        }

        public int Width { get; }
        public int Height { get; }
        public Color32[] Normals { get; }
        public Color32[] Mask { get; }
    }
}

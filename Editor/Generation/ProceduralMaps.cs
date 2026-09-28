using UnityEngine;

namespace Larje.SpriteMapsGenerator.Editor
{
    public sealed class ProceduralMaps
    {
        public ProceduralMaps( float[] distance, float[] detail, float[] heightMap, float maxHeight, Vector3[] normals )
        {
            Distance = distance;
            Detail = detail;
            HeightMap = heightMap;
            MaxHeight = maxHeight;
            Normals = normals;
        }

        public float[] Distance { get; }
        public float[] Detail { get; }
        public float[] HeightMap { get; }
        public float MaxHeight { get; }
        public Vector3[] Normals { get; }
    }
}

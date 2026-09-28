using UnityEngine;

namespace Larje.SpriteMapsGenerator.Editor
{
    public sealed class NormalBlender
    {
        public Vector3[] Blend( Vector3[] procedural, Vector3[] ai, float[] alpha, float alphaThreshold, float aiWeight )
        {
            Vector3[] result = new Vector3[ procedural.Length ];
            bool hasAi = ai != null && aiWeight > 0f;

            for ( int i = 0; i < procedural.Length; i++ )
            {
                if ( alpha[ i ] < alphaThreshold )
                {
                    result[ i ] = Vector3.forward;
                }
                else if ( hasAi )
                {
                    Vector3 blended = Vector3.Lerp( procedural[ i ], ai[ i ], aiWeight );
                    result[ i ] = blended.sqrMagnitude > 0f ? blended.normalized : procedural[ i ];
                }
                else
                {
                    result[ i ] = procedural[ i ];
                }
            }

            return result;
        }

        public Vector3[] Decode( Color32[] pixels )
        {
            Vector3[] normals = new Vector3[ pixels.Length ];

            for ( int i = 0; i < pixels.Length; i++ )
            {
                Color32 pixel = pixels[ i ];
                Vector3 normal = new( pixel.r / 255f * 2f - 1f, pixel.g / 255f * 2f - 1f, pixel.b / 255f * 2f - 1f );
                normals[ i ] = normal.sqrMagnitude > 0f ? normal.normalized : Vector3.forward;
            }

            return normals;
        }

        public Color32[] Encode( Vector3[] normals )
        {
            Color32[] pixels = new Color32[ normals.Length ];

            for ( int i = 0; i < normals.Length; i++ )
            {
                Vector3 normal = normals[ i ];
                pixels[ i ] = new Color32( ToByte( normal.x ), ToByte( normal.y ), ToByte( normal.z ), 255 );
            }

            return pixels;
        }

        private byte ToByte( float component )
        {
            return (byte)Mathf.RoundToInt( Mathf.Clamp01( component * 0.5f + 0.5f ) * 255f );
        }
    }
}

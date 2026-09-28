using UnityEngine;

namespace Larje.SpriteMapsGenerator.Editor
{
    public sealed class ProceduralMapGenerator
    {
        private const float Diagonal = 1.4142135f;
        private const float MinHeight = 0.0001f;

        private readonly ProceduralMapSettings _settings;

        private int _width;
        private int _height;

        public ProceduralMapGenerator( ProceduralMapSettings settings )
        {
            _settings = settings;
        }

        public ProceduralMaps Generate( SourceImage source )
        {
            _width = source.Width;
            _height = source.Height;

            float[] alpha = source.Alpha;
            float[] luminance = NormalizeLuminance( alpha, source.Luminance );

            float[] distance = Blur( Blur( ComputeDistance( alpha ), _settings.BevelSmoothRadius ), _settings.BevelSmoothRadius );
            float[] detail = Blur( Blur( luminance, _settings.DetailBlurRadius ), _settings.DetailBlurRadius );
            float[] microDetail = Blur( luminance, _settings.MicroBlurRadius );
            float[] heightMap = ComputeHeight( distance, detail, microDetail );
            Vector3[] normals = ComputeNormals( alpha, heightMap );
            float maxHeight = Mathf.Max( _settings.BevelStrength + _settings.DetailStrength, MinHeight );

            return new ProceduralMaps( distance, detail, heightMap, maxHeight, normals );
        }

        private float[] NormalizeLuminance( float[] alpha, float[] source )
        {
            float[] luminance = new float[ source.Length ];
            float min = float.MaxValue;
            float max = float.MinValue;

            for ( int i = 0; i < source.Length; i++ )
            {
                if ( alpha[ i ] >= _settings.AlphaThreshold )
                {
                    min = Mathf.Min( min, source[ i ] );
                    max = Mathf.Max( max, source[ i ] );
                }
            }

            float range = Mathf.Max( max - min, MinHeight );

            for ( int i = 0; i < source.Length; i++ )
            {
                luminance[ i ] = Mathf.Clamp01( ( source[ i ] - min ) / range ) * alpha[ i ];
            }

            return luminance;
        }

        private float[] ComputeDistance( float[] alpha )
        {
            float[] distance = new float[ alpha.Length ];
            float far = _width + _height;

            for ( int i = 0; i < alpha.Length; i++ )
            {
                distance[ i ] = alpha[ i ] >= _settings.AlphaThreshold ? far : 0f;
            }

            for ( int y = 0; y < _height; y++ )
            {
                for ( int x = 0; x < _width; x++ )
                {
                    int index = y * _width + x;

                    if ( distance[ index ] > 0f )
                    {
                        float best = distance[ index ];
                        best = Mathf.Min( best, GetValue( distance, x - 1, y ) + 1f );
                        best = Mathf.Min( best, GetValue( distance, x, y - 1 ) + 1f );
                        best = Mathf.Min( best, GetValue( distance, x - 1, y - 1 ) + Diagonal );
                        best = Mathf.Min( best, GetValue( distance, x + 1, y - 1 ) + Diagonal );
                        distance[ index ] = best;
                    }
                }
            }

            for ( int y = _height - 1; y >= 0; y-- )
            {
                for ( int x = _width - 1; x >= 0; x-- )
                {
                    int index = y * _width + x;

                    if ( distance[ index ] > 0f )
                    {
                        float best = distance[ index ];
                        best = Mathf.Min( best, GetValue( distance, x + 1, y ) + 1f );
                        best = Mathf.Min( best, GetValue( distance, x, y + 1 ) + 1f );
                        best = Mathf.Min( best, GetValue( distance, x + 1, y + 1 ) + Diagonal );
                        best = Mathf.Min( best, GetValue( distance, x - 1, y + 1 ) + Diagonal );
                        distance[ index ] = best;
                    }
                }
            }

            return distance;
        }

        private float[] Blur( float[] source, int radius )
        {
            if ( radius <= 0 )
            {
                return (float[])source.Clone();
            }

            float[] horizontal = new float[ source.Length ];
            float[] result = new float[ source.Length ];
            float size = radius * 2 + 1;

            for ( int y = 0; y < _height; y++ )
            {
                float sum = 0f;

                for ( int k = -radius; k <= radius; k++ )
                {
                    sum += source[ y * _width + Mathf.Clamp( k, 0, _width - 1 ) ];
                }

                for ( int x = 0; x < _width; x++ )
                {
                    horizontal[ y * _width + x ] = sum / size;

                    int removed = Mathf.Clamp( x - radius, 0, _width - 1 );
                    int added = Mathf.Clamp( x + radius + 1, 0, _width - 1 );
                    sum += source[ y * _width + added ] - source[ y * _width + removed ];
                }
            }

            for ( int x = 0; x < _width; x++ )
            {
                float sum = 0f;

                for ( int k = -radius; k <= radius; k++ )
                {
                    sum += horizontal[ Mathf.Clamp( k, 0, _height - 1 ) * _width + x ];
                }

                for ( int y = 0; y < _height; y++ )
                {
                    result[ y * _width + x ] = sum / size;

                    int removed = Mathf.Clamp( y - radius, 0, _height - 1 );
                    int added = Mathf.Clamp( y + radius + 1, 0, _height - 1 );
                    sum += horizontal[ added * _width + x ] - horizontal[ removed * _width + x ];
                }
            }

            return result;
        }

        private float[] ComputeHeight( float[] distance, float[] detail, float[] microDetail )
        {
            float[] heightMap = new float[ distance.Length ];

            for ( int i = 0; i < distance.Length; i++ )
            {
                float t = Mathf.Clamp01( distance[ i ] / _settings.BevelWidth );
                float bevel = Mathf.Sqrt( 1f - ( 1f - t ) * ( 1f - t ) );
                float micro = ( microDetail[ i ] - detail[ i ] ) * _settings.MicroDetailStrength;

                heightMap[ i ] = bevel * _settings.BevelStrength + detail[ i ] * _settings.DetailStrength + micro;
            }

            return heightMap;
        }

        private Vector3[] ComputeNormals( float[] alpha, float[] heightMap )
        {
            Vector3[] normals = new Vector3[ alpha.Length ];

            for ( int y = 0; y < _height; y++ )
            {
                for ( int x = 0; x < _width; x++ )
                {
                    int index = y * _width + x;

                    if ( alpha[ index ] < _settings.AlphaThreshold )
                    {
                        normals[ index ] = Vector3.forward;
                    }
                    else
                    {
                        float gradientX =
                            GetValue( heightMap, x + 1, y - 1 ) + 2f * GetValue( heightMap, x + 1, y ) + GetValue( heightMap, x + 1, y + 1 ) -
                            GetValue( heightMap, x - 1, y - 1 ) - 2f * GetValue( heightMap, x - 1, y ) - GetValue( heightMap, x - 1, y + 1 );
                        float gradientY =
                            GetValue( heightMap, x - 1, y + 1 ) + 2f * GetValue( heightMap, x, y + 1 ) + GetValue( heightMap, x + 1, y + 1 ) -
                            GetValue( heightMap, x - 1, y - 1 ) - 2f * GetValue( heightMap, x, y - 1 ) - GetValue( heightMap, x + 1, y - 1 );

                        normals[ index ] = new Vector3( -gradientX * _settings.NormalStrength, -gradientY * _settings.NormalStrength, 1f ).normalized;
                    }
                }
            }

            return normals;
        }

        private float GetValue( float[] map, int x, int y )
        {
            if ( x < 0 || y < 0 || x >= _width || y >= _height )
            {
                return 0f;
            }

            return map[ y * _width + x ];
        }
    }
}

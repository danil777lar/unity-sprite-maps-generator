using System.Collections.Generic;
using UnityEngine;

namespace Larje.SpriteMapsGenerator.Editor
{
    public sealed class MaskMapGenerator
    {
        private const int Unvisited = int.MaxValue;
        private const int Empty = -1;

        private readonly float _alphaThreshold;
        private readonly MaskMapSettings _settings;

        private int _width;
        private int _height;

        public MaskMapGenerator( MaskMapSettings settings, float alphaThreshold )
        {
            _settings = settings;
            _alphaThreshold = alphaThreshold;
        }

        public Color32[] Generate( SourceImage source, ProceduralMaps procedural, Vector3[] normals )
        {
            _width = source.Width;
            _height = source.Height;

            float[] alpha = source.Alpha;
            int[] regions = LabelRegions( alpha, out int regionCount );
            float[] sweepOrder = ComputeSweepOrder( regions, regionCount );
            Color32[] mask = new Color32[ alpha.Length ];

            for ( int i = 0; i < alpha.Length; i++ )
            {
                if ( regions[ i ] >= 0 )
                {
                    float slope = 1f - normals[ i ].z;
                    float shineSource = Mathf.Max( slope * _settings.SlopeGain, procedural.Detail[ i ] );
                    float shine = alpha[ i ] * Mathf.Lerp( _settings.ShineFloor, 1f, Mathf.Clamp01( shineSource ) );
                    float normalizedHeight = procedural.HeightMap[ i ] / procedural.MaxHeight;
                    float order = Mathf.Clamp01( sweepOrder[ i ] + ( normalizedHeight - 0.5f ) * _settings.HeightWarp );
                    float edge = alpha[ i ] * ( 1f - Mathf.Clamp01( procedural.Distance[ i ] / _settings.RimWidth ) );

                    mask[ i ] = new Color32( ToByte( shine ), ToByte( order ), ToByte( edge ), 255 );
                }
                else
                {
                    mask[ i ] = new Color32( 0, 0, 0, 255 );
                }
            }

            return mask;
        }

        private byte ToByte( float channel )
        {
            return (byte)Mathf.RoundToInt( Mathf.Clamp01( channel ) * 255f );
        }

        private int[] LabelRegions( float[] alpha, out int regionCount )
        {
            int[] regions = new int[ alpha.Length ];
            Queue< int > queue = new();
            regionCount = 0;

            for ( int i = 0; i < alpha.Length; i++ )
            {
                regions[ i ] = alpha[ i ] >= _alphaThreshold ? Unvisited : Empty;
            }

            for ( int i = 0; i < regions.Length; i++ )
            {
                if ( regions[ i ] == Unvisited )
                {
                    FloodRegion( regions, queue, i, regionCount );
                    regionCount++;
                }
            }

            return regions;
        }

        private void FloodRegion( int[] regions, Queue< int > queue, int start, int region )
        {
            regions[ start ] = region;
            queue.Enqueue( start );

            while ( queue.Count > 0 )
            {
                int index = queue.Dequeue();
                int x = index % _width;
                int y = index / _width;

                TryVisit( regions, queue, x - 1, y, region );
                TryVisit( regions, queue, x + 1, y, region );
                TryVisit( regions, queue, x, y - 1, region );
                TryVisit( regions, queue, x, y + 1, region );
            }
        }

        private void TryVisit( int[] regions, Queue< int > queue, int x, int y, int region )
        {
            if ( x < 0 || y < 0 || x >= _width || y >= _height )
            {
                return;
            }

            int index = y * _width + x;

            if ( regions[ index ] == Unvisited )
            {
                regions[ index ] = region;
                queue.Enqueue( index );
            }
        }

        private float[] ComputeSweepOrder( int[] regions, int regionCount )
        {
            float angle = _settings.SweepAngle * Mathf.Deg2Rad;
            Vector2 direction = new( Mathf.Cos( angle ), Mathf.Sin( angle ) );
            float[] minProjection = new float[ regionCount ];
            float[] maxProjection = new float[ regionCount ];
            float[] order = new float[ regions.Length ];

            for ( int r = 0; r < regionCount; r++ )
            {
                minProjection[ r ] = float.MaxValue;
                maxProjection[ r ] = float.MinValue;
            }

            for ( int i = 0; i < regions.Length; i++ )
            {
                int region = regions[ i ];

                if ( region >= 0 )
                {
                    float projection = GetProjection( i, direction );
                    minProjection[ region ] = Mathf.Min( minProjection[ region ], projection );
                    maxProjection[ region ] = Mathf.Max( maxProjection[ region ], projection );
                }
            }

            for ( int i = 0; i < regions.Length; i++ )
            {
                int region = regions[ i ];

                if ( region >= 0 )
                {
                    float range = Mathf.Max( maxProjection[ region ] - minProjection[ region ], 1f );
                    order[ i ] = ( GetProjection( i, direction ) - minProjection[ region ] ) / range;
                }
            }

            return order;
        }

        private float GetProjection( int index, Vector2 direction )
        {
            return ( index % _width ) * direction.x + ( index / _width ) * direction.y;
        }
    }
}

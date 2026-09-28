using UnityEngine;

namespace Larje.SpriteMapsGenerator.Editor
{
    public sealed class SourceImage
    {
        public SourceImage( Color32[] pixels, int width, int height )
        {
            Pixels = pixels;
            Width = width;
            Height = height;
            Alpha = new float[ pixels.Length ];
            Luminance = new float[ pixels.Length ];

            for ( int i = 0; i < pixels.Length; i++ )
            {
                Color32 pixel = pixels[ i ];
                Alpha[ i ] = pixel.a / 255f;
                Luminance[ i ] = ( pixel.r * 0.299f + pixel.g * 0.587f + pixel.b * 0.114f ) / 255f;
            }
        }

        public int Width { get; }
        public int Height { get; }
        public Color32[] Pixels { get; }
        public float[] Alpha { get; }
        public float[] Luminance { get; }
    }
}

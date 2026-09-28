using System;
using UnityEngine;

namespace Larje.SpriteMapsGenerator.Editor
{
    [ Serializable ]
    public sealed class ProceduralMapSettings
    {
        [ Header( "Shape" ) ]
        [ SerializeField, Range( 0.01f, 1f ) ] private float _alphaThreshold = 0.1f;
        [ SerializeField, Min( 1f ) ] private float _bevelWidth = 28f;
        [ SerializeField, Min( 0f ) ] private float _bevelStrength = 2f;
        [ SerializeField, Range( 0, 8 ) ] private int _bevelSmoothRadius = 2;

        [ Header( "Detail" ) ]
        [ SerializeField, Range( 0, 16 ) ] private int _detailBlurRadius = 5;
        [ SerializeField, Min( 0f ) ] private float _detailStrength = 0.25f;
        [ SerializeField, Range( 1, 4 ) ] private int _microBlurRadius = 2;
        [ SerializeField, Min( 0f ) ] private float _microDetailStrength = 0.3f;
        [ SerializeField, Min( 0f ) ] private float _normalStrength = 3f;

        public float AlphaThreshold => _alphaThreshold;
        public float BevelWidth => _bevelWidth;
        public float BevelStrength => _bevelStrength;
        public int BevelSmoothRadius => _bevelSmoothRadius;
        public int DetailBlurRadius => _detailBlurRadius;
        public float DetailStrength => _detailStrength;
        public int MicroBlurRadius => _microBlurRadius;
        public float MicroDetailStrength => _microDetailStrength;
        public float NormalStrength => _normalStrength;
    }
}

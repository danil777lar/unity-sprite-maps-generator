using System;
using UnityEngine;

namespace Larje.SpriteMapsGenerator.Editor
{
    [ Serializable ]
    public sealed class MaskMapSettings
    {
        [ Header( "Sweep" ) ]
        [ SerializeField, Range( -180f, 180f ) ] private float _sweepAngle = 20f;
        [ SerializeField, Range( 0f, 1f ) ] private float _heightWarp = 0.15f;

        [ Header( "Shine" ) ]
        [ SerializeField, Range( 0f, 1f ) ] private float _shineFloor = 0.35f;
        [ SerializeField, Min( 0f ) ] private float _slopeGain = 3f;
        [ SerializeField, Min( 1f ) ] private float _rimWidth = 12f;

        public float SweepAngle => _sweepAngle;
        public float HeightWarp => _heightWarp;
        public float ShineFloor => _shineFloor;
        public float SlopeGain => _slopeGain;
        public float RimWidth => _rimWidth;
    }
}

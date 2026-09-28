using System;
using UnityEngine;

namespace Larje.SpriteMapsGenerator.Editor
{
    [ Serializable ]
    public sealed class AiMapSettings
    {
        [ Header( "Inference" ) ]
        [ SerializeField, Range( 256, 1536 ) ] private int _resolution = 768;
        [ SerializeField, Range( 1, 10 ) ] private int _ensembleSize = 5;
        [ SerializeField, Range( 1, 50 ) ] private int _steps = 4;

        [ Header( "Shape" ) ]
        [ SerializeField, Range( 0.25f, 4f ) ] private float _normalGain = 1f;
        [ SerializeField, Min( 1f ) ] private float _edgeWidth = 10f;
        [ SerializeField, Min( 0f ) ] private float _edgeStrength = 1f;
        [ SerializeField, Range( 0f, 1f ) ] private float _edgeBlend = 0.6f;

        public int Resolution => _resolution;
        public int EnsembleSize => _ensembleSize;
        public int Steps => _steps;
        public float NormalGain => _normalGain;
        public float EdgeWidth => _edgeWidth;
        public float EdgeStrength => _edgeStrength;
        public float EdgeBlend => _edgeBlend;
    }
}

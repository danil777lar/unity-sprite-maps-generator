using System;
using UnityEngine;

namespace Larje.SpriteMapsGenerator.Editor
{
    [ Serializable ]
    public sealed class TextureMapSettings
    {
        [ SerializeField ] private ProceduralMapSettings _procedural = new();
        [ SerializeField ] private MaskMapSettings _mask = new();
        [ SerializeField ] private AiMapSettings _ai = new();
        [ SerializeField, Range( 0f, 1f ) ] private float _aiBlend = 1f;

        public ProceduralMapSettings Procedural => _procedural;
        public MaskMapSettings Mask => _mask;
        public AiMapSettings Ai => _ai;
        public float AiBlend => _aiBlend;

        public TextureMapSettings Clone()
        {
            return JsonUtility.FromJson< TextureMapSettings >( JsonUtility.ToJson( this ) );
        }
    }
}

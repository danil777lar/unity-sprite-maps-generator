using UnityEngine;

namespace Larje.SpriteMapsGenerator.Editor
{
    public sealed class TextureMapSettingsHolder : ScriptableObject
    {
        [ SerializeField ] private TextureMapSettings _settings = new();

        public TextureMapSettings Settings
        {
            get => _settings;
            set => _settings = value;
        }
    }
}

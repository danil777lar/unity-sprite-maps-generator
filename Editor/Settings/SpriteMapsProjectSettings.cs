using UnityEditor;
using UnityEngine;

namespace Larje.SpriteMapsGenerator.Editor
{
    [ FilePath( "ProjectSettings/SpriteMapsGeneratorSettings.asset", FilePathAttribute.Location.ProjectFolder ) ]
    public sealed class SpriteMapsProjectSettings : ScriptableSingleton< SpriteMapsProjectSettings >
    {
        [ Header( "File Suffixes" ) ]
        [ SerializeField ] private string _normalSuffix = "_normal";
        [ SerializeField ] private string _maskSuffix = "_mask";

        [ Header( "Sprite Secondary Textures" ) ]
        [ SerializeField ] private string _normalTextureName = "_NormalMap";
        [ SerializeField ] private string _maskTextureName = "_MaskTex";

        public string NormalSuffix => _normalSuffix;
        public string MaskSuffix => _maskSuffix;
        public string NormalTextureName => _normalTextureName;
        public string MaskTextureName => _maskTextureName;

        public void SaveSettings()
        {
            Save( true );
        }
    }
}

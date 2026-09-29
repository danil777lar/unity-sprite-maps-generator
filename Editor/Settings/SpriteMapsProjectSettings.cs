using UnityEditor;
using UnityEngine;

namespace Larje.SpriteMapsGenerator.Editor
{
    [ FilePath( "ProjectSettings/SpriteMapsGeneratorSettings.asset", FilePathAttribute.Location.ProjectFolder ) ]
    public sealed class SpriteMapsProjectSettings : ScriptableSingleton< SpriteMapsProjectSettings >
    {
        [ Header( "Output" ) ]
        [ SerializeField ] private string _outputFolder = "Assets/SpriteMapsGenerated";

        [ Header( "File Suffixes" ) ]
        [ SerializeField ] private string _normalSuffix = "_normal";
        [ SerializeField ] private string _proceduralNormalSuffix = "_normal_procedural";
        [ SerializeField ] private string _aiNormalSuffix = "_normal_ai";
        [ SerializeField ] private string _maskSuffix = "_mask";

        [ Header( "Sprite Secondary Textures" ) ]
        [ SerializeField ] private string _normalTextureName = "_NormalMap";
        [ SerializeField ] private string _proceduralNormalTextureName = "_NormalMapProcedural";
        [ SerializeField ] private string _aiNormalTextureName = "_NormalMapAI";
        [ SerializeField ] private string _maskTextureName = "_MaskTex";

        public string OutputFolder => _outputFolder.Replace( '\\', '/' ).TrimEnd( '/' );
        public string NormalSuffix => _normalSuffix;
        public string ProceduralNormalSuffix => _proceduralNormalSuffix;
        public string AiNormalSuffix => _aiNormalSuffix;
        public string MaskSuffix => _maskSuffix;
        public string NormalTextureName => _normalTextureName;
        public string ProceduralNormalTextureName => _proceduralNormalTextureName;
        public string AiNormalTextureName => _aiNormalTextureName;
        public string MaskTextureName => _maskTextureName;

        public void SaveSettings()
        {
            Save( true );
        }
    }
}

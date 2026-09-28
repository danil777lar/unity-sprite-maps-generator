using UnityEditor;
using UnityEngine;

namespace Larje.SpriteMapsGenerator.Editor
{
    [ FilePath( "SpriteMapsGenerator/UserSettings.asset", FilePathAttribute.Location.PreferencesFolder ) ]
    public sealed class SpriteMapsUserSettings : ScriptableSingleton< SpriteMapsUserSettings >
    {
        [ Header( "Python Environment" ) ]
        [ SerializeField ] private TorchBackend _backend = TorchBackend.Cuda;
        [ SerializeField ] private string _uvExecutable = "uv";
        [ SerializeField ] private string _environmentPath = string.Empty;

        [ Header( "Inference" ) ]
        [ SerializeField, Range( 1, 32 ) ] private int _cpuThreads = 4;
        [ SerializeField ] private string _modelId = "prs-eth/marigold-normals-v1-1";

        public TorchBackend Backend
        {
            get => _backend;
            set => _backend = value;
        }

        public string UvExecutable => _uvExecutable;
        public string EnvironmentPath => _environmentPath;
        public int CpuThreads => _cpuThreads;
        public string ModelId => _modelId;

        public void SaveSettings()
        {
            Save( true );
        }
    }
}

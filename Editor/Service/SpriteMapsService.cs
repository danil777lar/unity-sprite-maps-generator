using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Larje.SpriteMapsGenerator.Editor
{
    public sealed class SpriteMapsService
    {
        private const string ProgressTitle = "Sprite Maps";
        private const int MaxLogLength = 20000;

        private static SpriteMapsService _instance;

        private int _progressIndex;
        private int _progressTotal;
        private string _progressLabel = string.Empty;
        private SpriteMapsJobResult _lastAiResult;
        private SpriteMapsJobResult _lastInstallResult;

        private readonly AiNormalsRunner _aiRunner = new();
        private readonly StringBuilder _log = new();
        private readonly List< Texture2D > _aiTargets = new();
        private readonly List< string > _lastAiTextures = new();

        public static SpriteMapsService Instance
        {
            get
            {
                _instance ??= new SpriteMapsService();
                return _instance;
            }
        }

        public SpriteMapsGenerator Generator { get; }
        public PythonEnvironment Environment { get; }
        public SpineAtlasLocator AtlasLocator { get; }
        public bool IsAiRunning => _aiRunner.IsRunning;
        public bool IsBusy => _aiRunner.IsRunning || Environment.IsInstalling;
        public int ProgressIndex => _progressIndex;
        public int ProgressTotal => _progressTotal;
        public string ProgressLabel => _progressLabel;
        public SpriteMapsJobResult LastAiResult => _lastAiResult;
        public SpriteMapsJobResult LastInstallResult => _lastInstallResult;
        public IReadOnlyList< string > LastAiTextures => _lastAiTextures;
        public string Log => _log.ToString();

        public event Action< string > OutputReceived;
        public event Action ProgressChanged;
        public event Action< bool > AiCompleted;
        public event Action< bool > InstallCompleted;

        private SpriteMapsService()
        {
            Generator = new SpriteMapsGenerator();
            Environment = new PythonEnvironment( SpriteMapsUserSettings.instance );
            AtlasLocator = new SpineAtlasLocator();

            Environment.OutputReceived += AppendLog;
            Environment.InstallCompleted += HandleInstallCompleted;
            _aiRunner.OutputReceived += AppendLog;
            _aiRunner.ProgressChanged += HandleAiProgress;
            _aiRunner.Completed += HandleAiCompleted;
        }

        public void Apply( IReadOnlyList< Texture2D > textures, IReadOnlyList< TextureMapSettings > settings )
        {
            try
            {
                for ( int i = 0; i < textures.Count; i++ )
                {
                    ShowApplyProgress( textures, i );
                    Generator.Apply( textures[ i ], settings[ i ] );
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        public void ApplyStored( IReadOnlyList< Texture2D > textures )
        {
            List< TextureMapSettings > settings = new();

            for ( int i = 0; i < textures.Count; i++ )
            {
                settings.Add( Generator.LoadSettings( textures[ i ] ) );
            }

            Apply( textures, settings );
        }

        public bool RunAi( IReadOnlyList< Texture2D > textures, IReadOnlyList< TextureMapSettings > settings )
        {
            if ( IsBusy || !Environment.IsInstalled || textures.Count == 0 )
            {
                return false;
            }

            Apply( textures, settings );

            List< AiNormalsJob > jobs = new();
            _aiTargets.Clear();

            for ( int i = 0; i < textures.Count; i++ )
            {
                Texture2D texture = textures[ i ];
                string path = AssetDatabase.GetAssetPath( texture );
                string atlasPath = AtlasLocator.Find( path );
                string outputPath = Path.GetFullPath( Generator.PrepareAiNormalPath( texture ) );

                jobs.Add( new AiNormalsJob( Path.GetFullPath( path ), atlasPath, outputPath, settings[ i ].Ai ) );
                _aiTargets.Add( texture );
            }

            _progressIndex = 0;
            _progressTotal = jobs.Count;
            _progressLabel = "Loading model";
            ProgressChanged?.Invoke();

            if ( !_aiRunner.Run( Environment, SpriteMapsUserSettings.instance, jobs ) )
            {
                _aiTargets.Clear();
                return false;
            }

            return true;
        }

        public bool Install( bool isClean )
        {
            if ( IsBusy )
            {
                return false;
            }

            Environment.Install( isClean );
            return Environment.IsInstalling;
        }

        public void Cancel()
        {
            _aiRunner.Cancel();
            Environment.Cancel();
        }

        public void ClearLog()
        {
            _log.Clear();
        }

        public string GetLogTail( int lineCount )
        {
            string[] lines = _log.ToString().Split( '\n' );
            int end = lines.Length;

            while ( end > 0 && string.IsNullOrWhiteSpace( lines[ end - 1 ] ) )
            {
                end--;
            }

            int start = Mathf.Max( 0, end - lineCount );
            StringBuilder builder = new();

            for ( int i = start; i < end; i++ )
            {
                builder.AppendLine( lines[ i ].TrimEnd( '\r' ) );
            }

            return builder.ToString();
        }

        private void ShowApplyProgress( IReadOnlyList< Texture2D > textures, int index )
        {
            if ( textures.Count > 1 )
            {
                EditorUtility.DisplayProgressBar( ProgressTitle, textures[ index ].name, (float)index / textures.Count );
            }
        }

        private void HandleAiProgress( int index, int total, string label )
        {
            _progressIndex = index;
            _progressTotal = total;
            _progressLabel = label;
            ProgressChanged?.Invoke();
        }

        private void HandleAiCompleted( bool isSuccess )
        {
            AssetDatabase.Refresh();

            List< Texture2D > targets = new();
            _lastAiTextures.Clear();

            for ( int i = 0; i < _aiTargets.Count; i++ )
            {
                if ( _aiTargets[ i ] != null && Generator.HasAiNormals( _aiTargets[ i ] ) )
                {
                    targets.Add( _aiTargets[ i ] );
                    _lastAiTextures.Add( AssetDatabase.GetAssetPath( _aiTargets[ i ] ) );
                }
            }

            _aiTargets.Clear();
            ApplyStored( targets );

            _lastAiResult = isSuccess ? SpriteMapsJobResult.Succeeded : SpriteMapsJobResult.Failed;

            if ( !isSuccess )
            {
                Debug.LogWarning( "[SpriteMapsGenerator] AI normals finished with errors, see the log in the Sprite Maps window" );
            }

            AiCompleted?.Invoke( isSuccess );
        }

        private void HandleInstallCompleted( bool isSuccess )
        {
            _lastInstallResult = isSuccess ? SpriteMapsJobResult.Succeeded : SpriteMapsJobResult.Failed;
            InstallCompleted?.Invoke( isSuccess );
        }

        private void AppendLog( string line )
        {
            _log.AppendLine( line );

            if ( _log.Length > MaxLogLength )
            {
                _log.Remove( 0, _log.Length - MaxLogLength );
            }

            OutputReceived?.Invoke( line );
        }
    }
}

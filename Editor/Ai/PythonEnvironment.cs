using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Larje.SpriteMapsGenerator.Editor
{
    public sealed class PythonEnvironment
    {
        private const string PythonVersion = "3.12";
        private const string ReadyMarkerName = ".spritemaps-ready";
        private const string CacheFolderName = "larje-spritemapsgenerator";
        private const string EnvironmentFolderName = "venv";
        private const string CudaIndexUrl = "https://download.pytorch.org/whl/cu128";
        private const string RocmIndexUrl = "https://download.pytorch.org/whl/rocm6.4";
        private const string CpuIndexUrl = "https://download.pytorch.org/whl/cpu";

        private bool _isInstalling;
        private TorchBackend _installingBackend;

        private readonly SpriteMapsUserSettings _settings;
        private readonly ProcessRunner _runner = new();
        private readonly Queue< ProcessCommand > _pending = new();

        public string RootPath => string.IsNullOrWhiteSpace( _settings.EnvironmentPath ) ? GetDefaultRoot() : _settings.EnvironmentPath;

        public string PythonPath
        {
            get
            {
                bool isWindows = Application.platform == RuntimePlatform.WindowsEditor;
                return isWindows ? Path.Combine( RootPath, "Scripts", "python.exe" ) : Path.Combine( RootPath, "bin", "python" );
            }
        }

        public bool IsInstalled => File.Exists( PythonPath ) && File.Exists( MarkerPath );
        public bool IsInstalling => _isInstalling;
        public string InstalledBackend => File.Exists( MarkerPath ) ? File.ReadAllText( MarkerPath ).Trim() : string.Empty;

        private string MarkerPath => Path.Combine( RootPath, ReadyMarkerName );

        public event Action< string > OutputReceived;
        public event Action< bool > InstallCompleted;

        public PythonEnvironment( SpriteMapsUserSettings settings )
        {
            _settings = settings;
            _runner.OutputReceived += ForwardOutput;
            _runner.Exited += HandleExited;
        }

        public void Install( bool isClean )
        {
            if ( _isInstalling )
            {
                return;
            }

            string root = RootPath;

            if ( isClean && Directory.Exists( root ) )
            {
                Directory.Delete( root, true );
            }

            if ( File.Exists( MarkerPath ) )
            {
                File.Delete( MarkerPath );
            }

            string uv = _settings.UvExecutable;
            _installingBackend = _settings.Backend;
            _pending.Clear();

            if ( !File.Exists( PythonPath ) )
            {
                _pending.Enqueue( new ProcessCommand( uv, CommandLine.Join( "venv", "--python", PythonVersion, root ) ) );
            }

            _pending.Enqueue( new ProcessCommand( uv, CommandLine.Join( "pip", "install", "--python", PythonPath, "--index-url", GetTorchIndexUrl( _installingBackend ), "torch", "torchvision" ) ) );
            _pending.Enqueue( new ProcessCommand( uv, CommandLine.Join( "pip", "install", "--python", PythonPath, "-r", PackagePaths.Requirements ) ) );

            _isInstalling = true;
            RunNext();
        }

        public void Cancel()
        {
            _pending.Clear();
            _runner.Cancel();
        }

        private void RunNext()
        {
            if ( _pending.Count == 0 )
            {
                File.WriteAllText( MarkerPath, _installingBackend.ToString() );
                Complete( true );
                return;
            }

            ProcessCommand command = _pending.Dequeue();
            OutputReceived?.Invoke( $"> {command.FileName} {command.Arguments}" );

            if ( !_runner.Start( command.FileName, command.Arguments, null ) )
            {
                _pending.Clear();
                Complete( false );
            }
        }

        private void HandleExited( int exitCode )
        {
            if ( exitCode != 0 )
            {
                _pending.Clear();
                OutputReceived?.Invoke( $"Command failed with exit code {exitCode}" );
                Complete( false );
                return;
            }

            RunNext();
        }

        private void Complete( bool isSuccess )
        {
            _isInstalling = false;
            OutputReceived?.Invoke( isSuccess ? "Environment is ready" : "Environment installation failed" );
            InstallCompleted?.Invoke( isSuccess );
        }

        private void ForwardOutput( string line )
        {
            OutputReceived?.Invoke( line );
        }

        private string GetTorchIndexUrl( TorchBackend backend )
        {
            switch ( backend )
            {
                case TorchBackend.Rocm:
                    return RocmIndexUrl;
                case TorchBackend.Cpu:
                    return CpuIndexUrl;
                default:
                    return CudaIndexUrl;
            }
        }

        private string GetDefaultRoot()
        {
            bool isWindows = Application.platform == RuntimePlatform.WindowsEditor;
            string cacheRoot = isWindows
                ? Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData )
                : Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.UserProfile ), ".cache" );

            return Path.Combine( cacheRoot, CacheFolderName, EnvironmentFolderName );
        }

        private sealed class ProcessCommand
        {
            public ProcessCommand( string fileName, string arguments )
            {
                FileName = fileName;
                Arguments = arguments;
            }

            public string FileName { get; }
            public string Arguments { get; }
        }
    }
}

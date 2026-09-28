using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Larje.SpriteMapsGenerator.Editor
{
    public sealed class AiNormalsRunner
    {
        private const string ProgressPrefix = "PROGRESS ";
        private const string TempFolder = "Temp/SpriteMapsGenerator";
        private const string JobsFileName = "jobs.json";

        private string _jobsPath;

        private readonly ProcessRunner _runner = new();

        public bool IsRunning => _runner.IsRunning;

        public event Action< string > OutputReceived;
        public event Action< int, int, string > ProgressChanged;
        public event Action< bool > Completed;

        public AiNormalsRunner()
        {
            _runner.OutputReceived += HandleOutput;
            _runner.Exited += HandleExited;
        }

        public bool Run( PythonEnvironment environment, SpriteMapsUserSettings settings, IReadOnlyList< AiNormalsJob > jobs )
        {
            if ( IsRunning || jobs.Count == 0 )
            {
                return false;
            }

            Directory.CreateDirectory( TempFolder );
            _jobsPath = Path.GetFullPath( Path.Combine( TempFolder, JobsFileName ) );

            JobList list = new() { model = settings.ModelId, threads = settings.CpuThreads, jobs = new List< AiNormalsJob >( jobs ) };
            File.WriteAllText( _jobsPath, JsonUtility.ToJson( list, true ) );

            Dictionary< string, string > environmentVariables = new()
            {
                { "PYTHONUNBUFFERED", "1" },
                { "PYTHONIOENCODING", "utf-8" }
            };

            OutputReceived?.Invoke( $"> AI normals for {jobs.Count} texture(s)" );
            return _runner.Start( environment.PythonPath, CommandLine.Join( "-u", PackagePaths.NormalsScript, _jobsPath ), environmentVariables );
        }

        public void Cancel()
        {
            _runner.Cancel();
        }

        private void HandleOutput( string line )
        {
            if ( line.StartsWith( ProgressPrefix, StringComparison.Ordinal ) )
            {
                string[] parts = line.Split( new[] { ' ' }, 4 );

                if ( parts.Length == 4 && int.TryParse( parts[ 1 ], out int index ) && int.TryParse( parts[ 2 ], out int total ) )
                {
                    ProgressChanged?.Invoke( index, total, parts[ 3 ] );
                }
            }

            OutputReceived?.Invoke( line );
        }

        private void HandleExited( int exitCode )
        {
            if ( File.Exists( _jobsPath ) )
            {
                File.Delete( _jobsPath );
            }

            OutputReceived?.Invoke( exitCode == 0 ? "AI normals are ready" : $"AI normals failed with exit code {exitCode}" );
            Completed?.Invoke( exitCode == 0 );
        }

        [ Serializable ]
        private sealed class JobList
        {
            public string model;
            public int threads;
            public List< AiNormalsJob > jobs;
        }
    }
}

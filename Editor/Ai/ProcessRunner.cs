using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEditor;

namespace Larje.SpriteMapsGenerator.Editor
{
    public sealed class ProcessRunner
    {
        private volatile bool _hasExited;
        private int _exitCode;
        private bool _isReloadLocked;

        private Process _process;
        private readonly ConcurrentQueue< string > _output = new();

        public event Action< string > OutputReceived;
        public event Action< int > Exited;

        public bool IsRunning => _process != null;

        public bool Start( string fileName, string arguments, IReadOnlyDictionary< string, string > environment )
        {
            if ( IsRunning )
            {
                return false;
            }

            ProcessStartInfo startInfo = new( fileName, arguments )
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            if ( environment != null )
            {
                foreach ( KeyValuePair< string, string > pair in environment )
                {
                    startInfo.EnvironmentVariables[ pair.Key ] = pair.Value;
                }
            }

            Process process = new() { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += HandleData;
            process.ErrorDataReceived += HandleData;
            process.Exited += HandleExited;

            try
            {
                process.Start();
            }
            catch ( Exception exception )
            {
                process.Dispose();
                OutputReceived?.Invoke( $"Failed to start '{fileName}': {exception.Message}" );
                return false;
            }

            _process = process;
            _hasExited = false;
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            EditorApplication.LockReloadAssemblies();
            _isReloadLocked = true;
            EditorApplication.update += Poll;

            return true;
        }

        public void Cancel()
        {
            if ( _process == null || _hasExited )
            {
                return;
            }

            try
            {
                _process.Kill();
            }
            catch ( InvalidOperationException )
            {
                _hasExited = true;
            }
        }

        private void HandleData( object sender, DataReceivedEventArgs args )
        {
            if ( args.Data != null )
            {
                _output.Enqueue( args.Data );
            }
        }

        private void HandleExited( object sender, EventArgs args )
        {
            Process process = (Process)sender;
            process.WaitForExit();
            _exitCode = process.ExitCode;
            _hasExited = true;
        }

        private void Poll()
        {
            DrainOutput();

            if ( !_hasExited )
            {
                return;
            }

            DrainOutput();
            EditorApplication.update -= Poll;

            _process.Dispose();
            _process = null;

            if ( _isReloadLocked )
            {
                _isReloadLocked = false;
                EditorApplication.UnlockReloadAssemblies();
            }

            Exited?.Invoke( _exitCode );
        }

        private void DrainOutput()
        {
            while ( _output.TryDequeue( out string line ) )
            {
                OutputReceived?.Invoke( line );
            }
        }
    }
}

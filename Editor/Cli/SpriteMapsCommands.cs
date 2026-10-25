using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace Larje.SpriteMapsGenerator.Editor.Cli
{
    public static class SpriteMapsCommands
    {
        private const string Group = "larje.sprite_maps";

        private const string StatusDescription =
            "Sprite Maps Generator: report the AI Python environment state, the running AI normals job (progress, last result) and the tail of the tool log. " +
            "Set --wait_seconds to block until the running AI job or environment install finishes (or the timeout elapses) instead of polling.";

        private const string TextureInfoDescription =
            "Sprite Maps Generator: for sprite textures or Spine atlas pages (asset paths or folders) return the stored per-texture settings, " +
            "the detected Spine atlas and the paths of the generated maps (normal, procedural normal, AI normal, mask).";

        private const string GenerateProceduralDescription =
            "Sprite Maps Generator: generate procedural normal and mask maps for sprite textures or Spine atlas pages and assign them as secondary sprite textures. " +
            "Optional --settings are merged over each texture's stored settings and saved to the texture. Runs synchronously. " +
            "If AI normals already exist the final normal map blends them according to aiBlend.";

        private const string GenerateAiDescription =
            "Sprite Maps Generator: start AI normal map generation (Marigold, local Python environment) for sprite textures or Spine atlas pages. " +
            "Procedural maps are regenerated first with the merged settings. The job runs in the background and can take minutes: " +
            "call larje.sprite_maps status with --wait_seconds to wait for it. When it finishes the final normal and mask maps are rebuilt automatically. " +
            "Requires an installed environment (see larje.sprite_maps install_environment).";

        private const string InstallEnvironmentDescription =
            "Sprite Maps Generator: install the Python environment for AI normals (uv venv + torch + diffusers). Downloads several GB and runs in the background: " +
            "call larje.sprite_maps status with --wait_seconds to wait for it. Requires 'uv' on PATH or configured in Preferences > Sprite Maps Generator.";

        private const string CancelDescription = "Sprite Maps Generator: cancel the running AI normals job or environment install.";

        private const string SettingsSchemaDescription = "Sprite Maps Generator: JSON schema of the per-texture settings accepted by --settings, with ranges and defaults.";

        private const string PathsDescription =
            "JSON array of asset paths of sprite textures (.png/.jpg) or Spine atlas pages, or folders to scan recursively, e.g. '[\"Assets/Art/Hero.png\", \"Assets/Art/Enemies\"]'.";

        private const string SettingsDescription =
            "Optional partial settings JSON merged over each texture's stored settings, e.g. '{\"procedural\": {\"bevelWidth\": 20}, \"aiBlend\": 0.5}'. " +
            "Values are clamped to their ranges. Use larje.sprite_maps texture_info to read the current values.";

        private const int MaxWaitSeconds = 600;
        private const int DefaultLogLines = 20;
        private const int MaxLogLines = 200;

        [ CliGroupCommand( Group, StatusDescription, Tags = new[] { "larje", "assets" } ) ]
        public static async Task< object > Status(
            [ CliArg( "wait_seconds", "Wait up to this many seconds (max 600) for the running AI job or install to finish before reporting" ) ] int waitSeconds = 0,
            [ CliArg( "log_lines", "Number of trailing log lines to include (max 200)" ) ] int logLines = DefaultLogLines,
            [ CliArg( "timeout", "Request timeout in seconds" ) ] double timeoutInSeconds = MaxWaitSeconds + 30 )
        {
            SpriteMapsService service = SpriteMapsService.Instance;
            int wait = Mathf.Clamp( waitSeconds, 0, MaxWaitSeconds );
            int lines = Mathf.Clamp( logLines, 0, MaxLogLines );

            if ( wait > 0 && service.IsBusy )
            {
                await WaitForIdle( service, wait );
            }

            return BuildStatus( service, lines );
        }

        [ CliGroupCommand( Group, TextureInfoDescription, Tags = new[] { "larje", "assets" } ) ]
        public static object TextureInfo(
            [ CliArg( "paths", PathsDescription, Required = true ) ] List< string > paths )
        {
            List< string > errors = new();
            List< Texture2D > textures = ResolveTextures( paths, errors );
            ThrowIfErrors( "Invalid paths", errors );

            return new { textures = BuildTextureInfos( textures, true ) };
        }

        [ CliGroupCommand( Group, GenerateProceduralDescription, Tags = new[] { "larje", "assets" } ) ]
        public static object GenerateProcedural(
            [ CliArg( "paths", PathsDescription, Required = true ) ] List< string > paths,
            [ CliArg( "settings", SettingsDescription ) ] string settings = null )
        {
            SpriteMapsService service = SpriteMapsService.Instance;

            if ( service.IsBusy )
            {
                throw new InvalidOperationException( "An AI normals job or environment install is running. Wait for it with larje.sprite_maps status or cancel it with larje.sprite_maps cancel." );
            }

            List< string > errors = new();
            List< Texture2D > textures = ResolveTextures( paths, errors );
            List< TextureMapSettings > merged = MergeSettings( service, textures, settings, errors );
            ThrowIfErrors( "Invalid parameters", errors );

            service.Apply( textures, merged );

            return new { textures = BuildTextureInfos( textures, false ) };
        }

        [ CliGroupCommand( Group, GenerateAiDescription, Tags = new[] { "larje", "assets" } ) ]
        public static object GenerateAi(
            [ CliArg( "paths", PathsDescription, Required = true ) ] List< string > paths,
            [ CliArg( "settings", SettingsDescription ) ] string settings = null )
        {
            SpriteMapsService service = SpriteMapsService.Instance;

            if ( service.IsBusy )
            {
                throw new InvalidOperationException( "An AI normals job or environment install is already running. Wait for it with larje.sprite_maps status or cancel it with larje.sprite_maps cancel." );
            }

            if ( !service.Environment.IsInstalled )
            {
                throw new InvalidOperationException( $"The AI environment is not installed at {service.Environment.RootPath}. Run larje.sprite_maps install_environment first." );
            }

            List< string > errors = new();
            List< Texture2D > textures = ResolveTextures( paths, errors );
            List< TextureMapSettings > merged = MergeSettings( service, textures, settings, errors );
            ThrowIfErrors( "Invalid parameters", errors );

            if ( !service.RunAi( textures, merged ) )
            {
                throw new InvalidOperationException( "Start failed:\n" + string.Join( "\n", service.GetLogTail( DefaultLogLines ) ) );
            }

            return new { started = true, textures = GetAssetPaths( textures ) };
        }

        [ CliGroupCommand( Group, InstallEnvironmentDescription, Tags = new[] { "larje", "assets" } ) ]
        public static object InstallEnvironment(
            [ CliArg( "backend", "Torch backend: Cuda (NVIDIA), Rocm (AMD, Linux) or Cpu. Defaults to the backend selected in Preferences" ) ] string backend = null,
            [ CliArg( "clean", "Delete the existing environment folder before installing" ) ] bool clean = false )
        {
            SpriteMapsService service = SpriteMapsService.Instance;

            if ( service.IsBusy )
            {
                throw new InvalidOperationException( "An AI normals job or environment install is already running." );
            }

            if ( !string.IsNullOrEmpty( backend ) )
            {
                if ( !Enum.TryParse( backend, true, out TorchBackend parsed ) )
                {
                    throw new ArgumentException( $"Invalid backend '{backend}'. Valid: {string.Join( ", ", Enum.GetNames( typeof( TorchBackend ) ) )}" );
                }

                SpriteMapsUserSettings.instance.Backend = parsed;
                SpriteMapsUserSettings.instance.SaveSettings();
            }

            if ( !service.Install( clean ) )
            {
                throw new InvalidOperationException( "Start failed:\n" + string.Join( "\n", service.GetLogTail( DefaultLogLines ) ) );
            }

            return new { installing = true, backend = SpriteMapsUserSettings.instance.Backend.ToString(), location = service.Environment.RootPath };
        }

        [ CliGroupCommand( Group, CancelDescription, Tags = new[] { "larje", "assets" } ) ]
        public static object Cancel()
        {
            SpriteMapsService service = SpriteMapsService.Instance;

            if ( !service.IsBusy )
            {
                return new { cancelled = false, reason = "Nothing is running" };
            }

            service.Cancel();
            return new { cancelled = true };
        }

        [ CliGroupCommand( Group, SettingsSchemaDescription, Tags = new[] { "larje", "assets" } ) ]
        public static object SettingsSchema()
        {
            return new SpriteMapsSettingsJson().BuildSchema( SettingsDescription );
        }

        private static async Task WaitForIdle( SpriteMapsService service, int waitSeconds )
        {
            TaskCompletionSource< bool > completion = new();
            Action< bool > handler = isSuccess => completion.TrySetResult( isSuccess );

            service.AiCompleted += handler;
            service.InstallCompleted += handler;

            try
            {
                await Task.WhenAny( completion.Task, Task.Delay( TimeSpan.FromSeconds( waitSeconds ) ) );
            }
            finally
            {
                service.AiCompleted -= handler;
                service.InstallCompleted -= handler;
            }
        }

        private static object BuildStatus( SpriteMapsService service, int logLines )
        {
            PythonEnvironment environment = service.Environment;

            return new
            {
                state = service.IsAiRunning ? "ai_running" : environment.IsInstalling ? "installing" : "idle",
                environment = new
                {
                    installed = environment.IsInstalled,
                    installing = environment.IsInstalling,
                    installedBackend = environment.InstalledBackend,
                    selectedBackend = SpriteMapsUserSettings.instance.Backend.ToString(),
                    location = environment.RootPath,
                    lastInstallResult = service.LastInstallResult.ToString()
                },
                ai = new
                {
                    running = service.IsAiRunning,
                    progressIndex = service.ProgressIndex,
                    progressTotal = service.ProgressTotal,
                    progressLabel = service.ProgressLabel,
                    lastResult = service.LastAiResult.ToString(),
                    lastTextures = service.LastAiTextures
                },
                log = service.GetLogTail( logLines )
            };
        }

        private static void ThrowIfErrors( string title, List< string > errors )
        {
            if ( errors.Count > 0 )
            {
                throw new ArgumentException( title + ":\n" + string.Join( "\n", errors ) );
            }
        }

        private static List< Texture2D > ResolveTextures( List< string > paths, List< string > errors )
        {
            List< Texture2D > textures = new();

            if ( paths == null || paths.Count == 0 )
            {
                errors.Add( "'paths' must be a non-empty JSON array of texture or folder asset paths" );
                return textures;
            }

            for ( int i = 0; i < paths.Count; i++ )
            {
                string path = paths[ i ] != null ? NormalizePath( paths[ i ] ) : null;

                if ( string.IsNullOrEmpty( path ) )
                {
                    errors.Add( $"paths[{i}] must be a non-empty string" );
                }
                else if ( AssetDatabase.IsValidFolder( path ) )
                {
                    AddFolder( path, textures, errors );
                }
                else
                {
                    AddTexture( path, textures, errors );
                }
            }

            return textures;
        }

        private static void AddFolder( string folder, List< Texture2D > textures, List< string > errors )
        {
            string[] guids = AssetDatabase.FindAssets( "t:Texture2D", new[] { folder } );
            int found = 0;

            for ( int i = 0; i < guids.Length; i++ )
            {
                Texture2D texture = AssetDatabase.LoadAssetAtPath< Texture2D >( AssetDatabase.GUIDToAssetPath( guids[ i ] ) );

                if ( SpriteMapsGenerator.IsSupported( texture ) && !textures.Contains( texture ) )
                {
                    textures.Add( texture );
                    found++;
                }
            }

            if ( found == 0 )
            {
                errors.Add( $"'{folder}' contains no supported textures" );
            }
        }

        private static void AddTexture( string path, List< Texture2D > textures, List< string > errors )
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath< Texture2D >( path );

            if ( texture == null )
            {
                errors.Add( $"'{path}' is not a texture asset" );
            }
            else if ( !SpriteMapsGenerator.IsSupported( texture ) )
            {
                errors.Add( $"'{path}' is not supported (needs a .png/.jpg texture outside the generated maps folder)" );
            }
            else if ( !textures.Contains( texture ) )
            {
                textures.Add( texture );
            }
        }

        private static List< TextureMapSettings > MergeSettings( SpriteMapsService service, List< Texture2D > textures, string settings, List< string > errors )
        {
            List< TextureMapSettings > merged = new();
            JObject overrides = ReadSettingsOverrides( settings, errors );
            HashSet< string > mergeErrors = new();
            SpriteMapsSettingsJson json = new();

            for ( int i = 0; i < textures.Count; i++ )
            {
                merged.Add( json.Merge( service.Generator.LoadSettings( textures[ i ] ), overrides, mergeErrors ) );
            }

            if ( overrides != null && textures.Count == 0 )
            {
                json.Merge( new TextureMapSettings(), overrides, mergeErrors );
            }

            errors.AddRange( mergeErrors );
            return merged;
        }

        private static JObject ReadSettingsOverrides( string settings, List< string > errors )
        {
            if ( string.IsNullOrWhiteSpace( settings ) )
            {
                return null;
            }

            try
            {
                return JObject.Parse( settings );
            }
            catch ( JsonReaderException exception )
            {
                errors.Add( $"'settings' is not a valid JSON object: {exception.Message}" );
                return null;
            }
        }

        private static List< object > BuildTextureInfos( List< Texture2D > textures, bool isSettingsIncluded )
        {
            SpriteMapsService service = SpriteMapsService.Instance;
            SpriteMapsProjectSettings projectSettings = SpriteMapsProjectSettings.instance;
            SpriteMapsSettingsJson json = new();
            SpriteMapsPaths paths = new();
            List< object > infos = new();

            for ( int i = 0; i < textures.Count; i++ )
            {
                string path = AssetDatabase.GetAssetPath( textures[ i ] );
                string atlasPath = service.AtlasLocator.Find( path );

                infos.Add( new
                {
                    path,
                    atlas = atlasPath != null ? Path.GetFileName( atlasPath ) : null,
                    hasAiNormals = service.Generator.HasAiNormals( textures[ i ] ),
                    maps = new
                    {
                        normal = GetExistingMap( paths, path, projectSettings.NormalSuffix ),
                        proceduralNormal = GetExistingMap( paths, path, projectSettings.ProceduralNormalSuffix ),
                        aiNormal = GetExistingMap( paths, path, projectSettings.AiNormalSuffix ),
                        mask = GetExistingMap( paths, path, projectSettings.MaskSuffix )
                    },
                    settings = isSettingsIncluded ? json.ToJson( service.Generator.LoadSettings( textures[ i ] ) ) : null
                } );
            }

            return infos;
        }

        private static string GetExistingMap( SpriteMapsPaths paths, string sourcePath, string suffix )
        {
            string mapPath = paths.GetMapPath( sourcePath, suffix );
            return File.Exists( mapPath ) ? mapPath : null;
        }

        private static List< string > GetAssetPaths( List< Texture2D > textures )
        {
            List< string > result = new();

            for ( int i = 0; i < textures.Count; i++ )
            {
                result.Add( AssetDatabase.GetAssetPath( textures[ i ] ) );
            }

            return result;
        }

        private static string NormalizePath( string path )
        {
            return path.Trim().Replace( '\\', '/' ).TrimEnd( '/' );
        }
    }
}

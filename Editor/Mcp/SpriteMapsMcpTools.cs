using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.ToolRegistry;
using UnityEditor;
using UnityEngine;

namespace Larje.SpriteMapsGenerator.Editor.Mcp
{
    public static class SpriteMapsMcpTools
    {
        private const string GetStatusName = "SpriteMaps.GetStatus";
        private const string GetTextureInfoName = "SpriteMaps.GetTextureInfo";
        private const string GenerateProceduralName = "SpriteMaps.GenerateProcedural";
        private const string GenerateAiName = "SpriteMaps.GenerateAi";
        private const string InstallEnvironmentName = "SpriteMaps.InstallEnvironment";
        private const string CancelName = "SpriteMaps.Cancel";
        private const string AssetsGroup = "assets";

        private const string GetStatusDescription =
            "Sprite Maps Generator: report the AI Python environment state, the running AI normals job (progress, last result) and the tail of the tool log. " +
            "Set waitSeconds to block until the running AI job or environment install finishes (or the timeout elapses) instead of polling.";

        private const string GetTextureInfoDescription =
            "Sprite Maps Generator: for sprite textures or Spine atlas pages (asset paths or folders) return the stored per-texture settings, " +
            "the detected Spine atlas and the paths of the generated maps (normal, procedural normal, AI normal, mask).";

        private const string GenerateProceduralDescription =
            "Sprite Maps Generator: generate procedural normal and mask maps for sprite textures or Spine atlas pages and assign them as secondary sprite textures. " +
            "Optional settings are merged over each texture's stored settings and saved to the texture. Runs synchronously. " +
            "If AI normals already exist the final normal map blends them according to aiBlend.";

        private const string GenerateAiDescription =
            "Sprite Maps Generator: start AI normal map generation (Marigold, local Python environment) for sprite textures or Spine atlas pages. " +
            "Procedural maps are regenerated first with the merged settings. The job runs in the background and can take minutes: " +
            "call SpriteMaps_GetStatus with waitSeconds to wait for it. When it finishes the final normal and mask maps are rebuilt automatically. " +
            "Requires an installed environment (see SpriteMaps_InstallEnvironment).";

        private const string InstallEnvironmentDescription =
            "Sprite Maps Generator: install the Python environment for AI normals (uv venv + torch + diffusers). Downloads several GB and runs in the background: " +
            "call SpriteMaps_GetStatus with waitSeconds to wait for it. Requires 'uv' on PATH or configured in Preferences > Sprite Maps Generator.";

        private const string CancelDescription = "Sprite Maps Generator: cancel the running AI normals job or environment install.";

        private const string PathsDescription =
            "Asset paths of sprite textures (.png/.jpg) or Spine atlas pages, or folders to scan recursively, e.g. [\"Assets/Art/Hero.png\", \"Assets/Art/Enemies\"]. " +
            "A single string is also accepted.";

        private const string SettingsDescription =
            "Optional partial settings merged over each texture's stored settings, e.g. {\"procedural\": {\"bevelWidth\": 20}, \"aiBlend\": 0.5}. " +
            "Values are clamped to their ranges. Use SpriteMaps_GetTextureInfo to read the current values.";

        private const int MaxWaitSeconds = 600;
        private const int DefaultLogLines = 20;
        private const int MaxLogLines = 200;

        [ McpSchema( GetStatusName ) ]
        public static object GetStatusSchema()
        {
            return new JObject
            {
                [ "type" ] = "object",
                [ "properties" ] = new JObject
                {
                    [ "waitSeconds" ] = new JObject
                    {
                        [ "type" ] = "integer",
                        [ "minimum" ] = 0,
                        [ "maximum" ] = MaxWaitSeconds,
                        [ "default" ] = 0,
                        [ "description" ] = "Wait up to this many seconds for the running AI job or install to finish before reporting."
                    },
                    [ "logLines" ] = new JObject
                    {
                        [ "type" ] = "integer",
                        [ "minimum" ] = 0,
                        [ "maximum" ] = MaxLogLines,
                        [ "default" ] = DefaultLogLines,
                        [ "description" ] = "Number of trailing log lines to include."
                    }
                }
            };
        }

        [ McpTool( GetStatusName, GetStatusDescription, "Get Sprite Maps Generator status", EnabledByDefault = true, Groups = new[] { AssetsGroup } ) ]
        public static async Task< object > GetStatus( JObject parameters )
        {
            SpriteMapsService service = SpriteMapsService.Instance;
            int waitSeconds = Mathf.Clamp( ReadInt( parameters, "waitSeconds", 0 ), 0, MaxWaitSeconds );
            int logLines = Mathf.Clamp( ReadInt( parameters, "logLines", DefaultLogLines ), 0, MaxLogLines );

            if ( waitSeconds > 0 && service.IsBusy )
            {
                await WaitForIdle( service, waitSeconds );
            }

            string message = service.IsAiRunning ? "AI normals job is running" : service.Environment.IsInstalling ? "Environment install is running" : "Idle";
            return Response.Success( message, BuildStatus( service, logLines ) );
        }

        [ McpSchema( GetTextureInfoName ) ]
        public static object GetTextureInfoSchema()
        {
            return BuildPathsSchema( false );
        }

        [ McpTool( GetTextureInfoName, GetTextureInfoDescription, "Get sprite texture map info", EnabledByDefault = true, Groups = new[] { AssetsGroup } ) ]
        public static object GetTextureInfo( JObject parameters )
        {
            List< string > errors = new();
            List< Texture2D > textures = ResolveTextures( parameters, errors );

            if ( errors.Count > 0 )
            {
                return Response.Error( "INVALID_PATHS", new { errors } );
            }

            return Response.Success( $"{textures.Count} texture(s)", new { textures = BuildTextureInfos( textures, true ) } );
        }

        [ McpSchema( GenerateProceduralName ) ]
        public static object GenerateProceduralSchema()
        {
            return BuildPathsSchema( true );
        }

        [ McpTool( GenerateProceduralName, GenerateProceduralDescription, "Generate procedural sprite maps", EnabledByDefault = true, Groups = new[] { AssetsGroup } ) ]
        public static object GenerateProcedural( JObject parameters )
        {
            SpriteMapsService service = SpriteMapsService.Instance;

            if ( service.IsBusy )
            {
                return Response.Error( "BUSY", new { reason = "An AI normals job or environment install is running. Wait for it with SpriteMaps_GetStatus or cancel it with SpriteMaps_Cancel." } );
            }

            List< string > errors = new();
            List< Texture2D > textures = ResolveTextures( parameters, errors );
            List< TextureMapSettings > settings = MergeSettings( service, textures, parameters, errors );

            if ( errors.Count > 0 )
            {
                return Response.Error( "INVALID_PARAMETERS", new { errors } );
            }

            service.Apply( textures, settings );

            return Response.Success( $"Generated procedural maps for {textures.Count} texture(s)", new { textures = BuildTextureInfos( textures, false ) } );
        }

        [ McpSchema( GenerateAiName ) ]
        public static object GenerateAiSchema()
        {
            return BuildPathsSchema( true );
        }

        [ McpTool( GenerateAiName, GenerateAiDescription, "Generate AI sprite normal maps", EnabledByDefault = true, Groups = new[] { AssetsGroup } ) ]
        public static object GenerateAi( JObject parameters )
        {
            SpriteMapsService service = SpriteMapsService.Instance;

            if ( service.IsBusy )
            {
                return Response.Error( "BUSY", new { reason = "An AI normals job or environment install is already running. Wait for it with SpriteMaps_GetStatus or cancel it with SpriteMaps_Cancel." } );
            }

            if ( !service.Environment.IsInstalled )
            {
                return Response.Error( "ENVIRONMENT_NOT_INSTALLED", new { reason = "Install the AI environment with SpriteMaps_InstallEnvironment first.", location = service.Environment.RootPath } );
            }

            List< string > errors = new();
            List< Texture2D > textures = ResolveTextures( parameters, errors );
            List< TextureMapSettings > settings = MergeSettings( service, textures, parameters, errors );

            if ( errors.Count > 0 )
            {
                return Response.Error( "INVALID_PARAMETERS", new { errors } );
            }

            if ( !service.RunAi( textures, settings ) )
            {
                return Response.Error( "START_FAILED", new { log = service.GetLogTail( DefaultLogLines ) } );
            }

            return Response.Success(
                $"Started AI normals for {textures.Count} texture(s). Call {Sanitize( GetStatusName )} with waitSeconds to wait for completion.",
                new { textures = GetAssetPaths( textures ) } );
        }

        [ McpSchema( InstallEnvironmentName ) ]
        public static object InstallEnvironmentSchema()
        {
            return new JObject
            {
                [ "type" ] = "object",
                [ "properties" ] = new JObject
                {
                    [ "backend" ] = new JObject
                    {
                        [ "type" ] = "string",
                        [ "enum" ] = new JArray( Enum.GetNames( typeof( TorchBackend ) ) ),
                        [ "description" ] = "Torch backend: Cuda (NVIDIA), Rocm (AMD, Linux) or Cpu. Defaults to the backend selected in Preferences."
                    },
                    [ "clean" ] = new JObject
                    {
                        [ "type" ] = "boolean",
                        [ "default" ] = false,
                        [ "description" ] = "Delete the existing environment folder before installing."
                    }
                }
            };
        }

        [ McpTool( InstallEnvironmentName, InstallEnvironmentDescription, "Install sprite maps AI environment", EnabledByDefault = true, Groups = new[] { AssetsGroup } ) ]
        public static object InstallEnvironment( JObject parameters )
        {
            SpriteMapsService service = SpriteMapsService.Instance;

            if ( service.IsBusy )
            {
                return Response.Error( "BUSY", new { reason = "An AI normals job or environment install is already running." } );
            }

            string backendName = parameters?[ "backend" ]?.Type == JTokenType.String ? (string)parameters[ "backend" ] : null;

            if ( !string.IsNullOrEmpty( backendName ) )
            {
                if ( !Enum.TryParse( backendName, true, out TorchBackend backend ) )
                {
                    return Response.Error( "INVALID_BACKEND", new { valid = Enum.GetNames( typeof( TorchBackend ) ) } );
                }

                SpriteMapsUserSettings.instance.Backend = backend;
                SpriteMapsUserSettings.instance.SaveSettings();
            }

            bool isClean = parameters?[ "clean" ]?.Type == JTokenType.Boolean && (bool)parameters[ "clean" ];

            if ( !service.Install( isClean ) )
            {
                return Response.Error( "START_FAILED", new { log = service.GetLogTail( DefaultLogLines ) } );
            }

            return Response.Success(
                $"Installing the environment ({SpriteMapsUserSettings.instance.Backend}) to {service.Environment.RootPath}. Call {Sanitize( GetStatusName )} with waitSeconds to wait for completion." );
        }

        [ McpTool( CancelName, CancelDescription, "Cancel sprite maps job", EnabledByDefault = true, Groups = new[] { AssetsGroup } ) ]
        public static object Cancel()
        {
            SpriteMapsService service = SpriteMapsService.Instance;

            if ( !service.IsBusy )
            {
                return Response.Success( "Nothing is running" );
            }

            service.Cancel();
            return Response.Success( "Cancellation requested" );
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

        private static JObject BuildPathsSchema( bool hasSettings )
        {
            JObject properties = new()
            {
                [ "paths" ] = new JObject
                {
                    [ "type" ] = "array",
                    [ "items" ] = new JObject { [ "type" ] = "string" },
                    [ "minItems" ] = 1,
                    [ "description" ] = PathsDescription
                }
            };

            if ( hasSettings )
            {
                properties[ "settings" ] = new SpriteMapsSettingsJson().BuildSchema( SettingsDescription );
            }

            return new JObject
            {
                [ "type" ] = "object",
                [ "properties" ] = properties,
                [ "required" ] = new JArray( "paths" )
            };
        }

        private static List< Texture2D > ResolveTextures( JObject parameters, List< string > errors )
        {
            List< Texture2D > textures = new();
            JToken token = parameters?[ "paths" ];
            JArray paths = token is JArray array ? array : token?.Type == JTokenType.String ? new JArray( token ) : null;

            if ( paths == null || paths.Count == 0 )
            {
                errors.Add( "'paths' must be a non-empty array of texture or folder asset paths" );
                return textures;
            }

            for ( int i = 0; i < paths.Count; i++ )
            {
                string path = paths[ i ].Type == JTokenType.String ? NormalizePath( (string)paths[ i ] ) : null;

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

        private static List< TextureMapSettings > MergeSettings( SpriteMapsService service, List< Texture2D > textures, JObject parameters, List< string > errors )
        {
            List< TextureMapSettings > settings = new();
            JObject overrides = ReadSettingsOverrides( parameters, errors );
            HashSet< string > mergeErrors = new();
            SpriteMapsSettingsJson json = new();

            for ( int i = 0; i < textures.Count; i++ )
            {
                settings.Add( json.Merge( service.Generator.LoadSettings( textures[ i ] ), overrides, mergeErrors ) );
            }

            if ( overrides != null && textures.Count == 0 )
            {
                json.Merge( new TextureMapSettings(), overrides, mergeErrors );
            }

            errors.AddRange( mergeErrors );
            return settings;
        }

        private static JObject ReadSettingsOverrides( JObject parameters, List< string > errors )
        {
            JToken token = parameters?[ "settings" ];

            if ( token == null || token.Type == JTokenType.Null )
            {
                return null;
            }

            if ( token is JObject settings )
            {
                return settings;
            }

            if ( token.Type == JTokenType.String )
            {
                try
                {
                    return JObject.Parse( (string)token );
                }
                catch ( JsonReaderException exception )
                {
                    errors.Add( $"'settings' is not valid JSON: {exception.Message}" );
                    return null;
                }
            }

            errors.Add( "'settings' must be an object" );
            return null;
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

        private static int ReadInt( JObject parameters, string key, int fallback )
        {
            JToken token = parameters?[ key ];
            bool isNumber = token != null && ( token.Type == JTokenType.Integer || token.Type == JTokenType.Float );

            return isNumber ? token.Value< int >() : fallback;
        }

        private static string NormalizePath( string path )
        {
            return path.Trim().Replace( '\\', '/' ).TrimEnd( '/' );
        }

        private static string Sanitize( string toolName )
        {
            return McpToolRegistry.SanitizeToolName( toolName );
        }
    }
}

using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Larje.SpriteMapsGenerator.Editor
{
    public sealed class SpriteMapsWindow : EditorWindow
    {
        private const string WindowTitle = "Sprite Maps";
        private const string ToolsMenuPath = "Tools/Sprite Maps Generator";
        private const string AssetsMenuPath = "Assets/Sprite Maps Generator";
        private const string ProceduralPath = "_settings._procedural";
        private const string MaskPath = "_settings._mask";
        private const string AiPath = "_settings._ai";
        private const string BlendPath = "_settings._aiBlend";
        private const double ApplyDelay = 0.4;
        private const float PreviewSize = 160f;
        private const float ButtonWidth = 90f;
        private const float LogHeight = 160f;
        private const int MaxLogLength = 20000;

        [ SerializeField ] private bool _showProcedural = true;
        [ SerializeField ] private bool _showMask;
        [ SerializeField ] private bool _showAi = true;
        [ SerializeField ] private bool _showLog;

        private bool _isApplyPending;
        private bool _isPreviewDirty;
        private int _progressIndex;
        private int _progressTotal;
        private double _applyTime;
        private string _progressLabel = string.Empty;
        private string _activeAtlasPath;
        private Vector2 _scroll;
        private Vector2 _logScroll;

        private SpriteMapsGenerator _generator;
        private PythonEnvironment _environment;
        private AiNormalsRunner _aiRunner;
        private SpineAtlasLocator _atlasLocator;
        private TextureMapSettingsHolder _holder;
        private SerializedObject _serializedHolder;
        private Texture2D _activeTexture;
        private Texture2D _normalPreview;
        private Texture2D _maskPreview;
        private readonly StringBuilder _log = new();
        private readonly List< Texture2D > _selection = new();
        private readonly List< Texture2D > _aiTargets = new();

        [ MenuItem( ToolsMenuPath ) ]
        private static void Open()
        {
            GetWindow< SpriteMapsWindow >( WindowTitle );
        }

        [ MenuItem( AssetsMenuPath ) ]
        private static void OpenFromAssets()
        {
            Open();
        }

        [ MenuItem( AssetsMenuPath, true ) ]
        private static bool ValidateOpenFromAssets()
        {
            Texture2D[] textures = Selection.GetFiltered< Texture2D >( SelectionMode.Assets );

            for ( int i = 0; i < textures.Length; i++ )
            {
                if ( SpriteMapsGenerator.IsSupported( textures[ i ] ) )
                {
                    return true;
                }
            }

            return false;
        }

        private void OnEnable()
        {
            _generator = new SpriteMapsGenerator();
            _environment = new PythonEnvironment( SpriteMapsUserSettings.instance );
            _aiRunner = new AiNormalsRunner();
            _atlasLocator = new SpineAtlasLocator();
            _holder = CreateInstance< TextureMapSettingsHolder >();
            _holder.hideFlags = HideFlags.HideAndDontSave;
            _serializedHolder = new SerializedObject( _holder );

            _environment.OutputReceived += AppendLog;
            _environment.InstallCompleted += HandleInstallCompleted;
            _aiRunner.OutputReceived += AppendLog;
            _aiRunner.ProgressChanged += HandleAiProgress;
            _aiRunner.Completed += HandleAiCompleted;

            RefreshSelection();
        }

        private void OnDisable()
        {
            FlushPendingApply();

            _environment.OutputReceived -= AppendLog;
            _environment.InstallCompleted -= HandleInstallCompleted;
            _aiRunner.OutputReceived -= AppendLog;
            _aiRunner.ProgressChanged -= HandleAiProgress;
            _aiRunner.Completed -= HandleAiCompleted;

            _aiRunner.Cancel();
            _environment.Cancel();

            DestroyImmediate( _holder );
            DestroyPreview( _normalPreview );
            DestroyPreview( _maskPreview );
        }

        private void OnSelectionChange()
        {
            FlushPendingApply();
            RefreshSelection();
            Repaint();
        }

        private void Update()
        {
            if ( _isApplyPending && EditorApplication.timeSinceStartup >= _applyTime )
            {
                FlushPendingApply();
            }
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView( _scroll );

            DrawEnvironment();
            EditorGUILayout.Space();
            DrawSelection();

            if ( _activeTexture != null )
            {
                EditorGUILayout.Space();
                DrawSettings();
                EditorGUILayout.Space();
                DrawActions();
                EditorGUILayout.Space();
                DrawPreview();
            }

            EditorGUILayout.Space();
            DrawLog();

            EditorGUILayout.EndScrollView();
        }

        private void DrawEnvironment()
        {
            SpriteMapsUserSettings userSettings = SpriteMapsUserSettings.instance;
            bool isBusy = _environment.IsInstalling || _aiRunner.IsRunning;
            string status = _environment.IsInstalled ? $"Installed ({_environment.InstalledBackend})" : "Not installed";

            EditorGUILayout.LabelField( "AI Environment", EditorStyles.boldLabel );
            EditorGUILayout.LabelField( "Status", status );
            EditorGUILayout.LabelField( "Location", _environment.RootPath, EditorStyles.wordWrappedMiniLabel );

            using ( new EditorGUI.DisabledScope( isBusy ) )
            {
                EditorGUI.BeginChangeCheck();
                TorchBackend backend = (TorchBackend)EditorGUILayout.EnumPopup( "Backend", userSettings.Backend );

                if ( EditorGUI.EndChangeCheck() )
                {
                    userSettings.Backend = backend;
                    userSettings.SaveSettings();
                }
            }

            using ( new EditorGUILayout.HorizontalScope() )
            {
                if ( _environment.IsInstalling )
                {
                    EditorGUILayout.LabelField( "Installing...", EditorStyles.miniLabel );

                    if ( GUILayout.Button( "Cancel", GUILayout.Width( ButtonWidth ) ) )
                    {
                        _environment.Cancel();
                    }
                }
                else
                {
                    using ( new EditorGUI.DisabledScope( isBusy ) )
                    {
                        if ( GUILayout.Button( _environment.IsInstalled ? "Reinstall" : "Install" ) )
                        {
                            InstallEnvironment();
                        }
                    }

                    if ( GUILayout.Button( "Preferences", GUILayout.Width( ButtonWidth ) ) )
                    {
                        SettingsService.OpenUserPreferences( SpriteMapsSettingsProviders.UserSettingsPath );
                    }
                }
            }
        }

        private void DrawSelection()
        {
            EditorGUILayout.LabelField( "Textures", EditorStyles.boldLabel );

            if ( _activeTexture == null )
            {
                EditorGUILayout.HelpBox( "Select sprite textures or Spine atlas pages in the Project window.", MessageType.Info );
                return;
            }

            EditorGUILayout.LabelField( "Active", _activeTexture.name );
            EditorGUILayout.LabelField( "Spine atlas", _activeAtlasPath != null ? Path.GetFileName( _activeAtlasPath ) : "None" );
            EditorGUILayout.LabelField( "AI normals", _generator.HasAiNormals( _activeTexture ) ? "Generated" : "None" );

            if ( _selection.Count > 1 )
            {
                EditorGUILayout.HelpBox( $"{_selection.Count} textures selected. Settings are applied to all of them.", MessageType.None );
            }
        }

        private void DrawSettings()
        {
            _serializedHolder.Update();

            bool isChanged = DrawGroup( "Procedural", ProceduralPath, ref _showProcedural );
            isChanged |= DrawGroup( "Mask", MaskPath, ref _showMask );
            isChanged |= DrawGroup( "AI", AiPath, ref _showAi );

            EditorGUILayout.Space();
            EditorGUILayout.LabelField( "Blend", EditorStyles.boldLabel );

            bool hasAiNormals = _generator.HasAiNormals( _activeTexture );
            EditorGUI.BeginChangeCheck();

            using ( new EditorGUI.DisabledScope( !hasAiNormals ) )
            {
                EditorGUILayout.Slider( _serializedHolder.FindProperty( BlendPath ), 0f, 1f, new GUIContent( "Procedural ↔ AI" ) );
            }

            bool isBlendChanged = EditorGUI.EndChangeCheck();

            if ( !hasAiNormals )
            {
                EditorGUILayout.HelpBox( "Generate AI normals to enable blending.", MessageType.None );
            }

            if ( !isChanged && !isBlendChanged )
            {
                return;
            }

            _serializedHolder.ApplyModifiedPropertiesWithoutUndo();
            ScheduleApply();

            if ( isBlendChanged )
            {
                UpdatePreview();
            }
        }

        private bool DrawGroup( string title, string path, ref bool isExpanded )
        {
            isExpanded = EditorGUILayout.Foldout( isExpanded, title, true, EditorStyles.foldoutHeader );

            if ( !isExpanded )
            {
                return false;
            }

            EditorGUI.BeginChangeCheck();
            EditorGUI.indentLevel++;

            SerializedProperty property = _serializedHolder.FindProperty( path );
            SerializedProperty end = property.GetEndProperty();
            bool hasNext = property.NextVisible( true );

            while ( hasNext && !SerializedProperty.EqualContents( property, end ) )
            {
                EditorGUILayout.PropertyField( property, true );
                hasNext = property.NextVisible( false );
            }

            EditorGUI.indentLevel--;
            return EditorGUI.EndChangeCheck();
        }

        private void DrawActions()
        {
            bool isBusy = _aiRunner.IsRunning || _environment.IsInstalling;

            EditorGUILayout.LabelField( "Generate", EditorStyles.boldLabel );

            using ( new EditorGUI.DisabledScope( isBusy ) )
            {
                if ( GUILayout.Button( "Generate Procedural Maps" ) )
                {
                    _isApplyPending = false;
                    ApplyWithSettings( _selection, _holder.Settings );
                }

                using ( new EditorGUI.DisabledScope( !_environment.IsInstalled ) )
                {
                    if ( GUILayout.Button( "Generate AI Normals" ) )
                    {
                        RunAi();
                    }
                }
            }

            if ( !_environment.IsInstalled )
            {
                EditorGUILayout.HelpBox( "Install the AI environment to generate AI normals.", MessageType.None );
            }

            if ( !_aiRunner.IsRunning )
            {
                return;
            }

            using ( new EditorGUILayout.HorizontalScope() )
            {
                Rect rect = GUILayoutUtility.GetRect( 18f, 18f, GUILayout.ExpandWidth( true ) );
                float progress = _progressTotal > 0 ? (float)_progressIndex / _progressTotal : 0f;
                EditorGUI.ProgressBar( rect, progress, $"{_progressLabel} ({_progressIndex}/{_progressTotal})" );

                if ( GUILayout.Button( "Cancel", GUILayout.Width( ButtonWidth ) ) )
                {
                    _aiRunner.Cancel();
                }
            }
        }

        private void DrawPreview()
        {
            if ( _isPreviewDirty )
            {
                UpdatePreview();
            }

            EditorGUILayout.LabelField( "Preview", EditorStyles.boldLabel );

            using ( new EditorGUILayout.HorizontalScope() )
            {
                DrawPreviewTexture( "Source", _activeTexture, true );
                DrawPreviewTexture( "Normal", _normalPreview, false );
                DrawPreviewTexture( "Mask", _maskPreview, false );
            }
        }

        private void DrawPreviewTexture( string title, Texture2D texture, bool isTransparent )
        {
            using ( new EditorGUILayout.VerticalScope() )
            {
                EditorGUILayout.LabelField( title, EditorStyles.centeredGreyMiniLabel );
                Rect rect = GUILayoutUtility.GetRect( PreviewSize, PreviewSize, GUILayout.ExpandWidth( true ) );

                if ( texture == null )
                {
                    return;
                }

                if ( isTransparent )
                {
                    EditorGUI.DrawTextureTransparent( rect, texture, ScaleMode.ScaleToFit );
                }
                else
                {
                    GUI.DrawTexture( rect, texture, ScaleMode.ScaleToFit, false );
                }
            }
        }

        private void DrawLog()
        {
            _showLog = EditorGUILayout.Foldout( _showLog, "Log", true, EditorStyles.foldoutHeader );

            if ( !_showLog )
            {
                return;
            }

            _logScroll = EditorGUILayout.BeginScrollView( _logScroll, GUILayout.Height( LogHeight ) );
            EditorGUILayout.SelectableLabel( _log.ToString(), EditorStyles.wordWrappedMiniLabel, GUILayout.ExpandHeight( true ) );
            EditorGUILayout.EndScrollView();

            if ( GUILayout.Button( "Clear Log", GUILayout.Width( ButtonWidth ) ) )
            {
                _log.Clear();
            }
        }

        private void RefreshSelection()
        {
            _selection.Clear();

            Texture2D[] textures = Selection.GetFiltered< Texture2D >( SelectionMode.Assets );

            for ( int i = 0; i < textures.Length; i++ )
            {
                if ( SpriteMapsGenerator.IsSupported( textures[ i ] ) )
                {
                    _selection.Add( textures[ i ] );
                }
            }

            Texture2D active = Selection.activeObject as Texture2D;
            _activeTexture = null;
            _activeAtlasPath = null;

            if ( active != null && _selection.Contains( active ) )
            {
                _activeTexture = active;
            }
            else if ( _selection.Count > 0 )
            {
                _activeTexture = _selection[ 0 ];
            }

            if ( _activeTexture != null )
            {
                _holder.Settings = _generator.LoadSettings( _activeTexture );
                _serializedHolder.Update();
                _activeAtlasPath = _atlasLocator.Find( AssetDatabase.GetAssetPath( _activeTexture ) );
            }

            _isPreviewDirty = true;
        }

        private void ScheduleApply()
        {
            _isApplyPending = true;
            _applyTime = EditorApplication.timeSinceStartup + ApplyDelay;
        }

        private void FlushPendingApply()
        {
            if ( !_isApplyPending )
            {
                return;
            }

            _isApplyPending = false;
            ApplyWithSettings( _selection, _holder.Settings );
        }

        private void ApplyWithSettings( List< Texture2D > textures, TextureMapSettings settings )
        {
            try
            {
                for ( int i = 0; i < textures.Count; i++ )
                {
                    ShowApplyProgress( textures, i );
                    _generator.Apply( textures[ i ], settings.Clone() );
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            UpdatePreview();
        }

        private void ApplyWithStoredSettings( List< Texture2D > textures )
        {
            try
            {
                for ( int i = 0; i < textures.Count; i++ )
                {
                    ShowApplyProgress( textures, i );
                    _generator.Apply( textures[ i ], _generator.LoadSettings( textures[ i ] ) );
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            RefreshSelection();
            UpdatePreview();
        }

        private void ShowApplyProgress( List< Texture2D > textures, int index )
        {
            if ( textures.Count > 1 )
            {
                EditorUtility.DisplayProgressBar( WindowTitle, textures[ index ].name, (float)index / textures.Count );
            }
        }

        private void UpdatePreview()
        {
            _isPreviewDirty = false;

            if ( _activeTexture == null )
            {
                return;
            }

            ComposedMaps maps = _generator.Compose( _activeTexture, _holder.Settings );
            _normalPreview = WritePreview( _normalPreview, maps.Normals, maps.Width, maps.Height );
            _maskPreview = WritePreview( _maskPreview, maps.Mask, maps.Width, maps.Height );
            Repaint();
        }

        private Texture2D WritePreview( Texture2D texture, Color32[] pixels, int width, int height )
        {
            if ( texture == null || texture.width != width || texture.height != height )
            {
                DestroyPreview( texture );
                texture = new Texture2D( width, height, TextureFormat.RGBA32, false, true ) { hideFlags = HideFlags.HideAndDontSave };
            }

            texture.SetPixels32( pixels );
            texture.Apply( false );
            return texture;
        }

        private void DestroyPreview( Texture2D texture )
        {
            if ( texture != null )
            {
                DestroyImmediate( texture );
            }
        }

        private void InstallEnvironment()
        {
            bool isClean = _environment.IsInstalled;

            if ( isClean && !EditorUtility.DisplayDialog( WindowTitle, $"Delete and reinstall the environment at\n{_environment.RootPath}?", "Reinstall", "Cancel" ) )
            {
                return;
            }

            _showLog = true;
            _environment.Install( isClean );
        }

        private void RunAi()
        {
            _isApplyPending = false;
            ApplyWithSettings( _selection, _holder.Settings );

            List< AiNormalsJob > jobs = new();
            _aiTargets.Clear();

            for ( int i = 0; i < _selection.Count; i++ )
            {
                Texture2D texture = _selection[ i ];
                string path = AssetDatabase.GetAssetPath( texture );
                string atlasPath = _atlasLocator.Find( path );
                string outputPath = Path.GetFullPath( _generator.PrepareAiNormalPath( texture ) );

                jobs.Add( new AiNormalsJob( Path.GetFullPath( path ), atlasPath, outputPath, _holder.Settings.Ai ) );
                _aiTargets.Add( texture );
            }

            _progressIndex = 0;
            _progressTotal = jobs.Count;
            _progressLabel = "Loading model";
            _showLog = true;

            if ( !_aiRunner.Run( _environment, SpriteMapsUserSettings.instance, jobs ) )
            {
                _aiTargets.Clear();
            }
        }

        private void HandleAiProgress( int index, int total, string label )
        {
            _progressIndex = index;
            _progressTotal = total;
            _progressLabel = label;
            Repaint();
        }

        private void HandleAiCompleted( bool isSuccess )
        {
            AssetDatabase.Refresh();

            List< Texture2D > targets = new();

            for ( int i = 0; i < _aiTargets.Count; i++ )
            {
                if ( _aiTargets[ i ] != null && _generator.HasAiNormals( _aiTargets[ i ] ) )
                {
                    targets.Add( _aiTargets[ i ] );
                }
            }

            _aiTargets.Clear();
            ApplyWithStoredSettings( targets );

            if ( !isSuccess )
            {
                Debug.LogWarning( "[SpriteMapsGenerator] AI normals finished with errors, see the log in the Sprite Maps window" );
            }
        }

        private void HandleInstallCompleted( bool isSuccess )
        {
            Repaint();
        }

        private void AppendLog( string line )
        {
            _log.AppendLine( line );

            if ( _log.Length > MaxLogLength )
            {
                _log.Remove( 0, _log.Length - MaxLogLength );
            }

            _logScroll.y = float.MaxValue;
            Repaint();
        }
    }
}

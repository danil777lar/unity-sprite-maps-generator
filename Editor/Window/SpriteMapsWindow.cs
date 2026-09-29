using System.Collections.Generic;
using System.IO;
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

        [ SerializeField ] private bool _showProcedural = true;
        [ SerializeField ] private bool _showMask;
        [ SerializeField ] private bool _showAi = true;
        [ SerializeField ] private bool _showLog;

        private bool _isApplyPending;
        private bool _isApplying;
        private bool _isPreviewDirty;
        private double _applyTime;
        private string _activeAtlasPath;
        private Vector2 _scroll;
        private Vector2 _logScroll;

        private SpriteMapsService _service;
        private SpriteMapsGenerator _generator;
        private PythonEnvironment _environment;
        private TextureMapSettingsHolder _holder;
        private SerializedObject _serializedHolder;
        private Texture2D _activeTexture;
        private Texture2D _normalPreview;
        private Texture2D _proceduralNormalPreview;
        private Texture2D _aiNormalPreview;
        private Texture2D _maskPreview;
        private readonly List< Texture2D > _selection = new();

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
            _service = SpriteMapsService.Instance;
            _generator = _service.Generator;
            _environment = _service.Environment;
            _holder = CreateInstance< TextureMapSettingsHolder >();
            _holder.hideFlags = HideFlags.HideAndDontSave & ~HideFlags.NotEditable;
            _serializedHolder = new SerializedObject( _holder );

            _service.OutputReceived += HandleOutput;
            _service.ProgressChanged += Repaint;
            _service.AiCompleted += HandleJobCompleted;
            _service.InstallCompleted += HandleJobCompleted;
            SpriteMapsEvents.MapsGenerated += HandleMapsGenerated;

            RefreshSelection();
        }

        private void OnDisable()
        {
            FlushPendingApply();

            _service.OutputReceived -= HandleOutput;
            _service.ProgressChanged -= Repaint;
            _service.AiCompleted -= HandleJobCompleted;
            _service.InstallCompleted -= HandleJobCompleted;
            SpriteMapsEvents.MapsGenerated -= HandleMapsGenerated;

            DestroyImmediate( _holder );
            DestroyPreview( _normalPreview );
            DestroyPreview( _proceduralNormalPreview );
            DestroyPreview( _aiNormalPreview );
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
            bool isBusy = _service.IsBusy;
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
                        _service.Cancel();
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
            bool isBusy = _service.IsBusy;

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

            if ( !_service.IsAiRunning )
            {
                return;
            }

            using ( new EditorGUILayout.HorizontalScope() )
            {
                Rect rect = GUILayoutUtility.GetRect( 18f, 18f, GUILayout.ExpandWidth( true ) );
                float progress = _service.ProgressTotal > 0 ? (float)_service.ProgressIndex / _service.ProgressTotal : 0f;
                EditorGUI.ProgressBar( rect, progress, $"{_service.ProgressLabel} ({_service.ProgressIndex}/{_service.ProgressTotal})" );

                if ( GUILayout.Button( "Cancel", GUILayout.Width( ButtonWidth ) ) )
                {
                    _service.Cancel();
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
                DrawPreviewTexture( "Mask", _maskPreview, false );
            }

            using ( new EditorGUILayout.HorizontalScope() )
            {
                DrawPreviewTexture( "Procedural Normal", _proceduralNormalPreview, false );
                DrawPreviewTexture( "AI Normal", _aiNormalPreview, false );
                DrawPreviewTexture( "Final Normal", _normalPreview, false );
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
                    GUI.Label( rect, "None", EditorStyles.centeredGreyMiniLabel );
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
            EditorGUILayout.SelectableLabel( _service.Log, EditorStyles.wordWrappedMiniLabel, GUILayout.ExpandHeight( true ) );
            EditorGUILayout.EndScrollView();

            if ( GUILayout.Button( "Clear Log", GUILayout.Width( ButtonWidth ) ) )
            {
                _service.ClearLog();
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
                _activeAtlasPath = _service.AtlasLocator.Find( AssetDatabase.GetAssetPath( _activeTexture ) );
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
            _isApplying = true;

            try
            {
                _service.Apply( textures, CloneForEach( textures, settings ) );
            }
            finally
            {
                _isApplying = false;
            }

            UpdatePreview();
        }

        private List< TextureMapSettings > CloneForEach( List< Texture2D > textures, TextureMapSettings settings )
        {
            List< TextureMapSettings > clones = new();

            for ( int i = 0; i < textures.Count; i++ )
            {
                clones.Add( settings.Clone() );
            }

            return clones;
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
            _proceduralNormalPreview = WritePreview( _proceduralNormalPreview, maps.ProceduralNormals, maps.Width, maps.Height );
            _aiNormalPreview = WritePreview( _aiNormalPreview, maps.AiNormals, maps.Width, maps.Height );
            _maskPreview = WritePreview( _maskPreview, maps.Mask, maps.Width, maps.Height );
            Repaint();
        }

        private Texture2D WritePreview( Texture2D texture, Color32[] pixels, int width, int height )
        {
            if ( pixels == null )
            {
                DestroyPreview( texture );
                return null;
            }

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
            _service.Install( isClean );
        }

        private void RunAi()
        {
            _isApplyPending = false;
            _showLog = true;
            _isApplying = true;

            try
            {
                _service.RunAi( _selection, CloneForEach( _selection, _holder.Settings ) );
            }
            finally
            {
                _isApplying = false;
            }

            UpdatePreview();
        }

        private void HandleMapsGenerated( SpriteMapsResult result )
        {
            if ( _isApplying || result.Source != _activeTexture )
            {
                return;
            }

            _isApplyPending = false;
            RefreshSelection();
            Repaint();
        }

        private void HandleJobCompleted( bool isSuccess )
        {
            Repaint();
        }

        private void HandleOutput( string line )
        {
            _logScroll.y = float.MaxValue;
            Repaint();
        }
    }
}

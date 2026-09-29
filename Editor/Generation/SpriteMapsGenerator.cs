using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Larje.SpriteMapsGenerator.Editor
{
    public sealed class SpriteMapsGenerator
    {
        private readonly NormalBlender _blender = new();
        private readonly SpriteMapsPaths _paths = new();
        private readonly TextureMapSettingsStore _store = new();
        private readonly Dictionary< string, CachedTexture > _cache = new();

        public static bool IsSupported( Texture2D texture )
        {
            if ( texture == null )
            {
                return false;
            }

            string path = AssetDatabase.GetAssetPath( texture );
            string extension = Path.GetExtension( path ).ToLowerInvariant();
            SpriteMapsPaths paths = new();

            bool isSupportedFormat = extension == ".png" || extension == ".jpg" || extension == ".jpeg";

            return isSupportedFormat && !paths.IsInOutputFolder( path ) && AssetImporter.GetAtPath( path ) is TextureImporter;
        }

        public TextureMapSettings LoadSettings( Texture2D texture )
        {
            return _store.Load( GetImporter( texture ) );
        }

        public string GetAiNormalPath( Texture2D texture )
        {
            return _paths.GetMapPath( AssetDatabase.GetAssetPath( texture ), SpriteMapsProjectSettings.instance.AiNormalSuffix );
        }

        public string PrepareAiNormalPath( Texture2D texture )
        {
            _paths.EnsureFolder( AssetDatabase.GetAssetPath( texture ) );
            return GetAiNormalPath( texture );
        }

        public bool HasAiNormals( Texture2D texture )
        {
            return File.Exists( GetAiNormalPath( texture ) );
        }

        public ComposedMaps Compose( Texture2D texture, TextureMapSettings settings )
        {
            string path = AssetDatabase.GetAssetPath( texture );
            CachedTexture cached = GetCached( path );
            ProceduralMaps procedural = GetProcedural( cached, settings.Procedural );
            Vector3[] aiNormals = GetAiNormals( cached, GetAiNormalPath( texture ) );

            Vector3[] normals = _blender.Blend( procedural.Normals, aiNormals, cached.Source.Alpha, settings.Procedural.AlphaThreshold, settings.AiBlend );
            MaskMapGenerator maskGenerator = new( settings.Mask, settings.Procedural.AlphaThreshold );
            Color32[] mask = maskGenerator.Generate( cached.Source, procedural, normals );

            Color32[] encodedAiNormals = aiNormals != null ? _blender.Encode( aiNormals ) : null;

            return new ComposedMaps( cached.Source.Width, cached.Source.Height, _blender.Encode( normals ), _blender.Encode( procedural.Normals ), encodedAiNormals, mask );
        }

        public void Apply( Texture2D texture, TextureMapSettings settings )
        {
            SpriteMapsProjectSettings projectSettings = SpriteMapsProjectSettings.instance;
            string sourcePath = AssetDatabase.GetAssetPath( texture );
            string normalPath = _paths.GetMapPath( sourcePath, projectSettings.NormalSuffix );
            string proceduralNormalPath = _paths.GetMapPath( sourcePath, projectSettings.ProceduralNormalSuffix );
            string aiNormalPath = _paths.GetMapPath( sourcePath, projectSettings.AiNormalSuffix );
            string maskPath = _paths.GetMapPath( sourcePath, projectSettings.MaskSuffix );
            TextureImporter sourceImporter = GetImporter( texture );
            ComposedMaps maps = Compose( texture, settings );

            _paths.EnsureFolder( sourcePath );

            Texture2D normalMap = WriteMap( normalPath, maps.Normals, maps, sourceImporter );
            Texture2D proceduralNormalMap = WriteMap( proceduralNormalPath, maps.ProceduralNormals, maps, sourceImporter );
            Texture2D maskMap = WriteMap( maskPath, maps.Mask, maps, sourceImporter );
            Texture2D aiNormalMap = LoadDataTexture( aiNormalPath, sourceImporter );

            SecondarySpriteTexture[] ownTextures =
            {
                new() { name = projectSettings.NormalTextureName, texture = normalMap },
                new() { name = projectSettings.ProceduralNormalTextureName, texture = proceduralNormalMap },
                new() { name = projectSettings.AiNormalTextureName, texture = aiNormalMap },
                new() { name = projectSettings.MaskTextureName, texture = maskMap }
            };

            bool isImporterChanged = AssignSecondaryTextures( sourceImporter, ownTextures );
            isImporterChanged |= _store.Store( sourceImporter, settings );

            if ( isImporterChanged )
            {
                sourceImporter.SaveAndReimport();
            }

            SpriteMapsEvents.RaiseMapsGenerated( new SpriteMapsResult( texture, sourceImporter, normalMap, proceduralNormalMap, aiNormalMap, maskMap ) );
        }

        public void ClearCache()
        {
            _cache.Clear();
        }

        private TextureImporter GetImporter( Texture2D texture )
        {
            return (TextureImporter)AssetImporter.GetAtPath( AssetDatabase.GetAssetPath( texture ) );
        }

        private CachedTexture GetCached( string path )
        {
            DateTime timestamp = File.GetLastWriteTimeUtc( path );

            if ( !_cache.TryGetValue( path, out CachedTexture cached ) || cached.SourceTimestamp != timestamp )
            {
                Color32[] pixels = PngFile.Read( path, out int width, out int height );
                cached = new CachedTexture( new SourceImage( pixels, width, height ), timestamp );
                _cache[ path ] = cached;
            }

            return cached;
        }

        private ProceduralMaps GetProcedural( CachedTexture cached, ProceduralMapSettings settings )
        {
            string key = JsonUtility.ToJson( settings );

            if ( cached.Procedural == null || cached.ProceduralKey != key )
            {
                ProceduralMapGenerator generator = new( settings );
                cached.SetProcedural( key, generator.Generate( cached.Source ) );
            }

            return cached.Procedural;
        }

        private Vector3[] GetAiNormals( CachedTexture cached, string aiNormalPath )
        {
            if ( !File.Exists( aiNormalPath ) )
            {
                cached.SetAiNormals( default, null );
                return null;
            }

            DateTime timestamp = File.GetLastWriteTimeUtc( aiNormalPath );

            if ( cached.AiNormals == null || cached.AiTimestamp != timestamp )
            {
                Color32[] pixels = PngFile.Read( aiNormalPath, out int width, out int height );

                if ( width != cached.Source.Width || height != cached.Source.Height )
                {
                    Debug.LogWarning( $"[SpriteMapsGenerator] '{aiNormalPath}' size does not match the source texture, AI normals are ignored" );
                    return null;
                }

                cached.SetAiNormals( timestamp, _blender.Decode( pixels ) );
            }

            return cached.AiNormals;
        }

        private Texture2D WriteMap( string path, Color32[] pixels, ComposedMaps maps, TextureImporter sourceImporter )
        {
            PngFile.Write( path, pixels, maps.Width, maps.Height );
            AssetDatabase.ImportAsset( path, ImportAssetOptions.ForceSynchronousImport );

            return LoadDataTexture( path, sourceImporter );
        }

        private Texture2D LoadDataTexture( string path, TextureImporter sourceImporter )
        {
            if ( !File.Exists( path ) )
            {
                return null;
            }

            ConfigureDataTexture( path, sourceImporter );

            return AssetDatabase.LoadAssetAtPath< Texture2D >( path );
        }

        private void ConfigureDataTexture( string path, TextureImporter sourceImporter )
        {
            TextureImporter importer = AssetImporter.GetAtPath( path ) as TextureImporter;

            if ( importer == null )
            {
                AssetDatabase.ImportAsset( path, ImportAssetOptions.ForceSynchronousImport );
                importer = AssetImporter.GetAtPath( path ) as TextureImporter;
            }

            if ( importer == null )
            {
                return;
            }

            bool isConfigured =
                importer.textureType == TextureImporterType.Default &&
                !importer.sRGBTexture &&
                importer.alphaSource == TextureImporterAlphaSource.None &&
                !importer.mipmapEnabled &&
                importer.npotScale == TextureImporterNPOTScale.None &&
                importer.wrapMode == TextureWrapMode.Clamp &&
                importer.filterMode == sourceImporter.filterMode &&
                importer.maxTextureSize == sourceImporter.maxTextureSize &&
                importer.textureCompression == sourceImporter.textureCompression;

            if ( isConfigured )
            {
                return;
            }

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = sourceImporter.filterMode;
            importer.maxTextureSize = sourceImporter.maxTextureSize;
            importer.textureCompression = sourceImporter.textureCompression;
            importer.SaveAndReimport();
        }

        private bool AssignSecondaryTextures( TextureImporter importer, SecondarySpriteTexture[] ownTextures )
        {
            SecondarySpriteTexture[] existing = importer.secondarySpriteTextures ?? Array.Empty< SecondarySpriteTexture >();
            List< SecondarySpriteTexture > textures = new();

            for ( int i = 0; i < existing.Length; i++ )
            {
                if ( !IsOwnTexture( existing[ i ], ownTextures ) )
                {
                    textures.Add( existing[ i ] );
                }
            }

            for ( int i = 0; i < ownTextures.Length; i++ )
            {
                if ( ownTextures[ i ].texture != null )
                {
                    textures.Add( ownTextures[ i ] );
                }
            }

            if ( AreSameTextures( existing, textures ) )
            {
                return false;
            }

            importer.secondarySpriteTextures = textures.ToArray();

            return true;
        }

        private bool IsOwnTexture( SecondarySpriteTexture texture, SecondarySpriteTexture[] ownTextures )
        {
            for ( int i = 0; i < ownTextures.Length; i++ )
            {
                bool isOwnName = texture.name == ownTextures[ i ].name;
                bool isOwnAsset = texture.texture != null && texture.texture == ownTextures[ i ].texture;

                if ( isOwnName || isOwnAsset )
                {
                    return true;
                }
            }

            return false;
        }

        private bool AreSameTextures( SecondarySpriteTexture[] existing, List< SecondarySpriteTexture > textures )
        {
            if ( existing.Length != textures.Count )
            {
                return false;
            }

            for ( int i = 0; i < existing.Length; i++ )
            {
                if ( existing[ i ].name != textures[ i ].name || existing[ i ].texture != textures[ i ].texture )
                {
                    return false;
                }
            }

            return true;
        }

        private sealed class CachedTexture
        {
            public CachedTexture( SourceImage source, DateTime sourceTimestamp )
            {
                Source = source;
                SourceTimestamp = sourceTimestamp;
            }

            public SourceImage Source { get; }
            public DateTime SourceTimestamp { get; }
            public string ProceduralKey { get; private set; }
            public ProceduralMaps Procedural { get; private set; }
            public DateTime AiTimestamp { get; private set; }
            public Vector3[] AiNormals { get; private set; }

            public void SetProcedural( string key, ProceduralMaps procedural )
            {
                ProceduralKey = key;
                Procedural = procedural;
            }

            public void SetAiNormals( DateTime timestamp, Vector3[] normals )
            {
                AiTimestamp = timestamp;
                AiNormals = normals;
            }
        }
    }
}

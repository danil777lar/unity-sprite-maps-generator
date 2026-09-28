using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Larje.SpriteMapsGenerator.Editor
{
    public sealed class SpriteMapsGenerator
    {
        public const string AiNormalSuffix = "_normal_ai";

        private readonly NormalBlender _blender = new();
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
            string name = Path.GetFileNameWithoutExtension( path );
            SpriteMapsProjectSettings settings = SpriteMapsProjectSettings.instance;

            bool isSupportedFormat = extension == ".png" || extension == ".jpg" || extension == ".jpeg";
            bool isGenerated =
                EndsWithSuffix( name, settings.NormalSuffix ) ||
                EndsWithSuffix( name, settings.MaskSuffix ) ||
                EndsWithSuffix( name, AiNormalSuffix );

            return isSupportedFormat && !isGenerated && AssetImporter.GetAtPath( path ) is TextureImporter;
        }

        public TextureMapSettings LoadSettings( Texture2D texture )
        {
            return _store.Load( GetImporter( texture ) );
        }

        public string GetAiNormalPath( Texture2D texture )
        {
            return GetSiblingPath( AssetDatabase.GetAssetPath( texture ), AiNormalSuffix );
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
            Vector3[] aiNormals = GetAiNormals( cached, path );

            Vector3[] normals = _blender.Blend( procedural.Normals, aiNormals, cached.Source.Alpha, settings.Procedural.AlphaThreshold, settings.AiBlend );
            MaskMapGenerator maskGenerator = new( settings.Mask, settings.Procedural.AlphaThreshold );
            Color32[] mask = maskGenerator.Generate( cached.Source, procedural, normals );

            return new ComposedMaps( cached.Source.Width, cached.Source.Height, _blender.Encode( normals ), mask );
        }

        public void Apply( Texture2D texture, TextureMapSettings settings )
        {
            SpriteMapsProjectSettings projectSettings = SpriteMapsProjectSettings.instance;
            string sourcePath = AssetDatabase.GetAssetPath( texture );
            string normalPath = GetSiblingPath( sourcePath, projectSettings.NormalSuffix );
            string maskPath = GetSiblingPath( sourcePath, projectSettings.MaskSuffix );
            string aiNormalPath = GetSiblingPath( sourcePath, AiNormalSuffix );
            ComposedMaps maps = Compose( texture, settings );

            PngFile.Write( normalPath, maps.Normals, maps.Width, maps.Height );
            PngFile.Write( maskPath, maps.Mask, maps.Width, maps.Height );
            AssetDatabase.ImportAsset( normalPath, ImportAssetOptions.ForceSynchronousImport );
            AssetDatabase.ImportAsset( maskPath, ImportAssetOptions.ForceSynchronousImport );

            TextureImporter sourceImporter = GetImporter( texture );
            ConfigureDataTexture( normalPath, sourceImporter );
            ConfigureDataTexture( maskPath, sourceImporter );

            if ( File.Exists( aiNormalPath ) )
            {
                ConfigureDataTexture( aiNormalPath, sourceImporter );
            }

            Texture2D normalMap = AssetDatabase.LoadAssetAtPath< Texture2D >( normalPath );
            Texture2D maskMap = AssetDatabase.LoadAssetAtPath< Texture2D >( maskPath );

            bool isImporterChanged = AssignSecondaryTextures( sourceImporter, projectSettings, normalMap, maskMap );
            isImporterChanged |= _store.Store( sourceImporter, settings );

            if ( isImporterChanged )
            {
                sourceImporter.SaveAndReimport();
            }

            SpriteMapsEvents.RaiseMapsGenerated( new SpriteMapsResult( texture, sourceImporter, normalMap, maskMap ) );
        }

        public void ClearCache()
        {
            _cache.Clear();
        }

        private static bool EndsWithSuffix( string name, string suffix )
        {
            return !string.IsNullOrEmpty( suffix ) && name.EndsWith( suffix, StringComparison.Ordinal );
        }

        private TextureImporter GetImporter( Texture2D texture )
        {
            return (TextureImporter)AssetImporter.GetAtPath( AssetDatabase.GetAssetPath( texture ) );
        }

        private string GetSiblingPath( string sourcePath, string suffix )
        {
            string directory = Path.GetDirectoryName( sourcePath );
            string name = Path.GetFileNameWithoutExtension( sourcePath );

            return Path.Combine( directory, name + suffix + ".png" ).Replace( '\\', '/' );
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

        private Vector3[] GetAiNormals( CachedTexture cached, string sourcePath )
        {
            string aiNormalPath = GetSiblingPath( sourcePath, AiNormalSuffix );

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

        private bool AssignSecondaryTextures( TextureImporter importer, SpriteMapsProjectSettings settings, Texture2D normalMap, Texture2D maskMap )
        {
            SecondarySpriteTexture[] existing = importer.secondarySpriteTextures ?? Array.Empty< SecondarySpriteTexture >();
            List< SecondarySpriteTexture > textures = new();
            bool hasNormal = false;
            bool hasMask = false;

            for ( int i = 0; i < existing.Length; i++ )
            {
                SecondarySpriteTexture texture = existing[ i ];
                bool isOwnName = texture.name == settings.NormalTextureName || texture.name == settings.MaskTextureName;
                bool isOwnTexture = texture.texture == normalMap || texture.texture == maskMap;

                hasNormal |= texture.name == settings.NormalTextureName && texture.texture == normalMap;
                hasMask |= texture.name == settings.MaskTextureName && texture.texture == maskMap;

                if ( !isOwnName && !isOwnTexture )
                {
                    textures.Add( texture );
                }
            }

            if ( hasNormal && hasMask && textures.Count + 2 == existing.Length )
            {
                return false;
            }

            textures.Add( new SecondarySpriteTexture { name = settings.NormalTextureName, texture = normalMap } );
            textures.Add( new SecondarySpriteTexture { name = settings.MaskTextureName, texture = maskMap } );
            importer.secondarySpriteTextures = textures.ToArray();

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

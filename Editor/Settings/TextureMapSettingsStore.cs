using System;
using UnityEditor;
using UnityEngine;

namespace Larje.SpriteMapsGenerator.Editor
{
    public sealed class TextureMapSettingsStore
    {
        private const string ToolId = "com.larje.spritemapsgenerator";

        public TextureMapSettings Load( TextureImporter importer )
        {
            Envelope envelope = Parse( importer.userData );

            if ( envelope == null || envelope.settings == null )
            {
                return new TextureMapSettings();
            }

            return envelope.settings;
        }

        public bool CanStore( TextureImporter importer )
        {
            return string.IsNullOrEmpty( importer.userData ) || Parse( importer.userData ) != null;
        }

        public bool Store( TextureImporter importer, TextureMapSettings settings )
        {
            if ( !CanStore( importer ) )
            {
                Debug.LogWarning( $"[SpriteMapsGenerator] '{importer.assetPath}' userData is used by another tool, settings are not saved" );
                return false;
            }

            string json = JsonUtility.ToJson( new Envelope { tool = ToolId, settings = settings } );

            if ( importer.userData == json )
            {
                return false;
            }

            importer.userData = json;
            return true;
        }

        private Envelope Parse( string userData )
        {
            if ( string.IsNullOrEmpty( userData ) )
            {
                return null;
            }

            try
            {
                Envelope envelope = JsonUtility.FromJson< Envelope >( userData );
                return envelope != null && envelope.tool == ToolId ? envelope : null;
            }
            catch ( ArgumentException )
            {
                return null;
            }
        }

        [ Serializable ]
        private sealed class Envelope
        {
            public string tool;
            public TextureMapSettings settings;
        }
    }
}

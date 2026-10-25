using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Larje.SpriteMapsGenerator.Editor.Cli
{
    public sealed class SpriteMapsSettingsJson
    {
        private const string FieldPrefix = "_";
        private const int DefaultDigits = 4;
        private const int OutputDigits = 6;
        private const BindingFlags FieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Dictionary< string, string > Descriptions = new()
        {
            { "procedural", "Procedural normal map: bevel built from the sprite silhouette plus detail from color luminance. alphaThreshold is also used by the mask and the blend." },
            { "mask", "Mask map: R = shine, G = sweep order, B = rim. Derived from the procedural height and the final normals." },
            { "ai", "AI normal inference (Marigold). Used only by SpriteMaps_GenerateAi." },
            { "aiBlend", "Final normal map blend: 0 = procedural normals only, 1 = AI normals only. Has effect only when AI normals exist." }
        };

        public JObject ToJson( TextureMapSettings settings )
        {
            JObject source = JObject.Parse( JsonUtility.ToJson( settings ) );
            return StripPrefixes( source );
        }

        public TextureMapSettings Merge( TextureMapSettings baseSettings, JObject overrides, ICollection< string > errors )
        {
            JObject target = JObject.Parse( JsonUtility.ToJson( baseSettings ) );

            if ( overrides != null )
            {
                MergeObject( typeof( TextureMapSettings ), overrides, target, string.Empty, errors );
            }

            return JsonUtility.FromJson< TextureMapSettings >( target.ToString() );
        }

        public JObject BuildSchema( string description )
        {
            JObject schema = BuildObjectSchema( typeof( TextureMapSettings ), new TextureMapSettings() );
            schema[ "description" ] = description;

            return schema;
        }

        private JObject StripPrefixes( JObject source )
        {
            JObject result = new();

            foreach ( JProperty property in source.Properties() )
            {
                result[ StripPrefix( property.Name ) ] = CleanToken( property.Value );
            }

            return result;
        }

        private JToken CleanToken( JToken token )
        {
            if ( token is JObject nested )
            {
                return StripPrefixes( nested );
            }

            if ( token.Type == JTokenType.Float )
            {
                return Math.Round( token.Value< double >(), OutputDigits );
            }

            return token.DeepClone();
        }

        private void MergeObject( Type type, JObject source, JObject target, string path, ICollection< string > errors )
        {
            foreach ( JProperty property in source.Properties() )
            {
                string key = path + StripPrefix( property.Name );
                FieldInfo field = FindField( type, property.Name );

                if ( field == null )
                {
                    errors.Add( $"Unknown setting '{key}'. Valid keys: {string.Join( ", ", GetKeys( type ) )}" );
                }
                else if ( IsNumeric( field.FieldType ) )
                {
                    MergeNumber( field, property.Value, target, key, errors );
                }
                else if ( property.Value is JObject nested && target[ field.Name ] is JObject nestedTarget )
                {
                    MergeObject( field.FieldType, nested, nestedTarget, key + ".", errors );
                }
                else
                {
                    errors.Add( $"Setting '{key}' must be an object" );
                }
            }
        }

        private void MergeNumber( FieldInfo field, JToken token, JObject target, string key, ICollection< string > errors )
        {
            if ( token.Type != JTokenType.Integer && token.Type != JTokenType.Float )
            {
                errors.Add( $"Setting '{key}' must be a number" );
                return;
            }

            float number = ClampToAttributes( field, token.Value< float >() );

            if ( field.FieldType == typeof( int ) )
            {
                target[ field.Name ] = Mathf.RoundToInt( number );
            }
            else
            {
                target[ field.Name ] = number;
            }
        }

        private float ClampToAttributes( FieldInfo field, float number )
        {
            RangeAttribute range = field.GetCustomAttribute< RangeAttribute >();

            if ( range != null )
            {
                return Mathf.Clamp( number, range.min, range.max );
            }

            MinAttribute min = field.GetCustomAttribute< MinAttribute >();

            if ( min != null )
            {
                return Mathf.Max( number, min.min );
            }

            return number;
        }

        private JObject BuildObjectSchema( Type type, object defaults )
        {
            JObject properties = new();
            string header = null;
            List< FieldInfo > fields = GetSerializedFields( type );

            for ( int i = 0; i < fields.Count; i++ )
            {
                FieldInfo field = fields[ i ];
                string name = StripPrefix( field.Name );
                HeaderAttribute headerAttribute = field.GetCustomAttribute< HeaderAttribute >();

                if ( headerAttribute != null )
                {
                    header = headerAttribute.header;
                }

                JObject fieldSchema = IsNumeric( field.FieldType )
                    ? BuildNumberSchema( field, field.GetValue( defaults ) )
                    : BuildObjectSchema( field.FieldType, field.GetValue( defaults ) );

                if ( Descriptions.TryGetValue( name, out string description ) )
                {
                    fieldSchema[ "description" ] = description;
                }
                else if ( header != null )
                {
                    fieldSchema[ "description" ] = $"{header} group";
                }

                properties[ name ] = fieldSchema;
            }

            return new JObject
            {
                [ "type" ] = "object",
                [ "properties" ] = properties,
                [ "additionalProperties" ] = false
            };
        }

        private JObject BuildNumberSchema( FieldInfo field, object defaultNumber )
        {
            bool isInteger = field.FieldType == typeof( int );
            JObject schema = new() { [ "type" ] = isInteger ? "integer" : "number" };
            RangeAttribute range = field.GetCustomAttribute< RangeAttribute >();
            MinAttribute min = field.GetCustomAttribute< MinAttribute >();

            if ( range != null )
            {
                schema[ "minimum" ] = range.min;
                schema[ "maximum" ] = range.max;
            }
            else if ( min != null )
            {
                schema[ "minimum" ] = min.min;
            }

            if ( isInteger )
            {
                schema[ "default" ] = (int)defaultNumber;
            }
            else
            {
                schema[ "default" ] = Math.Round( (float)defaultNumber, DefaultDigits );
            }

            return schema;
        }

        private FieldInfo FindField( Type type, string name )
        {
            string key = StripPrefix( name );
            List< FieldInfo > fields = GetSerializedFields( type );

            for ( int i = 0; i < fields.Count; i++ )
            {
                if ( StripPrefix( fields[ i ].Name ) == key )
                {
                    return fields[ i ];
                }
            }

            return null;
        }

        private List< string > GetKeys( Type type )
        {
            List< FieldInfo > fields = GetSerializedFields( type );
            List< string > keys = new();

            for ( int i = 0; i < fields.Count; i++ )
            {
                keys.Add( StripPrefix( fields[ i ].Name ) );
            }

            return keys;
        }

        private List< FieldInfo > GetSerializedFields( Type type )
        {
            FieldInfo[] fields = type.GetFields( FieldFlags );
            List< FieldInfo > serialized = new();

            for ( int i = 0; i < fields.Length; i++ )
            {
                bool isSerialized = fields[ i ].IsPublic || fields[ i ].IsDefined( typeof( SerializeField ) );

                if ( isSerialized && !fields[ i ].IsNotSerialized )
                {
                    serialized.Add( fields[ i ] );
                }
            }

            return serialized;
        }

        private bool IsNumeric( Type type )
        {
            return type == typeof( float ) || type == typeof( int );
        }

        private string StripPrefix( string name )
        {
            return name.StartsWith( FieldPrefix, StringComparison.Ordinal ) ? name.Substring( FieldPrefix.Length ) : name;
        }
    }
}

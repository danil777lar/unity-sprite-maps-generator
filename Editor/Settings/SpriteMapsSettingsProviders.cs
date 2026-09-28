using System;
using UnityEditor;
using UnityEngine;

namespace Larje.SpriteMapsGenerator.Editor
{
    public static class SpriteMapsSettingsProviders
    {
        public const string ProjectSettingsPath = "Project/Sprite Maps Generator";
        public const string UserSettingsPath = "Preferences/Sprite Maps Generator";

        [ SettingsProvider ]
        public static SettingsProvider CreateProjectProvider()
        {
            return new SettingsProvider( ProjectSettingsPath, SettingsScope.Project )
            {
                guiHandler = searchContext => DrawSettings( SpriteMapsProjectSettings.instance, SpriteMapsProjectSettings.instance.SaveSettings )
            };
        }

        [ SettingsProvider ]
        public static SettingsProvider CreateUserProvider()
        {
            return new SettingsProvider( UserSettingsPath, SettingsScope.User )
            {
                guiHandler = searchContext => DrawSettings( SpriteMapsUserSettings.instance, SpriteMapsUserSettings.instance.SaveSettings )
            };
        }

        private static void DrawSettings( ScriptableObject settings, Action save )
        {
            settings.hideFlags &= ~HideFlags.NotEditable;

            SerializedObject serializedSettings = new( settings );
            SerializedProperty property = serializedSettings.GetIterator();
            property.NextVisible( true );

            EditorGUI.BeginChangeCheck();

            while ( property.NextVisible( false ) )
            {
                EditorGUILayout.PropertyField( property, true );
            }

            if ( EditorGUI.EndChangeCheck() )
            {
                serializedSettings.ApplyModifiedPropertiesWithoutUndo();
                save();
            }
        }
    }
}

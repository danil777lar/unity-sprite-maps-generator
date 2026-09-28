using System;

namespace Larje.SpriteMapsGenerator.Editor
{
    public static class SpriteMapsEvents
    {
        public static event Action< SpriteMapsResult > MapsGenerated;

        internal static void RaiseMapsGenerated( SpriteMapsResult result )
        {
            MapsGenerated?.Invoke( result );
        }
    }
}

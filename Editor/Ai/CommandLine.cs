using System.Text;

namespace Larje.SpriteMapsGenerator.Editor
{
    internal static class CommandLine
    {
        public static string Join( params string[] arguments )
        {
            StringBuilder builder = new();

            for ( int i = 0; i < arguments.Length; i++ )
            {
                if ( i > 0 )
                {
                    builder.Append( ' ' );
                }

                builder.Append( '"' ).Append( arguments[ i ].Replace( "\"", "\\\"" ) ).Append( '"' );
            }

            return builder.ToString();
        }
    }
}

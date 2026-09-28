using System.IO;

namespace Larje.SpriteMapsGenerator.Editor
{
    internal static class PackagePaths
    {
        private const string PackageName = "com.larje.spritemapsgenerator";
        private const string PythonFolder = "Python~";

        public static string Root
        {
            get
            {
                UnityEditor.PackageManager.PackageInfo info = UnityEditor.PackageManager.PackageInfo.FindForAssembly( typeof( PackagePaths ).Assembly );
                return info != null ? info.resolvedPath : Path.GetFullPath( Path.Combine( "Packages", PackageName ) );
            }
        }

        public static string NormalsScript => Path.Combine( Root, PythonFolder, "sprite_normals.py" );
        public static string Requirements => Path.Combine( Root, PythonFolder, "requirements.txt" );
    }
}

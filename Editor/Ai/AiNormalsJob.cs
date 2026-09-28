using System;

namespace Larje.SpriteMapsGenerator.Editor
{
    [ Serializable ]
    public sealed class AiNormalsJob
    {
        public string source;
        public string atlas;
        public string output;
        public int resolution;
        public int ensembleSize;
        public int steps;
        public float normalGain;
        public float edgeWidth;
        public float edgeStrength;
        public float edgeBlend;

        public AiNormalsJob( string sourcePath, string atlasPath, string outputPath, AiMapSettings settings )
        {
            source = sourcePath;
            atlas = atlasPath ?? string.Empty;
            output = outputPath;
            resolution = settings.Resolution;
            ensembleSize = settings.EnsembleSize;
            steps = settings.Steps;
            normalGain = settings.NormalGain;
            edgeWidth = settings.EdgeWidth;
            edgeStrength = settings.EdgeStrength;
            edgeBlend = settings.EdgeBlend;
        }
    }
}

using System;
using System.Collections.Generic;

namespace Lore.Unity.Infrastructure.Runtime
{
    [Serializable]
    public sealed class RuntimeManifest
    {
        public string loreVersion;
        public List<RuntimeArtifact> artifacts = new List<RuntimeArtifact>();
    }

    [Serializable]
    public sealed class RuntimeArtifact
    {
        public string platform;
        public string officialArtifactUrl;
        public string sha256;
        public long downloadSize;
        public string artifactFormat;
    }
}

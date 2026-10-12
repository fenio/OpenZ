using System.IO;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>Repository-relative paths (the project is published as open source: no machine-specific paths in code).
    /// </summary>
    public static class OpenZPaths
    {
        public static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", ".."));
        public static string Records { get { var p = Path.Combine(RepoRoot, "work", "records"); Directory.CreateDirectory(p); return p; } }
    }
}

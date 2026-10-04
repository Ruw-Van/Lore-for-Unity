using System;
using System.IO;
using System.Runtime.InteropServices;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Integration.Assets
{
    // A Lore repository may be an ancestor of the Unity project. Never infer
    // repository identity from the path; the detector already obtained it from Lore.
    public sealed class UnityAssetPathMapper
    {
        private readonly string _projectRoot;
        private readonly string _repositoryRoot;
        private readonly string _projectPrefix;
        private readonly StringComparison _comparison;

        public UnityAssetPathMapper(AbsolutePath projectRoot, AbsolutePath repositoryRoot)
        {
            if (string.IsNullOrEmpty(projectRoot.Value) || string.IsNullOrEmpty(repositoryRoot.Value))
                throw new ArgumentException("Project and repository roots are required.");
            _projectRoot = Path.GetFullPath(projectRoot.Value);
            _repositoryRoot = Path.GetFullPath(repositoryRoot.Value);
            _comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!Within(_projectRoot, _repositoryRoot, _comparison))
                throw new ArgumentException("Unity project must be inside the detected Lore repository.");
            var relative = Path.GetRelativePath(_repositoryRoot, _projectRoot).Replace('\\', '/');
            _projectPrefix = relative == "." ? string.Empty : relative + "/";
        }

        public Result<RepositoryPath> ToRepositoryPath(UnityAssetPath path)
        {
            var value = path.Value;
            if (!ValidUnityPath(value)) return Invalid<RepositoryPath>();
            var full = Path.GetFullPath(Path.Combine(_projectRoot, value.Replace('/', Path.DirectorySeparatorChar)));
            if (!Within(full, _projectRoot, _comparison)) return Invalid<RepositoryPath>();
            try { return Result<RepositoryPath>.Success(new RepositoryPath(_projectPrefix + value)); }
            catch (ArgumentException) { return Invalid<RepositoryPath>(); }
        }

        public Result<UnityAssetPath> ToUnityPath(RepositoryPath path)
        {
            if (string.IsNullOrEmpty(path.Value) ||
                !path.Value.StartsWith(_projectPrefix, _comparison)) return Invalid<UnityAssetPath>();
            var relative = path.Value.Substring(_projectPrefix.Length);
            if (!ValidUnityPath(relative)) return Invalid<UnityAssetPath>();
            var full = Path.GetFullPath(Path.Combine(_repositoryRoot,
                path.Value.Replace('/', Path.DirectorySeparatorChar)));
            if (!Within(full, _projectRoot, _comparison)) return Invalid<UnityAssetPath>();
            return Result<UnityAssetPath>.Success(new UnityAssetPath(relative));
        }

        private static bool ValidUnityPath(string value)
        {
            if (string.IsNullOrEmpty(value) || value.IndexOf('\\') >= 0 || value.IndexOf('\0') >= 0 ||
                !(value.StartsWith("Assets/", StringComparison.Ordinal) ||
                  value.StartsWith("ProjectSettings/", StringComparison.Ordinal) ||
                  value.StartsWith("Packages/", StringComparison.Ordinal))) return false;
            foreach (var segment in value.Split('/'))
                if (segment.Length == 0 || segment == "." || segment == ".." || segment.IndexOf(':') >= 0)
                    return false;
            return true;
        }

        private static bool Within(string path, string root, StringComparison comparison) =>
            path.Equals(root, comparison) || path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison);

        private static Result<T> Invalid<T>() => Result<T>.Failure(new LoreError(ErrorCode.ValidationFailed,
            "Path is not a safe Unity project asset path within the Lore repository."));
    }
}

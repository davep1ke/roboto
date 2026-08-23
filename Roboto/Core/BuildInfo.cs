using System.Linq;
using System.Reflection;

namespace RobotoChatBot
{
    /// <summary>
    /// Reads the GitCommit/BuildDate AssemblyMetadata attributes Roboto.csproj embeds at compile
    /// time (see its own comment - a real MSBuild property baked into the assembly, not something
    /// read from the environment at runtime, so it survives however the container gets launched).
    /// Exists to answer "which build is actually running" - see mod_standard's /version command.
    /// </summary>
    public static class BuildInfo
    {
        private static readonly AssemblyMetadataAttribute[] Metadata =
            Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().ToArray();

        public static string GitCommit => Metadata.FirstOrDefault(a => a.Key == "GitCommit")?.Value ?? "unknown";
        public static string BuildDate => Metadata.FirstOrDefault(a => a.Key == "BuildDate")?.Value ?? "unknown";
    }
}

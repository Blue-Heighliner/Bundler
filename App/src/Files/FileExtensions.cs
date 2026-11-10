namespace BlueHeighliner.Bundler;

/// <summary>
/// Extensions of <see cref="File" />.
/// </summary>
internal static class FileExtensions
{
    extension(File)
    {
        /// <summary>
        /// Deletes a file when it is there. <see cref="File.Delete(string)" /> ignores a missing file but throws when the folder is missing too,
        /// which an uninstall meets when a person already removed the folder.
        /// </summary>
        /// <param name="path">The file.</param>
        public static void DeleteIfPresent(string path)
        {
            if (Directory.Exists(Path.GetDirectoryName(path)))
            {
                File.Delete(path);
            }
        }
    }
}

namespace VSHelpDesk.Application.Common.IO;

public interface ITemporaryFileFactory
{
    (FileStream Stream, string Path) CreateTempFile();
}

public sealed class DefaultTemporaryFileFactory : ITemporaryFileFactory
{
    public (FileStream Stream, string Path) CreateTempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var fs = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 8192, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        return (fs, path);
    }
}
